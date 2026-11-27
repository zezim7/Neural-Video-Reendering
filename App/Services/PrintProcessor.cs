using System.IO;

namespace VideoNR.Services;

internal sealed record PrintResult(RawImage Enhanced, RawImage? Original, bool WasDownscaled);

internal static class PrintProcessor
{
    internal static double SafeVideoCaptureTime(double position, double duration)
    {
        if (!double.IsFinite(position))
            return 0;

        return duration > 0 && double.IsFinite(duration) ? Math.Clamp(position, 0, duration) : Math.Max(0, position);
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

            return await RenderComparisonAsync(exporter, request, onProgress, cancellationToken);
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
            var inputPath = Path.Combine(tempDir, "still.y4m");
            var outputMp4 = Path.Combine(tempDir, "still-out.mp4");

            await Task.Run(
                () => StillFrameInput.Write(padded, inputPath, cancellationToken),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var request = new ExportRequest(
                adapterPath,
                enginePath,
                inputPath,
                outputMp4,
                0,
                0,
                "h264",
                modelTag,
                encodedLayers,
                MaxFrames: 1);

            var result = await RenderComparisonAsync(exporter, request, onProgress, cancellationToken);
            if (!wasPadded) return result with { WasDownscaled = wasDownscaled };
            return new PrintResult(result.Enhanced.Crop(working.Width, working.Height),
                result.Original?.Crop(working.Width, working.Height), wasDownscaled);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private static async Task<PrintResult> RenderComparisonAsync(
        VideoExporter exporter, ExportRequest request, Action<int> progress, CancellationToken token)
    {
        var baseline = request with
        {
            OutputPath = Path.Combine(Path.GetDirectoryName(request.OutputPath)!, "reference.mp4"),
            EncodedLayers = []
        };
        await exporter.ExportAsync(baseline, value => progress(value / 2), token);
        await exporter.ExportAsync(request, value => progress(5000 + value / 2), token);
        return await Task.Run(() => new PrintResult(
            MediaFoundationFrameCodec.DecodeFrame(request.OutputPath),
            MediaFoundationFrameCodec.DecodeFrame(baseline.OutputPath), WasDownscaled: false), token);
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
