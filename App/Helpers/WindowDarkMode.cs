using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace VideoNR.Helpers;

internal static class WindowDarkMode
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwcpRound = 2;
    private const int DarkroomBorderBgr = 0x002E2E2E;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    internal static void Apply(Window window, FrameworkElement? chromeRoot = null)
    {
        window.SourceInitialized += (_, _) => ApplyDwmAttributes(window);

        if (chromeRoot is not null)
        {
            void SyncMaximizedPadding() =>
                chromeRoot.Margin = window.WindowState == WindowState.Maximized
                    ? new Thickness(7)
                    : new Thickness(0);

            window.StateChanged += (_, _) => SyncMaximizedPadding();
            window.Loaded += (_, _) => SyncMaximizedPadding();
        }
    }

    private static void ApplyDwmAttributes(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == nint.Zero) return;

            var dark = 1;
            if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int)) != 0)
                _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeLegacy, ref dark, sizeof(int));

            var corner = DwmwcpRound;
            _ = DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref corner, sizeof(int));

            var border = DarkroomBorderBgr;
            _ = DwmSetWindowAttribute(handle, DwmwaBorderColor, ref border, sizeof(int));
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }
}
