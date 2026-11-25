using System.IO;
using System.Runtime.InteropServices;

namespace VideoNR.Services;

internal sealed record PrintResult(RawImage Enhanced, RawImage? Original, bool WasDownscaled);

internal static class PrintProcessor
{
    internal static double SafeVideoCaptureTime(double position, double duration)
    {
        if (duration <= 0 || !double.IsFinite(position) || !double.IsFinite(duration))
            return 0;

        return Math.Clamp(position, 0, Math.Max(0, duration - 0.05));
    }

    internal static async Task<PrintResult> ProcessVideoFrameAsync(
        VideoExporter exporter,
        string adapterPath,
        string enginePath,
        string videoPath,
        double positionSeconds,
        double durationSeconds,
        string modelTag,
        string[] encodedLayers,
        Action<int> onProgress,
        CancellationToken cancellationToken)
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var outputMp4 = Path.Combine(tempDir, "frame-out.mp4");
            var captureTime = SafeVideoCaptureTime(positionSeconds, durationSeconds);
            var request = new ExportRequest(
                adapterPath,
                enginePath,
                videoPath,
                outputMp4,
                captureTime,
                0,
                "h264",
                modelTag,
                encodedLayers,
                MaxFrames: 1);

            await exporter.ExportAsync(request, onProgress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var enhanced = await Task.Run(() => MediaFoundationFrameCodec.DecodeFrame(outputMp4, 0), cancellationToken);

            RawImage? original = null;
            try
            {
                var decodedOriginal = await Task.Run(
                    () => MediaFoundationFrameCodec.DecodeFrame(videoPath, captureTime),
                    cancellationToken);
                if (decodedOriginal.Width == enhanced.Width && decodedOriginal.Height == enhanced.Height)
                    original = decodedOriginal;
            }
            catch (Exception error) when (error is IOException or COMException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
            {
                original = null;
            }

            return new PrintResult(enhanced, original, WasDownscaled: false);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    internal static async Task<PrintResult> ProcessImageAsync(
        VideoExporter exporter,
        string adapterPath,
        string enginePath,
        RawImage source,
        string modelTag,
        string[] encodedLayers,
        Action<int> onProgress,
        CancellationToken cancellationToken)
    {
        source.Validate();
        var (working, wasDownscaled, padded, wasPadded) = await Task.Run(() =>
        {
            var (downscaled, scaled) = source.DownscaleIfExceeds(4096, 2304);
            var (encodedReady, didPad) = downscaled.PadForVideoEncoding(160, 120);
            return (downscaled, scaled, encodedReady, didPad);
        }, cancellationToken);

        var tempDir = CreateTempDirectory();
        try
        {
            var inputMp4 = Path.Combine(tempDir, "still-in.mp4");
            var outputMp4 = Path.Combine(tempDir, "still-out.mp4");

            await Task.Run(
                () => MediaFoundationFrameCodec.EncodeStillVideo(padded, inputMp4, frameCount: 2, fps: 30),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var request = new ExportRequest(
                adapterPath,
                enginePath,
                inputMp4,
                outputMp4,
                0,
                0,
                "h264",
                modelTag,
                encodedLayers,
                MaxFrames: 1);

            await exporter.ExportAsync(request, onProgress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var enhanced = await Task.Run(() =>
            {
                var decoded = MediaFoundationFrameCodec.DecodeFrame(outputMp4, 0);
                return (wasPadded || decoded.Width != working.Width || decoded.Height != working.Height)
                    && decoded.Width >= working.Width
                    && decoded.Height >= working.Height
                    ? decoded.Crop(working.Width, working.Height)
                    : decoded;
            }, cancellationToken);

            return new PrintResult(enhanced, working, wasDownscaled);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "VideoNR-Print-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
