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

    private TrimSelection trim;
    public TrimSelection Trim
    {
        get => trim;
        set
        {
            if (trim == value) return;
            trim = value;
            Notify(nameof(Trim));
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
            Notify(nameof(Subtitle));
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

    public string Subtitle
    {
        get
        {
            if (Duration <= 0) return Status;
            if (Trim == default) return $"{Status} · {TrimSelection.Format(Duration)}";
            var clipLength = Math.Max(0, Trim.EndAt(Duration) - Trim.Start);
            return $"{Status} · corte {TrimSelection.Format(clipLength)}";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Notify(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
