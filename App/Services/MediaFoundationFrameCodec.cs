using System.IO;
using System.Runtime.InteropServices;
using static VideoNR.Services.MediaFoundationInterop;

namespace VideoNR.Services;

internal sealed record VideoProbeInfo(int Width, int Height, double? FrameRate, double DurationSeconds, bool HasAudio);

internal static class MediaFoundationFrameCodec
{
    public static double? TryGetFrameRate(string videoPath) => TryProbe(videoPath)?.FrameRate;

    public static double? TryGetDuration(string videoPath)
    {
        var probe = TryProbe(videoPath);
        return probe is { DurationSeconds: > 0 } ? probe.DurationSeconds : null;
    }

    internal static VideoProbeInfo? TryProbe(string videoPath)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
            return null;

        var mfStarted = false;
        IMFSourceReader? reader = null;
        IMFMediaType? videoType = null;
        IMFMediaType? audioType = null;
        try
        {
            if (MFStartup(MfApiVersion, MfStartupNoSocket) < 0)
                return null;
            mfStarted = true;

            if (MFCreateSourceReaderFromURL(videoPath, null, out reader) < 0 || reader is null)
                return null;

            if (reader.GetNativeMediaType(MfSourceReaderFirstVideoStream, 0, out videoType) < 0 || videoType is null)
            {
                ReleaseCom(videoType);
                videoType = null;
                if (reader.GetCurrentMediaType(MfSourceReaderFirstVideoStream, out videoType) < 0 || videoType is null)
                    return null;
            }

            var width = 0;
            var height = 0;
            if (videoType.GetUINT64(in MfMtFrameSize, out var packedSize) == 0)
            {
                var (w, h) = Unpack2UInt32(packedSize);
                width = (int)w;
                height = (int)h;
            }

            double? fps = null;
            if (videoType.GetUINT64(in MfMtFrameRate, out var packedRate) == 0)
            {
                var (numerator, denominator) = Unpack2UInt32(packedRate);
                if (numerator > 0 && denominator > 0)
                {
                    var candidate = (double)numerator / denominator;
                    if (double.IsFinite(candidate) && candidate > 0)
                        fps = candidate;
                }
            }

            var duration = 0.0;
            if (reader.GetPresentationAttribute(MfSourceReaderMediaSource, in MfPdDuration, out var durationVar) == 0
                && (durationVar.vt == VtUi8 || durationVar.vt == VtI8)
                && durationVar.hVal > 0)
            {
                duration = durationVar.hVal / 10_000_000.0;
            }

            var hasAudio = reader.GetNativeMediaType(MfSourceReaderFirstAudioStream, 0, out audioType) == 0 && audioType is not null;
            return new VideoProbeInfo(width, height, fps, duration, hasAudio);
        }
        catch (Exception error) when (error is IOException or COMException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or DllNotFoundException)
        {
            return null;
        }
        finally
        {
            ReleaseCom(audioType);
            ReleaseCom(videoType);
            ReleaseCom(reader);
            if (mfStarted)
                _ = MFShutdown();
        }
    }

    internal static void EncodeStillVideo(RawImage image, string outputMp4Path, int frameCount = 2, int fps = 30)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Media Foundation está disponível apenas no Windows.");

        image.Validate();
        if (image.Width < 16 || image.Height < 16 || (image.Width & 1) != 0 || (image.Height & 1) != 0)
            throw new ArgumentException("A imagem para codificação deve ter dimensões pares >= 16x16.");
        if (frameCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(frameCount));
        if (fps <= 0)
            throw new ArgumentOutOfRangeException(nameof(fps));

        var folder = Path.GetDirectoryName(outputMp4Path);
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);

        CheckHr(MFStartup(MfApiVersion, MfStartupNoSocket), "MFStartup");
        IMFAttributes? writerAttrs = null;
        IMFSinkWriter? writer = null;
        IMFMediaType? outputType = null;
        IMFMediaType? inputType = null;
        try
        {
            CheckHr(MFCreateAttributes(out writerAttrs, 2), "MFCreateAttributes");
            CheckHr(writerAttrs.SetUINT32(in MfSinkWriterDisableThrottling, 1), "SetUINT32(MF_SINK_WRITER_DISABLE_THROTTLING)");
            CheckHr(writerAttrs.SetUINT32(in MfReadWriteEnableHardwareTransforms, 0), "SetUINT32(MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS)");

            CheckHr(MFCreateSinkWriterFromURL(outputMp4Path, IntPtr.Zero, writerAttrs, out writer), "MFCreateSinkWriterFromURL");

            var packedSize = Pack2UInt32((uint)image.Width, (uint)image.Height);
            var packedRate = Pack2UInt32((uint)fps, 1u);
            var packedPar = Pack2UInt32(1u, 1u);
            var bitrate = (uint)Math.Clamp((long)image.Width * image.Height * 24L, 12_000_000L, 60_000_000L);

            CheckHr(MFCreateMediaType(out outputType), "MFCreateMediaType(out)");
            CheckHr(outputType.SetGUID(in MfMtMajorType, in MfMediaTypeVideo), "out.SetGUID(MajorType)");
            CheckHr(outputType.SetGUID(in MfMtSubtype, in MfVideoFormatH264), "out.SetGUID(H264)");
            CheckHr(outputType.SetUINT32(in MfMtAvgBitrate, bitrate), "out.SetUINT32(Bitrate)");
            CheckHr(outputType.SetUINT32(in MfMtInterlaceMode, MfVideoInterlaceProgressive), "out.SetUINT32(Interlace)");
            CheckHr(outputType.SetUINT64(in MfMtFrameSize, packedSize), "out.SetUINT64(FrameSize)");
            CheckHr(outputType.SetUINT64(in MfMtFrameRate, packedRate), "out.SetUINT64(FrameRate)");
            CheckHr(outputType.SetUINT64(in MfMtPixelAspectRatio, packedPar), "out.SetUINT64(PAR)");
            CheckHr(writer.AddStream(outputType, out var streamIndex), "AddStream");

            CheckHr(MFCreateMediaType(out inputType), "MFCreateMediaType(in)");
            CheckHr(inputType.SetGUID(in MfMtMajorType, in MfMediaTypeVideo), "in.SetGUID(MajorType)");
            CheckHr(inputType.SetGUID(in MfMtSubtype, in MfVideoFormatRgb32), "in.SetGUID(RGB32)");
            CheckHr(inputType.SetUINT32(in MfMtInterlaceMode, MfVideoInterlaceProgressive), "in.SetUINT32(Interlace)");
            CheckHr(inputType.SetUINT64(in MfMtFrameSize, packedSize), "in.SetUINT64(FrameSize)");
            CheckHr(inputType.SetUINT64(in MfMtFrameRate, packedRate), "in.SetUINT64(FrameRate)");
            CheckHr(inputType.SetUINT64(in MfMtPixelAspectRatio, packedPar), "in.SetUINT64(PAR)");
            CheckHr(inputType.SetUINT32(in MfMtDefaultStride, unchecked((uint)image.Stride)), "in.SetUINT32(DefaultStride)");
            CheckHr(writer.SetInputMediaType(streamIndex, inputType, null), "SetInputMediaType");

            CheckHr(writer.BeginWriting(), "BeginWriting");

            var frameBytes = checked((uint)(image.Stride * image.Height));
            var frameDurationHns = 10_000_000L / fps;

            for (var i = 0; i < frameCount; i++)
            {
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
                    CheckHr(sample.SetSampleTime(i * frameDurationHns), "sample.SetSampleTime");
                    CheckHr(sample.SetSampleDuration(frameDurationHns), "sample.SetSampleDuration");
                    CheckHr(writer.WriteSample(streamIndex, sample), "writer.WriteSample");
                }
                finally
                {
                    if (locked && buffer is not null)
                        _ = buffer.Unlock();
                    ReleaseCom(sample);
                    ReleaseCom(buffer);
                }
            }

            CheckHr(writer.Finalize(), "Finalize");
        }
        finally
        {
            ReleaseCom(inputType);
            ReleaseCom(outputType);
            ReleaseCom(writer);
            ReleaseCom(writerAttrs);
            _ = MFShutdown();
        }
    }

    internal static RawImage DecodeFrame(string videoPath, double seekSeconds = 0)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Media Foundation está disponível apenas no Windows.");
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
            throw new FileNotFoundException("Arquivo de vídeo não encontrado para decodificação.", videoPath);

        CheckHr(MFStartup(MfApiVersion, MfStartupNoSocket), "MFStartup");
        IMFAttributes? readerAttrs = null;
        IMFSourceReader? reader = null;
        IMFMediaType? requestedType = null;
        IMFMediaType? currentType = null;
        IMFSample? selectedSample = null;
        try
        {
            CheckHr(MFCreateAttributes(out readerAttrs, 1), "MFCreateAttributes");
            CheckHr(readerAttrs.SetUINT32(in MfSourceReaderEnableVideoProcessing, 1), "SetUINT32(MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING)");

            CheckHr(MFCreateSourceReaderFromURL(videoPath, readerAttrs, out reader), "MFCreateSourceReaderFromURL");
            CheckHr(reader.SetStreamSelection(MfSourceReaderAllStreams, false), "DeselectAllStreams");
            CheckHr(reader.SetStreamSelection(MfSourceReaderFirstVideoStream, true), "SelectFirstVideoStream");

            CheckHr(MFCreateMediaType(out requestedType), "MFCreateMediaType(RGB32)");
            CheckHr(requestedType.SetGUID(in MfMtMajorType, in MfMediaTypeVideo), "SetGUID(MajorType)");
            CheckHr(requestedType.SetGUID(in MfMtSubtype, in MfVideoFormatRgb32), "SetGUID(RGB32)");
            CheckHr(reader.SetCurrentMediaType(MfSourceReaderFirstVideoStream, IntPtr.Zero, requestedType), "SetCurrentMediaType");

            var targetHns = seekSeconds > 0 && double.IsFinite(seekSeconds)
                ? (long)Math.Round(seekSeconds * 10_000_000.0)
                : 0L;

            if (targetHns > 0)
            {
                var pos = new PropVariant { vt = VtI8, hVal = targetHns };
                CheckHr(reader.SetCurrentPosition(in Guid.Empty, in pos), "SetCurrentPosition");
            }

            const int maxConsecutiveEmptyReads = 128;
            const int maxTotalReads = 4096;
            var emptyReads = 0;

            for (var totalReads = 0; totalReads < maxTotalReads; totalReads++)
            {
                CheckHr(
                    reader.ReadSample(MfSourceReaderFirstVideoStream, 0, out _, out var flags, out var timestamp, out var sample),
                    "ReadSample");

                if ((flags & MfSourceReaderFlagError) != 0)
                {
                    ReleaseCom(sample);
                    throw new IOException("Erro do Media Foundation ao ler o frame de vídeo.");
                }

                if (sample is not null)
                {
                    emptyReads = 0;
                    if (targetHns <= 0 || timestamp >= targetHns - 150_000)
                    {
                        ReleaseCom(selectedSample);
                        selectedSample = sample;
                        break;
                    }

                    ReleaseCom(selectedSample);
                    selectedSample = sample;
                    continue;
                }

                if ((flags & MfSourceReaderFlagEndOfStream) != 0)
                    break;

                if (++emptyReads >= maxConsecutiveEmptyReads)
                    break;
            }

            if (selectedSample is null)
                throw new IOException("Nenhum frame de vídeo pôde ser decodificado.");

            CheckHr(reader.GetCurrentMediaType(MfSourceReaderFirstVideoStream, out currentType), "GetCurrentMediaType");
            var layout = ReadVideoLayout(currentType);
            return ExtractBgraFromSample(selectedSample, layout.BufH, layout.DefaultStride, layout.CropX, layout.CropY, layout.CropW, layout.CropH);
        }
        finally
        {
            ReleaseCom(selectedSample);
            ReleaseCom(currentType);
            ReleaseCom(requestedType);
            ReleaseCom(reader);
            ReleaseCom(readerAttrs);
            _ = MFShutdown();
        }
    }

    internal readonly record struct VideoSampleLayout(int BufW, int BufH, int DefaultStride, int CropX, int CropY, int CropW, int CropH);

    internal static VideoSampleLayout ReadVideoLayout(IMFMediaType currentType)
    {
        CheckHr(currentType.GetUINT64(in MfMtFrameSize, out var packedSize), "GetUINT64(FrameSize)");
        var (rawW, rawH) = Unpack2UInt32(packedSize);
        var bufW = (int)rawW;
        var bufH = (int)rawH;
        if (bufW <= 0 || bufH <= 0)
            throw new IOException($"Dimensões inválidas retornadas pelo decodificador ({bufW}x{bufH}).");

        var defaultStride = bufW * 4;
        if (currentType.GetUINT32(in MfMtDefaultStride, out var rawStride) == 0)
        {
            var signed = unchecked((int)rawStride);
            if (signed != 0)
                defaultStride = signed;
        }

        var cropX = 0;
        var cropY = 0;
        var cropW = bufW;
        var cropH = bufH;
        var aperturePtr = Marshal.AllocHGlobal(16);
        try
        {
            if (currentType.GetBlob(in MfMtMinimumDisplayAperture, aperturePtr, 16, out var blobSize) == 0 && blobSize >= 16)
            {
                var aperture = new byte[16];
                Marshal.Copy(aperturePtr, aperture, 0, 16);
                int ox = BitConverter.ToInt16(aperture, 2);
                int oy = BitConverter.ToInt16(aperture, 6);
                var aw = BitConverter.ToInt32(aperture, 8);
                var ah = BitConverter.ToInt32(aperture, 12);
                if (ox >= 0 && oy >= 0 && aw > 0 && ah > 0 && ox + aw <= bufW && oy + ah <= bufH)
                {
                    cropX = ox;
                    cropY = oy;
                    cropW = aw;
                    cropH = ah;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(aperturePtr);
        }

        return new VideoSampleLayout(bufW, bufH, defaultStride, cropX, cropY, cropW, cropH);
    }

    internal static RawImage ExtractBgraFromSample(
        IMFSample sample,
        int bufH,
        int defaultStride,
        int cropX,
        int cropY,
        int cropW,
        int cropH)
    {
        IMFMediaBuffer? buffer = null;
        IMF2DBuffer? buffer2d = null;
        var locked2d = false;
        var locked1d = false;
        try
        {
            CheckHr(sample.ConvertToContiguousBuffer(out buffer), "ConvertToContiguousBuffer");

            IntPtr scanline0;
            int pitch;
            buffer2d = buffer as IMF2DBuffer;
            if (buffer2d is not null && buffer2d.Lock2D(out scanline0, out pitch) == 0)
            {
                locked2d = true;
            }
            else
            {
                CheckHr(buffer.Lock(out var rawPtr, out _, out var currentLength), "buffer.Lock");
                locked1d = true;
                pitch = defaultStride;
                var absPitch = Math.Abs(pitch);
                if ((long)absPitch * bufH > currentLength)
                    throw new IOException("Tamanho do buffer de frame menor do que o esperado.");
                scanline0 = pitch >= 0
                    ? rawPtr
                    : IntPtr.Add(rawPtr, checked((bufH - 1) * absPitch));
            }

            var dstStride = cropW * 4;
            var dst = new byte[checked(dstStride * cropH)];
            var xOffsetBytes = cropX * 4L;
            for (var row = 0; row < cropH; row++)
            {
                var srcRow = IntPtr.Add(scanline0, checked((int)((cropY + row) * (long)pitch + xOffsetBytes)));
                Marshal.Copy(srcRow, dst, row * dstStride, dstStride);
            }

            for (var i = 3; i < dst.Length; i += 4)
                dst[i] = 0xFF;

            return new RawImage(dst, cropW, cropH);
        }
        finally
        {
            if (locked2d && buffer2d is not null)
                _ = buffer2d.Unlock2D();
            if (locked1d && buffer is not null)
                _ = buffer.Unlock();
            ReleaseCom(buffer);
        }
    }
}
