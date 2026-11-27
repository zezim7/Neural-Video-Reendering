using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VideoNR;
using VideoNR.Services;
using VideoNR.Models;
using VideoNR.Imaging;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args is ["--verify-package", var packageDir, var outputDir])
        {
            VerifyPackage(packageDir, outputDir);
            return;
        }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/VideoNR;component/Themes/Theme.xaml", UriKind.Relative)
        });
        var dir = Directory.CreateTempSubdirectory("VideoNR-WPF-").FullName;
        try
        {
            var chart = TestChart(640, 360);
            var source = Path.Combine(dir, "Barras de cor.mp4");
            VideoFixture.EncodeStillVideo(chart, source, 60, 30);
            var editor = new Editor(Path.Combine(dir, "settings.json")) { WindowState = WindowState.Normal };
            editor.Show();
            editor.AddPaths([source], true, targetTrackIndex: 0);
            editor.AddPaths([source], true, targetTrackIndex: 1, dropTimelineSeconds: 1);
            editor.EditedExportRadio.IsChecked = true;
            if (editor.ExportButton.Content?.ToString() != "Exportar trechos (2)")
                throw new InvalidOperationException("O exportador deve oferecer os dois trechos.");
            editor.NrVersion.SelectedIndex = 3;
            if (editor.AmdResolutionHint.Visibility != Visibility.Visible)
                throw new InvalidOperationException("O limite de resolução AMD deve ficar visível.");
            editor.UpdateLayout();
            var layer = editor.LayerCards.Items.Cast<NrLayer>().Single();
            var chevron = Descendants(editor.LayerCards).OfType<TextBlock>().Single(t =>
                t.GetBindingExpression(TextBlock.TextProperty)?.ParentBinding.Path.Path == nameof(NrLayer.Chevron));
            layer.Expanded = true;
            editor.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            if (chevron.Text != "▾") throw new InvalidOperationException("A seta deve acompanhar a camada expandida.");
            layer.Expanded = false;
            layer.Enabled = false;
            if (editor.PresetsGrid.IsEnabled || editor.SavePresetButton.IsEnabled)
                throw new InvalidOperationException("Predefinições exigem uma camada ativa.");
            layer.Enabled = true;
            editor.QueueToggleButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Snapshot(editor, "editor-timeline.png", editor.TimelineDeck);

            var preview = new PrintPreviewWindow(new PrintResult(chart, chart, false),
                "Padrão de teste", 1, "barras", dir) { Owner = editor };
            preview.Show();
            if (preview.CompareRadio.IsChecked != true || preview.OriginalRadio.Content?.ToString() != "Sem NR")
                throw new InvalidOperationException("O comparador deve identificar a referência sem NR.");
            Snapshot(preview, "print-preview.png");
            preview.Close();
            editor.Close();
        }
        finally
        {
            app.Shutdown();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    private static void VerifyPackage(string packageDir, string outputDir)
    {
        var root = Path.GetFullPath(packageDir);
        var adapter = EngineLocator.FindAdapter(root) ?? throw new FileNotFoundException("Engine.exe ausente no pacote.");
        var vendor = new[] { "NVIDIA", "AMD" }.Single(v => EngineLocator.FindBundledEngine(v, root) is not null);
        var engine = EngineLocator.FindBundledEngine(vendor, root)!;
        var model = NrLayer.Models[NrLayer.InitialModelIndex(vendor == "NVIDIA" ? "sf-performance" : "amd-small", [vendor])];
        var folder = Directory.CreateDirectory(Path.Combine(outputDir, $"VideoNR-{Guid.NewGuid():N}")).FullName;
        var source = TestChart(640, 360);
        var layers = new[] { new NrLayer().Encode() };
        WpfImageBridge.SaveToFile(source, Path.Combine(folder, "input.png"), false);
        var exporter = new VideoExporter();
        var result = PrintProcessor.ProcessImageAsync(exporter, adapter, engine, source, model.Tag, layers,
            _ => { }, CancellationToken.None).GetAwaiter().GetResult();
        WpfImageBridge.SaveToFile(result.Original!, Path.Combine(folder, "without-NR.png"), false);
        WpfImageBridge.SaveToFile(result.Enhanced, Path.Combine(folder, "with-NR.png"), false);
        var clip = Path.Combine(folder, "input.mp4");
        VideoFixture.EncodeStillVideo(source, clip, 60, 60);
        var output = Path.Combine(folder, "cut.mp4");
        var request = new ExportRequest(adapter, engine, clip, output, .25, .75, "h264", model.Tag, layers);
        exporter.ExportAsync(request, _ => { }, CancellationToken.None).GetAwaiter().GetResult();
        var info = MediaFoundationFrameCodec.TryProbe(output);
        if (info is null || Math.Abs(info.DurationSeconds - .5) > .05)
            throw new InvalidOperationException("O trecho exportado deve durar meio segundo.");
        File.WriteAllText(Path.Combine(folder, "parameters.txt"), $"Model: {model.Tag}\nLayer: {layers[0]}\nTrim: 0.25–0.75 s\n");
        Console.WriteLine(folder);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Snapshot(Window window, string name, FrameworkElement? element = null)
    {
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
        var visual = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth),
            (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        BitmapSource capture = bitmap;
        if (element is not null)
        {
            var bounds = element.TransformToAncestor(visual).TransformBounds(new Rect(element.RenderSize));
            capture = new CroppedBitmap(bitmap, new Int32Rect((int)Math.Floor(bounds.Left),
                (int)Math.Floor(bounds.Top), (int)Math.Floor(bounds.Width), (int)Math.Floor(bounds.Height)));
        }
        var pixels = new byte[capture.PixelWidth * capture.PixelHeight * 4];
        capture.CopyPixels(pixels, capture.PixelWidth * 4, 0);
        if (pixels.Where((_, offset) => offset % 4 == 2).Max() < 120)
            throw new InvalidOperationException($"Captura de interface vazia: {name}");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(capture));
        var folder = Directory.CreateDirectory("ui-snapshots");
        using var stream = File.Create(Path.Combine(folder.FullName, name));
        encoder.Save(stream);
    }

    private static RawImage TestChart(int width, int height)
    {
        uint[] bars = [0xbfbfbf, 0xbfbf00, 0x00bfbf, 0x00bf00, 0xbf00bf, 0xbf0000, 0x0000bf];
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var band = bars[x * bars.Length / width];
            if (y >= height * 3 / 4)
                band = (uint)(x * 255 / (width - 1)) * 0x010101;
            var i = (y * width + x) * 4;
            pixels[i] = (byte)band;
            pixels[i + 1] = (byte)(band >> 8);
            pixels[i + 2] = (byte)(band >> 16);
            pixels[i + 3] = 255;
        }
        return new RawImage(pixels, width, height);
    }
}
