using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Shell;
using Microsoft.Win32;
using VideoNR.Imaging;
using VideoNR.Models;
using VideoNR.Services;

namespace VideoNR;

public partial class Editor
{
    private string OutputStem(string sourcePath, int layerCount, bool isSequence)
    {
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        if (isSequence)
            return baseName + (layerCount > 0 ? "-sequencia-NR" : "-sequencia");
        return baseName + (layerCount > 0 ? $"-NR-x{layerCount}" : "-exportado");
    }

    private async void ExportClick(object sender, RoutedEventArgs e)
    {
        if (busy || adapterPath is null || EnginePath is not { } engine) return;

        if (IsBatchExportMode)
            await ExportBatchAsync(engine);
        else
            await ExportSequenceAsync(engine);
    }

    private async Task ExportSequenceAsync(string engine)
    {
        var slices = sequence.ResolveComposition()
            .Where(s => s.Duration >= TimelineSegment.MinDuration * 0.5)
            .Select(s => s with { Segment = s.Segment.Clone(preserveId: true) })
            .ToList();

        if (slices.Count == 0)
        {
            Status.Text = "Nenhum trecho ativo ou visível nas faixas para exportar.";
            return;
        }

        var globalProfile = GetGlobalNrProfile();
        var anyNrLayers = slices.Any(s => GetEffectiveNrProfile(s.Segment).ActiveLayerCount > 0);
        var defaultDir = Path.GetDirectoryName(slices[0].Segment.SourcePath);

        var dialog = new SaveFileDialog
        {
            Title = "Exportar sequência MP4",
            Filter = "Vídeo MP4|*.mp4",
            DefaultExt = ".mp4",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = OutputStem(slices[0].Segment.SourcePath, anyNrLayers ? Math.Max(1, globalProfile.ActiveLayerCount) : 0, isSequence: slices.Count > 1),
            InitialDirectory = defaultDir
        };
        if (dialog.ShowDialog(this) != true) return;

        var destination = Path.GetFullPath(dialog.FileName);
        if (slices.Any(s => destination.Equals(Path.GetFullPath(s.Segment.SourcePath), StringComparison.OrdinalIgnoreCase)))
        {
            Status.Text = "O arquivo de saída não pode sobrescrever um dos vídeos de origem.";
            return;
        }

        var folder = Path.GetDirectoryName(destination)!;
        var codec = HevcRadio.IsChecked == true ? "hevc" : "h264";
        var model = Model;
        Pause();

        await RunExportTask("Preparando exportação da sequência…", async cancellationToken =>
        {
            if (slices.Count == 1)
            {
                var slice = slices[0];
                var effectiveLayers = GetEffectiveNrProfile(slice.Segment).EncodeActiveLayers();
                var request = new ExportRequest(
                    adapterPath!, engine, slice.Segment.SourcePath, destination,
                    slice.SourceStart, slice.SourceEnd, codec, model, effectiveLayers);

                await exporter.ExportAsync(request, value =>
                {
                    ReportSequenceProgress("Exportando sequência", value);
                }, cancellationToken);

                outputFolder = folder;
                Status.Text = $"Sequência exportada em {Path.GetFileName(destination)}.";
                return;
            }

            var tempDir = Path.Combine(Path.GetTempPath(), $"VideoNR-seq-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            var renderedParts = new List<string>(slices.Count);
            try
            {
                for (var i = 0; i < slices.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var slice = slices[i];
                    var partPath = Path.Combine(tempDir, $"part-{i + 1:D3}.mp4");
                    var effectiveLayers = GetEffectiveNrProfile(slice.Segment).EncodeActiveLayers();
                    var request = new ExportRequest(
                        adapterPath!, engine, slice.Segment.SourcePath, partPath,
                        slice.SourceStart, slice.SourceEnd, codec, model, effectiveLayers);

                    var partIndex = i;
                    var activity = $"Trecho {i + 1}/{slices.Count}";
                    await exporter.ExportAsync(request, value =>
                    {
                        var overall = (int)Math.Round(((partIndex + value / 10000.0) / slices.Count) * 8000.0);
                        ReportSequenceProgress(activity, overall);
                    }, cancellationToken);

                    renderedParts.Add(partPath);
                }

                cancellationToken.ThrowIfCancellationRequested();
                ReportSequenceProgress("Unindo faixas em 1 MP4", 8100);
                await VideoConcatenator.ConcatenateAsync(
                    renderedParts,
                    destination,
                    preferHevc: codec == "hevc",
                    value =>
                    {
                        var overall = 8000 + (int)Math.Round(value * 0.2);
                        ReportSequenceProgress("Unindo trechos e áudio", overall);
                    },
                    cancellationToken);

                outputFolder = folder;
                Status.Text = $"Sequência ({slices.Count} partes) exportada em {Path.GetFileName(destination)}.";
            }
            finally
            {
                TryDeleteDirectory(tempDir);
            }
        });
    }

    private async Task ExportBatchAsync(string engine)
    {
        if (clips.Count == 0) return;
        var jobs = clips.ToArray();
        string? single = null;
        string folder;

        var globalProfile = GetGlobalNrProfile();
        var encodedLayers = globalProfile.EncodeActiveLayers();

        if (jobs.Length == 1)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Salvar MP4",
                Filter = "Vídeo MP4|*.mp4",
                DefaultExt = ".mp4",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = OutputStem(jobs[0].Path, encodedLayers.Length, isSequence: false),
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
        var codec = HevcRadio.IsChecked == true ? "hevc" : "h264";
        var model = Model;
        foreach (var item in jobs) item.Status = "Na fila";

        await RunExportTask("Preparando exportação em lote…", async cancellationToken =>
        {
            var saved = 0;
            for (var i = 0; i < jobs.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = jobs[i];
                var destination = single ?? VideoExporter.UniqueOutputPath(folder, OutputStem(item.Path, encodedLayers.Length, isSequence: false));
                var activity = $"Exportando {i + 1}/{jobs.Length}";
                item.Status = "Exportando";
                Progress.Value = 0;
                try
                {
                    var request = new ExportRequest(
                        adapterPath!, engine, item.Path, destination,
                        item.Trim.Start, item.Trim.End, codec, model, encodedLayers);

                    await exporter.ExportAsync(request, value =>
                    {
                        Progress.Value = value;
                        TaskbarInfo.ProgressState = TaskbarItemProgressState.Normal;
                        TaskbarInfo.ProgressValue = Math.Clamp((i + value / 10000.0) / jobs.Length, 0, 1);
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

    private void ReportSequenceProgress(string label, int value)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ReportSequenceProgress(label, value));
            return;
        }
        var clamped = Math.Clamp(value, 0, 10000);
        Progress.Value = clamped;
        TaskbarInfo.ProgressState = TaskbarItemProgressState.Normal;
        TaskbarInfo.ProgressValue = clamped / 10000.0;
        if (!CancelRequested)
            Status.Text = clamped == 0
                ? $"{label} · preparando GPU…"
                : $"{label} · {Math.Min(99, clamped / 100)}%";
    }

    private async Task RunExportTask(string message, Func<CancellationToken, Task> action)
    {
        busy = true;
        exportErrors.Clear();
        Status.Text = message;
        Progress.Value = 0;
        TaskbarInfo.ProgressState = TaskbarItemProgressState.Indeterminate;
        TaskbarInfo.ProgressValue = 0;
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
            TaskbarInfo.ProgressState = TaskbarItemProgressState.None;
            TaskbarInfo.ProgressValue = 0;
            exportCancellation.Dispose();
            exportCancellation = null;
            busy = false;
            UpdateControls();
            if (closePending) Close();
        }
    }

    private bool TryGetPrintContext(SegmentNrProfile? overrideProfile, out string adapter, out string engine, out string[] encodedLayers)
    {
        adapter = adapterPath ?? "";
        engine = EnginePath ?? "";
        encodedLayers = [];
        if (busy || adapterPath is null || EnginePath is null) return false;

        var profile = overrideProfile ?? GetGlobalNrProfile();
        encodedLayers = profile.EncodeActiveLayers();
        if (encodedLayers.Length == 0)
        {
            Status.Text = "Ative a melhoria NR e ao menos 1 camada para aplicar NR no print.";
            return false;
        }
        return true;
    }

    private void ReportPrintProgress(string label, int value) => ReportSequenceProgress(label, value);

    private void ShowPrintPreview(
        PrintResult result,
        string modelLabel,
        int layerCount,
        string fileNameStem,
        string? initialDirectory)
    {
        var preview = new PrintPreviewWindow(result, modelLabel, layerCount, fileNameStem, initialDirectory)
        {
            Owner = this
        };
        preview.ShowDialog();
        if (!string.IsNullOrEmpty(preview.SavedFolder))
        {
            outputFolder = preview.SavedFolder;
            UpdateControls();
        }
    }

    private async void FramePrintClick(object sender, RoutedEventArgs e)
    {
        string targetPath;
        double capturePosition;
        double sourceDuration;
        SegmentNrProfile profile;

        if (sequence.LocateCompositionAt(timelinePosition) is { } slice)
        {
            targetPath = slice.Segment.SourcePath;
            capturePosition = slice.SourceStart + Math.Max(0, timelinePosition - slice.TimelineStart);
            sourceDuration = slice.Segment.SourceDuration;
            profile = GetEffectiveNrProfile(slice.Segment);
        }
        else if (sequence.LocateAt(timelinePosition, selectedSegmentId) is { } hit)
        {
            targetPath = hit.Segment.SourcePath;
            capturePosition = hit.SourceTime;
            sourceDuration = hit.Segment.SourceDuration;
            profile = GetEffectiveNrProfile(hit.Segment);
        }
        else if (clip is not null)
        {
            targetPath = clip.Path;
            capturePosition = 0;
            sourceDuration = clip.Duration;
            profile = GetGlobalNrProfile();
        }
        else
        {
            return;
        }

        if (!TryGetPrintContext(profile, out var adapter, out var engine, out var encodedLayers)) return;
        Pause();

        var modelTag = Model;
        var modelLabel = SelectedModel.Label;
        var layerCount = encodedLayers.Length;

        PrintResult? result = null;
        await RunExportTask("Renderizando print do frame com NR…", async cancellationToken =>
        {
            result = await PrintProcessor.ProcessVideoFrameAsync(
                exporter,
                adapter,
                engine,
                targetPath,
                capturePosition,
                sourceDuration,
                modelTag,
                encodedLayers,
                value => ReportPrintProgress("Print do frame", value),
                cancellationToken);
            Status.Text = "Print do frame pronto.";
        });

        if (result is not null && !closePending)
        {
            var stem = Path.GetFileNameWithoutExtension(targetPath) + "-NR-print";
            ShowPrintPreview(result, modelLabel, layerCount, stem, Path.GetDirectoryName(targetPath));
        }
    }

    private async void ScreenPrintClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetPrintContext(null, out var adapter, out var engine, out var encodedLayers)) return;
        Pause();

        RawImage? captured = null;
        var captureFailed = false;
        try
        {
            Hide();
            await Task.Delay(220);
            var snapshot = WpfImageBridge.CaptureVirtualScreen();
            var overlay = new ScreenCaptureOverlay(snapshot);
            if (overlay.ShowDialog() == true)
                captured = overlay.CapturedImage;
        }
        catch (Exception error)
        {
            captureFailed = true;
            Status.Text = $"Falha ao capturar tela: {error.Message}";
        }
        finally
        {
            Show();
            Activate();
        }

        if (captureFailed) return;
        if (captured is null)
        {
            Status.Text = "Captura de tela cancelada.";
            return;
        }

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        await RunImagePrintAsync(adapter, engine, encodedLayers, captured, $"Print-NR-{stamp}", null);
    }

    private async void ImagePrintClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetPrintContext(null, out _, out _, out _)) return;
        var dialog = new OpenFileDialog
        {
            Title = "Selecionar imagem para aplicar NR",
            Filter = ImageFilter
        };
        if (dialog.ShowDialog(this) == true)
            await RunImagePrintFromFileAsync(dialog.FileName);
    }

