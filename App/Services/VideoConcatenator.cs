using System.IO;
using System.Runtime.InteropServices;
using static VideoNR.Services.MediaFoundationInterop;

namespace VideoNR.Services;

internal static class VideoConcatenator
{
    private const uint AudioChannels = 2;
    private const uint AudioSampleRate = 48_000;
    private const uint AudioBitsPerSample = 16;
    private const uint AudioBlockAlign = AudioChannels * (AudioBitsPerSample / 8);
    private const uint AudioPcmBytesPerSec = AudioSampleRate * AudioBlockAlign;
    private const uint AudioAacBytesPerSec = 24_000; // 192 kbps stereo AAC

    internal static Task ConcatenateAsync(
        IReadOnlyList<string> inputPaths,
        string outputPath,
        bool preferHevc,
        Action<int>? onProgress,
        CancellationToken cancellationToken, bool allowOverwrite = false) =>
        Task.Run(() => Concatenate(inputPaths, outputPath, preferHevc, onProgress, cancellationToken, allowOverwrite), cancellationToken);

    internal static void Concatenate(
        IReadOnlyList<string> inputPaths,
        string outputPath,
        bool preferHevc,
        Action<int>? onProgress,
        CancellationToken cancellationToken, bool allowOverwrite = false)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("A junção de vídeos via Media Foundation está disponível apenas no Windows.");
        if (inputPaths is null || inputPaths.Count == 0)
            throw new ArgumentException("Nenhum segmento informado para concatenação.", nameof(inputPaths));

        cancellationToken.ThrowIfCancellationRequested();
        using var output = new OutputTransaction(outputPath);
        if (inputPaths.Count == 1)
        {
            File.Copy(inputPaths[0], output.StagedPath);
            output.Commit(allowOverwrite, cancellationToken);
            onProgress?.Invoke(10000);
            return;
        }

        var probes = new VideoProbeInfo[inputPaths.Count];
        var totalExpectedHns = 0L;
        var anyAudio = false;
        VideoProbeInfo? firstValid = null;

        for (var i = 0; i < inputPaths.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = inputPaths[i];
            if (!File.Exists(path))
                throw new FileNotFoundException("Segmento intermediário não encontrado para concatenação.", path);

            var probe = MediaFoundationFrameCodec.TryProbe(path)
                ?? throw new IOException($"Não foi possível inspecionar o segmento '{Path.GetFileName(path)}'.");
            probes[i] = probe;
            firstValid ??= probe;
            anyAudio |= probe.HasAudio;
            totalExpectedHns += (long)Math.Round(Math.Max(0.1, probe.DurationSeconds) * 10_000_000.0);
        }

        var targetW = AlignEven(Math.Max(16, firstValid!.Width));
        var targetH = AlignEven(Math.Max(16, firstValid.Height));
        var fpsValue = firstValid.FrameRate is { } rate && double.IsFinite(rate) && rate >= 1 && rate <= 240
            ? rate
            : 30.0;
        var fpsNum = (uint)Math.Max(1, Math.Round(fpsValue * 1000.0));
        var fpsDen = 1000u;
        if (Math.Abs(fpsValue - Math.Round(fpsValue)) < 0.01)
        {
            fpsNum = (uint)Math.Max(1, Math.Round(fpsValue));
            fpsDen = 1u;
        }
        var defaultFrameDurationHns = Math.Max(1L, (long)Math.Round(10_000_000.0 / fpsValue));

