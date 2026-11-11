using System.ComponentModel;
using System.Runtime.InteropServices;
using RectI = System.Drawing.Rectangle;

namespace ScreenTranslate;

internal static class MonitorLayout
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { internal int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern nint MonitorFromRect(ref NativeRect rectangle, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint monitor, int kind, out uint x, out uint y);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint window);

    internal static PopupLayout ForSelection(RectI selection)
    {
        var rectangle = new NativeRect { Left = selection.Left, Top = selection.Top, Right = selection.Right, Bottom = selection.Bottom };
        var monitor = MonitorFromRect(ref rectangle, 2);
        if (monitor == 0 || GetDpiForMonitor(monitor, 0, out var x, out var y) != 0 || x == 0 || y == 0)
            throw new Win32Exception("Cannot determine monitor DPI.");
        return new PopupLayout(System.Windows.Forms.Screen.FromRectangle(selection).WorkingArea, x / 96.0, y / 96.0);
    }
}
