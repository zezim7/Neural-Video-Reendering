using System.IO;
using System.Runtime.InteropServices;
using VideoNR.Services;
using static VideoNR.Services.MediaFoundationInterop;

internal static class VideoFixture
{
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
}