        CheckHr(MFStartup(MfApiVersion, MfStartupNoSocket), "MFStartup");
        IMFAttributes? writerAttrs = null;
        IMFSinkWriter? writer = null;
        try
        {
            CheckHr(MFCreateAttributes(out writerAttrs, 2), "MFCreateAttributes");
            CheckHr(writerAttrs.SetUINT32(in MfSinkWriterDisableThrottling, 1), "SetUINT32(DisableThrottling)");
            CheckHr(writerAttrs.SetUINT32(in MfReadWriteEnableHardwareTransforms, 0), "SetUINT32(HardwareTransforms)");

            CheckHr(MFCreateSinkWriterFromURL(output.StagedPath, IntPtr.Zero, writerAttrs, out writer), "MFCreateSinkWriterFromURL");

            var videoStreamIndex = ConfigureVideoStream(writer, targetW, targetH, fpsNum, fpsDen, preferHevc);
            uint? audioStreamIndex = anyAudio ? TryConfigureAudioStream(writer) ?? throw new IOException("Falha ao configurar a faixa AAC.") : null;

            CheckHr(writer.BeginWriting(), "BeginWriting");

            var timelineOffsetHns = 0L;
            var lastWrittenVideoPts = -1L;
            var lastWrittenAudioEnd = 0L;

            for (var i = 0; i < inputPaths.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var segDurationHns = AppendSegment(
                    writer,
                    videoStreamIndex,
                    audioStreamIndex,
                    inputPaths[i],
                    probes[i],
                    targetW,
                    targetH,
                    defaultFrameDurationHns,
                    timelineOffsetHns,
                    ref lastWrittenVideoPts,
                    ref lastWrittenAudioEnd,
                    totalExpectedHns,
                    onProgress,
                    cancellationToken);

                timelineOffsetHns += segDurationHns;
                if (audioStreamIndex is { } audioIdx && lastWrittenAudioEnd < timelineOffsetHns)
                {
                    WriteAudioSilence(writer, audioIdx, lastWrittenAudioEnd, timelineOffsetHns - lastWrittenAudioEnd, cancellationToken);
                    lastWrittenAudioEnd = timelineOffsetHns;
                }
            }

            CheckHr(writer.Finalize(), "Finalize");
            ReleaseCom(writer);
            writer = null;

            output.Commit(allowOverwrite, cancellationToken);
            onProgress?.Invoke(10000);
        }
        finally
        {
            ReleaseCom(writer);
            ReleaseCom(writerAttrs);
            _ = MFShutdown();
        }
    }

    private static uint ConfigureVideoStream(
        IMFSinkWriter writer,
        int width,
        int height,
        uint fpsNum,
        uint fpsDen,
        bool preferHevc)
    {
        var packedSize = Pack2UInt32((uint)width, (uint)height);
        var packedRate = Pack2UInt32(fpsNum, fpsDen);
        var packedPar = Pack2UInt32(1u, 1u);
        var bitrate = (uint)Math.Clamp((long)width * height * 16L, 10_000_000L, 50_000_000L);

        var codecGuid = preferHevc ? MfVideoFormatHevc : MfVideoFormatH264;
        IMFMediaType? outType = null;
        IMFMediaType? inType = null;
        try
        {
            CheckHr(MFCreateMediaType(out outType), "MFCreateMediaType(videoOut)");
            CheckHr(outType.SetGUID(in MfMtMajorType, in MfMediaTypeVideo), "SetGUID(MajorType)");
            CheckHr(outType.SetGUID(in MfMtSubtype, in codecGuid), "SetGUID(VideoCodec)");
            CheckHr(outType.SetUINT32(in MfMtAvgBitrate, bitrate), "SetUINT32(Bitrate)");
            CheckHr(outType.SetUINT32(in MfMtInterlaceMode, MfVideoInterlaceProgressive), "SetUINT32(Interlace)");
            CheckHr(outType.SetUINT64(in MfMtFrameSize, packedSize), "SetUINT64(FrameSize)");
            CheckHr(outType.SetUINT64(in MfMtFrameRate, packedRate), "SetUINT64(FrameRate)");
            CheckHr(outType.SetUINT64(in MfMtPixelAspectRatio, packedPar), "SetUINT64(PAR)");

            CheckHr(writer.AddStream(outType, out var streamIndex), "AddStream(video)");

            CheckHr(MFCreateMediaType(out inType), "MFCreateMediaType(videoIn)");
            CheckHr(inType.SetGUID(in MfMtMajorType, in MfMediaTypeVideo), "SetGUID(MajorType)");
            CheckHr(inType.SetGUID(in MfMtSubtype, in MfVideoFormatRgb32), "SetGUID(RGB32)");
            CheckHr(inType.SetUINT32(in MfMtInterlaceMode, MfVideoInterlaceProgressive), "SetUINT32(Interlace)");
            CheckHr(inType.SetUINT64(in MfMtFrameSize, packedSize), "SetUINT64(FrameSize)");
            CheckHr(inType.SetUINT64(in MfMtFrameRate, packedRate), "SetUINT64(FrameRate)");
            CheckHr(inType.SetUINT64(in MfMtPixelAspectRatio, packedPar), "SetUINT64(PAR)");
            CheckHr(inType.SetUINT32(in MfMtDefaultStride, unchecked((uint)(width * 4))), "SetUINT32(DefaultStride)");

            CheckHr(writer.SetInputMediaType(streamIndex, inType, null), "SetInputMediaType(video)");
            return streamIndex;
        }
        finally
        {
            ReleaseCom(inType);
            ReleaseCom(outType);
        }
    }

    private static uint? TryConfigureAudioStream(IMFSinkWriter writer)
    {
        IMFMediaType? outType = null;
        IMFMediaType? inType = null;
        try
        {
            if (MFCreateMediaType(out outType) < 0 || outType is null) return null;
            if (outType.SetGUID(in MfMtMajorType, in MfMediaTypeAudio) < 0) return null;
            if (outType.SetGUID(in MfMtSubtype, in MfAudioFormatAac) < 0) return null;
            if (outType.SetUINT32(in MfMtAudioNumChannels, AudioChannels) < 0) return null;
            if (outType.SetUINT32(in MfMtAudioSamplesPerSecond, AudioSampleRate) < 0) return null;
            if (outType.SetUINT32(in MfMtAudioBitsPerSample, AudioBitsPerSample) < 0) return null;
            if (outType.SetUINT32(in MfMtAudioAvgBytesPerSecond, AudioAacBytesPerSec) < 0) return null;

            if (writer.AddStream(outType, out var streamIndex) < 0)
                return null;

            if (MFCreateMediaType(out inType) < 0 || inType is null) return null;
            if (inType.SetGUID(in MfMtMajorType, in MfMediaTypeAudio) < 0) return null;
            if (inType.SetGUID(in MfMtSubtype, in MfAudioFormatPcm) < 0) return null;
            if (inType.SetUINT32(in MfMtAudioNumChannels, AudioChannels) < 0) return null;
            if (inType.SetUINT32(in MfMtAudioSamplesPerSecond, AudioSampleRate) < 0) return null;
            if (inType.SetUINT32(in MfMtAudioBitsPerSample, AudioBitsPerSample) < 0) return null;
            if (inType.SetUINT32(in MfMtAudioBlockAlignment, AudioBlockAlign) < 0) return null;
            if (inType.SetUINT32(in MfMtAudioAvgBytesPerSecond, AudioPcmBytesPerSec) < 0) return null;

            return writer.SetInputMediaType(streamIndex, inType, null) >= 0 ? streamIndex : null;
        }
        finally
        {
            ReleaseCom(inType);
            ReleaseCom(outType);
        }
    }

    private static long AppendSegment(
        IMFSinkWriter writer,
        uint videoStreamIndex,
        uint? audioStreamIndex,
        string inputPath,
        VideoProbeInfo probe,
        int targetW,
        int targetH,
        long defaultFrameDurationHns,
        long timelineOffsetHns,
        ref long lastWrittenVideoPts,
        ref long lastWrittenAudioEnd,
        long totalExpectedHns,
        Action<int>? onProgress,
        CancellationToken cancellationToken)
    {
        IMFAttributes? readerAttrs = null;
        IMFSourceReader? reader = null;
        IMFMediaType? reqVideo = null;
        IMFMediaType? curVideo = null;
        IMFMediaType? reqAudio = null;
        try
        {
            CheckHr(MFCreateAttributes(out readerAttrs, 1), "MFCreateAttributes(reader)");
            CheckHr(readerAttrs.SetUINT32(in MfSourceReaderEnableVideoProcessing, 1), "SetUINT32(EnableVideoProcessing)");
            CheckHr(MFCreateSourceReaderFromURL(inputPath, readerAttrs, out reader), "MFCreateSourceReaderFromURL");

            CheckHr(reader.SetStreamSelection(MfSourceReaderAllStreams, false), "DeselectAll");
            CheckHr(reader.SetStreamSelection(MfSourceReaderFirstVideoStream, true), "SelectVideo");

            CheckHr(MFCreateMediaType(out reqVideo), "MFCreateMediaType(reqVideo)");
            CheckHr(reqVideo.SetGUID(in MfMtMajorType, in MfMediaTypeVideo), "SetGUID(Video)");
            CheckHr(reqVideo.SetGUID(in MfMtSubtype, in MfVideoFormatRgb32), "SetGUID(RGB32)");
            CheckHr(reader.SetCurrentMediaType(MfSourceReaderFirstVideoStream, IntPtr.Zero, reqVideo), "SetCurrentMediaType(video)");
            CheckHr(reader.GetCurrentMediaType(MfSourceReaderFirstVideoStream, out curVideo), "GetCurrentMediaType(video)");
            var layout = MediaFoundationFrameCodec.ReadVideoLayout(curVideo);

            var readAudio = false;
            if (audioStreamIndex.HasValue && probe.HasAudio)
            {
                if (reader.SetStreamSelection(MfSourceReaderFirstAudioStream, true) >= 0
                    && MFCreateMediaType(out reqAudio) >= 0
                    && reqAudio is not null
                    && reqAudio.SetGUID(in MfMtMajorType, in MfMediaTypeAudio) >= 0
                    && reqAudio.SetGUID(in MfMtSubtype, in MfAudioFormatPcm) >= 0
                    && reqAudio.SetUINT32(in MfMtAudioNumChannels, AudioChannels) >= 0
                    && reqAudio.SetUINT32(in MfMtAudioSamplesPerSecond, AudioSampleRate) >= 0
                    && reqAudio.SetUINT32(in MfMtAudioBitsPerSample, AudioBitsPerSample) >= 0
                    && reader.SetCurrentMediaType(MfSourceReaderFirstAudioStream, IntPtr.Zero, reqAudio) >= 0)
                {
                    readAudio = true;
                }
                else throw new IOException($"Falha ao decodificar o áudio de {Path.GetFileName(inputPath)}.");
            }

            var (resolvedVideoIdx, resolvedAudioIdx) = ResolveSelectedStreamIndices(reader, wantAudio: readAudio);
            if (resolvedVideoIdx is not { } readerVideoIdx)
                throw new IOException($"Nenhuma faixa de vídeo encontrada em '{Path.GetFileName(inputPath)}'.");
            if (readAudio && !resolvedAudioIdx.HasValue)
                throw new IOException("Faixa de áudio selecionada não encontrada.");

            const int maxConsecutiveEmptyReads = 256;
            var consecutiveEmptyReads = 0;
            var expectedDurationHns = Math.Max(defaultFrameDurationHns, (long)Math.Round(probe.DurationSeconds * 10_000_000.0));
            long? segmentOrigin = null;
            var maxVideoEndLocal = 0L;
            var frameCount = 0;
            var videoEof = false;
            var audioEof = !readAudio || !resolvedAudioIdx.HasValue || audioStreamIndex is null;

            // Single-pass interleaved read keeps SinkWriter memory bounded and prevents EOF loss on pass 2.
            while (!videoEof || !audioEof)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var readTarget = videoEof
                    ? resolvedAudioIdx!.Value
                    : audioEof
                        ? readerVideoIdx
                        : MfSourceReaderAnyStream;

                var hr = reader.ReadSample(readTarget, 0, out var actualIdx, out var flags, out var ts, out var sample);
                if (hr < 0)
                {
                    ReleaseCom(sample);
                    CheckHr(hr, "ReadSample");
                }

                if ((flags & MfSourceReaderFlagError) != 0)
                {
                    ReleaseCom(sample);
                    throw new IOException($"Erro ao ler vídeo de '{Path.GetFileName(inputPath)}'.");
                }

                var isAudioSample = readAudio && resolvedAudioIdx.HasValue && actualIdx == resolvedAudioIdx.Value;
                if (sample is not null)
                {
                    consecutiveEmptyReads = 0;
                    try
                    {
                        segmentOrigin ??= ts;
                        if (actualIdx == readerVideoIdx)
                        {
                            var sampleDuration = sample.GetSampleDuration(out var rawDur) == 0 && rawDur > 0
                                ? rawDur
                                : defaultFrameDurationHns;

                            var localPts = Math.Max(0L, ts - segmentOrigin.Value);
                            var outPts = timelineOffsetHns + localPts;
                            if (outPts <= lastWrittenVideoPts)
                                outPts = lastWrittenVideoPts + Math.Max(1L, defaultFrameDurationHns / 2);

                            var frame = MediaFoundationFrameCodec.ExtractBgraFromSample(
                                sample, layout.BufH, layout.DefaultStride, layout.CropX, layout.CropY, layout.CropW, layout.CropH);
                            var normalized = frame.Width == targetW && frame.Height == targetH
                                ? frame
                                : FitLetterbox(frame, targetW, targetH);

                            WriteVideoFrame(writer, videoStreamIndex, normalized, outPts, sampleDuration);
                            lastWrittenVideoPts = outPts;
                            frameCount++;

                            var localEnd = (outPts - timelineOffsetHns) + sampleDuration;
                            if (localEnd > maxVideoEndLocal)
                                maxVideoEndLocal = localEnd;

                            if (onProgress is not null && totalExpectedHns > 0)
                            {
                                var pct = (int)Math.Clamp((timelineOffsetHns + localEnd) * 9900L / totalExpectedHns, 0, 9900);
                                onProgress(pct);
                            }
                        }
                        else if (isAudioSample && audioStreamIndex is { } audioIdx)
                        {

                            var localPts = Math.Max(0L, ts - segmentOrigin.Value);
                            var audioCapHns = videoEof && maxVideoEndLocal > 0
                                ? maxVideoEndLocal
                                : Math.Max(maxVideoEndLocal, expectedDurationHns) + defaultFrameDurationHns;

                            if (localPts < audioCapHns)
                            {
                                var outPts = Math.Max(lastWrittenAudioEnd, timelineOffsetHns + localPts);
                                if (outPts > lastWrittenAudioEnd + 208)
                                    WriteAudioSilence(writer, audioIdx, lastWrittenAudioEnd, outPts - lastWrittenAudioEnd, cancellationToken);
                                var dur = sample.GetSampleDuration(out var rawDur) == 0 && rawDur > 0
                                    ? rawDur
                                    : EstimateAudioDuration(sample);
                                CheckHr(sample.SetSampleTime(outPts), "audio.SetSampleTime");
                                if (dur > 0)
                                    _ = sample.SetSampleDuration(dur);
                                CheckHr(writer.WriteSample(audioIdx, sample), "writer.WriteSample(audio)");
                                lastWrittenAudioEnd = outPts + Math.Max(1L, dur);
                            }
                            else if (videoEof && maxVideoEndLocal > 0 && localPts >= maxVideoEndLocal + 5_000_000)
                            {
                                audioEof = true;
                            }
                        }
                    }
                    finally
                    {
                        ReleaseCom(sample);
                    }
                }
                else if ((flags & MfSourceReaderFlagEndOfStream) == 0)
                {
                    consecutiveEmptyReads++;
                    if (consecutiveEmptyReads >= maxConsecutiveEmptyReads)
                        throw new IOException("Leitura interrompida antes do final do segmento.");
                }

                if ((flags & MfSourceReaderFlagEndOfStream) != 0)
                {
                    if (actualIdx == readerVideoIdx)
                        videoEof = true;
                    else if (readAudio && resolvedAudioIdx.HasValue && actualIdx == resolvedAudioIdx.Value)
                        audioEof = true;
                    else if (actualIdx == MfSourceReaderAllStreams)
                    {
                        videoEof = true;
                        audioEof = true;
                    }
                    else if (!videoEof)
                        videoEof = true;
                    else
                        audioEof = true;
                }
            }

            var segmentDurationHns = maxVideoEndLocal > 0
                ? maxVideoEndLocal
                : expectedDurationHns;

            if (frameCount == 0) throw new IOException($"Nenhum quadro em {Path.GetFileName(inputPath)}.");
            return segmentDurationHns;
        }
        finally
        {
            ReleaseCom(reqAudio);
            ReleaseCom(curVideo);
            ReleaseCom(reqVideo);
            ReleaseCom(reader);
            ReleaseCom(readerAttrs);
        }
    }

    private static (uint? VideoIdx, uint? AudioIdx) ResolveSelectedStreamIndices(IMFSourceReader reader, bool wantAudio)
    {
        uint? videoIdx = null;
        uint? audioIdx = null;

        for (uint idx = 0; idx < 32; idx++)
        {
            if (reader.GetStreamSelection(idx, out var selected) < 0)
                break;
            if (!selected)
                continue;

            IMFMediaType? mt = null;
            try
            {
                if (reader.GetCurrentMediaType(idx, out mt) >= 0 && mt is not null
                    && mt.GetGUID(in MfMtMajorType, out var major) >= 0)
                {
                    if (major == MfMediaTypeVideo && videoIdx is null)
                        videoIdx = idx;
                    else if (wantAudio && major == MfMediaTypeAudio && audioIdx is null)
                        audioIdx = idx;
                }
            }
            finally
            {
                ReleaseCom(mt);
            }

            if (videoIdx.HasValue && (!wantAudio || audioIdx.HasValue))
                break;
        }

        return (videoIdx, audioIdx);
    }

    private static void WriteVideoFrame(
        IMFSinkWriter writer,
        uint streamIndex,
        RawImage image,
        long timestampHns,
        long durationHns)
    {
        var frameBytes = checked((uint)(image.Stride * image.Height));
        IMFMediaBuffer? buffer = null;
        IMFSample? sample = null;
        var locked = false;
        try
        {
            CheckHr(MFCreateMemoryBuffer(frameBytes, out buffer), "MFCreateMemoryBuffer");
            CheckHr(buffer.Lock(out var dstPtr, out _, out _), "buffer.Lock");
            locked = true;
            Marshal.Copy(image.Bgra, 0, dstPtr, (int)frameBytes);
            CheckHr(buffer.Unlock(), "buffer.Unlock");
            locked = false;
            CheckHr(buffer.SetCurrentLength(frameBytes), "buffer.SetCurrentLength");

            CheckHr(MFCreateSample(out sample), "MFCreateSample");
            CheckHr(sample.AddBuffer(buffer), "sample.AddBuffer");
            CheckHr(sample.SetSampleTime(timestampHns), "SetSampleTime");
            CheckHr(sample.SetSampleDuration(durationHns), "SetSampleDuration");
            CheckHr(writer.WriteSample(streamIndex, sample), "WriteSample(video)");
        }
        finally
        {
            if (locked && buffer is not null)
                _ = buffer.Unlock();
            ReleaseCom(sample);
            ReleaseCom(buffer);
        }
    }

    private static void WriteAudioSilence(
        IMFSinkWriter writer,
        uint audioStreamIndex,
        long startPtsHns,
        long durationHns,
        CancellationToken cancellationToken)
    {
        const long chunkDurationHns = 500_000; // 50 ms
        var remaining = durationHns;
        var cursor = startPtsHns;

        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sliceHns = Math.Min(remaining, chunkDurationHns);
            var sampleCount = (uint)Math.Max(1L, sliceHns * AudioSampleRate / 10_000_000L);
            var byteCount = sampleCount * AudioBlockAlign;
            var actualDurHns = (long)sampleCount * 10_000_000L / AudioSampleRate;

            IMFMediaBuffer? buffer = null;
            IMFSample? sample = null;
            var locked = false;
            try
            {
                CheckHr(MFCreateMemoryBuffer(byteCount, out buffer), "MFCreateMemoryBuffer(silence)");
                CheckHr(buffer.Lock(out var ptr, out _, out _), "buffer.Lock(silence)");
                locked = true;
                var zeros = new byte[byteCount];
                Marshal.Copy(zeros, 0, ptr, zeros.Length);
                CheckHr(buffer.Unlock(), "buffer.Unlock(silence)");
                locked = false;
                CheckHr(buffer.SetCurrentLength(byteCount), "SetCurrentLength(silence)");

                CheckHr(MFCreateSample(out sample), "MFCreateSample(silence)");
                CheckHr(sample.AddBuffer(buffer), "AddBuffer(silence)");
                CheckHr(sample.SetSampleTime(cursor), "SetSampleTime(silence)");
                CheckHr(sample.SetSampleDuration(actualDurHns), "SetSampleDuration(silence)");
                CheckHr(writer.WriteSample(audioStreamIndex, sample), "WriteSample(silence)");
            }
            finally
            {
                if (locked && buffer is not null)
                    _ = buffer.Unlock();
                ReleaseCom(sample);
                ReleaseCom(buffer);
            }

            cursor += actualDurHns;
            remaining -= Math.Max(1L, actualDurHns);
        }
    }

    private static long EstimateAudioDuration(IMFSample sample)
    {
        if (sample.GetTotalLength(out var bytes) != 0 || bytes == 0)
            return 0;
        return (long)bytes * 10_000_000L / AudioPcmBytesPerSec;
    }

    internal static RawImage FitLetterbox(RawImage source, int targetW, int targetH)
    {
        var scale = Math.Min((double)targetW / source.Width, (double)targetH / source.Height);
        var scaledW = Math.Clamp((int)Math.Round(source.Width * scale), 1, targetW);
        var scaledH = Math.Clamp((int)Math.Round(source.Height * scale), 1, targetH);
        var offsetX = (targetW - scaledW) / 2;
        var offsetY = (targetH - scaledH) / 2;

        var dst = new byte[checked(targetW * targetH * 4)];
        for (var i = 3; i < dst.Length; i += 4)
            dst[i] = 0xFF;

        var src = source.Bgra;
        var srcStride = source.Stride;
        var dstStride = targetW * 4;

        for (var y = 0; y < scaledH; y++)
        {
            var sy = Math.Min(source.Height - 1, (int)((y + 0.5) * source.Height / scaledH));
            var srcRow = sy * srcStride;
            var dstRow = (offsetY + y) * dstStride + offsetX * 4;

            for (var x = 0; x < scaledW; x++)
            {
                var sx = Math.Min(source.Width - 1, (int)((x + 0.5) * source.Width / scaledW));
                var s = srcRow + sx * 4;
                var d = dstRow + x * 4;
                dst[d] = src[s];
                dst[d + 1] = src[s + 1];
                dst[d + 2] = src[s + 2];
                dst[d + 3] = 0xFF;
            }
        }

        return new RawImage(dst, targetW, targetH);
    }

    private static int AlignEven(int value) => value % 2 == 0 ? value : value + 1;

}
