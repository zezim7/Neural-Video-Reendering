using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using VideoNR.Helpers;
using VideoNR.Imaging;
using VideoNR.Services;

namespace VideoNR;

public partial class PrintPreviewWindow : Window
{
    private readonly PrintResult result;
    private readonly BitmapSource enhancedBitmap;
    private readonly BitmapSource? originalBitmap;
    private readonly string defaultFileNameStem;
    private readonly string? initialDirectory;

    private double splitRatio = 0.5;
    private bool draggingSplit;

    internal string? SavedFolder { get; private set; }

    internal PrintPreviewWindow(
        PrintResult result,
        string modelLabel,
        int activeLayerCount,
        string defaultFileNameStem,
        string? initialDirectory)
    {
        this.result = result;
        this.defaultFileNameStem = defaultFileNameStem;
        this.initialDirectory = initialDirectory;
        enhancedBitmap = WpfImageBridge.ToBitmapSource(result.Enhanced);
        originalBitmap = result.Original is not null ? WpfImageBridge.ToBitmapSource(result.Original) : null;

        InitializeComponent();
        WindowDarkMode.Apply(this, RootGrid);

        EnhancedImage.Source = enhancedBitmap;
        OriginalImage.Source = originalBitmap;

        var layerText = activeLayerCount == 1 ? "1 camada" : $"{activeLayerCount} camadas";
        var downscaleNote = result.WasDownscaled ? " · máx. 4K" : "";
        MetadataLabel.Text = $"{result.Enhanced.Width} × {result.Enhanced.Height} · {modelLabel} · {layerText}{downscaleNote}";

        if (originalBitmap is null)
        {
            CompareRadio.IsEnabled = false;
            OriginalRadio.IsEnabled = false;
            EnhancedRadio.IsChecked = true;
            PreviewStatus.Text = "Visualizando imagem renderizada com DLSS 5 / NR.";
        }
        else
        {
            CompareRadio.IsChecked = true;
        }

        Loaded += (_, _) => UpdateStageLayout();
    }

