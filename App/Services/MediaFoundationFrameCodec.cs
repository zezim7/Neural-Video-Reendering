using System.IO;
using System.Runtime.InteropServices;

namespace VideoNR.Services;

internal static class MediaFoundationFrameCodec
{
    private const uint MfApiVersion = 0x00020070;
    private const uint MfStartupNoSocket = 0x00000001;
    private const uint MfSourceReaderAllStreams = 0xFFFFFFFE;
    private const uint MfSourceReaderFirstVideoStream = 0xFFFFFFFC;
    private const uint MfSourceReaderFlagError = 0x00000001;
    private const uint MfSourceReaderFlagEndOfStream = 0x00000002;
    private const uint MfSourceReaderFlagStreamTick = 0x00000100;
    private const uint MfVideoInterlaceProgressive = 2;
    private const ushort VtI8 = 20;

    private static readonly Guid MfMediaTypeVideo = new("73646976-0000-0010-8000-00aa00389b71");
    private static readonly Guid MfVideoFormatH264 = new("34363248-0000-0010-8000-00aa00389b71");
    private static readonly Guid MfVideoFormatRgb32 = new("00000016-0000-0010-8000-00aa00389b71");

    private static readonly Guid MfMtMajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    private static readonly Guid MfMtSubtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    private static readonly Guid MfMtFrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
    private static readonly Guid MfMtFrameRate = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    private static readonly Guid MfMtPixelAspectRatio = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    private static readonly Guid MfMtInterlaceMode = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    private static readonly Guid MfMtAvgBitrate = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    private static readonly Guid MfMtDefaultStride = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    private static readonly Guid MfMtMinimumDisplayAperture = new("d7388766-18fe-48c6-a177-ee894867c8c4");

    private static readonly Guid MfSinkWriterDisableThrottling = new("08b845d8-2b74-4afe-9d53-be16d2d5ae4f");
    private static readonly Guid MfReadWriteEnableHardwareTransforms = new("a634a91c-822b-41b9-a494-4de4643612b0");
    private static readonly Guid MfSourceReaderEnableVideoProcessing = new("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d");

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

            while (true)
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

