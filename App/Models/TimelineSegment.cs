using System.IO;
using VideoNR.Services;

namespace VideoNR.Models;

internal sealed record SegmentNrProfile(bool NrEnabled, List<SavedLayer> Layers)
{
    internal SegmentNrProfile DeepClone() =>
        new(NrEnabled, Layers.Select(l => l with { }).ToList());

    internal int ActiveLayerCount => NrEnabled ? Layers.Count(l => l.Enabled) : 0;

    internal string[] EncodeActiveLayers()
    {
        if (!NrEnabled) return [];
        var encoded = new List<string>(Layers.Count);
        foreach (var layer in Layers)
        {
            if (!layer.Enabled) continue;
            var model = new NrLayer
            {
                Enabled = true,
                ResolutionIndex = layer.ResolutionIndex,
                Intensity = layer.Intensity,
                Tone = layer.Tone,
                Structure = layer.Structure
            };
            encoded.Add(model.Encode());
        }
        return encoded.ToArray();
    }
}

internal sealed class TimelineTrackInfo
{
    public int Index { get; set; }
    public string Name => $"V{Index + 1}";
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }

    public TimelineTrackInfo(int index, bool visible = true, bool locked = false)
    {
        Index = Math.Max(0, index);
        Visible = visible;
        Locked = locked;
    }

    public TimelineTrackInfo Clone() => new(Index, Visible, Locked);
}

internal sealed class TimelineSegment
{
    internal const double MinDuration = 0.05;

    public Guid Id { get; init; } = Guid.NewGuid();
    public string SourcePath { get; }
    public string SourceName => Path.GetFileName(SourcePath);
    public double SourceDuration { get; set; }
    public double SourceStart { get; set; }
    public double SourceEnd { get; set; }
    public double TimelineStart { get; set; }
    public int TrackIndex { get; set; }
    public bool Enabled { get; set; } = true;
    public int ColorIndex { get; set; }
    public bool UseCustomNr { get; set; }
    public SegmentNrProfile? CustomNr { get; set; }

    public TimelineSegment(
        string sourcePath,
        double sourceDuration,
        double sourceStart,
        double sourceEnd,
        int colorIndex = 0,
        bool enabled = true,
        bool useCustomNr = false,
        SegmentNrProfile? customNr = null,
        Guid? id = null,
        double timelineStart = 0,
        int trackIndex = 0)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("O caminho do vídeo de origem não pode ser vazio.", nameof(sourcePath));

        Id = id ?? Guid.NewGuid();
        SourcePath = sourcePath;
        SourceDuration = Math.Max(0, sourceDuration);
        var (start, end) = ClampBounds(sourceStart, sourceEnd, SourceDuration);
        SourceStart = start;
        SourceEnd = end;
        TimelineStart = double.IsFinite(timelineStart) && timelineStart > 0 ? timelineStart : 0;
        TrackIndex = Math.Max(0, trackIndex);
        ColorIndex = colorIndex;
        Enabled = enabled;
        UseCustomNr = useCustomNr;
        CustomNr = customNr?.DeepClone();
    }

    public double Duration => Math.Max(0, SourceEnd - SourceStart);
    public double TimelineEnd => TimelineStart + Duration;

    public string NrBadge
    {
        get
        {
            if (!UseCustomNr || CustomNr is null) return "";
            var count = CustomNr.ActiveLayerCount;
            return count == 0 ? "NR OFF" : $"NR ×{count}";
        }
    }

    public TimelineSegment Clone(bool preserveId = true) => new(
        SourcePath,
        SourceDuration,
        SourceStart,
        SourceEnd,
        ColorIndex,
        Enabled,
        UseCustomNr,
        CustomNr?.DeepClone(),
        preserveId ? Id : Guid.NewGuid(),
        TimelineStart,
        TrackIndex);

    public void UpdateSourceDuration(double duration)
    {
        if (!double.IsFinite(duration) || duration <= 0) return;
        var wasFullLength = SourceDuration <= 0 || Math.Abs(SourceEnd - SourceDuration) < 0.02;
        SourceDuration = duration;
        if (SourceEnd <= 0 || wasFullLength)
        {
            SourceStart = Math.Clamp(SourceStart, 0, Math.Max(0, duration - MinDuration));
            SourceEnd = duration;
        }
        else
        {
            var (start, end) = ClampBounds(SourceStart, SourceEnd, duration);
            SourceStart = start;
            SourceEnd = end;
        }
    }

    internal static (double Start, double End) ClampBounds(double start, double end, double sourceDuration)
    {
        if (!double.IsFinite(start) || start < 0) start = 0;
        if (!double.IsFinite(end) || end < 0) end = 0;

        if (sourceDuration <= 0)
            return (start, Math.Max(start, end));

        var effectiveEnd = end <= 0 ? sourceDuration : Math.Min(end, sourceDuration);
        var minGap = Math.Min(MinDuration, sourceDuration);
        start = Math.Clamp(start, 0, Math.Max(0, sourceDuration - minGap));
        effectiveEnd = Math.Clamp(effectiveEnd, Math.Min(sourceDuration, start + minGap), sourceDuration);
        return (start, effectiveEnd);
    }
}
