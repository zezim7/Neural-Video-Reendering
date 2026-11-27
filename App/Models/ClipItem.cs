using System.ComponentModel;

namespace VideoNR.Models;

internal sealed class ClipItem(string path) : INotifyPropertyChanged
{
    public string Path { get; } = path;
    public string Name => System.IO.Path.GetFileName(Path);

    private double duration;
    public double Duration
    {
        get => duration;
        set
        {
            if (Math.Abs(duration - value) < 1e-6) return;
            duration = value;
            Notify(nameof(Duration));
            Notify(nameof(Subtitle));
        }
    }

    private string status = "Na fila";
    public string Status
    {
        get => status;
        set
        {
            if (status == value) return;
            status = value;
            Notify(nameof(Status));
            Notify(nameof(Subtitle));
        }
    }

    private object? thumbnail;
    public object? Thumbnail
    {
        get => thumbnail;
        set
        {
            if (ReferenceEquals(thumbnail, value)) return;
            thumbnail = value;
            Notify(nameof(Thumbnail));
        }
    }

    private int videoWidth;
    public int VideoWidth
    {
        get => videoWidth;
        set
        {
            if (videoWidth == value) return;
            videoWidth = value;
            Notify(nameof(VideoWidth));
            Notify(nameof(ResolutionBadge));
            Notify(nameof(MetadataBadge));
            Notify(nameof(Subtitle));
        }
    }

    private int videoHeight;
    public int VideoHeight
    {
        get => videoHeight;
        set
        {
            if (videoHeight == value) return;
            videoHeight = value;
            Notify(nameof(VideoHeight));
            Notify(nameof(ResolutionBadge));
            Notify(nameof(MetadataBadge));
            Notify(nameof(Subtitle));
        }
    }

    private double? frameRate;
    public double? FrameRate
    {
        get => frameRate;
        set
        {
            var normalized = value is { } fps && double.IsFinite(fps) && fps > 0 ? fps : (double?)null;
            if (frameRate.HasValue == normalized.HasValue
                && (!normalized.HasValue || Math.Abs(frameRate!.Value - normalized.Value) < 1e-6))
                return;
            frameRate = normalized;
            Notify(nameof(FrameRate));
            Notify(nameof(FrameRateBadge));
            Notify(nameof(MetadataBadge));
        }
    }

    public string ResolutionBadge => VideoHeight switch
    {
        <= 0 => "",
        >= 2160 => "4K",
        >= 1440 => "1440p",
        >= 1080 => "1080p",
        >= 720 => "720p",
        _ => $"{VideoHeight}p"
    };

    public string FrameRateBadge
    {
        get
        {
            if (frameRate is not { } fps)
                return "";
            return Math.Abs(fps - Math.Round(fps)) < 0.01
                ? FormattableString.Invariant($"{Math.Round(fps):0} fps")
                : FormattableString.Invariant($"{fps:0.##} fps");
        }
    }

    public string MetadataBadge
    {
        get
        {
            var resolution = VideoWidth > 0 && VideoHeight > 0
                ? FormattableString.Invariant($"{VideoWidth}×{VideoHeight}")
                : ResolutionBadge;
            var fps = FrameRateBadge;
            if (resolution.Length == 0) return fps;
            if (fps.Length == 0) return resolution;
            return $"{resolution} · {fps}";
        }
    }

    public string Subtitle => Duration > 0 ? $"{Status} · {TrimSelection.Format(Duration)}" : Status;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Notify(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