    private void ViewModeChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateStageLayout();
    }

    private void ZoomModeChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || StageScroll is null) return;
        UpdateStageLayout();
    }

    private void StageSizeChanged(object sender, SizeChangedEventArgs e) => UpdateStageLayout();

    private void StageMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (CompareRadio.IsChecked != true || originalBitmap is null) return;
        draggingSplit = StageHost.CaptureMouse();
        UpdateSplitFromPointer(e.GetPosition(ViewportGrid));
        e.Handled = true;
    }

    private void StageMouseMove(object sender, MouseEventArgs e)
    {
        if (!draggingSplit || e.LeftButton != MouseButtonState.Pressed) return;
        UpdateSplitFromPointer(e.GetPosition(ViewportGrid));
    }

    private void StageMouseUp(object sender, MouseEventArgs e)
    {
        if (!draggingSplit) return;
        draggingSplit = false;
        UpdateSplitFromPointer(e.GetPosition(ViewportGrid));
        StageHost.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void StageLostCapture(object sender, MouseEventArgs e) => draggingSplit = false;

    private void UpdateSplitFromPointer(Point p)
    {
        if (ViewportGrid.Width <= 0) return;
        splitRatio = Math.Clamp(p.X / ViewportGrid.Width, 0.0, 1.0);
        UpdateStageLayout();
    }

    private void UpdateStageLayout()
    {
        var hostW = StageScroll.ViewportWidth > 0 ? StageScroll.ViewportWidth : StageScroll.ActualWidth;
        var hostH = StageScroll.ViewportHeight > 0 ? StageScroll.ViewportHeight : StageScroll.ActualHeight;
        if (hostW <= 0 || hostH <= 0) return;

        StageHost.MinWidth = hostW;
        StageHost.MinHeight = hostH;

        double viewW;
        double viewH;
        if (ActualZoomRadio.IsChecked == true)
        {
            StageScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            StageScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            var dpi = VisualTreeHelper.GetDpi(this);
            viewW = Math.Max(1, result.Enhanced.Width / Math.Max(1.0, dpi.DpiScaleX));
            viewH = Math.Max(1, result.Enhanced.Height / Math.Max(1.0, dpi.DpiScaleY));
            RenderOptions.SetBitmapScalingMode(EnhancedImage, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetBitmapScalingMode(OriginalImage, BitmapScalingMode.NearestNeighbor);
        }
        else
        {
            StageScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            StageScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            var scale = Math.Min(hostW / result.Enhanced.Width, hostH / result.Enhanced.Height);
            viewW = Math.Max(1, result.Enhanced.Width * scale);
            viewH = Math.Max(1, result.Enhanced.Height * scale);
            RenderOptions.SetBitmapScalingMode(EnhancedImage, BitmapScalingMode.HighQuality);
            RenderOptions.SetBitmapScalingMode(OriginalImage, BitmapScalingMode.HighQuality);
        }

        ViewportGrid.Width = viewW;
        ViewportGrid.Height = viewH;

        var hasOriginal = originalBitmap is not null;
        if (hasOriginal && CompareRadio.IsChecked == true)
        {
            StageHost.Cursor = Cursors.SizeWE;
            OriginalImage.Visibility = Visibility.Visible;
            var splitX = viewW * splitRatio;
            OriginalClip.Rect = new Rect(0, 0, splitX, viewH);

            DividerOverlay.Visibility = Visibility.Visible;
            SplitLine.Height = viewH;
            Canvas.SetLeft(SplitLine, splitX - 0.75);
            Canvas.SetTop(SplitLine, 0);

            Canvas.SetLeft(SplitKnob, splitX - SplitKnob.Width / 2.0);
            Canvas.SetTop(SplitKnob, (viewH - SplitKnob.Height) / 2.0);

            OriginalPill.Visibility = splitRatio > 0.08 ? Visibility.Visible : Visibility.Collapsed;
            NrPill.Visibility = splitRatio < 0.92 ? Visibility.Visible : Visibility.Collapsed;
        }
        else if (hasOriginal && OriginalRadio.IsChecked == true)
        {
            StageHost.Cursor = Cursors.Arrow;
            OriginalImage.Visibility = Visibility.Visible;
            OriginalClip.Rect = new Rect(0, 0, viewW, viewH);
            DividerOverlay.Visibility = Visibility.Collapsed;
            OriginalPill.Visibility = Visibility.Visible;
            NrPill.Visibility = Visibility.Collapsed;
        }
        else
        {
            StageHost.Cursor = Cursors.Arrow;
            OriginalImage.Visibility = Visibility.Collapsed;
            DividerOverlay.Visibility = Visibility.Collapsed;
            OriginalPill.Visibility = Visibility.Collapsed;
            NrPill.Visibility = Visibility.Visible;
        }
    }

    private void CopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetImage(enhancedBitmap);
            PreviewStatus.Text = "Imagem DLSS 5 / NR copiada para a área de transferência.";
        }
        catch (ExternalException)
        {
            PreviewStatus.Text = "A área de transferência está ocupada. Tente copiar novamente.";
        }
    }

    private void SavePngClick(object sender, RoutedEventArgs e) => SaveImage(jpeg: false);
    private void SaveJpgClick(object sender, RoutedEventArgs e) => SaveImage(jpeg: true);

    private void SaveImage(bool jpeg)
    {
        var ext = jpeg ? ".jpg" : ".png";
        var dialog = new SaveFileDialog
        {
            Title = jpeg ? "Salvar print JPG" : "Salvar print PNG",
            Filter = jpeg ? "Imagem JPEG|*.jpg;*.jpeg" : "Imagem PNG|*.png",
            DefaultExt = ext,
            AddExtension = true,
            OverwritePrompt = true,
            FileName = defaultFileNameStem + ext,
            InitialDirectory = Directory.Exists(initialDirectory)
                ? initialDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
        };

        if (dialog.ShowDialog(this) != true) return;
        try
        {
            WpfImageBridge.SaveToFile(result.Enhanced, dialog.FileName, jpeg);
            SavedFolder = Path.GetDirectoryName(dialog.FileName);
            PreviewStatus.Text = $"Salvo em {dialog.FileName}";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            PreviewStatus.Text = $"Falha ao salvar: {error.Message}";
        }
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();

    private void PreviewKeyDownHandler(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}
