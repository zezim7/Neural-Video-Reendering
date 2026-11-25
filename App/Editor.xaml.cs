using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using VideoNR.Models;
using VideoNR.Services;

namespace VideoNR;

public partial class Editor : Window
{
    private const int LayerLimit = 4;
    private const string VideoFilter = "Vídeos|*.mp4;*.mkv;*.mov;*.webm;*.avi;*.m4v;*.mts;*.m2ts|Todos os arquivos|*.*";

    private readonly ObservableCollection<ClipItem> clips = [];
    private readonly ObservableCollection<NrLayer> layers = [new()];
    private readonly Dictionary<string, string> engines = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly VideoExporter exporter = new();
    private readonly List<string> exportErrors = [];
    private readonly string? adapterPath;

    private CancellationTokenSource? exportCancellation;
    private string? outputFolder;
    private ClipItem? clip;
    private ClipItem? loadingClip;
    private bool busy, playing, ready, scrubbing, refreshingFields, closePending;
    private double position, trimPointerOffset;

    public Editor()
    {
        InitializeComponent();
        Queue.ItemsSource = clips;
        LayerCards.ItemsSource = layers;
        NrVersion.SelectedIndex = 0;

        adapterPath = EngineLocator.FindAdapter();
        foreach (var vendor in new[] { "NVIDIA", "AMD" })
            if (EngineLocator.FindBundledEngine(vendor) is { } path)
                engines[vendor] = path;
        if (!engines.ContainsKey("NVIDIA") && engines.ContainsKey("AMD"))
            NrVersion.SelectedIndex = 3;

        ReindexLayers();
        RefreshTrimFields();
        UpdateControls();
        clock.Tick += (_, _) => UpdateClock();
        Loaded += (_, _) => AddPaths(Environment.GetCommandLineArgs().Skip(1));
    }

    private string Model => (NrVersion.SelectedItem as ComboBoxItem)?.Tag as string ?? "original";
    private string Vendor => Model.StartsWith("amd-", StringComparison.Ordinal) ? "AMD" : "NVIDIA";
    private string? EnginePath => engines.GetValueOrDefault(Vendor);
    private int ActiveCount => NrSwitch.IsChecked == true ? layers.Count(l => l.Enabled) : 0;
    private bool CancelRequested => exportCancellation?.IsCancellationRequested == true;

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
        AddButton.IsEnabled = EngineButton.IsEnabled = NrSwitch.IsEnabled = NrVersion.IsEnabled = LayerCards.IsEnabled = Codec.IsEnabled = !busy;
        RemoveButton.IsEnabled = !busy && clip is not null;
        ClearButton.IsEnabled = !busy && clips.Count > 0;
        AddLayerButton.IsEnabled = !busy && layers.Count < LayerLimit;

        if (adapterPath is null)
        {
            EngineButton.Visibility = Visibility.Collapsed;
            EngineLabel.Text = "Adaptador Engine.exe não encontrado";
        }
        else
        {
            EngineButton.Visibility = EnginePath is null ? Visibility.Visible : Visibility.Collapsed;
            EngineLabel.Text = EnginePath is null
                ? $"Motor de NR · pacote {Vendor} não localizado"
                : $"Motor de NR · {Vendor}";
        }

