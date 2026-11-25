using System.IO;
using System.Text.Json;
using VideoNR.Models;

namespace VideoNR.Services;

internal sealed record SavedLayer(
    bool Enabled,
    int ResolutionIndex,
    double Intensity,
    double Tone,
    double Structure);

internal sealed record SavedCustomPreset(
    string Name,
    double Intensity,
    double Tone,
    double Structure);

internal sealed record UserSettings(
    bool NrEnabled = true,
    string ModelTag = "original",
    int CodecIndex = 0,
    List<SavedLayer>? Layers = null,
    Dictionary<string, string>? CustomEngines = null,
    int ExportModeIndex = 0,
    double QueueWidth = 248,
    double InspectorWidth = 316,
    double TimelineHeight = 236,
    bool QueueOpen = true,
    List<SavedCustomPreset>? CustomPresets = null);

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VideoNR",
        "settings.json");

    internal static UserSettings Load(string? path = null)
    {
        var file = path ?? DefaultPath;
        if (!File.Exists(file)) return new UserSettings();
        try
        {
            return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(file)) ?? new UserSettings();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return new UserSettings();
        }
    }

    internal static void Save(UserSettings settings, string? path = null)
    {
        var file = path ?? DefaultPath;
        try
        {
            var directory = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var temp = file + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temp, file, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    internal static List<NrLayer> ToNrLayers(UserSettings settings, int limit)
    {
        if (settings.Layers is not { Count: > 0 }) return [new NrLayer()];
        return settings.Layers
            .Take(limit)
            .Select(saved => new NrLayer
            {
                Enabled = saved.Enabled,
                ResolutionIndex = saved.ResolutionIndex,
                Intensity = saved.Intensity,
                Tone = saved.Tone,
                Structure = saved.Structure
            })
            .ToList();
    }

    internal static List<SavedLayer> FromNrLayers(IEnumerable<NrLayer> layers) =>
        layers.Select(l => new SavedLayer(l.Enabled, l.ResolutionIndex, l.Intensity, l.Tone, l.Structure)).ToList();
}
