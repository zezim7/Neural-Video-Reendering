using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using VideoNR.Helpers;
using VideoNR.Imaging;
using VideoNR.Models;
using VideoNR.Services;

namespace VideoNR;

public partial class Editor : Window
{
    private const int LayerLimit = 4;
    private const double FrameStepSeconds = 1.0 / 30.0;
    private const string VideoFilter = "Vídeos|*.mp4;*.mkv;*.mov;*.webm;*.avi;*.m4v;*.mts;*.m2ts;*.wmv;*.flv;*.ts|Todos os arquivos|*.*";
    private const string ImageFilter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.tif;*.tiff|Todos os arquivos|*.*";
    private const string PlayGlyph = "\uE768";
    private const string PauseGlyph = "\uE769";
    private const string VolumeGlyph = "\uE767";
    private const string MuteGlyph = "\uE74F";
    private const string MaximizeGlyph = "\uE922";
    private const string RestoreGlyph = "\uE923";

    private static readonly double[] RulerSteps = [0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1800, 3600];
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".mov", ".webm", ".avi", ".m4v", ".mts", ".m2ts", ".wmv", ".flv", ".ts"
    };
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".tif", ".tiff"
    };

    private readonly ObservableCollection<ClipItem> clips = [];
    private readonly ObservableCollection<NrLayer> layers = [];
    private readonly Dictionary<string, string> engines = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> customEngines = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly VideoExporter exporter = new();
    private readonly List<string> exportErrors = [];
    private readonly CancellationTokenSource lifetimeCts = new();
    private readonly string? adapterPath;

    private CancellationTokenSource? exportCancellation;
    private string? outputFolder;
    private ClipItem? clip;
    private ClipItem? loadingClip;
    private bool busy, playing, ready, scrubbing, refreshingFields, closePending;
    private bool queueOpen = true;
    private double position, trimPointerOffset;

    public Editor()
    {
        InitializeComponent();
        WindowDarkMode.Apply(this, RootGrid);

        Queue.ItemsSource = clips;
        LayerCards.ItemsSource = layers;
        NrVersion.ItemsSource = NrLayer.Models;

        var saved = SettingsStore.Load();
        NrSwitch.IsChecked = saved.NrEnabled;
        if (saved.CodecIndex == 1) HevcRadio.IsChecked = true;
        else H264Radio.IsChecked = true;
        foreach (var layer in SettingsStore.ToNrLayers(saved, LayerLimit))
            layers.Add(layer);

        adapterPath = EngineLocator.FindAdapter();
        foreach (var vendor in new[] { "NVIDIA", "AMD" })
        {
            if (EngineLocator.FindBundledEngine(vendor) is { } bundled)
                engines[vendor] = bundled;
            else if (saved.CustomEngines is not null
                && saved.CustomEngines.TryGetValue(vendor, out var customRoot)
                && EngineLocator.FindEngineIn(customRoot, vendor) is { } custom)
            {
                engines[vendor] = custom;
                customEngines[vendor] = customRoot;
            }
        }

        var savedModelIndex = Array.FindIndex(NrLayer.Models, m => m.Tag == saved.ModelTag);
        if (savedModelIndex >= 0)
            NrVersion.SelectedIndex = savedModelIndex;
        else if (!engines.ContainsKey("NVIDIA") && engines.ContainsKey("AMD"))
            NrVersion.SelectedIndex = Array.FindIndex(NrLayer.Models, m => m.Vendor == "AMD");
        else
            NrVersion.SelectedIndex = 0;

        ReindexLayers();
        RefreshTrimFields();
        UpdateControls();
        clock.Tick += (_, _) => UpdateClock();
        StateChanged += (_, _) => SyncMaximizeIcon();
        Loaded += (_, _) =>
        {
            SyncMaximizeIcon();
            AddPaths(Environment.GetCommandLineArgs().Skip(1), filterExtensions: false);
        };
    }

    private NrModelOption SelectedModel => NrVersion.SelectedItem as NrModelOption ?? NrLayer.Models[0];
    private string Model => SelectedModel.Tag;
    private string Vendor => SelectedModel.Vendor;
    private string? EnginePath => engines.GetValueOrDefault(Vendor);
    private int ActiveCount => NrSwitch.IsChecked == true ? layers.Count(l => l.Enabled) : 0;
    private bool CancelRequested => exportCancellation?.IsCancellationRequested == true;

    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseClick(object sender, RoutedEventArgs e) => Close();

    private void SyncMaximizeIcon() =>
        MaximizeIcon.Text = WindowState == WindowState.Maximized ? RestoreGlyph : MaximizeGlyph;

    private void ToggleQueueClick(object sender, RoutedEventArgs e) => SetQueueOpen(!queueOpen);

    private void SetQueueOpen(bool open)
    {
        queueOpen = open;
        QueuePane.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        QueueSplitter.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        QueueColumn.Width = open ? new GridLength(248) : new GridLength(0);
    }

    private void ReindexLayers()
    {
        var canDuplicate = layers.Count < LayerLimit;
        for (var i = 0; i < layers.Count; i++)
        {
            layers[i].Index = i + 1;
            layers[i].CanDuplicate = canDuplicate;
        }
    }

    private void UpdateControls()
    {
        QueueTitle.Text = $"Fila · {clips.Count}";
        EmptyQueue.Visibility = clips.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Queue.IsEnabled = !busy;
        TopOpenButton.IsEnabled = AddButton.IsEnabled = EmptyOpenButton.IsEnabled = EngineButton.IsEnabled
            = NrSwitch.IsEnabled = NrVersion.IsEnabled = LayerCards.IsEnabled
            = PresetsGrid.IsEnabled = H264Radio.IsEnabled = HevcRadio.IsEnabled = !busy;
        RemoveButton.IsEnabled = !busy && clip is not null;
        ClearButton.IsEnabled = !busy && clips.Count > 0;
        AddLayerButton.IsEnabled = !busy && layers.Count < LayerLimit;

        if (adapterPath is null)
        {
            EngineDot.Fill = (Brush)FindResource("StatusWarn");
            EngineButton.Visibility = Visibility.Collapsed;
            EngineLabel.Text = "Engine.exe ausente";
        }
        else if (EnginePath is null)
        {
            EngineDot.Fill = (Brush)FindResource("StatusWarn");
            EngineButton.Visibility = Visibility.Visible;
            EngineLabel.Text = $"Pacote {Vendor} ausente";
        }
        else
        {
            EngineDot.Fill = (Brush)FindResource("StatusOk");
            EngineButton.Visibility = Visibility.Collapsed;
            EngineLabel.Text = $"{Vendor} pronto";
        }

        LayerSummary.Text = ActiveCount switch
        {
            0 => "NR desativada",
            1 => "1 camada ativa",
            _ => $"{ActiveCount} camadas em sequência"
        };

        UpdateHeaderClipInfo();

        PlayButton.IsEnabled = ready;
        StepBackFrameButton.IsEnabled = ready && !busy;
        StepForwardFrameButton.IsEnabled = ready && !busy;
        MuteButton.IsEnabled = ready;
        TrimTrack.IsEnabled = ready && clip?.Duration > 0;
        StartHandle.IsEnabled = EndHandle.IsEnabled = StartTime.IsEnabled = EndTime.IsEnabled
            = MarkStartButton.IsEnabled = MarkEndButton.IsEnabled
            = ResetTrimButton.IsEnabled = !busy && TrimTrack.IsEnabled;

        var hasEngine = !busy && adapterPath is not null && EnginePath is not null;
        ScreenPrintButton.IsEnabled = ImagePrintButton.IsEnabled = EmptyPrintButton.IsEnabled = hasEngine;
        FramePrintButton.IsEnabled = hasEngine && clip is not null;
        ExportButton.IsEnabled = hasEngine && clips.Count > 0;
        ExportButton.Content = clips.Count > 1 ? $"Exportar lote ({clips.Count})" : "Exportar vídeo";

        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.IsEnabled = busy && !CancelRequested;
        ShowOutputButton.Visibility = outputFolder is null ? Visibility.Collapsed : Visibility.Visible;
        DrawTrim();
    }

    private void UpdateHeaderClipInfo()
    {
        if (clip is null)
        {
            HeaderClipInfo.Text = "";
            return;
        }

        var parts = new List<string>(4) { clip.Name };
        var w = clip.VideoWidth > 0 ? clip.VideoWidth : Player.NaturalVideoWidth;
        var h = clip.VideoHeight > 0 ? clip.VideoHeight : Player.NaturalVideoHeight;
        if (w > 0 && h > 0)
            parts.Add($"{w}×{h}");
        if (!string.IsNullOrEmpty(clip.FrameRateBadge))
            parts.Add(clip.FrameRateBadge);
        if (clip.Duration > 0)
            parts.Add(TrimSelection.Format(clip.Duration));
        HeaderClipInfo.Text = string.Join(" · ", parts);
    }

    private void AddFiles(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Adicionar vídeos", Filter = VideoFilter, Multiselect = true };
        if (dialog.ShowDialog(this) == true) AddPaths(dialog.FileNames, filterExtensions: false);
    }

    private async void DropFiles(object sender, DragEventArgs e)
    {
        if (busy || e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        if (paths.Length == 1 && File.Exists(paths[0]) && ImageExtensions.Contains(Path.GetExtension(paths[0])))
        {
            await RunImagePrintFromFileAsync(paths[0]);
            return;
        }
        AddPaths(paths, filterExtensions: true);
    }

    private void AddPaths(IEnumerable<string> paths, bool filterExtensions)
    {
        var candidates = new List<string>();
        var skipped = 0;
        foreach (var raw in paths)
        {
            if (Directory.Exists(raw))
            {
                try
                {
                    foreach (var file in Directory.EnumerateFiles(raw))
                    {
                        if (VideoExtensions.Contains(Path.GetExtension(file)))
                            candidates.Add(file);
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
            else if (File.Exists(raw))
            {
                if (!filterExtensions || VideoExtensions.Contains(Path.GetExtension(raw)))
                    candidates.Add(raw);
                else
                    skipped++;
            }
        }

        ClipItem? first = null;
        foreach (var path in candidates.Select(Path.GetFullPath))
        {
            if (clips.Any(c => c.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) continue;
            var item = new ClipItem(path);
            clips.Add(item);
            first ??= item;
            _ = HydrateThumbnailAsync(item);
        }
        if (first is not null)
        {
            if (clips.Count > 1 && !queueOpen)
                SetQueueOpen(true);
            Queue.SelectedItem = first;
            Status.Text = skipped > 0
                ? $"Vídeos adicionados ({skipped} arquivo(s) ignorado(s) por não serem vídeo)."
                : "Pronto para ajustar camadas NR, cortar ou exportar.";
        }
        else if (skipped > 0)
        {
            Status.Text = "Nenhum vídeo compatível encontrado nos arquivos soltos.";
        }
        UpdateControls();
    }

    private async Task HydrateThumbnailAsync(ClipItem item)
    {
        try
        {
            var info = await ThumbnailService.ExtractAsync(item.Path, lifetimeCts.Token);
            if (info is null || lifetimeCts.IsCancellationRequested) return;
            item.Thumbnail = info.Bitmap;
            item.VideoWidth = info.Width;
            item.VideoHeight = info.Height;
            item.FrameRate = info.FrameRate;
            if (ReferenceEquals(item, clip))
                UpdateHeaderClipInfo();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void SelectClip(object sender, SelectionChangedEventArgs e)
    {
        clip = Queue.SelectedItem as ClipItem;
        LoadMedia(clip);
        RefreshTrimFields();
        UpdateControls();
    }

    private void RemoveSelected(object sender, RoutedEventArgs e)
    {
        if (clip is null) return;
        var index = clips.IndexOf(clip);
        clips.Remove(clip);
        Queue.SelectedIndex = Math.Min(index, clips.Count - 1);
        UpdateControls();
    }

    private void ClearQueue(object sender, RoutedEventArgs e)
    {
        clips.Clear();
        UpdateControls();
    }

    private void AddLayerClick(object sender, RoutedEventArgs e)
    {
        if (layers.Count >= LayerLimit) return;
        layers.Add(new NrLayer());
        ReindexLayers();
        UpdateControls();
    }

    private void LayerToggleClick(object sender, RoutedEventArgs e) => UpdateControls();

    private void ToggleLayerExpandClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NrLayer layer })
            layer.Expanded = !layer.Expanded;
    }

    private void DuplicateLayerClick(object sender, RoutedEventArgs e)
    {
        if (layers.Count >= LayerLimit || sender is not FrameworkElement { DataContext: NrLayer layer }) return;
        var index = layers.IndexOf(layer);
        if (index < 0) return;
        layers.Insert(index + 1, layer.CloneCollapsed());
        ReindexLayers();
        UpdateControls();
    }

    private void RemoveLayerClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: NrLayer layer }) return;
        layers.Remove(layer);
        ReindexLayers();
        UpdateControls();
    }

    private void ApplyPresetClick(object sender, RoutedEventArgs e)
    {
        if (busy || sender is not FrameworkElement { Tag: string rawIndex }
            || !int.TryParse(rawIndex, out var index)
            || index < 0 || index >= NrLayer.Presets.Length)
            return;

        var preset = NrLayer.Presets[index];
        var targets = layers.Where(l => l.Enabled).ToList();
        if (targets.Count == 0 && layers.Count > 0)
            targets.Add(layers[0]);

        foreach (var layer in targets)
            layer.ApplyPreset(preset);

        Status.Text = $"Predefinição \"{preset.Name}\" aplicada.";
    }

    private void SliderResetDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || sender is not Slider { DataContext: NrLayer layer } slider)
            return;

        switch (slider.Tag as string)
        {
            case "Intensity": layer.Intensity = 1.0; break;
            case "Tone": layer.Tone = 1.0; break;
            case "Structure": layer.Structure = 1.0; break;
            default: layer.ResetDefaults(); break;
        }
        e.Handled = true;
    }

    private void ToggleNr(object sender, RoutedEventArgs e) => UpdateControls();

    private void ChangeVersion(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (EnginePath is null && engines.Count > 0)
        {
            var available = string.Join(" / ", engines.Keys);
            Status.Text = $"Este modo exige o pacote {Vendor}. Clique em 'Localizar motor' ou selecione um modo {available}.";
        }
        UpdateControls();
    }

    private void EditorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBoxBase or ComboBox) return;

        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.Control && e.Key == Key.O && !busy)
        {
            AddFiles(sender, e);
            e.Handled = true;
            return;
        }
        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.P && !busy)
        {
            ScreenPrintClick(sender, e);
            e.Handled = true;
            return;
        }
        if (modifiers == ModifierKeys.Control && e.Key == Key.V && !busy && Clipboard.ContainsImage())
        {
            PasteImagePrint();
            e.Handled = true;
            return;
        }
        if (modifiers == ModifierKeys.None && e.Key == Key.Q)
        {
            ToggleQueueClick(sender, e);
            e.Handled = true;
            return;
        }
        if (modifiers == ModifierKeys.None && e.Key == Key.P && !busy && clip is not null)
        {
            FramePrintClick(sender, e);
            e.Handled = true;
            return;
        }
        if (modifiers == ModifierKeys.None && e.Key == Key.Delete && !busy && clip is not null)
        {
            RemoveSelected(sender, e);
            e.Handled = true;
            return;
        }
        if (!ready) return;

        if (modifiers == ModifierKeys.None)
        {
            if (e.Key == Key.Space && e.OriginalSource is not ButtonBase and not ListBoxItem)
            {
                TogglePlayback();
                e.Handled = true;
            }
            else if (e.Key == Key.M)
            {
                ToggleMute();
                e.Handled = true;
            }
            else if (e.Key == Key.I && !busy)
            {
                MoveTrimEdge(true, position);
                e.Handled = true;
            }
            else if (e.Key == Key.O && !busy)
            {
                MoveTrimEdge(false, position);
                e.Handled = true;
            }
            else if (e.Key == Key.OemComma && !busy)
            {
                StepFrame(-1);
                e.Handled = true;
            }
            else if (e.Key == Key.OemPeriod && !busy)
            {
                StepFrame(1);
                e.Handled = true;
            }
            else if (e.Key == Key.Left && e.OriginalSource is not Slider)
            {
                Seek(position - 1);
                e.Handled = true;
            }
            else if (e.Key == Key.Right && e.OriginalSource is not Slider)
            {
                Seek(position + 1);
                e.Handled = true;
            }
        }
        else if (modifiers == ModifierKeys.Shift && e.OriginalSource is not Slider)
        {
            if (e.Key == Key.Left) { Seek(position - 5); e.Handled = true; }
            else if (e.Key == Key.Right) { Seek(position + 5); e.Handled = true; }
        }
    }

    private void FindEngineClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = $"Localizar pacote de processamento {Vendor} {EngineLocator.ExpectedVersion}" };
        if (dialog.ShowDialog(this) != true) return;
        var path = EngineLocator.FindEngineIn(dialog.FolderName, Vendor);
        if (path is null)
        {
            Status.Text = $"Selecione uma pasta com o pacote de processamento {Vendor} {EngineLocator.ExpectedVersion}.";
            return;
        }
        engines[Vendor] = path;
        customEngines[Vendor] = dialog.FolderName;
        Status.Text = "Motor localizado.";
        UpdateControls();
    }

    private void SavePreferences()
    {
        var settings = new UserSettings(
            NrEnabled: NrSwitch.IsChecked == true,
            ModelTag: Model,
            CodecIndex: HevcRadio.IsChecked == true ? 1 : 0,
            Layers: SettingsStore.FromNrLayers(layers),
            CustomEngines: customEngines.Count > 0 ? new Dictionary<string, string>(customEngines) : null);
        SettingsStore.Save(settings);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (busy)
        {
            e.Cancel = true;
            closePending = true;
            RequestCancel();
        }
        else
        {
            SavePreferences();
            lifetimeCts.Cancel();
            lifetimeCts.Dispose();
            clock.Stop();
            Player.Close();
        }
        base.OnClosing(e);
    }
}
