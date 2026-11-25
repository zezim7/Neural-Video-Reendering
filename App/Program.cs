using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace VideoNR;

public partial class App : Application { }

public partial class Editor : Window
{
    private const int LayerLimit = 4;
    private const string VideoFilter = "Vídeos|*.mp4;*.mkv;*.mov;*.webm;*.avi;*.m4v;*.mts;*.m2ts|Todos os arquivos|*.*";
    private static readonly (string Name, int Policy)[] Resolutions =
        [("1080p", 0), ("1440p", 5), ("900p", 4), ("720p", 3), ("480p", 2), ("Nativa", 1)];
    private readonly ObservableCollection<ClipItem> clips = [];
    private readonly List<Layer> layers = [new()];
    private readonly Dictionary<string, string> engines = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private string? scratch;
    private string? cancelPath;
    private string? outputFolder;
    private ClipItem? clip;
    private bool busy, playing, ready, scrubbing, refreshingFields, closePending;
    private double position, trimPointerOffset;

    private sealed class ClipItem(string path) : INotifyPropertyChanged
    {
        public string Path { get; } = path;
        public string Name => System.IO.Path.GetFileName(Path);
        public double Duration { get; set; }
        public TrimSelection Trim { get; set; }
        private string status = "Na fila";
        public string Status
        {
            get => status;
            set { status = value; PropertyChanged?.Invoke(this, new(nameof(Status))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed record Layer
    {
        public bool Enabled { get; set; } = true;
        public bool Expanded { get; set; }
        public int Resolution { get; set; }
        public double Intensity { get; set; } = 1;
        public double Tone { get; set; } = 1;
        public double Structure { get; set; } = 1;
        public string Encode() => FormattableString.Invariant($"{Resolutions[Resolution].Policy} {Intensity:F2} {Tone:F2} {Structure:F2}");
    }

    public Editor()
    {
        InitializeComponent();
        Queue.ItemsSource = clips;
        NrVersion.SelectedIndex = 0;
        foreach (var vendor in new[] { "NVIDIA", "AMD" })
            if (FindEngine(vendor) is { } path) engines[vendor] = path;
        if (!engines.ContainsKey("NVIDIA") && engines.ContainsKey("AMD"))
            NrVersion.SelectedIndex = 3;

        DrawLayers();
        RefreshTrimFields();
        UpdateControls();
        clock.Tick += (_, _) => UpdateClock();
        clock.Start();
        Loaded += (_, _) => AddPaths(Environment.GetCommandLineArgs().Skip(1));
    }

    private string Model => (NrVersion.SelectedItem as ComboBoxItem)?.Tag as string ?? "original";
    private string Vendor => Model.StartsWith("amd-", StringComparison.Ordinal) ? "AMD" : "NVIDIA";
    private string? EnginePath => engines.GetValueOrDefault(Vendor);
    private int ActiveCount => NrSwitch.IsChecked == true ? layers.Count(l => l.Enabled) : 0;
    private bool CancelRequested => cancelPath is not null && File.Exists(cancelPath);

    private void UpdateControls()
    {
        QueueTitle.Text = $"Fila · {clips.Count}";
        EmptyQueue.Visibility = clips.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Queue.IsEnabled = !busy;
        AddButton.IsEnabled = EngineButton.IsEnabled = NrSwitch.IsEnabled = NrVersion.IsEnabled = LayerCards.IsEnabled = Codec.IsEnabled = !busy;
        RemoveButton.IsEnabled = !busy && clip is not null;
        ClearButton.IsEnabled = !busy && clips.Count > 0;
        AddLayerButton.IsEnabled = !busy && layers.Count < LayerLimit;
        EngineLabel.Text = EnginePath is null ? $"Motor de NR · pacote {Vendor} não localizado" : $"Motor de NR · {Vendor}";
        LayerSummary.Text = ActiveCount == 1 ? "1 camada ativa" : $"{ActiveCount} camadas em sequência";
        PlayButton.IsEnabled = ready;
        TrimTrack.IsEnabled = ready && clip?.Duration > 0;
        StartHandle.IsEnabled = EndHandle.IsEnabled = StartTime.IsEnabled = EndTime.IsEnabled
            = ResetTrimButton.IsEnabled = !busy && TrimTrack.IsEnabled;
        ExportButton.IsEnabled = !busy && clips.Count > 0 && EnginePath is not null;
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
        foreach (var path in paths.Where(File.Exists).Select(System.IO.Path.GetFullPath))
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
        LoadMedia(clip?.Path);
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

    private void DrawLayers()
    {
        LayerCards.Children.Clear();
        var panelStyle = (Style)FindResource("PanelStyle");
        for (var i = 0; i < layers.Count; i++)
        {
            var layer = layers[i];
            var index = i;
            var stack = new StackPanel();
            var card = new Border { Style = panelStyle, Margin = new Thickness(0, 0, 0, 7), Padding = new Thickness(8), Child = stack };
            var row = new DockPanel { LastChildFill = true };
            stack.Children.Add(row);
            var toggle = new CheckBox { IsChecked = layer.Enabled, Style = (Style)FindResource("Switch"),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            System.Windows.Automation.AutomationProperties.SetName(toggle, $"Ativar camada NR {i + 1}");
            toggle.Click += (_, _) => { layer.Enabled = toggle.IsChecked == true; UpdateControls(); };
            DockPanel.SetDock(toggle, Dock.Right);
            row.Children.Add(toggle);

            var duplicate = new Button { Content = "⧉", ToolTip = "Duplicar camada", Padding = new Thickness(6, 3, 6, 3),
                VerticalAlignment = VerticalAlignment.Center, IsEnabled = layers.Count < LayerLimit };
            System.Windows.Automation.AutomationProperties.SetName(duplicate, $"Duplicar camada NR {i + 1}");
            duplicate.Click += (_, _) => { layers.Insert(index + 1, layer with { Expanded = false }); LayersChanged(); };
            DockPanel.SetDock(duplicate, Dock.Right);
            row.Children.Add(duplicate);

            var caption = $"Camada NR {i + 1}";
            var hint = new TextBlock { Text = LayerDetails(layer), Style = (Style)FindResource("Secondary"), FontSize = 11 };
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = caption, FontWeight = FontWeights.SemiBold });
            text.Children.Add(hint);
            var edit = new Button { Background = Brushes.Transparent, BorderBrush = Brushes.Transparent,
                Padding = new Thickness(3), Content = text, ToolTip = "Abrir ajustes da camada" };
            System.Windows.Automation.AutomationProperties.SetName(edit, $"{caption} — ajustes");
            row.Children.Add(edit);

            var settings = new StackPanel { Margin = new Thickness(4, 8, 4, 2),
                Visibility = layer.Expanded ? Visibility.Visible : Visibility.Collapsed };
            edit.Click += (_, _) =>
            {
                layer.Expanded = !layer.Expanded;
                settings.Visibility = layer.Expanded ? Visibility.Visible : Visibility.Collapsed;
            };
            settings.Children.Add(new TextBlock { Text = "Resolução do processamento", Margin = new Thickness(0, 0, 0, 4) });
            var resolution = new ComboBox { ItemsSource = Resolutions.Select(r => r.Name).ToArray(), SelectedIndex = layer.Resolution };
            resolution.SelectionChanged += (_, _) =>
            {
                layer.Resolution = resolution.SelectedIndex;
                hint.Text = LayerDetails(layer);
            };
            settings.Children.Add(resolution);
            settings.Children.Add(LayerSlider("Intensidade", layer.Intensity, value => { layer.Intensity = value; hint.Text = LayerDetails(layer); }));
            settings.Children.Add(LayerSlider("Tom", layer.Tone, value => layer.Tone = value));
            settings.Children.Add(LayerSlider("Estrutura", layer.Structure, value => layer.Structure = value));
            var remove = new Button { Content = "Remover camada", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 5, 0, 0) };
            remove.Click += (_, _) => { layers.RemoveAt(index); LayersChanged(); };
            settings.Children.Add(remove);
            stack.Children.Add(settings);
            LayerCards.Children.Add(card);
        }
    }

    private static string LayerDetails(Layer layer) => $"{Resolutions[layer.Resolution].Name} · intensidade {layer.Intensity:F2}";

    private FrameworkElement LayerSlider(string title, double initial, Action<double> update)
    {
        var panel = new DockPanel { Margin = new Thickness(0, 7, 0, 0) };
        var label = new TextBlock { Text = $"{title} {initial:F2}", Width = 111 };
        panel.Children.Add(label);
        var slider = new Slider { Minimum = 0, Maximum = 1, Value = initial, TickFrequency = .01, IsSnapToTickEnabled = true };
        System.Windows.Automation.AutomationProperties.SetName(slider, title);
        slider.ValueChanged += (_, _) =>
        {
            label.Text = $"{title} {slider.Value:F2}";
            update(slider.Value);
        };
        panel.Children.Add(slider);
        return panel;
    }

    private void AddLayerClick(object sender, RoutedEventArgs e)
    {
        if (layers.Count >= LayerLimit) return;
        layers.Add(new Layer());
        LayersChanged();
    }

    private void LayersChanged() { DrawLayers(); UpdateControls(); }
    private void ToggleNr(object sender, RoutedEventArgs e) => UpdateControls();

    private void ChangeVersion(object sender, SelectionChangedEventArgs e) => UpdateControls();

    private void LoadMedia(string? path)
    {
        ready = playing = false;
        position = 0;
        Player.Close();
        Player.Source = path is null ? null : new Uri(path, UriKind.Absolute);
        VideoTag.Visibility = Visibility.Collapsed;
        VideoPlaceholder.Visibility = Visibility.Visible;
        VideoMessage.Text = path is null ? "Abra um vídeo para visualizar" : "Carregando vídeo…";
        PlayButton.Content = "Reproduzir";
        if (path is not null) { Player.Play(); Player.Pause(); }
        DrawPosition();
    }

    private void MediaReady(object sender, RoutedEventArgs e)
    {
        if (clip is null) return;
        ready = true;
        if (Player.NaturalDuration.HasTimeSpan)
            clip.Duration = Player.NaturalDuration.TimeSpan.TotalSeconds;
        Player.Pause();
        if (clip.Duration > .03) Player.Position = TimeSpan.FromMilliseconds(30);
        VideoPlaceholder.Visibility = Visibility.Collapsed;
        VideoTag.Visibility = Visibility.Visible;
        RefreshTrimFields();
        UpdateControls();
    }

    private void MediaFailure(object sender, ExceptionRoutedEventArgs e)
    {
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
            PlayButton.Content = "Pausar";
        }
    }

    private void Pause()
    {
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

    private double MouseTimeRaw(double duration) =>
        (Mouse.GetPosition(TrimTrack).X - StartHandle.Width) / TrackWidth * duration;

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

    private void ResetTrim(object sender, RoutedEventArgs e)
    {
        if (clip is null) return;
        clip.Trim = default;
        RefreshTrimFields();
    }

    private void EditorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is ButtonBase or TextBoxBase or ComboBox or ListBoxItem || Keyboard.Modifiers != ModifierKeys.None)
            return;
        if (e.Key == Key.Space && ready) { TogglePlayback(); e.Handled = true; }
    }
    private string[] Snapshot() => NrSwitch.IsChecked == true ? layers.Where(l => l.Enabled).Select(l => l.Encode()).ToArray() : [];