    private async void PasteImagePrint()
    {
        if (!TryGetPrintContext(null, out var adapter, out var engine, out var encodedLayers)) return;
        RawImage? source = null;
        try
        {
            var bitmap = Clipboard.GetImage();
            if (bitmap is not null)
                source = WpfImageBridge.FromBitmapSource(bitmap, forceOpaqueAlpha: true);
        }
        catch (ExternalException)
        {
            Status.Text = "Não foi possível ler a imagem da área de transferência.";
            return;
        }

        if (source is null)
        {
            Status.Text = "Nenhuma imagem disponível na área de transferência.";
            return;
        }

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        await RunImagePrintAsync(adapter, engine, encodedLayers, source, $"Clipboard-NR-{stamp}", null);
    }

    private async Task RunImagePrintFromFileAsync(string path)
    {
        if (!TryGetPrintContext(null, out var adapter, out var engine, out var encodedLayers)) return;
        RawImage source;
        try
        {
            source = WpfImageBridge.LoadFromFile(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or FormatException or COMException)
        {
            Status.Text = $"Não foi possível abrir a imagem: {error.Message}";
            return;
        }

        var stem = Path.GetFileNameWithoutExtension(path) + "-NR";
        await RunImagePrintAsync(adapter, engine, encodedLayers, source, stem, Path.GetDirectoryName(path));
    }

    private async Task RunImagePrintAsync(
        string adapter,
        string engine,
        string[] encodedLayers,
        RawImage source,
        string fileNameStem,
        string? initialDirectory)
    {
        Pause();
        var modelTag = Model;
        var modelLabel = SelectedModel.Label;
        var layerCount = encodedLayers.Length;

        PrintResult? result = null;
        await RunExportTask("Renderizando print com NR…", async cancellationToken =>
        {
            result = await PrintProcessor.ProcessImageAsync(
                exporter,
                adapter,
                engine,
                source,
                modelTag,
                encodedLayers,
                value => ReportPrintProgress("Print NR", value),
                cancellationToken);
            Status.Text = "Print NR pronto.";
        });

        if (result is not null && !closePending)
            ShowPrintPreview(result, modelLabel, layerCount, fileNameStem, initialDirectory);
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

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
