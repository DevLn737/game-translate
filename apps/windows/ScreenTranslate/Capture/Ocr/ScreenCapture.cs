using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Security.Cryptography;

namespace ScreenTranslate.Capture.Ocr;

internal readonly record struct CaptureRegion(Rectangle ScreenBounds);

internal sealed class CapturedFrame(Bitmap bitmap, CaptureRegion region) : IDisposable
{
    private Bitmap? _bitmap = bitmap;
    internal CaptureRegion Region { get; } = region;
    internal CapturedFrame Crop(Rectangle localBounds) => new(
        (_bitmap ?? throw new ObjectDisposedException(nameof(CapturedFrame))).Clone(localBounds, PixelFormat.Format32bppPArgb),
        new CaptureRegion(new Rectangle(Region.ScreenBounds.X + localBounds.X, Region.ScreenBounds.Y + localBounds.Y, localBounds.Width, localBounds.Height)));
    internal System.Windows.Media.Imaging.BitmapSource CreatePreview()
    {
        var value = _bitmap ?? throw new ObjectDisposedException(nameof(CapturedFrame));
        var pixels = CopyPixels(value);
        var source = System.Windows.Media.Imaging.BitmapSource.Create(value.Width, value.Height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32, null, pixels, value.Width * 4);
        source.Freeze(); return source;
    }
    internal SoftwareBitmap CreateSoftwareBitmap()
    {
        var value = _bitmap ?? throw new ObjectDisposedException(nameof(CapturedFrame));
        return SoftwareBitmap.CreateCopyFromBuffer(CryptographicBuffer.CreateFromByteArray(CopyPixels(value)), BitmapPixelFormat.Bgra8, value.Width, value.Height, BitmapAlphaMode.Premultiplied);
    }
    private static byte[] CopyPixels(Bitmap bitmap)
    {
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try { var row = checked(bitmap.Width * 4); var pixels = new byte[checked(row * bitmap.Height)]; for (var y = 0; y < bitmap.Height; y++) Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), pixels, y * row, row); return pixels; }
        finally { bitmap.UnlockBits(data); }
    }
    public void Dispose() { _bitmap?.Dispose(); _bitmap = null; }
}

internal sealed class ScreenCapture
{
    internal CaptureRegion SelectMonitor()
    {
        if (!Native.GetCursorPos(out var point)) throw new Win32Exception("Windows did not return the cursor position.");
        var monitor = Native.MonitorFromPoint(point, 2);
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !Native.GetMonitorInfo(monitor, ref info)) throw new Win32Exception("Windows did not return monitor bounds.");
        return new CaptureRegion(Rectangle.FromLTRB(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom));
    }
    internal CapturedFrame CaptureMonitor(CaptureRegion region)
    {
        var bitmap = new Bitmap(region.ScreenBounds.Width, region.ScreenBounds.Height, PixelFormat.Format32bppPArgb);
        try { using var graphics = Graphics.FromImage(bitmap); graphics.CopyFromScreen(region.ScreenBounds.Location, Point.Empty, region.ScreenBounds.Size, CopyPixelOperation.SourceCopy); return new CapturedFrame(bitmap, region); }
        catch { bitmap.Dispose(); throw; }
    }
}

internal static class Native
{
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetWindowRect(nint window, out NativeRect bounds);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] internal static extern nint MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
[StructLayout(LayoutKind.Sequential)] internal struct NativePoint { internal int X; internal int Y; }
[StructLayout(LayoutKind.Sequential)] internal struct NativeRect { internal int Left; internal int Top; internal int Right; internal int Bottom; }
[StructLayout(LayoutKind.Sequential)] internal struct MonitorInfo { internal uint Size; internal NativeRect Monitor; internal NativeRect Work; internal uint Flags; }