    private async void ExportClick(object sender, RoutedEventArgs e)
    {
        if (busy || EnginePath is not { } engine || clips.Count == 0) return;
        var jobs = clips.ToArray();
        string? single = null;
        string folder;
        if (jobs.Length == 1)
        {
            var dialog = new SaveFileDialog { Title = "Salvar MP4", Filter = "Vídeo MP4|*.mp4", DefaultExt = ".mp4",
                AddExtension = true, OverwritePrompt = false, FileName = OutputStem(jobs[0]),
                InitialDirectory = System.IO.Path.GetDirectoryName(jobs[0].Path) };
            if (dialog.ShowDialog(this) != true) return;
            single = dialog.FileName;
            if (File.Exists(single)) { Status.Text = "Escolha um nome de saída que ainda não exista."; return; }
            folder = System.IO.Path.GetDirectoryName(single)!;
        }
        else
        {
            var dialog = new OpenFolderDialog { Title = "Pasta para os vídeos exportados" };
            if (dialog.ShowDialog(this) != true) return;
            folder = dialog.FolderName;
        }

        Pause();
        var snapshot = Snapshot();
        var codec = Codec.SelectedIndex == 0 ? "h264" : "hevc";
        var model = Model;
        foreach (var item in jobs) item.Status = "Na fila";
        await RunTask("Preparando exportação…", async () =>
        {
            var saved = 0;
            for (var i = 0; i < jobs.Length; i++)
            {
                if (CancelRequested) throw new OperationCanceledException();
                var item = jobs[i];
                var destination = single ?? UniqueOutput(folder, OutputStem(item));
                item.Status = "Exportando";
                Progress.Value = 0;
                try
                {
                    await ProcessVideo(engine, item.Path, destination, item.Trim.Start, item.Trim.End, codec, model, snapshot, $"Exportando {i + 1}/{jobs.Length}");
                    item.Status = "Concluído";
                    saved++;
                    outputFolder = folder;
                }
                catch (OperationCanceledException) { item.Status = "Cancelado"; throw; }
                catch (Exception error)
                {
                    item.Status = "Falhou";
                    itemStatusErrors.Add($"{item.Name}: {error.Message}");
                }
            }
            Status.Text = $"{saved} de {jobs.Length} vídeos exportados.";
            if (itemStatusErrors.Count > 0 && !closePending)
                MessageBox.Show(this, string.Join("\n\n", itemStatusErrors), "Falhas de exportação", MessageBoxButton.OK, MessageBoxImage.Warning);
        });
    }

