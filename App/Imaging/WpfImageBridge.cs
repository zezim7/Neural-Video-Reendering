using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VideoNR.Services;

namespace VideoNR.Imaging;

internal sealed record ScreenSnapshot(RawImage Image, int LeftPx, int TopPx);

internal static class WpfImageBridge
{
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;
    private const uint DibRgbColors = 0;
    private const uint BiRgb = 0;
    private const uint Srccopy = 0x00CC0020;
    private const uint Captureblt = 0x40000000;
    private const uint MonitorDefaultToNearest = 2;
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpShowWindow = 0x0040;

    internal static BitmapSource ToBitmapSource(RawImage image)
    {
        image.Validate();
        var bitmap = BitmapSource.Create(
            image.Width,
            image.Height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            image.Bgra,
            image.Stride);
        bitmap.Freeze();
        return bitmap;
    }

    internal static RawImage FromBitmapSource(BitmapSource source, bool forceOpaqueAlpha = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        var formatted = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        var width = formatted.PixelWidth;
        var height = formatted.PixelHeight;
        if (width <= 0 || height <= 0)
            throw new IOException("A imagem selecionada possui dimensões inválidas.");

        var stride = width * 4;
        var bgra = new byte[checked(stride * height)];
        formatted.CopyPixels(bgra, stride, 0);

        if (forceOpaqueAlpha || source.Format == PixelFormats.Bgr32)
        {
            for (var i = 3; i < bgra.Length; i += 4)
                bgra[i] = 0xFF;
        }

        return new RawImage(bgra, width, height);
    }

    internal static RawImage LoadFromFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0)
            throw new IOException("Nenhuma imagem encontrada no arquivo.");
        return FromBitmapSource(decoder.Frames[0]);
    }

    internal static void SaveToFile(RawImage image, string path, bool jpeg, int jpegQuality = 95)
    {
        var bitmap = ToBitmapSource(image);
        BitmapEncoder encoder = jpeg
            ? new JpegBitmapEncoder { QualityLevel = Math.Clamp(jpegQuality, 1, 100) }
            : new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
    }

    internal static ScreenSnapshot CaptureVirtualScreen()
    {
        var left = GetSystemMetrics(SmXVirtualScreen);
        var top = GetSystemMetrics(SmYVirtualScreen);
        var width = GetSystemMetrics(SmCxVirtualScreen);
        var height = GetSystemMetrics(SmCyVirtualScreen);
        if (width <= 0 || height <= 0)
            throw new IOException("Não foi possível obter as dimensões da tela para captura.");

        var screenDc = IntPtr.Zero;
        var memoryDc = IntPtr.Zero;
        var hBitmap = IntPtr.Zero;
        var previousObject = IntPtr.Zero;
        try
        {
            screenDc = GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero)
                throw new IOException("Não foi possível acessar o dispositivo gráfico da tela.");

            memoryDc = CreateCompatibleDC(screenDc);
            if (memoryDc == IntPtr.Zero)
                throw new IOException("Não foi possível preparar o buffer de captura.");

            var info = new BitmapInfo
            {
                bmiHeader = new BitmapInfoHeader
                {
                    biSize = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    biWidth = width,
                    biHeight = -height, // top-down DIB
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = BiRgb
                }
            };

            hBitmap = CreateDIBSection(screenDc, ref info, DibRgbColors, out var bits, IntPtr.Zero, 0);
            if (hBitmap == IntPtr.Zero || bits == IntPtr.Zero)
                throw new IOException("Não foi possível alocar a imagem de captura.");

            previousObject = SelectObject(memoryDc, hBitmap);
            if (!BitBlt(memoryDc, 0, 0, width, height, screenDc, left, top, Srccopy | Captureblt))
                throw new IOException("Falha ao capturar os pixels da tela.");

            var bgra = new byte[checked(width * height * 4)];
            Marshal.Copy(bits, bgra, 0, bgra.Length);
            for (var i = 3; i < bgra.Length; i += 4)
                bgra[i] = 0xFF;

            return new ScreenSnapshot(new RawImage(bgra, width, height), left, top);
        }
        finally
        {
            if (memoryDc != IntPtr.Zero && previousObject != IntPtr.Zero)
                _ = SelectObject(memoryDc, previousObject);
            if (hBitmap != IntPtr.Zero)
                _ = DeleteObject(hBitmap);
            if (memoryDc != IntPtr.Zero)
                _ = DeleteDC(memoryDc);
            if (screenDc != IntPtr.Zero)
                _ = ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    internal static void PositionOverlayToVirtualScreen(Window window, ScreenSnapshot snapshot)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        _ = SetWindowPos(
            hwnd,
            HwndTopmost,
            snapshot.LeftPx,
            snapshot.TopPx,
            snapshot.Image.Width,
            snapshot.Image.Height,
            SwpShowWindow);
    }

    internal static (int X, int Y, int Width, int Height) GetActiveMonitorBounds(ScreenSnapshot snapshot)
    {
        if (!GetCursorPos(out var pt))
            pt = default;

        var monitor = MonitorFromPoint(pt, MonitorDefaultToNearest);
        var mi = new MonitorInfo { cbSize = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref mi))
        {
            var x = Math.Clamp(mi.rcMonitor.Left - snapshot.LeftPx, 0, snapshot.Image.Width - 1);
            var y = Math.Clamp(mi.rcMonitor.Top - snapshot.TopPx, 0, snapshot.Image.Height - 1);
            var right = Math.Clamp(mi.rcMonitor.Right - snapshot.LeftPx, x + 1, snapshot.Image.Width);
            var bottom = Math.Clamp(mi.rcMonitor.Bottom - snapshot.TopPx, y + 1, snapshot.Image.Height);
            return (x, y, right - x, bottom - y);
        }

        return (0, 0, snapshot.Image.Width, snapshot.Image.Height);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader bmiHeader;
        public uint bmiColors;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Point pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(
        IntPtr hdc,
        ref BitmapInfo pbmi,
        uint usage,
        out IntPtr ppvBits,
        IntPtr hSection,
        uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr ho);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(
        IntPtr hdc,
        int x,
        int y,
        int cx,
        int cy,
        IntPtr hdcSrc,
        int x1,
        int y1,
        uint rop);
}
