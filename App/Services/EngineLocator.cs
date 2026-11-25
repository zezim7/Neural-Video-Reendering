using System.IO;
using System.Text.Json;

namespace VideoNR.Services;

internal static class EngineLocator
{
    internal const string ExpectedVersion = "2.2.0";

    internal static string? FindAdapter(string? baseDirectory = null)
    {
        var directory = baseDirectory ?? AppContext.BaseDirectory;
        var candidate = Path.Combine(directory, "Engine.exe");
        return File.Exists(candidate) ? candidate : null;
    }

    internal static string? FindBundledEngine(string vendor, string? baseDirectory = null)
    {
        var directory = baseDirectory ?? AppContext.BaseDirectory;
        return FindEngineIn(Path.Combine(directory, "motor"), vendor) ?? FindEngineIn(directory, vendor);
    }

    internal static string? FindEngineIn(string root, string vendor)
    {
        if (string.IsNullOrWhiteSpace(root)) return null;
        foreach (var folder in new[] { root, Path.Combine(root, "versions", ExpectedVersion) })
        {
            var executable = Path.Combine(folder, "veyra_qml_ui.exe");
            var manifestPath = Path.Combine(folder, "package-manifest.json");
            if (!File.Exists(executable) || !File.Exists(manifestPath)) continue;
            try
            {
                using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
                var value = manifest.RootElement;
                if (value.TryGetProperty("version", out var versionProp)
                    && value.TryGetProperty("gpuPackage", out var gpuProp)
                    && versionProp.GetString() == ExpectedVersion
                    && gpuProp.GetString()?.Equals(vendor, StringComparison.OrdinalIgnoreCase) == true)
                    return executable;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
            }
        }
        return null;
    }
}
