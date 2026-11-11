using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using RectI = System.Drawing.Rectangle;

namespace ScreenTranslate;

internal sealed class TranslationPopup : Window, IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly PopupPage _page;
    private readonly RectI _selection;
    private PopupLayout _layout;
    private DateTime _until;
    private bool _closed;
    internal event Action? OriginLost;

    internal TranslationPopup(string text, string? original, RectI selection, double seconds, nint origin, string? hint = null)
        : this(new PopupPage(text, original, hint), selection, seconds, origin, MonitorLayout.ForSelection(selection)) { }

    internal TranslationPopup(PopupPage page, RectI selection, double seconds, nint origin, PopupLayout layout)
    {
        _page = page; _selection = selection; _layout = layout;
        Title = "Screen Translate — перевод";
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        ShowActivated = false; ShowInTaskbar = false; Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        PassiveWindow.Configure(this);
        SetMeasuredContent();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            WindowNative.SetStyle(hwnd, -20, WindowNative.GetStyle(hwnd, -20) | 0x08000000 | 0x00000020 | 0x00000080);
            // Move the still-hidden HWND to the target monitor before its final DPI measurement.
            WindowNative.SetWindowPos(hwnd, new nint(-1), layout.WorkArea.X, layout.WorkArea.Y, 0, 0, 0x0011);
        };
        Loaded += (_, _) => { Position(); _until = DateTime.UtcNow.AddSeconds(seconds); _timer.Start(); };
        _timer.Tick += (_, _) =>
        {
            if (origin != 0 && WindowNative.GetForegroundWindow() != origin) { OriginLost?.Invoke(); Dispose(); }
            else if (DateTime.UtcNow >= _until) Dispose();
        };
        Closed += (_, _) => { _closed = true; _timer.Stop(); };
    }

    internal void ShowPrepared()
    {
        var handle = new WindowInteropHelper(this).EnsureHandle();
        var dpi = MonitorLayout.GetDpiForWindow(handle);
        if (dpi == 0) throw new PopupLayoutException();
        _layout = MonitorLayout.ForSelection(_selection) with { DpiX = dpi / 96.0, DpiY = dpi / 96.0 };
        // Fail explicitly if the monitor changed after pagination, never silently clip a page.
        SetMeasuredContent(); Position(); Show();
    }
    private void SetMeasuredContent()
    {
        if (!PopupPagination.Fits(_page, _layout)) throw new PopupLayoutException();
        var size = PopupPagination.Measure(_page, _layout);
        Width = _layout.Width; Height = Math.Ceiling(size.Height * _layout.DpiY) / _layout.DpiY;
        Content = PopupPagination.Content(_page);
    }
    private void Position()
    {
        var size = new System.Drawing.Size((int)Math.Ceiling(Width * _layout.DpiX), (int)Math.Ceiling(Height * _layout.DpiY));
        var point = TranslationPolicy.Place(_selection, _layout.WorkArea, size);
        WindowNative.SetWindowPos(new WindowInteropHelper(this).Handle, new nint(-1), point.X, point.Y, size.Width, size.Height, 0x0010);
    }
    public void Dispose() { if (!_closed) Close(); }
}
