using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using VideoNR.Helpers;
using VideoNR.Imaging;
using VideoNR.Services;

namespace VideoNR;

public partial class PrintPreviewWindow : Window
{
    private const double SplitStepFine = 0.02;
    private const double SplitStepCoarse = 0.10;

    private readonly PrintResult result;
    private readonly BitmapSource enhancedBitmap;
    private readonly BitmapSource? originalBitmap;
    private readonly string defaultFileNameStem;
    private readonly string? initialDirectory;

    private double splitRatio = 0.5;
    private bool draggingSplit;
    private bool panning;
    private bool spaceHeld;
    private bool canPan;
    private Point panStartPoint;
    private double panStartHorizontalOffset;
    private double panStartVerticalOffset;
    private Point? pendingZoomFocus;

    internal string? SavedFolder { get; private set; }

    private bool IsActualZoom => ActualZoomRadio.IsChecked == true;
    private bool IsCompareMode => originalBitmap is not null && CompareRadio.IsChecked == true;

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
        }
        else
        {
            CompareRadio.IsChecked = true;
        }

        Loaded += (_, _) =>
        {
            UpdateStageLayout();
            UpdateDefaultStatusHint();
        };
    }

    private void ViewModeChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateStageLayout();
        UpdateDefaultStatusHint();
    }

    private void ZoomModeChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || StageScroll is null) return;
        if (!IsActualZoom)
        {
            panning = false;
            spaceHeld = false;
        }
        UpdateStageLayout();
        UpdateDefaultStatusHint();
    }

    private void StageSizeChanged(object sender, SizeChangedEventArgs e) => UpdateStageLayout();

    private void StageScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (IsCompareMode && DividerOverlay.Visibility == Visibility.Visible)
            UpdateSplitKnobVerticalPosition(ViewportGrid.Height);
    }

    private void StagePreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control) return;

        if (e.Delta > 0 && !IsActualZoom)
            ToggleZoomAt(e.GetPosition(ViewportGrid), zoomToActual: true);
        else if (e.Delta < 0 && IsActualZoom)
            ToggleZoomAt(e.GetPosition(ViewportGrid), zoomToActual: false);

        e.Handled = true;
    }

    private void StageMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
        {
            ToggleZoomAt(e.GetPosition(ViewportGrid), zoomToActual: !IsActualZoom);
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Middle)
        {
            if (canPan)
            {
                StartPan(e);
                e.Handled = true;
            }
            return;
        }

        if (e.ChangedButton != MouseButton.Left) return;

        if (canPan && (!IsCompareMode || spaceHeld))
        {
            StartPan(e);
            e.Handled = true;
            return;
        }

        if (!IsCompareMode) return;
        draggingSplit = StageHost.CaptureMouse();
        UpdateSplitFromPointer(e.GetPosition(ViewportGrid));
        e.Handled = true;
    }

    private void StartPan(MouseButtonEventArgs e)
    {
        panStartPoint = e.GetPosition(StageScroll);
        panStartHorizontalOffset = StageScroll.HorizontalOffset;
        panStartVerticalOffset = StageScroll.VerticalOffset;
        panning = StageHost.CaptureMouse();
        UpdateStageCursor();
    }

    private void StageMouseMove(object sender, MouseEventArgs e)
    {
        if (panning)
        {
            if (e.LeftButton != MouseButtonState.Pressed && e.MiddleButton != MouseButtonState.Pressed)
            {
                panning = false;
                StageHost.ReleaseMouseCapture();
                UpdateStageCursor();
                return;
            }

            var current = e.GetPosition(StageScroll);
            var delta = current - panStartPoint;
            StageScroll.ScrollToHorizontalOffset(Math.Clamp(panStartHorizontalOffset - delta.X, 0, StageScroll.ScrollableWidth));
            StageScroll.ScrollToVerticalOffset(Math.Clamp(panStartVerticalOffset - delta.Y, 0, StageScroll.ScrollableHeight));
            return;
        }

        if (!draggingSplit || e.LeftButton != MouseButtonState.Pressed) return;
        UpdateSplitFromPointer(e.GetPosition(ViewportGrid));
    }

    private void StageMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (panning && (e.ChangedButton == MouseButton.Left || e.ChangedButton == MouseButton.Middle))
        {
            panning = false;
            StageHost.ReleaseMouseCapture();
            UpdateStageCursor();
            e.Handled = true;
            return;
        }

        if (!draggingSplit || e.ChangedButton != MouseButton.Left) return;
        draggingSplit = false;
        UpdateSplitFromPointer(e.GetPosition(ViewportGrid));
        StageHost.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void StageLostCapture(object sender, MouseEventArgs e)
    {
        draggingSplit = false;
        panning = false;
        UpdateStageCursor();
    }

    private void ToggleZoomAt(Point viewportPoint, bool zoomToActual)
    {
        if (zoomToActual)
        {
            var fx = ViewportGrid.Width > 0 ? Math.Clamp(viewportPoint.X / ViewportGrid.Width, 0.0, 1.0) : 0.5;
            var fy = ViewportGrid.Height > 0 ? Math.Clamp(viewportPoint.Y / ViewportGrid.Height, 0.0, 1.0) : 0.5;
            pendingZoomFocus = new Point(fx, fy);
            ActualZoomRadio.IsChecked = true;
        }
        else
        {
            pendingZoomFocus = null;
            FitZoomRadio.IsChecked = true;
        }
    }

    private void UpdateSplitFromPointer(Point p)
    {
        if (ViewportGrid.Width <= 0) return;
        splitRatio = Math.Clamp(p.X / ViewportGrid.Width, 0.0, 1.0);
        UpdateStageLayout();
    }

    private void UpdateStageCursor()
    {
        if (panning)
            StageHost.Cursor = Cursors.SizeAll;
        else if (canPan && (!IsCompareMode || spaceHeld))
            StageHost.Cursor = Cursors.Hand;
        else if (IsCompareMode)
            StageHost.Cursor = Cursors.SizeWE;
        else
            StageHost.Cursor = Cursors.Arrow;
    }

    private void UpdateDefaultStatusHint()
    {
        if (IsCompareMode)
        {
            PreviewStatus.Text = IsActualZoom && canPan
                ? "Arraste a linha âmbar · Espaço ou botão do meio para mover · Duplo-clique para ajustar."
                : "Arraste a linha âmbar ou use ←/→ para comparar · Duplo-clique para 100%.";
        }
        else
        {
            PreviewStatus.Text = IsActualZoom && canPan
                ? "Arraste para mover a imagem em 100% · Duplo-clique para ajustar."
                : "Visualizando em modo ajustar · Pressione 2 ou duplo-clique para 100%.";
        }
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
        if (IsActualZoom)
        {
            StageScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            StageScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            var dpi = VisualTreeHelper.GetDpi(this);
            viewW = Math.Max(1, result.Enhanced.Width / Math.Max(1.0, dpi.DpiScaleX));
            viewH = Math.Max(1, result.Enhanced.Height / Math.Max(1.0, dpi.DpiScaleY));
            RenderOptions.SetBitmapScalingMode(EnhancedImage, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetBitmapScalingMode(OriginalImage, BitmapScalingMode.NearestNeighbor);
            canPan = viewW > hostW + 0.5 || viewH > hostH + 0.5;
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
            canPan = false;
        }

        ViewportGrid.Width = viewW;
        ViewportGrid.Height = viewH;

        if (IsActualZoom && pendingZoomFocus is { } focus)
        {
            pendingZoomFocus = null;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (!IsActualZoom) return;
                var targetX = focus.X * viewW - StageScroll.ViewportWidth / 2.0;
                var targetY = focus.Y * viewH - StageScroll.ViewportHeight / 2.0;
                StageScroll.ScrollToHorizontalOffset(Math.Clamp(targetX, 0, StageScroll.ScrollableWidth));
                StageScroll.ScrollToVerticalOffset(Math.Clamp(targetY, 0, StageScroll.ScrollableHeight));
            });
        }

        var hasOriginal = originalBitmap is not null;
        if (hasOriginal && CompareRadio.IsChecked == true)
        {
            OriginalImage.Visibility = Visibility.Visible;
            var splitX = viewW * splitRatio;
            OriginalClip.Rect = new Rect(0, 0, splitX, viewH);

            DividerOverlay.Visibility = Visibility.Visible;
            SplitLine.Height = viewH;
            Canvas.SetLeft(SplitLine, splitX - 0.75);
            Canvas.SetTop(SplitLine, 0);

            Canvas.SetLeft(SplitKnob, splitX - SplitKnob.Width / 2.0);
            UpdateSplitKnobVerticalPosition(viewH);

            OriginalPill.Visibility = splitRatio > 0.08 ? Visibility.Visible : Visibility.Collapsed;
            NrPill.Visibility = splitRatio < 0.92 ? Visibility.Visible : Visibility.Collapsed;
        }
        else if (hasOriginal && OriginalRadio.IsChecked == true)
        {
            OriginalImage.Visibility = Visibility.Visible;
            OriginalClip.Rect = new Rect(0, 0, viewW, viewH);
            DividerOverlay.Visibility = Visibility.Collapsed;
            OriginalPill.Visibility = Visibility.Visible;
            NrPill.Visibility = Visibility.Collapsed;
        }
        else
        {
            OriginalImage.Visibility = Visibility.Collapsed;
            DividerOverlay.Visibility = Visibility.Collapsed;
            OriginalPill.Visibility = Visibility.Collapsed;
            NrPill.Visibility = Visibility.Visible;
        }

        UpdateStageCursor();
    }

    private void UpdateSplitKnobVerticalPosition(double viewH)
    {
        if (viewH <= 0 || double.IsNaN(viewH)) return;
        var halfKnob = SplitKnob.Height / 2.0;
        var visibleH = StageScroll.ViewportHeight;
        double centerY;
        if (IsActualZoom && visibleH > 0 && viewH > visibleH + 0.5)
        {
            centerY = StageScroll.VerticalOffset + visibleH / 2.0;
            var margin = Math.Min(halfKnob + 8, viewH / 2.0);
            centerY = Math.Clamp(centerY, margin, viewH - margin);
        }
        else
        {
            centerY = viewH / 2.0;
        }
        Canvas.SetTop(SplitKnob, centerY - halfKnob);
    }

    private void CopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetImage(enhancedBitmap);
            PreviewStatus.Text = "Imagem NR copiada para a área de transferência.";
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
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or COMException)
        {
            PreviewStatus.Text = $"Falha ao salvar: {error.Message}";
        }
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();

    private void WindowDeactivated(object? sender, EventArgs e)
    {
        if (!spaceHeld) return;
        spaceHeld = false;
        UpdateStageCursor();
    }

    private void PreviewKeyDownHandler(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;

        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        if (modifiers == ModifierKeys.Control && e.Key == Key.C)
        {
            CopyClick(sender, e);
            e.Handled = true;
            return;
        }

        if (modifiers == ModifierKeys.Control && e.Key == Key.S)
        {
            SavePngClick(sender, e);
            e.Handled = true;
            return;
        }

        if (modifiers == ModifierKeys.None)
        {
            if (e.Key is Key.D1 or Key.NumPad1)
            {
                FitZoomRadio.IsChecked = true;
                e.Handled = true;
                return;
            }
            if (e.Key is Key.D2 or Key.NumPad2)
            {
                ActualZoomRadio.IsChecked = true;
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Space && canPan)
            {
                if (!spaceHeld)
                {
                    spaceHeld = true;
                    UpdateStageCursor();
                }
                e.Handled = true;
                return;
            }
            if (IsCompareMode && e.Key == Key.Left)
            {
                splitRatio = Math.Clamp(splitRatio - SplitStepFine, 0.0, 1.0);
                UpdateStageLayout();
                e.Handled = true;
                return;
            }
            if (IsCompareMode && e.Key == Key.Right)
            {
                splitRatio = Math.Clamp(splitRatio + SplitStepFine, 0.0, 1.0);
                UpdateStageLayout();
                e.Handled = true;
                return;
            }
        }
        else if (modifiers == ModifierKeys.Shift && IsCompareMode)
        {
            if (e.Key == Key.Left)
            {
                splitRatio = Math.Clamp(splitRatio - SplitStepCoarse, 0.0, 1.0);
                UpdateStageLayout();
                e.Handled = true;
            }
            else if (e.Key == Key.Right)
            {
                splitRatio = Math.Clamp(splitRatio + SplitStepCoarse, 0.0, 1.0);
                UpdateStageLayout();
                e.Handled = true;
            }
        }
    }

    private void PreviewKeyUpHandler(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || !spaceHeld) return;
        spaceHeld = false;
        UpdateStageCursor();
        e.Handled = true;
    }
}
