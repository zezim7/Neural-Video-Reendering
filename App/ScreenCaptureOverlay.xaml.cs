using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VideoNR.Imaging;
using VideoNR.Services;

namespace VideoNR;

public partial class ScreenCaptureOverlay : Window
{
    private readonly ScreenSnapshot snapshot;
    private bool dragging;
    private Point anchorDip;
    private Rect currentSelectionDip = Rect.Empty;

    internal RawImage? CapturedImage { get; private set; }

    internal ScreenCaptureOverlay(ScreenSnapshot snapshot)
    {
        this.snapshot = snapshot;
        InitializeComponent();

        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        ScreenshotImage.Source = WpfImageBridge.ToBitmapSource(snapshot.Image);
        Loaded += (_, _) =>
        {
            UpdateScrimAndSelection();
            PositionToolbar();
            Activate();
            Focus();
        };
        SizeChanged += (_, _) =>
        {
            UpdateScrimAndSelection();
            PositionToolbar();
        };
    }

    private void PositionToolbar()
    {
        var dipW = CaptureSurface.ActualWidth > 0 ? CaptureSurface.ActualWidth : Width;
        var dipH = CaptureSurface.ActualHeight > 0 ? CaptureSurface.ActualHeight : Height;
        if (dipW <= 0 || dipH <= 0) return;

        var (mx, my, mw, _) = WpfImageBridge.GetActiveMonitorBounds(snapshot);
        var scaleX = dipW / snapshot.Image.Width;
        var scaleY = dipH / snapshot.Image.Height;

        ToolbarBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var toolbarW = ToolbarBorder.DesiredSize.Width;
        var left = mx * scaleX + (mw * scaleX - toolbarW) / 2.0;
        var top = my * scaleY + 18.0;

        Canvas.SetLeft(ToolbarBorder, Math.Clamp(left, 12, Math.Max(12, dipW - toolbarW - 12)));
        Canvas.SetTop(ToolbarBorder, Math.Clamp(top, 12, Math.Max(12, dipH - 56)));
    }

    private void SurfaceMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        anchorDip = e.GetPosition(CaptureSurface);
        currentSelectionDip = new Rect(anchorDip, new Size(0, 0));
        dragging = CaptureSurface.CaptureMouse();
        UpdateScrimAndSelection();
        e.Handled = true;
    }

    private void SurfaceMouseMove(object sender, MouseEventArgs e)
    {
        if (!dragging || e.LeftButton != MouseButtonState.Pressed) return;
        var pos = ClampToSurface(e.GetPosition(CaptureSurface));
        currentSelectionDip = new Rect(anchorDip, pos);
        UpdateScrimAndSelection();
    }

    private void SurfaceMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!dragging) return;
        dragging = false;
        var pos = ClampToSurface(e.GetPosition(CaptureSurface));
        currentSelectionDip = new Rect(anchorDip, pos);
        CaptureSurface.ReleaseMouseCapture();
        e.Handled = true;

        if (currentSelectionDip.Width < 6 || currentSelectionDip.Height < 6)
        {
            currentSelectionDip = Rect.Empty;
            UpdateScrimAndSelection();
            return;
        }

        CommitDipSelection(currentSelectionDip);
    }

    private void SurfaceRightClick(object sender, MouseButtonEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void SurfaceLostCapture(object sender, MouseEventArgs e)
    {
        dragging = false;
    }

    private Point ClampToSurface(Point p)
    {
        var maxW = Math.Max(1, CaptureSurface.ActualWidth);
        var maxH = Math.Max(1, CaptureSurface.ActualHeight);
        return new Point(Math.Clamp(p.X, 0, maxW), Math.Clamp(p.Y, 0, maxH));
    }

    private void UpdateScrimAndSelection()
    {
        var dipW = CaptureSurface.ActualWidth > 0 ? CaptureSurface.ActualWidth : Width;
        var dipH = CaptureSurface.ActualHeight > 0 ? CaptureSurface.ActualHeight : Height;
        if (dipW <= 0 || dipH <= 0) return;

        var full = new RectangleGeometry(new Rect(0, 0, dipW, dipH));
        if (currentSelectionDip.IsEmpty || currentSelectionDip.Width < 1 || currentSelectionDip.Height < 1)
        {
            ScrimPath.Data = full;
            SelectionBorder.Visibility = Visibility.Collapsed;
            DimensionBadge.Visibility = Visibility.Collapsed;
            return;
        }

        var hole = new RectangleGeometry(currentSelectionDip);
        ScrimPath.Data = new CombinedGeometry(GeometryCombineMode.Exclude, full, hole);

        Canvas.SetLeft(SelectionBorder, currentSelectionDip.X);
        Canvas.SetTop(SelectionBorder, currentSelectionDip.Y);
        SelectionBorder.Width = currentSelectionDip.Width;
        SelectionBorder.Height = currentSelectionDip.Height;
        SelectionBorder.Visibility = Visibility.Visible;

        var (_, _, pxW, pxH) = DipToPhysicalPixels(currentSelectionDip);
        DimensionText.Text = $"{pxW} × {pxH}";
        DimensionBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var badgeW = DimensionBadge.DesiredSize.Width;
        var badgeH = DimensionBadge.DesiredSize.Height;
        var badgeX = Math.Clamp(currentSelectionDip.X, 4, Math.Max(4, dipW - badgeW - 4));
        var badgeY = currentSelectionDip.Y >= badgeH + 8
            ? currentSelectionDip.Y - badgeH - 6
            : Math.Min(dipH - badgeH - 4, currentSelectionDip.Bottom + 6);

        Canvas.SetLeft(DimensionBadge, badgeX);
        Canvas.SetTop(DimensionBadge, badgeY);
        DimensionBadge.Visibility = Visibility.Visible;
    }

    private (int X, int Y, int Width, int Height) DipToPhysicalPixels(Rect rect)
    {
        var dipW = Math.Max(1, CaptureSurface.ActualWidth);
        var dipH = Math.Max(1, CaptureSurface.ActualHeight);
        var scaleX = snapshot.Image.Width / dipW;
        var scaleY = snapshot.Image.Height / dipH;

        var x = Math.Clamp((int)Math.Floor(rect.X * scaleX), 0, snapshot.Image.Width - 1);
        var y = Math.Clamp((int)Math.Floor(rect.Y * scaleY), 0, snapshot.Image.Height - 1);
        var right = Math.Clamp((int)Math.Ceiling(rect.Right * scaleX), x + 1, snapshot.Image.Width);
        var bottom = Math.Clamp((int)Math.Ceiling(rect.Bottom * scaleY), y + 1, snapshot.Image.Height);
        return (x, y, right - x, bottom - y);
    }

    private void CommitDipSelection(Rect rect)
    {
        var (x, y, w, h) = DipToPhysicalPixels(rect);
        CapturedImage = snapshot.Image.CropRegion(x, y, w, h);
        DialogResult = true;
        Close();
    }

    private void CaptureMonitorClick(object sender, RoutedEventArgs e)
    {
        var (x, y, w, h) = WpfImageBridge.GetActiveMonitorBounds(snapshot);
        CapturedImage = snapshot.Image.CropRegion(x, y, w, h);
        DialogResult = true;
        Close();
    }

    private void CaptureAllClick(object sender, RoutedEventArgs e)
    {
        CapturedImage = snapshot.Image;
        DialogResult = true;
        Close();
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OverlayKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelClick(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            CaptureMonitorClick(sender, e);
            e.Handled = true;
        }
    }
}