                if ((flags & MfSourceReaderFlagStreamTick) != 0)
                    continue;
            }

            if (selectedSample is null)
                throw new IOException("Nenhum frame de vídeo pôde ser decodificado.");

            CheckHr(reader.GetCurrentMediaType(MfSourceReaderFirstVideoStream, out currentType), "GetCurrentMediaType");
            CheckHr(currentType.GetUINT64(in MfMtFrameSize, out var packedSize), "GetUINT64(FrameSize)");
            var bufW = (int)(packedSize >> 32);
            var bufH = (int)(packedSize & 0xFFFFFFFF);
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

            return ExtractBgraFromSample(selectedSample, bufH, defaultStride, cropX, cropY, cropW, cropH);
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

    private static RawImage ExtractBgraFromSample(
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

    private static ulong Pack2UInt32(uint high, uint low) => ((ulong)high << 32) | low;

    private static void CheckHr(int hr, string operation)
    {
        if (hr < 0)
            throw new IOException($"Media Foundation falhou em {operation} (0x{unchecked((uint)hr):X8}).", Marshal.GetExceptionForHR(hr));
    }

    private static void ReleaseCom(object? comObject)
    {
        if (OperatingSystem.IsWindows() && comObject is not null && Marshal.IsComObject(comObject))
            _ = Marshal.FinalReleaseComObject(comObject);
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public long hVal;
    }

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFStartup(uint version, uint dwFlags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFShutdown();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateAttributes(out IMFAttributes ppMFAttributes, uint cInitialSize);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateMediaType(out IMFMediaType ppMFType);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateSample(out IMFSample ppIMFSample);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateMemoryBuffer(uint cbMaxLength, out IMFMediaBuffer ppBuffer);

    [DllImport("mfreadwrite.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int MFCreateSinkWriterFromURL(
        [MarshalAs(UnmanagedType.LPWStr)] string? pwszOutputURL,
        IntPtr pByteStream,
        IMFAttributes? pAttributes,
        out IMFSinkWriter ppSinkWriter);

    [DllImport("mfreadwrite.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int MFCreateSourceReaderFromURL(
        [MarshalAs(UnmanagedType.LPWStr)] string pwszURL,
        IMFAttributes? pAttributes,
        out IMFSourceReader ppSourceReader);

    [ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFAttributes
    {
        [PreserveSig] int GetItem(in Guid guidKey, IntPtr pValue);
        [PreserveSig] int GetItemType(in Guid guidKey, out uint pType);
        [PreserveSig] int CompareItem(in Guid guidKey, IntPtr value, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        [PreserveSig] int Compare(IMFAttributes pTheirs, uint matchType, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        [PreserveSig] int GetUINT32(in Guid guidKey, out uint punValue);
        [PreserveSig] int GetUINT64(in Guid guidKey, out ulong punValue);
        [PreserveSig] int GetDouble(in Guid guidKey, out double pfValue);
        [PreserveSig] int GetGUID(in Guid guidKey, out Guid pguidValue);
        [PreserveSig] int GetStringLength(in Guid guidKey, out uint pcchLength);
        [PreserveSig] int GetString(in Guid guidKey, IntPtr pwszValue, uint cchBufSize, out uint pcchLength);
        [PreserveSig] int GetAllocatedString(in Guid guidKey, out IntPtr ppwszValue, out uint pcchLength);
        [PreserveSig] int GetBlobSize(in Guid guidKey, out uint pcbBlobSize);
        [PreserveSig] int GetBlob(in Guid guidKey, IntPtr pBuf, uint cbBufSize, out uint pcbBlobSize);
        [PreserveSig] int GetAllocatedBlob(in Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
        [PreserveSig] int GetUnknown(in Guid guidKey, in Guid riid, out IntPtr ppv);
        [PreserveSig] int SetItem(in Guid guidKey, IntPtr value);
        [PreserveSig] int DeleteItem(in Guid guidKey);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(in Guid guidKey, uint unValue);
        [PreserveSig] int SetUINT64(in Guid guidKey, ulong unValue);
        [PreserveSig] int SetDouble(in Guid guidKey, double fValue);
        [PreserveSig] int SetGUID(in Guid guidKey, in Guid guidValue);
        [PreserveSig] int SetString(in Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        [PreserveSig] int SetBlob(in Guid guidKey, IntPtr pBuf, uint cbBufSize);
        [PreserveSig] int SetUnknown(in Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object? pUnknown);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out uint pcItems);
        [PreserveSig] int GetItemByIndex(uint unIndex, out Guid pguidKey, IntPtr pValue);
        [PreserveSig] int CopyAllItems(IMFAttributes pDest);
    }

    [ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFMediaType
    {
        [PreserveSig] int GetItem(in Guid guidKey, IntPtr pValue);
        [PreserveSig] int GetItemType(in Guid guidKey, out uint pType);
        [PreserveSig] int CompareItem(in Guid guidKey, IntPtr value, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        [PreserveSig] int Compare(IMFAttributes pTheirs, uint matchType, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        [PreserveSig] int GetUINT32(in Guid guidKey, out uint punValue);
        [PreserveSig] int GetUINT64(in Guid guidKey, out ulong punValue);
        [PreserveSig] int GetDouble(in Guid guidKey, out double pfValue);
        [PreserveSig] int GetGUID(in Guid guidKey, out Guid pguidValue);
        [PreserveSig] int GetStringLength(in Guid guidKey, out uint pcchLength);
        [PreserveSig] int GetString(in Guid guidKey, IntPtr pwszValue, uint cchBufSize, out uint pcchLength);
        [PreserveSig] int GetAllocatedString(in Guid guidKey, out IntPtr ppwszValue, out uint pcchLength);
        [PreserveSig] int GetBlobSize(in Guid guidKey, out uint pcbBlobSize);
        [PreserveSig] int GetBlob(in Guid guidKey, IntPtr pBuf, uint cbBufSize, out uint pcbBlobSize);
        [PreserveSig] int GetAllocatedBlob(in Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
        [PreserveSig] int GetUnknown(in Guid guidKey, in Guid riid, out IntPtr ppv);
        [PreserveSig] int SetItem(in Guid guidKey, IntPtr value);
        [PreserveSig] int DeleteItem(in Guid guidKey);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(in Guid guidKey, uint unValue);
        [PreserveSig] int SetUINT64(in Guid guidKey, ulong unValue);
        [PreserveSig] int SetDouble(in Guid guidKey, double fValue);
        [PreserveSig] int SetGUID(in Guid guidKey, in Guid guidValue);
        [PreserveSig] int SetString(in Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        [PreserveSig] int SetBlob(in Guid guidKey, IntPtr pBuf, uint cbBufSize);
        [PreserveSig] int SetUnknown(in Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object? pUnknown);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out uint pcItems);
        [PreserveSig] int GetItemByIndex(uint unIndex, out Guid pguidKey, IntPtr pValue);
        [PreserveSig] int CopyAllItems(IMFAttributes pDest);
        [PreserveSig] int GetMajorType(out Guid pguidMajorType);
        [PreserveSig] int IsCompressedFormat([MarshalAs(UnmanagedType.Bool)] out bool pfCompressed);
        [PreserveSig] int IsEqual(IMFMediaType pIMediaType, out uint pdwFlags);
        [PreserveSig] int GetRepresentation(Guid guidRepresentation, out IntPtr ppvRepresentation);
        [PreserveSig] int FreeRepresentation(Guid guidRepresentation, IntPtr pvRepresentation);
    }

    [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSample
    {
        [PreserveSig] int GetItem(in Guid guidKey, IntPtr pValue);
        [PreserveSig] int GetItemType(in Guid guidKey, out uint pType);
        [PreserveSig] int CompareItem(in Guid guidKey, IntPtr value, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        [PreserveSig] int Compare(IMFAttributes pTheirs, uint matchType, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        [PreserveSig] int GetUINT32(in Guid guidKey, out uint punValue);
        [PreserveSig] int GetUINT64(in Guid guidKey, out ulong punValue);
        [PreserveSig] int GetDouble(in Guid guidKey, out double pfValue);
        [PreserveSig] int GetGUID(in Guid guidKey, out Guid pguidValue);
        [PreserveSig] int GetStringLength(in Guid guidKey, out uint pcchLength);
        [PreserveSig] int GetString(in Guid guidKey, IntPtr pwszValue, uint cchBufSize, out uint pcchLength);
        [PreserveSig] int GetAllocatedString(in Guid guidKey, out IntPtr ppwszValue, out uint pcchLength);
        [PreserveSig] int GetBlobSize(in Guid guidKey, out uint pcbBlobSize);
        [PreserveSig] int GetBlob(in Guid guidKey, IntPtr pBuf, uint cbBufSize, out uint pcbBlobSize);
        [PreserveSig] int GetAllocatedBlob(in Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
        [PreserveSig] int GetUnknown(in Guid guidKey, in Guid riid, out IntPtr ppv);
        [PreserveSig] int SetItem(in Guid guidKey, IntPtr value);
        [PreserveSig] int DeleteItem(in Guid guidKey);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(in Guid guidKey, uint unValue);
        [PreserveSig] int SetUINT64(in Guid guidKey, ulong unValue);
        [PreserveSig] int SetDouble(in Guid guidKey, double fValue);
        [PreserveSig] int SetGUID(in Guid guidKey, in Guid guidValue);
        [PreserveSig] int SetString(in Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        [PreserveSig] int SetBlob(in Guid guidKey, IntPtr pBuf, uint cbBufSize);
        [PreserveSig] int SetUnknown(in Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object? pUnknown);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out uint pcItems);
        [PreserveSig] int GetItemByIndex(uint unIndex, out Guid pguidKey, IntPtr pValue);
        [PreserveSig] int CopyAllItems(IMFAttributes pDest);
        [PreserveSig] int GetSampleFlags(out uint pdwSampleFlags);
        [PreserveSig] int SetSampleFlags(uint dwSampleFlags);
        [PreserveSig] int GetSampleTime(out long phnsSampleTime);
        [PreserveSig] int SetSampleTime(long hnsSampleTime);
        [PreserveSig] int GetSampleDuration(out long phnsSampleDuration);
        [PreserveSig] int SetSampleDuration(long hnsSampleDuration);
        [PreserveSig] int GetBufferCount(out uint pdwBufferCount);
        [PreserveSig] int GetBufferByIndex(uint dwIndex, out IMFMediaBuffer ppBuffer);
        [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer ppBuffer);
        [PreserveSig] int AddBuffer(IMFMediaBuffer pBuffer);
        [PreserveSig] int RemoveBufferByIndex(uint dwIndex);
        [PreserveSig] int RemoveAllBuffers();
        [PreserveSig] int GetTotalLength(out uint pcbTotalLength);
        [PreserveSig] int CopyToBuffer(IMFMediaBuffer pBuffer);
    }

    [ComImport, Guid("045fa593-8799-42b8-bc8d-8968c6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFMediaBuffer
    {
        [PreserveSig] int Lock(out IntPtr ppbBuffer, out uint pcbMaxLength, out uint pcbCurrentLength);
        [PreserveSig] int Unlock();
        [PreserveSig] int GetCurrentLength(out uint pcbCurrentLength);
        [PreserveSig] int SetCurrentLength(uint cbCurrentLength);
        [PreserveSig] int GetMaxLength(out uint pcbMaxLength);
    }

    [ComImport, Guid("7dc9d5f9-9ed9-44ec-9bbf-0600bb589fbb"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMF2DBuffer
    {
        [PreserveSig] int Lock2D(out IntPtr ppbScanline0, out int plPitch);
        [PreserveSig] int Unlock2D();
        [PreserveSig] int GetScanline0AndPitch(out IntPtr pbScanline0, out int plPitch);
        [PreserveSig] int IsContiguousFormat([MarshalAs(UnmanagedType.Bool)] out bool pfIsContiguous);
        [PreserveSig] int GetContiguousLength(out uint pcbLength);
        [PreserveSig] int ContiguousCopyTo(IntPtr pbDestBuffer, uint cbDestBuffer);
        [PreserveSig] int ContiguousCopyFrom(IntPtr pbSrcBuffer, uint cbSrcBuffer);
    }

    [ComImport, Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSinkWriter
    {
        [PreserveSig] int AddStream(IMFMediaType pTargetMediaType, out uint pdwStreamIndex);
        [PreserveSig] int SetInputMediaType(uint dwStreamIndex, IMFMediaType pInputMediaType, IMFAttributes? pEncodingParameters);
        [PreserveSig] int BeginWriting();
        [PreserveSig] int WriteSample(uint dwStreamIndex, IMFSample pSample);
        [PreserveSig] int SendStreamTick(uint dwStreamIndex, long llTimestamp);
        [PreserveSig] int PlaceMarker(uint dwStreamIndex, IntPtr pvContext);
        [PreserveSig] int NotifyEndOfSegment(uint dwStreamIndex);
        [PreserveSig] int Flush(uint dwStreamIndex);
        [PreserveSig] int Finalize();
        [PreserveSig] int GetServiceForStream(uint dwStreamIndex, in Guid guidService, in Guid riid, out IntPtr ppvObject);
        [PreserveSig] int GetStatistics(uint dwStreamIndex, IntPtr pStats);
    }

    [ComImport, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSourceReader
    {
        [PreserveSig] int GetStreamSelection(uint dwStreamIndex, [MarshalAs(UnmanagedType.Bool)] out bool pfSelected);
        [PreserveSig] int SetStreamSelection(uint dwStreamIndex, [MarshalAs(UnmanagedType.Bool)] bool fSelected);
        [PreserveSig] int GetNativeMediaType(uint dwStreamIndex, uint dwMediaTypeIndex, out IMFMediaType ppMediaType);
        [PreserveSig] int GetCurrentMediaType(uint dwStreamIndex, out IMFMediaType ppMediaType);
        [PreserveSig] int SetCurrentMediaType(uint dwStreamIndex, IntPtr pdwReserved, IMFMediaType pMediaType);
        [PreserveSig] int SetCurrentPosition(in Guid guidTimeFormat, in PropVariant varPosition);
        [PreserveSig] int ReadSample(
            uint dwStreamIndex,
            uint dwControlFlags,
            out uint pdwActualStreamIndex,
            out uint pdwStreamFlags,
            out long pllTimestamp,
            out IMFSample? ppSample);
        [PreserveSig] int Flush(uint dwStreamIndex);
        [PreserveSig] int GetServiceForStream(uint dwStreamIndex, in Guid guidService, in Guid riid, out IntPtr ppvObject);
        [PreserveSig] int GetPresentationAttribute(uint dwStreamIndex, in Guid guidAttribute, out PropVariant pvarAttribute);
    }
}
