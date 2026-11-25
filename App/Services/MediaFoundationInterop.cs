using System.IO;
using System.Runtime.InteropServices;

namespace VideoNR.Services;

internal static class MediaFoundationInterop
{
    internal const uint MfApiVersion = 0x00020070;
    internal const uint MfStartupNoSocket = 0x00000001;
    internal const uint MfSourceReaderAllStreams = 0xFFFFFFFE;
    internal const uint MfSourceReaderFirstVideoStream = 0xFFFFFFFC;
    internal const uint MfSourceReaderFirstAudioStream = 0xFFFFFFFD;
    internal const uint MfSourceReaderMediaSource = 0xFFFFFFFF;
    internal const uint MfSourceReaderFlagError = 0x00000001;
    internal const uint MfSourceReaderFlagEndOfStream = 0x00000002;
    internal const uint MfSourceReaderFlagStreamTick = 0x00000100;
    internal const uint MfVideoInterlaceProgressive = 2;
    internal const ushort VtI8 = 20;
    internal const ushort VtUi8 = 21;

    internal static readonly Guid MfMediaTypeVideo = new("73646976-0000-0010-8000-00aa00389b71");
    internal static readonly Guid MfMediaTypeAudio = new("73647561-0000-0010-8000-00aa00389b71");
    internal static readonly Guid MfVideoFormatH264 = new("34363248-0000-0010-8000-00aa00389b71");
    internal static readonly Guid MfVideoFormatHevc = new("43564548-0000-0010-8000-00aa00389b71");
    internal static readonly Guid MfVideoFormatRgb32 = new("00000016-0000-0010-8000-00aa00389b71");
    internal static readonly Guid MfAudioFormatPcm = new("00000001-0000-0010-8000-00aa00389b71");
    internal static readonly Guid MfAudioFormatAac = new("00001610-0000-0010-8000-00aa00389b71");

    internal static readonly Guid MfMtMajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    internal static readonly Guid MfMtSubtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    internal static readonly Guid MfMtFrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
    internal static readonly Guid MfMtFrameRate = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    internal static readonly Guid MfMtPixelAspectRatio = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    internal static readonly Guid MfMtInterlaceMode = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    internal static readonly Guid MfMtAvgBitrate = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    internal static readonly Guid MfMtDefaultStride = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    internal static readonly Guid MfMtMinimumDisplayAperture = new("d7388766-18fe-48c6-a177-ee894867c8c4");
    internal static readonly Guid MfMtAudioNumChannels = new("37e48bf5-645e-4c5b-89de-ada9e29b696a");
    internal static readonly Guid MfMtAudioSamplesPerSecond = new("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
    internal static readonly Guid MfMtAudioBitsPerSample = new("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");
    internal static readonly Guid MfMtAudioAvgBytesPerSecond = new("1aab75c8-cfef-451c-ab95-ac034b8e1731");
    internal static readonly Guid MfMtAudioBlockAlignment = new("322de230-9eeb-43bd-ab7a-ff412251541d");
    internal static readonly Guid MfPdDuration = new("6c990d33-bb8e-477a-8598-0d5d96fcd88a");

    internal static readonly Guid MfSinkWriterDisableThrottling = new("08b845d8-2b74-4afe-9d53-be16d2d5ae4f");
    internal static readonly Guid MfReadWriteEnableHardwareTransforms = new("a634a91c-822b-41b9-a494-4de4643612b0");
    internal static readonly Guid MfSourceReaderEnableVideoProcessing = new("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d");

    internal static ulong Pack2UInt32(uint high, uint low) => ((ulong)high << 32) | low;

    internal static (uint High, uint Low) Unpack2UInt32(ulong packed) =>
        ((uint)(packed >> 32), (uint)(packed & 0xFFFFFFFF));

    internal static void CheckHr(int hr, string operation)
    {
        if (hr < 0)
            throw new IOException($"Media Foundation falhou em {operation} (0x{unchecked((uint)hr):X8}).", Marshal.GetExceptionForHR(hr));
    }

    internal static void ReleaseCom(object? comObject)
    {
        if (OperatingSystem.IsWindows() && comObject is not null && Marshal.IsComObject(comObject))
            _ = Marshal.FinalReleaseComObject(comObject);
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public long hVal;
    }

    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFStartup(uint version, uint dwFlags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFShutdown();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFCreateAttributes(out IMFAttributes ppMFAttributes, uint cInitialSize);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFCreateMediaType(out IMFMediaType ppMFType);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFCreateSample(out IMFSample ppIMFSample);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFCreateMemoryBuffer(uint cbMaxLength, out IMFMediaBuffer ppBuffer);

    [DllImport("mfreadwrite.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    internal static extern int MFCreateSinkWriterFromURL(
        [MarshalAs(UnmanagedType.LPWStr)] string? pwszOutputURL,
        IntPtr pByteStream,
        IMFAttributes? pAttributes,
        out IMFSinkWriter ppSinkWriter);

    [DllImport("mfreadwrite.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    internal static extern int MFCreateSourceReaderFromURL(
        [MarshalAs(UnmanagedType.LPWStr)] string pwszURL,
        IMFAttributes? pAttributes,
        out IMFSourceReader ppSourceReader);

    [ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFAttributes
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
    internal interface IMFMediaType
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
    internal interface IMFSample
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
    internal interface IMFMediaBuffer
    {
        [PreserveSig] int Lock(out IntPtr ppbBuffer, out uint pcbMaxLength, out uint pcbCurrentLength);
        [PreserveSig] int Unlock();
        [PreserveSig] int GetCurrentLength(out uint pcbCurrentLength);
        [PreserveSig] int SetCurrentLength(uint cbCurrentLength);
        [PreserveSig] int GetMaxLength(out uint pcbMaxLength);
    }

    [ComImport, Guid("7dc9d5f9-9ed9-44ec-9bbf-0600bb589fbb"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMF2DBuffer
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
    internal interface IMFSinkWriter
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
    internal interface IMFSourceReader
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
