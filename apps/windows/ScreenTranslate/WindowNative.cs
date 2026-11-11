using System.Runtime.InteropServices;

namespace ScreenTranslate;

internal static class WindowNative
{
    [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hwnd, out WindowRect bounds);
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect { internal int Left, Top, Right, Bottom; }
    internal static System.Drawing.Rectangle? GetBounds(nint hwnd)
        => hwnd != 0 && GetWindowRect(hwnd, out var bounds)
            ? System.Drawing.Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom)
            : null;
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetStyle(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] internal static extern nint SetStyle(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
}
