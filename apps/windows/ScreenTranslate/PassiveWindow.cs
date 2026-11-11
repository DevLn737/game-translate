using System.Windows;
using System.Windows.Interop;

namespace ScreenTranslate;

internal static class PassiveWindow
{
    internal const long NoActivate = 0x08000000;
    internal const long ToolWindow = 0x00000080;

    internal static void Configure(Window window)
    {
        window.ShowActivated = false;
        window.Focusable = false;
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            WindowNative.SetStyle(handle, -20, WindowNative.GetStyle(handle, -20) | (nint)NoActivate | (nint)ToolWindow);
            HwndSource.FromHwnd(handle)?.AddHook(MouseActivationHook);
        };
    }

    private static nint MouseActivationHook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        // MA_NOACTIVATE retains the mouse message, so dragging still reaches WPF
        // while the game's window remains foreground. Do not eat the click.
        if (message == 0x0021) { handled = true; return 3; }
        return 0;
    }
}