        LayerSummary.Text = ActiveCount switch
        {
            0 => "Melhoria NR desativada",
            1 => "1 camada ativa",
            _ => $"{ActiveCount} camadas em sequência"
        };
        PlayButton.IsEnabled = ready;
        TrimTrack.IsEnabled = ready && clip?.Duration > 0;
        StartHandle.IsEnabled = EndHandle.IsEnabled = StartTime.IsEnabled = EndTime.IsEnabled
            = MarkStartButton.IsEnabled = MarkEndButton.IsEnabled
            = ResetTrimButton.IsEnabled = !busy && TrimTrack.IsEnabled;
        ExportButton.IsEnabled = !busy && clips.Count > 0 && adapterPath is not null && EnginePath is not null;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.IsEnabled = busy && !CancelRequested;
        ShowOutputButton.Visibility = outputFolder is null ? Visibility.Collapsed : Visibility.Visible;
        DrawTrim();
    }

    private void ResizeTrim(object sender, SizeChangedEventArgs e) => DrawTrim();

    private void DrawTrim()
    {
        if (clip is not { Duration: > 0 } current)
        {
            TrimRange.Width = 0;
            StartHandle.Visibility = EndHandle.Visibility = Playhead.Visibility = Visibility.Hidden;
            return;
        }
        StartHandle.Visibility = EndHandle.Visibility = Playhead.Visibility = Visibility.Visible;
        var x1 = TimeToTrack(current.Trim.Start, current.Duration);
        var x2 = TimeToTrack(current.Trim.EndAt(current.Duration), current.Duration);
        Canvas.SetLeft(TrimRange, x1);
        Canvas.SetLeft(StartHandle, x1 - StartHandle.Width);
        Canvas.SetLeft(EndHandle, x2);
        TrimRange.Width = Math.Max(0, x2 - x1);
        DrawPosition();
    }

    private void DrawPosition()
    {
        var duration = clip?.Duration ?? 0;
        Canvas.SetLeft(Playhead, duration > 0 ? TimeToTrack(position, duration) - 1 : 0);
        TimeLabel.Text = $"{TrimSelection.Format(position)} / {TrimSelection.Format(duration)}";
    }

    private void RefreshTrimFields()
    {
        var duration = clip?.Duration ?? 0;
        var trim = clip?.Trim ?? default;
        refreshingFields = true;
        StartTime.Text = TrimSelection.Format(trim.Start, true);
        EndTime.Text = TrimSelection.Format(trim.EndAt(duration), true);
        refreshingFields = false;
        TrimLabel.Text = duration <= 0 ? "" : trim == default
            ? "Vídeo inteiro"
            : $"Duração do corte · {TrimSelection.Format(trim.EndAt(duration) - trim.Start, true)}";
        DrawTrim();
    }

    private void AddFiles(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Adicionar vídeos", Filter = VideoFilter, Multiselect = true };
        if (dialog.ShowDialog(this) == true) AddPaths(dialog.FileNames);
    }

    private void DropFiles(object sender, DragEventArgs e)
    {
        if (!busy && e.Data.GetData(DataFormats.FileDrop) is string[] paths) AddPaths(paths);
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        ClipItem? first = null;
        foreach (var path in paths.Where(File.Exists).Select(Path.GetFullPath))
        {
            if (clips.Any(c => c.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) continue;
            var item = new ClipItem(path);
            clips.Add(item);
            first ??= item;
        }
        if (first is not null)
        {
            Queue.SelectedItem = first;
            Status.Text = "Ajuste as camadas ou marque um trecho na linha do tempo.";
        }
        UpdateControls();
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

    private void LoadMedia(ClipItem? target)
    {
        clock.Stop();
        loadingClip = target;
        ready = playing = false;
        position = 0;
        Player.Close();
        Player.Source = target is null ? null : new Uri(target.Path, UriKind.Absolute);
        VideoTag.Visibility = Visibility.Collapsed;
        VideoPlaceholder.Visibility = Visibility.Visible;
        VideoMessage.Text = target is null ? "Abra um vídeo para visualizar" : "Carregando vídeo…";
        PlayButton.Content = "Reproduzir";
        if (target is not null) { Player.Play(); Player.Pause(); }
        DrawPosition();
    }

    private void MediaReady(object sender, RoutedEventArgs e)
    {
        if (clip is null || !ReferenceEquals(clip, loadingClip)) return;
        ready = true;
        if (Player.NaturalDuration.HasTimeSpan)
            clip.Duration = Player.NaturalDuration.TimeSpan.TotalSeconds;
        Player.Pause();
        position = clip.Trim.Start > 0 ? Math.Min(clip.Trim.Start, clip.Duration) : Math.Min(.03, clip.Duration);
        if (position > 0) Player.Position = TimeSpan.FromSeconds(position);
        VideoPlaceholder.Visibility = Visibility.Collapsed;
        VideoTag.Visibility = Visibility.Visible;
        RefreshTrimFields();
        UpdateControls();
    }

    private void MediaFailure(object sender, ExceptionRoutedEventArgs e)
    {
        if (clip is null || !ReferenceEquals(clip, loadingClip)) return;
        clock.Stop();
        playing = ready = false;
        VideoTag.Visibility = Visibility.Collapsed;
        VideoPlaceholder.Visibility = Visibility.Visible;
        VideoMessage.Text = "O Windows não conseguiu reproduzir este vídeo.";
        UpdateControls();
    }

    private void MediaFinished(object sender, RoutedEventArgs e) => Pause();
    private void PlayClick(object sender, RoutedEventArgs e) => TogglePlayback();

    private void TogglePlayback()
    {
        if (!ready) return;
        if (playing) Pause();
        else
        {
            if (clip is { Duration: > 0 } current &&
                (position < current.Trim.Start || position >= current.Trim.EndAt(current.Duration) - .005))
                Seek(current.Trim.Start);
            Player.Play();
            playing = true;
            clock.Start();
            PlayButton.Content = "Pausar";
        }
    }

    private void Pause()
    {
        clock.Stop();
        if (ready) Player.Pause();
        playing = false;
        PlayButton.Content = "Reproduzir";
    }

    private void UpdateClock()
    {
        if (!ready || !playing || clip is null) return;
        position = Player.Position.TotalSeconds;
        var end = clip.Trim.EndAt(clip.Duration);
        if (clip.Duration > 0 && position >= end)
        {
            Pause();
            Seek(end);
            return;
        }
        DrawPosition();
    }

    private double TrackWidth => Math.Max(1, TrimTrack.ActualWidth - 2 * StartHandle.Width);
    private double TimeToTrack(double seconds, double duration) =>
        StartHandle.Width + TrackWidth * Math.Clamp(seconds / duration, 0, 1);

    private double MouseTime() => clip is { Duration: > 0 } current
        ? Math.Clamp((Mouse.GetPosition(TrimTrack).X - StartHandle.Width) / TrackWidth, 0, 1) * current.Duration : 0;

    private double MouseTimeRaw(double duration) =>
        (Mouse.GetPosition(TrimTrack).X - StartHandle.Width) / TrackWidth * duration;

    private void Seek(double seconds)
    {
        if (!ready || clip is null) return;
        position = Math.Clamp(seconds, 0, clip.Duration);
        Player.Position = TimeSpan.FromSeconds(position);
        DrawPosition();
    }

    private void ScrubDown(object sender, MouseButtonEventArgs e)
    {
        if (!TrimTrack.IsEnabled || !TrimTrack.CaptureMouse()) return;
        scrubbing = true;
        Pause();
        Seek(MouseTime());
        e.Handled = true;
    }

    private void ScrubMove(object sender, MouseEventArgs e)
    {
        if (scrubbing && e.LeftButton == MouseButtonState.Pressed) Seek(MouseTime());
    }

    private void ScrubUp(object sender, MouseButtonEventArgs e)
    {
        if (!scrubbing) return;
        Seek(MouseTime());
        TrimTrack.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void ScrubLost(object sender, MouseEventArgs e) => scrubbing = false;

    private void TrimDragStarted(object sender, DragStartedEventArgs e)
    {
        if (sender is not Thumb handle || clip is not { Duration: > 0 } current) return;
        var edge = Equals(handle.Tag, "start") ? current.Trim.Start : current.Trim.EndAt(current.Duration);
        trimPointerOffset = edge - MouseTimeRaw(current.Duration);
        Pause();
    }

    private void TrimDrag(object sender, DragDeltaEventArgs e)
    {
        if (sender is Thumb handle && clip is { Duration: > 0 } current)
            MoveTrimEdge(Equals(handle.Tag, "start"), MouseTimeRaw(current.Duration) + trimPointerOffset);
    }

    private void MoveTrimEdge(bool start, double time)
    {
        if (busy || clip is not { Duration: > 0 } current) return;
        current.Trim = current.Trim.MoveEdge(start, time, current.Duration);
        RefreshTrimFields();
        Seek(start ? current.Trim.Start : current.Trim.EndAt(current.Duration));
    }

    private void TimecodeLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox field) ApplyTimecode(field);
    }

    private void TimecodeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox field) return;
        ApplyTimecode(field);
        e.Handled = true;
    }

    private void ApplyTimecode(TextBox field)
    {
        if (refreshingFields || busy || clip is not { Duration: > 0 } current) return;
        var isStart = Equals(field.Tag, "start");
        var priorValue = isStart ? current.Trim.Start : current.Trim.EndAt(current.Duration);
        if (field.Text == TrimSelection.Format(priorValue, true)) return;
        if (!TrimSelection.TryParse(field.Text, out var time))
        {
            Status.Text = "Digite um tempo no formato HH:MM:SS.mmm.";
            RefreshTrimFields();
            return;
        }
        Pause();
        MoveTrimEdge(isStart, time);
    }

    private void MarkStartClick(object sender, RoutedEventArgs e)
    {
        if (ready && !busy) MoveTrimEdge(true, position);
    }

    private void MarkEndClick(object sender, RoutedEventArgs e)
    {
        if (ready && !busy) MoveTrimEdge(false, position);
    }

    private void ResetTrim(object sender, RoutedEventArgs e)
    {
        if (clip is null) return;
        clip.Trim = default;
        RefreshTrimFields();
    }

    private void EditorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBoxBase or ComboBox) return;
        if (!ready) return;

        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None)
        {
            if (e.Key == Key.Space && e.OriginalSource is not ButtonBase and not ListBoxItem)
            {
                TogglePlayback();
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

    private string[] EncodeActiveLayers() =>
        NrSwitch.IsChecked == true ? layers.Where(l => l.Enabled).Select(l => l.Encode()).ToArray() : [];

    private string OutputStem(ClipItem item) =>
        Path.GetFileNameWithoutExtension(item.Path) + (ActiveCount > 0 ? $"-NR-x{ActiveCount}" : "-exportado");

    private async void ExportClick(object sender, RoutedEventArgs e)
    {
        if (busy || adapterPath is null || EnginePath is not { } engine || clips.Count == 0) return;
        var jobs = clips.ToArray();
        string? single = null;
        string folder;

        if (jobs.Length == 1)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Salvar MP4",
                Filter = "Vídeo MP4|*.mp4",
                DefaultExt = ".mp4",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = OutputStem(jobs[0]),
                InitialDirectory = Path.GetDirectoryName(jobs[0].Path)
            };
            if (dialog.ShowDialog(this) != true) return;
            single = Path.GetFullPath(dialog.FileName);
            if (single.Equals(Path.GetFullPath(jobs[0].Path), StringComparison.OrdinalIgnoreCase))
            {
                Status.Text = "O arquivo de saída não pode ser igual ao vídeo de origem.";
                return;
            }
            folder = Path.GetDirectoryName(single)!;
        }
        else
        {
            var dialog = new OpenFolderDialog { Title = "Pasta para os vídeos exportados" };
            if (dialog.ShowDialog(this) != true) return;
            folder = dialog.FolderName;
        }

        Pause();
        var encodedLayers = EncodeActiveLayers();
        var codec = Codec.SelectedIndex == 0 ? "h264" : "hevc";
        var model = Model;
        foreach (var item in jobs) item.Status = "Na fila";

        await RunExportTask("Preparando exportação…", async cancellationToken =>
        {
            var saved = 0;
            for (var i = 0; i < jobs.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = jobs[i];
                var destination = single ?? VideoExporter.UniqueOutputPath(folder, OutputStem(item));
                var activity = $"Exportando {i + 1}/{jobs.Length}";
                item.Status = "Exportando";
                Progress.Value = 0;
                try
                {
                    var request = new ExportRequest(
                        adapterPath, engine, item.Path, destination,
                        item.Trim.Start, item.Trim.End, codec, model, encodedLayers);

                    await exporter.ExportAsync(request, value =>
                    {
                        Progress.Value = value;
                        if (!CancelRequested)
                            Status.Text = value == 0
                                ? $"{activity} · preparando GPU…"
                                : $"{activity} · {Math.Min(99, value / 100)}%";
                    }, cancellationToken);

                    item.Status = "Concluído";
                    saved++;
                    outputFolder = folder;
                }
                catch (OperationCanceledException)
                {
                    item.Status = "Cancelado";
                    throw;
                }
                catch (Exception error)
                {
                    item.Status = "Falhou";
                    exportErrors.Add($"{item.Name}: {error.Message}");
                }
            }

            Status.Text = $"{saved} de {jobs.Length} vídeos exportados.";
            if (exportErrors.Count > 0 && !closePending)
                MessageBox.Show(this, string.Join("\n\n", exportErrors), "Falhas de exportação", MessageBoxButton.OK, MessageBoxImage.Warning);
        });
    }

    private async Task RunExportTask(string message, Func<CancellationToken, Task> action)
    {
        busy = true;
        exportErrors.Clear();
        Status.Text = message;
        Progress.Value = 0;
        exportCancellation = new CancellationTokenSource();
        UpdateControls();
        try
        {
            await action(exportCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Progress.Value = 0;
            Status.Text = "Processamento cancelado.";
        }
        catch (Exception error)
        {
            Progress.Value = 0;
            Status.Text = error.Message;
        }
        finally
        {
            exportCancellation.Dispose();
            exportCancellation = null;
            busy = false;
            UpdateControls();
            if (closePending) Close();
        }
    }

    private void CancelWork(object sender, RoutedEventArgs e) => RequestCancel();

    private void RequestCancel()
    {
        if (exportCancellation is null || exportCancellation.IsCancellationRequested) return;
        Status.Text = "Cancelando…";
        CancelButton.IsEnabled = false;
        exportCancellation.Cancel();
    }

    private void OpenOutput(object sender, RoutedEventArgs e)
    {
        if (outputFolder is null) return;
        try { Process.Start(new ProcessStartInfo(outputFolder) { UseShellExecute = true }); }
        catch (Win32Exception error) { Status.Text = error.Message; }
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
        Status.Text = "Motor localizado.";
        UpdateControls();
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
            clock.Stop();
            Player.Close();
        }
        base.OnClosing(e);
    }
}
