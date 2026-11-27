using System.Runtime.InteropServices;
using static VideoNR.Services.MediaFoundationInterop;

internal static class AudioFixture
{
    internal static double FindToneOnset(string path)
    {
        CheckHr(MFStartup(MfApiVersion, MfStartupNoSocket), "test MFStartup");
        IMFSourceReader? reader = null;
        IMFMediaType? type = null;
        try
        {
            CheckHr(MFCreateSourceReaderFromURL(path, null, out reader), "open rendered audio");
            CheckHr(reader.SetStreamSelection(MfSourceReaderAllStreams, false), "deselect streams");
            CheckHr(reader.SetStreamSelection(MfSourceReaderFirstAudioStream, true), "select audio");
            CheckHr(MFCreateMediaType(out type), "PCM type");
            CheckHr(type.SetGUID(in MfMtMajorType, in MfMediaTypeAudio), "audio type");
            CheckHr(type.SetGUID(in MfMtSubtype, in MfAudioFormatPcm), "PCM");
            CheckHr(type.SetUINT32(in MfMtAudioSamplesPerSecond, 48000), "sample rate");
            CheckHr(type.SetUINT32(in MfMtAudioBitsPerSample, 16), "sample size");
            CheckHr(type.SetUINT32(in MfMtAudioNumChannels, 2), "stereo");
            CheckHr(reader.SetCurrentMediaType(MfSourceReaderFirstAudioStream, IntPtr.Zero, type), "select PCM");
            for (var i = 0; i < 2048; i++)
            {
                CheckHr(reader.ReadSample(MfSourceReaderFirstAudioStream, 0, out _, out var flags, out var pts, out var sample), "decode PCM");
                IMFMediaBuffer? buffer = null;
                bool locked = false;
                try
                {
                    if (sample is not null)
                    {
                        CheckHr(sample.ConvertToContiguousBuffer(out buffer), "audio buffer");
                        CheckHr(buffer.Lock(out var data, out _, out var bytes), "lock PCM");
                        locked = true;
                        var pcm = new short[bytes / 2];
                        Marshal.Copy(data, pcm, 0, pcm.Length);
                        for (var j = 0; j < pcm.Length; j += 2)
                            if (Math.Abs((int)pcm[j]) > 3000)
                                return pts / 10000000.0 + j / 96000.0;
                    }
                    if ((flags & MfSourceReaderFlagEndOfStream) != 0) break;
                }
                finally
                {
                    if (locked) _ = buffer!.Unlock();
                    ReleaseCom(buffer);
                    ReleaseCom(sample);
                }
            }
            throw new IOException("Nenhum tom audível no MP4 exportado.");
        }
        finally
        {
            ReleaseCom(type);
            ReleaseCom(reader);
            _ = MFShutdown();
        }
    }
}
