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

    internal static FrameTimes ReadFrameTimes(string path, CancellationToken token)
    {
        CheckHr(MFStartup(MfApiVersion, MfStartupNoSocket), "MFStartup");
        IMFSourceReader? reader = null;
        try
        {
            CheckHr(MFCreateSourceReaderFromURL(path, null, out reader), "Open source");
            CheckHr(reader.SetStreamSelection(MfSourceReaderAllStreams, false), "Deselect streams");
            CheckHr(reader.SetStreamSelection(MfSourceReaderFirstVideoStream, true), "Select video");
            var times = new List<long>();
            var emptyReads = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var hr = reader.ReadSample(MfSourceReaderFirstVideoStream, 0, out _, out var flags, out var pts, out var sample);
                try
                {
                    CheckHr(hr, "Read presentation timestamp");
                    if ((flags & MfSourceReaderFlagError) != 0)
                        throw new IOException("Falha na leitura dos tempos dos quadros.");
                    if (sample is not null)
                    {
                        times.Add(pts);
                        emptyReads = 0;
                    }
                    if ((flags & MfSourceReaderFlagEndOfStream) != 0) break;
                    if (sample is null && ++emptyReads > 128)
                        throw new IOException("A origem parou de fornecer quadros.");
                }
                finally { ReleaseCom(sample); }
            }
            if (times.Count == 0) throw new IOException("Nenhum quadro na origem.");
            return new FrameTimes(times);
        }
        finally
        {
            ReleaseCom(reader);
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
                    if (targetHns <= 0 || timestamp >= targetHns)
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
