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
        var codec = HevcRadio.IsChecked == true ? "hevc" : "h264";
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

    private bool TryGetPrintContext(out string adapter, out string engine, out string[] encodedLayers)
    {
        adapter = adapterPath ?? "";
        engine = EnginePath ?? "";
        encodedLayers = [];
        if (busy || adapterPath is null || EnginePath is null) return false;
        if (ActiveCount == 0)
        {
            Status.Text = "Ative a melhoria NR e ao menos 1 camada para aplicar DLSS 5 no print.";
            return false;
        }
        encodedLayers = EncodeActiveLayers();
        return true;
    }

    private void ReportPrintProgress(string label, int value)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ReportPrintProgress(label, value));
            return;
        }
        Progress.Value = value;
        TaskbarInfo.ProgressState = TaskbarItemProgressState.Normal;
        TaskbarInfo.ProgressValue = Math.Clamp(value / 10000.0, 0, 1);
        if (!CancelRequested)
            Status.Text = value == 0
                ? $"{label} · preparando GPU…"
                : $"{label} · {Math.Min(99, value / 100)}%";
    }

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
        if (clip is null || !TryGetPrintContext(out var adapter, out var engine, out var encodedLayers)) return;
        Pause();

        var targetClip = clip;
        var capturePosition = position;
        var modelTag = Model;
        var modelLabel = SelectedModel.Label;
        var layerCount = encodedLayers.Length;

        PrintResult? result = null;
        await RunExportTask("Renderizando print do frame com DLSS 5…", async cancellationToken =>
        {
            result = await PrintProcessor.ProcessVideoFrameAsync(
                exporter,
                adapter,
                engine,
                targetClip.Path,
                capturePosition,
                targetClip.Duration,
                modelTag,
                encodedLayers,
                value => ReportPrintProgress("Print do frame", value),
                cancellationToken);
            Status.Text = "Print do frame pronto.";
        });

        if (result is not null && !closePending)
        {
            var stem = Path.GetFileNameWithoutExtension(targetClip.Path) + "-NR-print";
            ShowPrintPreview(result, modelLabel, layerCount, stem, Path.GetDirectoryName(targetClip.Path));
        }
    }

    private async void ScreenPrintClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetPrintContext(out var adapter, out var engine, out var encodedLayers)) return;
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
        if (!TryGetPrintContext(out _, out _, out _)) return;
        var dialog = new OpenFileDialog
        {
            Title = "Selecionar imagem para aplicar DLSS 5 / NR",
            Filter = ImageFilter
        };
        if (dialog.ShowDialog(this) == true)
            await RunImagePrintFromFileAsync(dialog.FileName);
    }

    private async void PasteImagePrint()
    {
        if (!TryGetPrintContext(out var adapter, out var engine, out var encodedLayers)) return;
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
        if (!TryGetPrintContext(out var adapter, out var engine, out var encodedLayers)) return;
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
        await RunExportTask("Renderizando print com DLSS 5…", async cancellationToken =>
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
}