    private readonly List<string> itemStatusErrors = [];
    private string OutputStem(ClipItem item) => System.IO.Path.GetFileNameWithoutExtension(item.Path) + (ActiveCount > 0 ? $"-NR-x{ActiveCount}" : "-exportado");
    private static string UniqueOutput(string directory, string stem)
    {
        var path = System.IO.Path.Combine(directory, stem + ".mp4");
        for (var i = 2; File.Exists(path); i++) path = System.IO.Path.Combine(directory, $"{stem}-{i}.mp4");
        return path;
    }

    private async Task RunTask(string message, Func<Task> action)
    {
        busy = true;
        itemStatusErrors.Clear();
        Status.Text = message;
        Progress.Value = 0;
        try
        {
            if (scratch is null)
                scratch = Directory.CreateTempSubdirectory("VideoNR-").FullName;
            cancelPath = System.IO.Path.Combine(scratch, $"cancel-{Guid.NewGuid():N}");
            UpdateControls();
            await action();
        }
        catch (OperationCanceledException) { Progress.Value = 0; Status.Text = "Processamento cancelado."; }
        catch (Exception error) { Progress.Value = 0; Status.Text = error.Message; }
        finally
        {
            if (cancelPath is not null)
                try { File.Delete(cancelPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            cancelPath = null;
            busy = false;
            UpdateControls();
            if (closePending) Close();
        }
    }

    private async Task ProcessVideo(string engine, string input, string output, double start, double end, string codec,
        string model, string[] recipe, string activity)
    {
        var path = System.IO.Path.Combine(scratch!, "Engine.exe");
        if (!File.Exists(path))
        {
            using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("VideoNR.Engine.exe")
                ?? throw new InvalidOperationException("Adaptador do motor não encontrado.");
            var temporary = path + ".tmp";
            using (var stream = File.Create(temporary)) await resource.CopyToAsync(stream);
            File.Move(temporary, path, true);
        }
        if (CancelRequested) throw new OperationCanceledException();

        var info = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8 };
        foreach (var argument in new[] { engine, input, output, cancelPath!, codec,
            start.ToString("R", CultureInfo.InvariantCulture), end.ToString("R", CultureInfo.InvariantCulture), model }.Concat(recipe))
            info.ArgumentList.Add(argument);

        using var process = Process.Start(info) ?? throw new IOException("Não foi possível abrir o motor de exportação.");
        string? detail = null, worker = null;
        while (await process.StandardOutput.ReadLineAsync() is { } line)
        {
            if (line.StartsWith("PROGRESS ", StringComparison.Ordinal) && int.TryParse(line.AsSpan(9), out var value))
            {
                Progress.Value = Math.Clamp(value, 0, 10000);
                if (!CancelRequested) Status.Text = value == 0 ? $"{activity} · preparando GPU…" : $"{activity} · {Math.Min(99, value / 100)}%";
            }
            else if (line.StartsWith("WORKER ", StringComparison.Ordinal)) worker = line[7..];
            else if (!string.IsNullOrWhiteSpace(line)) detail = line;
        }
        await process.WaitForExitAsync();
        if (process.ExitCode == 3) throw new OperationCanceledException();
        if (process.ExitCode != 0 || new FileInfo(output) is not { Exists: true, Length: > 0 })
        {
            var reason = detail ?? "Confira o codec de entrada e a compatibilidade da GPU.";
            throw new IOException($"O motor não concluiu o processamento. {reason}" + (worker is null ? "" : $" (tarefa {worker})"));
        }
        Progress.Value = 10000;
    }

    private void CancelWork(object sender, RoutedEventArgs e) => RequestCancel();
    private void RequestCancel()
    {
        if (cancelPath is null) return;
        try { File.WriteAllText(cancelPath, ""); Status.Text = "Cancelando…"; CancelButton.IsEnabled = false; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Status.Text = error.Message; }
    }

    private void OpenOutput(object sender, RoutedEventArgs e)
    {
        if (outputFolder is null) return;
        try { Process.Start(new ProcessStartInfo(outputFolder) { UseShellExecute = true }); }
        catch (Win32Exception error) { Status.Text = error.Message; }
    }

    private void FindEngineClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = $"Localizar pacote de processamento {Vendor} 2.2.0" };
        if (dialog.ShowDialog(this) != true) return;
        var path = EngineIn(dialog.FolderName, Vendor);
        if (path is null) { Status.Text = $"Selecione uma pasta com o pacote de processamento {Vendor} 2.2.0."; return; }
        engines[Vendor] = path;
        Status.Text = "Motor localizado.";
        UpdateControls();
    }

    private static string? FindEngine(string vendor)
    {
        var directory = AppContext.BaseDirectory;
        return EngineIn(System.IO.Path.Combine(directory, "motor"), vendor) ?? EngineIn(directory, vendor);
    }

    private static string? EngineIn(string root, string vendor)
    {
        foreach (var folder in new[] { root, System.IO.Path.Combine(root, "versions", "2.2.0") })
        {
            var worker = System.IO.Path.Combine(folder, "VideoNR-RenderWorker.exe");
            var executable = File.Exists(worker) ? worker : System.IO.Path.Combine(folder, "veyra_qml_ui.exe");
            if (!File.Exists(executable)) continue;
            try
            {
                using var manifest = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(folder, "package-manifest.json")));
                var value = manifest.RootElement;
                if (value.GetProperty("version").GetString() == "2.2.0"
                    && value.GetProperty("gpuPackage").GetString()?.Equals(vendor, StringComparison.OrdinalIgnoreCase) == true)
                    return executable;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException) { }
        }
        return null;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (busy) { e.Cancel = true; closePending = true; RequestCancel(); }
        else
        {
            clock.Stop();
            Player.Close();
            if (scratch is not null)
                try { Directory.Delete(scratch, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        base.OnClosing(e);
    }
}

