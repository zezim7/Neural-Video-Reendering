using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using VideoNR.Services;

namespace VideoNR.Imaging;

internal sealed record VideoThumbnailInfo(BitmapSource Bitmap, int Width, int Height, double? FrameRate, double DurationSeconds);

internal static class ThumbnailService
{
    private static readonly SemaphoreSlim Gate = new(2, 2);

    internal static async Task<VideoThumbnailInfo?> ExtractAsync(string videoPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
            return null;

        await Gate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                RawImage decoded;
                try
                {
                    decoded = MediaFoundationFrameCodec.DecodeFrame(videoPath, 0.25);
                }
                catch (Exception error) when (error is IOException or COMException or InvalidOperationException)
                {
                    decoded = MediaFoundationFrameCodec.DecodeFrame(videoPath, 0);
                }

                cancellationToken.ThrowIfCancellationRequested();
                var probe = MediaFoundationFrameCodec.TryProbe(videoPath);
                var (scaled, _) = decoded.DownscaleIfExceeds(128, 72);
                var bitmap = WpfImageBridge.ToBitmapSource(scaled);
                return new VideoThumbnailInfo(
                    bitmap,
                    decoded.Width,
                    decoded.Height,
                    probe?.FrameRate,
                    probe?.DurationSeconds ?? 0);
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error) when (error is IOException or COMException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }
}
