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
