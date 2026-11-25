using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VideoNR.Models;

public sealed record NrResolutionOption(string Name, int Policy)
{
    public override string ToString() => Name;
}

public sealed record NrModelOption(string Tag, string Label, string Vendor)
{
    public override string ToString() => Label;
}

public sealed record NrPreset(string Name, double Intensity, double Tone, double Structure);

internal sealed class NrLayer : INotifyPropertyChanged
{
    internal static readonly NrResolutionOption[] Resolutions =
    [
        new("1080p", 0),
        new("1440p", 5),
        new("900p", 4),
        new("720p", 3),
        new("480p", 2),
        new("Nativa", 1)
    ];

    internal static readonly string[] ResolutionNames = Resolutions.Select(r => r.Name).ToArray();

    internal static readonly NrPreset[] Presets =
    [
        new("Sutil", 0.45, 0.50, 0.50),
        new("Equilibrado", 0.75, 0.80, 0.75),
        new("Máximo", 1.00, 1.00, 1.00)
    ];

    internal static readonly NrModelOption[] Models =
    [
        new("original", "NVIDIA original · RTX 50", "NVIDIA"),
        new("sf-performance", "SF310.9.16 desempenho · RTX 20–50", "NVIDIA"),
        new("sf-legacy", "SF310.9.16 legado · RTX 20–50", "NVIDIA"),
        new("amd-small", "AMD Small · RX 7000 / 9000", "AMD"),
        new("amd-original", "AMD Original · RX 7000 / 9000", "AMD")
    ];

    private int index = 1;
    private bool enabled = true;
    private bool expanded;
    private int resolutionIndex;
    private double intensity = 1.0;
    private double tone = 1.0;
    private double structure = 1.0;
    private bool canDuplicate = true;

    public int Index
    {
        get => index;
        set
        {
            if (index == value) return;
            index = value;
            Notify();
            Notify(nameof(Title));
            Notify(nameof(ToggleAccessibilityName));
            Notify(nameof(DuplicateAccessibilityName));
            Notify(nameof(HeaderAccessibilityName));
        }
    }

    public bool Enabled
    {
        get => enabled;
        set
        {
            if (enabled == value) return;
            enabled = value;
            Notify();
        }
    }

    public bool Expanded
    {
        get => expanded;
        set
        {
            if (expanded == value) return;
            expanded = value;
            Notify();
            Notify(nameof(Chevron));
        }
    }

    public int ResolutionIndex
    {
        get => resolutionIndex;
        set
        {
            var clamped = Math.Clamp(value, 0, Resolutions.Length - 1);
            if (resolutionIndex == clamped) return;
            resolutionIndex = clamped;
            Notify();
            Notify(nameof(Summary));
        }
    }

    public double Intensity
    {
        get => intensity;
        set
        {
            var clamped = Math.Clamp(value, 0, 1);
            if (Math.Abs(intensity - clamped) < 1e-6) return;
            intensity = clamped;
            Notify();
            Notify(nameof(IntensityLabel));
            Notify(nameof(IntensityValue));
            Notify(nameof(Summary));
        }
    }

    public double Tone
    {
        get => tone;
        set
        {
            var clamped = Math.Clamp(value, 0, 1);
            if (Math.Abs(tone - clamped) < 1e-6) return;
            tone = clamped;
            Notify();
            Notify(nameof(ToneLabel));
            Notify(nameof(ToneValue));
        }
    }

    public double Structure
    {
        get => structure;
        set
        {
            var clamped = Math.Clamp(value, 0, 1);
            if (Math.Abs(structure - clamped) < 1e-6) return;
            structure = clamped;
            Notify();
            Notify(nameof(StructureLabel));
            Notify(nameof(StructureValue));
        }
    }

    public bool CanDuplicate
    {
        get => canDuplicate;
        set
        {
            if (canDuplicate == value) return;
            canDuplicate = value;
            Notify();
        }
    }

    public string Title => $"Camada NR {Index}";
    public string Chevron => Expanded ? "▾" : "▸";
    public string Summary => $"{Resolutions[ResolutionIndex].Name} · {Intensity:F2}";
    public string IntensityLabel => $"Intensidade {Intensity:F2}";
    public string ToneLabel => $"Tom {Tone:F2}";
    public string StructureLabel => $"Estrutura {Structure:F2}";
    public string IntensityValue => $"{Intensity:F2}";
    public string ToneValue => $"{Tone:F2}";
    public string StructureValue => $"{Structure:F2}";
    public string ToggleAccessibilityName => $"Ativar {Title}";
    public string DuplicateAccessibilityName => $"Duplicar {Title}";
    public string HeaderAccessibilityName => $"{Title} — ajustes";
    public string[] AvailableResolutions => ResolutionNames;

    public NrLayer CloneCollapsed() => new()
    {
        Enabled = Enabled,
        Expanded = false,
        ResolutionIndex = ResolutionIndex,
        Intensity = Intensity,
        Tone = Tone,
        Structure = Structure
    };

    internal void ApplyPreset(NrPreset preset)
    {
        Intensity = preset.Intensity;
        Tone = preset.Tone;
        Structure = preset.Structure;
    }

    internal void ResetDefaults()
    {
        Intensity = 1.0;
        Tone = 1.0;
        Structure = 1.0;
    }

    public string Encode() => FormattableString.Invariant(
        $"{Resolutions[ResolutionIndex].Policy} {Intensity:F2} {Tone:F2} {Structure:F2}");

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
