using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
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
    private const int CustomPresetLimit = 8;
    private const string VideoFilter = "Vídeos|*.mp4;*.mkv;*.mov;*.webm;*.avi;*.m4v;*.mts;*.m2ts;*.wmv;*.flv;*.ts|Todos os arquivos|*.*";
    private const string ImageFilter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.tif;*.tiff|Todos os arquivos|*.*";
    private const string PlayGlyph = "\uE768";
    private const string PauseGlyph = "\uE769";
    private const string VolumeGlyph = "\uE767";
    private const string MuteGlyph = "\uE74F";
    private const string MaximizeGlyph = "\uE922";
    private const string RestoreGlyph = "\uE923";
    private const string EyeGlyph = "\uE7B3";
    private const string EyeOffGlyph = "\uED1A";
    private const string DragClipDataFormat = "VideoNR.ClipPath";

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
    private readonly List<SavedCustomPreset> customPresets = [];
    private readonly TimelineSequence sequence = new();
    private readonly TimelineHistory history = new();
    private readonly Dictionary<string, int> sourceColors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> engines = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> customEngines = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly VideoExporter exporter = new();
    private readonly List<string> exportErrors = [];
    private readonly CancellationTokenSource lifetimeCts = new();
    private readonly string? adapterPath;

    // Backing store for Global NR when the inspector is showing a Segment's custom NR
    private bool globalNrEnabled = true;
    private List<SavedLayer> globalLayersBackup = [];
    private bool editingSegmentScope;
    private bool syncingNrUi;

    private CancellationTokenSource? exportCancellation;
    private string? outputFolder;
    private ClipItem? clip;
    private Guid? selectedSegmentId;
    private string? loadedMediaPath;
    private string? loadingMediaPath;
    private bool pendingAutoPlay;
    private double? pendingSourceSeek;

    private bool busy, playing, ready, scrubbing, refreshingFields, closePending;
    private bool queueOpen = true;
    private bool snapEnabled = true;
    private double timelinePosition;
    private Point queueDragOrigin;
    private bool queueDragArmed;

    private readonly string? settingsPath;

    public Editor() : this(null) { }

    internal Editor(string? settingsPath)
    {
        this.settingsPath = settingsPath;
        InitializeComponent();
        WindowDarkMode.Apply(this, RootGrid);

        Queue.ItemsSource = clips;
        LayerCards.ItemsSource = layers;
        NrVersion.ItemsSource = NrLayer.Models;

        var saved = SettingsStore.Load(settingsPath);
        globalNrEnabled = saved.NrEnabled;
        NrSwitch.IsChecked = saved.NrEnabled;
        if (saved.CodecIndex == 1) HevcRadio.IsChecked = true;
        else H264Radio.IsChecked = true;

        if (saved.ExportModeIndex == 2) EditedExportRadio.IsChecked = true;
        else if (saved.ExportModeIndex == 1) BatchExportRadio.IsChecked = true;
        else SequenceExportRadio.IsChecked = true;

        if (saved.QueueWidth >= 180 && saved.QueueWidth <= 480)
            QueueColumn.Width = new GridLength(saved.QueueWidth);
        if (saved.InspectorWidth >= 276 && saved.InspectorWidth <= 520)
            InspectorColumn.Width = new GridLength(saved.InspectorWidth);
        if (saved.TimelineHeight >= 190 && saved.TimelineHeight <= 460)
            TimelineRow.Height = new GridLength(saved.TimelineHeight);
        if (!saved.QueueOpen)
            SetQueueOpen(false);

        if (saved.CustomPresets is { Count: > 0 })
            customPresets.AddRange(saved.CustomPresets.Take(CustomPresetLimit));

        var initialLayers = SettingsStore.ToNrLayers(saved, LayerLimit);
        globalLayersBackup = SettingsStore.FromNrLayers(initialLayers);
        LoadLayersIntoUi(initialLayers);

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

        NrVersion.SelectedIndex = NrLayer.InitialModelIndex(saved.ModelTag, engines.Keys);

        RebuildCustomPresetsUi();
        SyncZoomSlider();
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
    private bool IsBatchExportMode => SequenceExportRadio.IsChecked != true;
    private TimelineSegment? SelectedSegment => selectedSegmentId is { } id ? sequence.FindById(id) : null;

    private int GetColorIndexForSource(string path)
    {
        if (!sourceColors.TryGetValue(path, out var idx))
        {
            idx = sourceColors.Count % SourceAccentPalette.Length;
            sourceColors[path] = idx;
        }
        return idx;
    }

    private ClipItem? FindClipByPath(string path) =>
        clips.FirstOrDefault(c => c.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

    private void LoadLayersIntoUi(IEnumerable<NrLayer> source)
    {
        syncingNrUi = true;
        try
        {
            foreach (var old in layers)
                old.PropertyChanged -= OnDisplayedLayerChanged;
            layers.Clear();
            foreach (var layer in source)
            {
                layer.PropertyChanged += OnDisplayedLayerChanged;
                layers.Add(layer);
            }
            ReindexLayers();
        }
        finally
        {
            syncingNrUi = false;
        }
    }

    private void OnDisplayedLayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (syncingNrUi) return;
        CommitCurrentNrScopeState();
        if (e.PropertyName == nameof(NrLayer.Enabled))
            UpdateControls();
    }

    private void CommitCurrentNrScopeState()
    {
        if (syncingNrUi) return;
        var currentSaved = SettingsStore.FromNrLayers(layers);
        var currentEnabled = NrSwitch.IsChecked == true;

        if (editingSegmentScope && SelectedSegment is { UseCustomNr: true } seg)
        {
            seg.CustomNr = new SegmentNrProfile(currentEnabled, currentSaved);
            DrawTimeline();
            UpdateVideoTag();
        }
        else if (!editingSegmentScope)
        {
            globalNrEnabled = currentEnabled;
            globalLayersBackup = currentSaved;
        }
    }

    private void SyncInspectorToCurrentScope()
    {
        syncingNrUi = true;
        try
        {
            var seg = SelectedSegment;
            if (editingSegmentScope && seg is not null)
            {
                SegmentOverrideBanner.Visibility = Visibility.Visible;
                SegmentOverrideTitle.Text = $"V{seg.TrackIndex + 1} · {seg.SourceName}";
                SegmentOverrideSwitch.IsChecked = seg.UseCustomNr;
                SegmentOverrideSub.Text = seg.UseCustomNr
                    ? "Ajustes exclusivos deste clipe"
                    : "Herdando perfil global da sequência";

                if (seg.UseCustomNr && seg.CustomNr is not null)
                {
                    NrSwitch.IsChecked = seg.CustomNr.NrEnabled;
                    LoadLayersIntoUi(ToNrLayersFromSaved(seg.CustomNr.Layers));
                }
                else
                {
                    NrSwitch.IsChecked = globalNrEnabled;
                    LoadLayersIntoUi(ToNrLayersFromSaved(globalLayersBackup));
                }
            }
            else
            {
                SegmentOverrideBanner.Visibility = Visibility.Collapsed;
                if (editingSegmentScope && seg is null)
                {
                    editingSegmentScope = false;
                    GlobalScopeRadio.IsChecked = true;
                }
                NrSwitch.IsChecked = globalNrEnabled;
                LoadLayersIntoUi(ToNrLayersFromSaved(globalLayersBackup));
            }
        }
        finally
        {
            syncingNrUi = false;
        }
    }

    private static List<NrLayer> ToNrLayersFromSaved(List<SavedLayer> saved)
    {
        return saved.Take(LayerLimit).Select(s => new NrLayer
        {
            Enabled = s.Enabled,
            ResolutionIndex = s.ResolutionIndex,
            Intensity = s.Intensity,
            Tone = s.Tone,
            Structure = s.Structure
        }).ToList();
    }

    private SegmentNrProfile GetGlobalNrProfile()
    {
        if (!editingSegmentScope)
        {
            globalNrEnabled = NrSwitch.IsChecked == true;
            globalLayersBackup = SettingsStore.FromNrLayers(layers);
        }
        return new SegmentNrProfile(globalNrEnabled, globalLayersBackup.Select(l => l with { }).ToList());
    }

    private SegmentNrProfile GetEffectiveNrProfile(TimelineSegment? segment)
    {
        if (segment is { UseCustomNr: true, CustomNr: not null })
            return segment.CustomNr.DeepClone();
        return GetGlobalNrProfile();
    }

    private void NrScopeChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || syncingNrUi) return;
        CommitCurrentNrScopeState();
        editingSegmentScope = SegmentScopeRadio.IsChecked == true;
        if (editingSegmentScope && SelectedSegment is null && sequence.Count > 0)
            selectedSegmentId = sequence.Segments[0].Id;
        SyncInspectorToCurrentScope();
        UpdateControls();
    }

    private void ToggleSegmentOverrideClick(object sender, RoutedEventArgs e)
    {
        if (busy || SelectedSegment is not { } seg || sequence.IsTrackLocked(seg.TrackIndex)) return;
        history.PushUndo(sequence, selectedSegmentId);
        seg.UseCustomNr = SegmentOverrideSwitch.IsChecked == true;
        if (seg.UseCustomNr && seg.CustomNr is null)
            seg.CustomNr = GetGlobalNrProfile();
        SyncInspectorToCurrentScope();
        UpdateControls();
        Status.Text = seg.UseCustomNr
            ? $"NR personalizado ativado para o trecho de {seg.SourceName}."
            : $"Trecho de {seg.SourceName} voltou a herdar o NR global.";
    }

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
        QueueColumn.Width = open ? new GridLength(256) : new GridLength(0);
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
        if (!IsInitialized) return;

        activeTrackIndex = Math.Clamp(activeTrackIndex, 0, sequence.TrackCount - 1);
        AddToTimelineLabel.Text = $"Inserir em V{activeTrackIndex + 1}";

        QueueTitle.Text = $"Biblioteca · {clips.Count}";
        EmptyQueue.Visibility = clips.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Queue.IsEnabled = !busy;
        TopOpenButton.IsEnabled = AddButton.IsEnabled = EmptyOpenButton.IsEnabled = EngineButton.IsEnabled
            = NrVersion.IsEnabled = H264Radio.IsEnabled = HevcRadio.IsEnabled
            = SequenceExportRadio.IsEnabled = EditedExportRadio.IsEnabled = BatchExportRadio.IsEnabled
            = GlobalScopeRadio.IsEnabled = !busy;

        var seg = SelectedSegment;
        var segLocked = seg is not null && sequence.IsTrackLocked(seg.TrackIndex);
        SegmentScopeRadio.IsEnabled = !busy && sequence.Count > 0;
        SegmentOverrideSwitch.IsEnabled = !busy && seg is not null && !segLocked;

        var nrControlsEditable = !busy && (!editingSegmentScope || (seg is not null && !segLocked && seg.UseCustomNr));
        NrSwitch.IsEnabled = nrControlsEditable;
        LayerCards.IsEnabled = nrControlsEditable;
        PresetsGrid.IsEnabled = nrControlsEditable && ActiveCount > 0;
        SavePresetButton.IsEnabled = PresetsGrid.IsEnabled;
        CustomPresetsPanel.IsEnabled = nrControlsEditable;
        AddLayerButton.IsEnabled = nrControlsEditable && layers.Count < LayerLimit;
        LayerCards.Opacity = nrControlsEditable || busy ? 1.0 : 0.45;

        AddToTimelineButton.IsEnabled = !busy && clip is not null && !sequence.IsTrackLocked(activeTrackIndex);
        RemoveButton.IsEnabled = !busy && clip is not null && !sequence.UsesLockedTrack(clip.Path);
        ClearButton.IsEnabled = !busy && !sequence.Tracks.Any(t => t.Locked) && (clips.Count > 0 || sequence.Count > 0);

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
            EngineLabel.Text = $"Pacote {Vendor} instalado";
        }

        if (editingSegmentScope && seg is not null && !seg.UseCustomNr)
        {
            LayerSummary.Text = "Herdando NR global";
        }
        else
        {
            LayerSummary.Text = ActiveCount switch
            {
                0 => "NR desativada",
                1 => "1 camada ativa",
                _ => $"{ActiveCount} camadas em sequência"
            };
        }

        AmdResolutionHint.Visibility = Vendor == "AMD" ? Visibility.Visible : Visibility.Collapsed;
        UpdateHeaderClipInfo();
        UpdateVideoTag();

        var hasTimeline = sequence.TotalDuration > 0;
        PlayButton.IsEnabled = hasTimeline;
        StepBackFrameButton.IsEnabled = !busy && hasTimeline;
        StepForwardFrameButton.IsEnabled = !busy && hasTimeline;
        MuteButton.IsEnabled = ready;

        SplitButton.IsEnabled = !busy && CanSplitAt(timelinePosition);
        DuplicateSegmentButton.IsEnabled = !busy && seg is not null && !segLocked;
        ToggleSegmentButton.IsEnabled = !busy && seg is not null && !segLocked;
        ToggleSegmentButton.Header = seg is { Enabled: false } ? "Ativar trecho" : "Desativar trecho";
        CloseGapsButton.IsEnabled = !busy && !sequence.IsEmpty;
        DeleteSegmentButton.IsEnabled = !busy && seg is not null && !segLocked;
        UndoButton.IsEnabled = !busy && history.CanUndo;
        RedoButton.IsEnabled = !busy && history.CanRedo;

        AddTrackButton.IsEnabled = !busy && sequence.TrackCount < TimelineSequence.MaxTracks;
        var topIdx = sequence.TrackCount - 1;
        RemoveTrackButton.IsEnabled = !busy && sequence.TrackCount > 2 && !sequence.IsTrackLocked(topIdx) && !sequence.Segments.Any(s => s.TrackIndex == topIdx);

        SnapDot.Fill = (Brush)FindResource(snapEnabled ? "Accent" : "TextMuted");
        FitTimelineButton.IsEnabled = !sequence.IsEmpty;
        ZoomOutButton.IsEnabled = pixelsPerSecond > MinPixelsPerSecond * 1.05;
        ZoomInButton.IsEnabled = pixelsPerSecond < MaxPixelsPerSecond * 0.95;

        var canTrimSegment = !busy && seg is { SourceDuration: > 0 } && !segLocked;
        StartTime.IsEnabled = EndTime.IsEnabled = MarkStartButton.IsEnabled = MarkEndButton.IsEnabled
            = ResetTrimButton.IsEnabled = canTrimSegment;

        var hasEngine = !busy && adapterPath is not null && EnginePath is not null;
        ScreenPrintButton.IsEnabled = ImagePrintButton.IsEnabled = EmptyPrintButton.IsEnabled = hasEngine;
        FramePrintButton.IsEnabled = hasEngine && (sequence.LocateCompositionAt(timelinePosition) is not null || sequence.IsEmpty && clip is not null);

        var activeSlices = sequence.ResolveComposition();
        if (IsBatchExportMode)
        {
            var edited = EditedExportRadio.IsChecked == true;
            var count = edited ? sequence.EnabledCount : clips.Count;
            ExportModeHint.Text = edited ? "1 MP4 por trecho ativo, com seus cortes e NR" : "Arquivos inteiros da biblioteca, com NR global";
            ExportButton.IsEnabled = hasEngine && count > 0;
            ExportButton.Content = edited ? $"Exportar trechos ({count})" : $"Exportar originais ({count})";
        }
        else
        {
            ExportModeHint.Text = activeSlices.Count > 1 ? "Composição multi-faixa em 1 MP4" : "1 vídeo final com áudio";
            ExportButton.IsEnabled = hasEngine && activeSlices.Count > 0;
            ExportButton.Content = activeSlices.Count > 1
                ? $"Exportar sequência ({activeSlices.Count})"
                : "Exportar sequência";
        }

        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.IsEnabled = busy && !CancelRequested;
        ShowOutputButton.Visibility = outputFolder is null ? Visibility.Collapsed : Visibility.Visible;
        EmptyTimelineHint.Visibility = sequence.IsEmpty ? Visibility.Visible : Visibility.Collapsed;

        DrawTimeline();
    }

    private void ExportModeChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateControls();
    }

    private void UpdateHeaderClipInfo()
    {
        if (sequence.Count > 0)
        {
            var segCount = sequence.Count;
            var activeCount = sequence.EnabledCount;
            var activeLabel = activeCount == segCount
                ? $"{segCount} {(segCount == 1 ? "clipe" : "clipes")}"
                : $"{activeCount}/{segCount} clipes ativos";
            var totalStr = TrimSelection.Format(sequence.ActiveDuration);
            var slice = sequence.LocateCompositionAt(timelinePosition);
            if (slice is { } s)
                HeaderClipInfo.Text = $"Sequência ({sequence.TrackCount} faixas) · {activeLabel} · {totalStr}  —  V{s.Segment.TrackIndex + 1}: {s.Segment.SourceName}";
            else
                HeaderClipInfo.Text = $"Sequência ({sequence.TrackCount} faixas) · {activeLabel} · {totalStr}";
            return;
        }

        if (clip is null)
        {
            HeaderClipInfo.Text = "";
            return;
        }

        var parts = new List<string>(4) { clip.Name };
        var w = clip.VideoWidth > 0 ? clip.VideoWidth : Player.NaturalVideoWidth;
        var h2 = clip.VideoHeight > 0 ? clip.VideoHeight : Player.NaturalVideoHeight;
        if (w > 0 && h2 > 0) parts.Add($"{w}×{h2}");
        if (!string.IsNullOrEmpty(clip.FrameRateBadge)) parts.Add(clip.FrameRateBadge);
        if (clip.Duration > 0) parts.Add(TrimSelection.Format(clip.Duration));
        HeaderClipInfo.Text = string.Join(" · ", parts);
    }

    private void UpdateVideoTag()
    {
        if (sequence.IsEmpty && !ready)
        {
            VideoTag.Visibility = Visibility.Collapsed;
            return;
        }

        if (sequence.LocateCompositionAt(timelinePosition) is { } s)
        {
            VideoTag.Visibility = Visibility.Visible;
            var nr = !string.IsNullOrEmpty(s.Segment.NrBadge) ? $" · {s.Segment.NrBadge}" : "";
            VideoTagText.Text = $"V{s.Segment.TrackIndex + 1} · {s.Segment.SourceName}{nr}";
            return;
        }

        if (!sequence.IsEmpty)
        {
            VideoTag.Visibility = Visibility.Visible;
            VideoTagText.Text = "Espaço vazio";
            return;
        }

        VideoTag.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        VideoTagText.Text = "Original";
    }

    private void AddFiles(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Importar vídeos", Filter = VideoFilter, Multiselect = true };
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

    internal void AddPaths(
        IEnumerable<string> paths,
        bool filterExtensions,
        int? targetTrackIndex = null,
        double? dropTimelineSeconds = null)
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

        var timelineWasEmpty = sequence.IsEmpty;
        var track = Math.Clamp(targetTrackIndex ?? activeTrackIndex, 0, TimelineSequence.MaxTracks - 1);
        var shouldInsertOnTimeline = !sequence.IsTrackLocked(track) && (timelineWasEmpty || dropTimelineSeconds.HasValue || targetTrackIndex.HasValue);
        var addedItems = new List<ClipItem>();
        var allResolved = new List<ClipItem>();

        foreach (var path in candidates.Select(Path.GetFullPath))
        {
            var existing = FindClipByPath(path);
            if (existing is not null)
            {
                allResolved.Add(existing);
                continue;
            }
            var item = new ClipItem(path);
            clips.Add(item);
            addedItems.Add(item);
            allResolved.Add(item);
            _ = HydrateThumbnailAsync(item);
        }

        if (shouldInsertOnTimeline && allResolved.Count > 0)
        {
            if (!timelineWasEmpty)
                history.PushUndo(sequence, selectedSegmentId);

            var cursorTime = dropTimelineSeconds ?? sequence.GetTrackEnd(track);
            TimelineSegment? firstAddedSeg = null;

            foreach (var item in allResolved)
            {
                var dur = item.Duration > 0 ? item.Duration : MediaFoundationFrameCodec.TryGetDuration(item.Path) ?? 0;
                if (dur > 0 && item.Duration <= 0)
                    item.Duration = dur;

                var seg = sequence.AddClipAt(item.Path, dur, cursorTime, track, colorIndex: GetColorIndexForSource(item.Path));
                if (seg is null) continue;
                cursorTime = seg.TimelineEnd;
                firstAddedSeg ??= seg;
            }

            if (firstAddedSeg is not null)
            {
                activeTrackIndex = firstAddedSeg.TrackIndex;
                selectedSegmentId = firstAddedSeg.Id;
                if (timelineWasEmpty || !hasUserZoomed)
                    FitTimelineToViewport(keepAutoFit: true);
                SeekTimeline(firstAddedSeg.TimelineStart, autoPlay: false);
            }
        }

        if (addedItems.Count > 0)
        {
            if (clips.Count > 1 && !queueOpen)
                SetQueueOpen(true);
            Queue.SelectedItem = addedItems[0];
            Status.Text = skipped > 0
                ? $"{addedItems.Count} vídeo(s) importado(s) ({skipped} ignorado(s))."
                : shouldInsertOnTimeline
                    ? $"{addedItems.Count} vídeo(s) adicionado(s) em V{activeTrackIndex + 1}."
                    : $"{addedItems.Count} vídeo(s) na biblioteca. Clique em '+' ou arraste para qualquer faixa (V1, V2…).";
        }
        else if (skipped > 0)
        {
            Status.Text = "Nenhum vídeo compatível encontrado nos arquivos soltos.";
        }

        SyncInspectorToCurrentScope();
        RefreshTrimFields();
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
            if (info.DurationSeconds > 0 && item.Duration <= 0)
            {
                item.Duration = info.DurationSeconds;
                sequence.UpdateSourceDuration(item.Path, info.DurationSeconds);
                if (!hasUserZoomed)
                    FitTimelineToViewport(keepAutoFit: true);
                if (!playing && activePlaybackSlice is null)
                    SyncTimelinePlaybackToPlayhead();
                RefreshTrimFields();
            }
            UpdateControls();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void SelectClip(object sender, SelectionChangedEventArgs e)
    {
        clip = Queue.SelectedItem as ClipItem;
        if (clip is not null)
        {
            if (sequence.IsEmpty)
            {
                LoadMediaForPath(clip.Path, seekToSeconds: 0, autoPlay: false);
            }
            else
            {
                var matching = sequence.Segments.FirstOrDefault(s =>
                    s.SourcePath.Equals(clip.Path, StringComparison.OrdinalIgnoreCase));
                if (matching is not null && selectedSegmentId != matching.Id)
                {
                    SelectSegment(matching.Id, seekToStart: false);
                    return;
                }
            }
        }
        else if (sequence.IsEmpty)
        {
            UnloadMedia();
        }
        RefreshTrimFields();
        UpdateControls();
    }

    private void AppendClipItemClick(object sender, RoutedEventArgs e)
    {
        if (busy || sender is not FrameworkElement { DataContext: ClipItem item }) return;
        AppendClipToTimeline(item);
        e.Handled = true;
    }

    private void AddSelectedClipToTimelineClick(object sender, RoutedEventArgs e)
    {
        if (busy || clip is null) return;
        AppendClipToTimeline(clip);
    }

    private void QueueItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (busy || clip is null) return;
        AppendClipToTimeline(clip);
        e.Handled = true;
    }

    private void QueuePreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        queueDragOrigin = e.GetPosition(Queue);
        queueDragArmed = !busy && e.ChangedButton == MouseButton.Left;
    }

    private void QueuePreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!queueDragArmed || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(Queue) - queueDragOrigin;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        queueDragArmed = false;
        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is { DataContext: ClipItem item })
        {
            var data = new DataObject(DragClipDataFormat, item.Path);
            DragDrop.DoDragDrop(Queue, data, DragDropEffects.Copy);
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void RemoveSelected(object sender, RoutedEventArgs e)
    {
        if (clip is null || busy) return;
        if (sequence.UsesLockedTrack(clip.Path))
        {
            Status.Text = "Desbloqueie a faixa antes de remover esta origem.";
            return;
        }
        var target = clip;
        var index = clips.IndexOf(target);

        if (sequence.Segments.Any(s => s.SourcePath.Equals(target.Path, StringComparison.OrdinalIgnoreCase)))
        {
            history.PushUndo(sequence, selectedSegmentId);
            sequence.RemoveBySourcePath(target.Path);
            EnsureValidSegmentSelection();
        }

        clips.Remove(target);
        Queue.SelectedIndex = Math.Min(index, clips.Count - 1);
        SyncAfterSequenceMutation();
    }

    private void ClearQueue(object sender, RoutedEventArgs e)
    {
        if (busy || !sequence.TryClear()) return;
        Pause();
        clips.Clear();
        history.Clear();
        selectedSegmentId = null;
        activeTrackIndex = 0;
        hasUserZoomed = false;
        timelinePosition = 0;
        UnloadMedia();
        SyncInspectorToCurrentScope();
        RefreshTrimFields();
        UpdateControls();
        Status.Text = "Biblioteca e timeline limpas.";
    }

    private void AddLayerClick(object sender, RoutedEventArgs e)
    {
        if (layers.Count >= LayerLimit) return;
        var added = new NrLayer();
        added.PropertyChanged += OnDisplayedLayerChanged;
        layers.Add(added);
        ReindexLayers();
        CommitCurrentNrScopeState();
        UpdateControls();
    }

    private void LayerToggleClick(object sender, RoutedEventArgs e)
    {
        CommitCurrentNrScopeState();
        UpdateControls();
    }

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
        var clone = layer.CloneCollapsed();
        clone.PropertyChanged += OnDisplayedLayerChanged;
        layers.Insert(index + 1, clone);
        ReindexLayers();
        CommitCurrentNrScopeState();
        UpdateControls();
    }

    private void RemoveLayerClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: NrLayer layer }) return;
        layer.PropertyChanged -= OnDisplayedLayerChanged;
        layers.Remove(layer);
        ReindexLayers();
        CommitCurrentNrScopeState();
        UpdateControls();
    }

    private void ApplyPresetClick(object sender, RoutedEventArgs e)
    {
        if (busy || sender is not FrameworkElement { Tag: string rawIndex }
            || !int.TryParse(rawIndex, out var index)
            || index < 0 || index >= NrLayer.Presets.Length)
            return;

        ApplyPresetValues(NrLayer.Presets[index]);
    }

    private void ApplyPresetValues(NrPreset preset)
    {
        if (!PresetsGrid.IsEnabled) return;
        var targets = layers.Where(l => l.Enabled).ToList();
        if (targets.Count == 0) return;

        foreach (var layer in targets)
            layer.ApplyPreset(preset);

        CommitCurrentNrScopeState();
        Status.Text = $"Predefinição \"{preset.Name}\" aplicada.";
    }

    private void SaveCustomPresetClick(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        if (customPresets.Count >= CustomPresetLimit)
        {
            Status.Text = $"Limite de {CustomPresetLimit} presets personalizados atingido. Remova um para salvar outro.";
            return;
        }

        var source = layers.FirstOrDefault(l => l.Enabled);
        if (source is null) return;

        var number = customPresets.Count + 1;
        var name = FormattableString.Invariant($"Meu {number} ({source.Intensity:0.00})");
        while (customPresets.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            number++;
            name = FormattableString.Invariant($"Meu {number} ({source.Intensity:0.00})");
        }

        customPresets.Add(new SavedCustomPreset(name, source.Intensity, source.Tone, source.Structure));
        RebuildCustomPresetsUi();
        Status.Text = $"Preset \"{name}\" salvo.";
    }

    private void RebuildCustomPresetsUi()
    {
        CustomPresetsPanel.Children.Clear();
        CustomPresetsPanel.Visibility = customPresets.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        for (var i = 0; i < customPresets.Count; i++)
        {
            var preset = customPresets[i];
            var idx = i;

            var chip = new Border
            {
                Background = (Brush)FindResource("RecessedBg"),
                BorderBrush = (Brush)FindResource("BorderControl"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 0, 5, 5),
                Padding = new Thickness(0)
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var applyBtn = new Button
            {
                Style = (Style)FindResource("SubtleButton"),
                Content = preset.Name,
                FontSize = 11,
                Padding = new Thickness(7, 3, 5, 3),
                ToolTip = string.Format(
                    CultureInfo.InvariantCulture,
                    "Intensidade {0:0.00} · Tom {1:0.00} · Estrutura {2:0.00}",
                    preset.Intensity, preset.Tone, preset.Structure)
            };
            applyBtn.SetBinding(IsEnabledProperty, new System.Windows.Data.Binding(nameof(IsEnabled)) { Source = PresetsGrid });
            applyBtn.Click += (_, _) =>
            {
                if (!busy)
                    ApplyPresetValues(new NrPreset(preset.Name, preset.Intensity, preset.Tone, preset.Structure));
            };

            var removeBtn = new Button
            {
                Style = (Style)FindResource("SubtleButton"),
                Content = "×",
                FontSize = 11,
                Padding = new Thickness(4, 2, 6, 2),
                Foreground = (Brush)FindResource("TextMuted"),
                ToolTip = $"Remover preset \"{preset.Name}\""
            };
            removeBtn.Click += (_, _) =>
            {
                if (busy || idx < 0 || idx >= customPresets.Count) return;
                customPresets.RemoveAt(idx);
                RebuildCustomPresetsUi();
                Status.Text = $"Preset \"{preset.Name}\" removido.";
            };

            row.Children.Add(applyBtn);
            row.Children.Add(removeBtn);
            chip.Child = row;
            CustomPresetsPanel.Children.Add(chip);
        }
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
        CommitCurrentNrScopeState();
        e.Handled = true;
    }

    private void ToggleNr(object sender, RoutedEventArgs e)
    {
        CommitCurrentNrScopeState();
        UpdateControls();
    }

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
        if (modifiers == ModifierKeys.Control && e.Key == Key.Z && !busy)
        {
            UndoClick(sender, e);
            e.Handled = true;
            return;
        }
        if (modifiers == ModifierKeys.Control && e.Key == Key.Y && !busy)
        {
            RedoClick(sender, e);
            e.Handled = true;
            return;
        }
        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Z && !busy)
        {
            RedoClick(sender, e);
            e.Handled = true;
            return;
        }
        if (modifiers == ModifierKeys.Control && e.Key == Key.D && !busy && SelectedSegment is not null)
        {
            DuplicateSegmentClick(sender, e);
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
        if (modifiers == ModifierKeys.None && e.Key == Key.F && !sequence.IsEmpty)
        {
            FitTimelineToViewport(keepAutoFit: false);
            e.Handled = true;
            return;
        }
        if (modifiers == ModifierKeys.None && e.Key == Key.G && !busy && !sequence.IsEmpty)
        {
            CloseGapsClick(sender, e);
            e.Handled = true;
            return;
        }
        if (modifiers == ModifierKeys.None && e.Key == Key.P && !busy && (sequence.Count > 0 || clip is not null))
        {
            FramePrintClick(sender, e);
            e.Handled = true;
            return;
        }
        if (modifiers == ModifierKeys.None && e.Key is Key.S or Key.C && !busy && CanSplitAt(timelinePosition))
        {
            SplitSegmentClick(sender, e);
            e.Handled = true;
            return;
        }
        if (modifiers == ModifierKeys.None && e.Key == Key.E && !busy && SelectedSegment is not null)
        {
            ToggleSegmentEnabledClick(sender, e);
            e.Handled = true;
            return;
        }
        if ((modifiers == ModifierKeys.None || modifiers == ModifierKeys.Shift) && e.Key == Key.Delete && !busy)
        {
            if (SelectedSegment is not null && !Queue.IsKeyboardFocusWithin)
            {
                DeleteSelectedSegment(ripple: modifiers == ModifierKeys.Shift);
                e.Handled = true;
                return;
            }
            if (modifiers == ModifierKeys.None && clip is not null)
            {
                RemoveSelected(sender, e);
                e.Handled = true;
                return;
            }
        }

        if (sequence.TotalDuration <= 0) return;

        if (modifiers == ModifierKeys.None)
        {
            if (e.Key == Key.Space)
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
                MarkStartClick(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.O && !busy)
            {
                MarkEndClick(sender, e);
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
                SeekTimeline(timelinePosition - 1, autoPlay: false);
                e.Handled = true;
            }
            else if (e.Key == Key.Right && e.OriginalSource is not Slider)
            {
                SeekTimeline(timelinePosition + 1, autoPlay: false);
                e.Handled = true;
            }
        }
        else if (modifiers == ModifierKeys.Shift && e.OriginalSource is not Slider)
        {
            if (e.Key == Key.Left) { SeekTimeline(timelinePosition - 5, autoPlay: false); e.Handled = true; }
            else if (e.Key == Key.Right) { SeekTimeline(timelinePosition + 5, autoPlay: false); e.Handled = true; }
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
        var globalProfile = GetGlobalNrProfile();
        var settings = new UserSettings(
            NrEnabled: globalProfile.NrEnabled,
            ModelTag: Model,
            CodecIndex: HevcRadio.IsChecked == true ? 1 : 0,
            Layers: globalProfile.Layers,
            CustomEngines: customEngines.Count > 0 ? new Dictionary<string, string>(customEngines) : null,
            ExportModeIndex: EditedExportRadio.IsChecked == true ? 2 : BatchExportRadio.IsChecked == true ? 1 : 0,
            QueueWidth: queueOpen && QueueColumn.ActualWidth >= 180 ? QueueColumn.ActualWidth : 256,
            InspectorWidth: InspectorColumn.ActualWidth >= 276 ? InspectorColumn.ActualWidth : 324,
            TimelineHeight: TimelineRow.ActualHeight >= 190 ? TimelineRow.ActualHeight : 236,
            QueueOpen: queueOpen,
            CustomPresets: customPresets.Count > 0 ? customPresets.ToList() : null);
        SettingsStore.Save(settings, settingsPath);
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
            indexingCancellation?.Dispose();
            lifetimeCts.Dispose();
            clock.Stop();
            Player.Close();
        }
        base.OnClosing(e);
    }
}
