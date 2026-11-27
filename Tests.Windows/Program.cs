using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VideoNR;
using VideoNR.Services;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.ResourceAssembly = typeof(Editor).Assembly;
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
            MediaFoundationFrameCodec.EncodeStillVideo(chart, source, 60, 30);
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
            Snapshot(editor, "editor-main.png");

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

    private static void Snapshot(Window window, string name)
    {
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
        var visual = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth),
            (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
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
