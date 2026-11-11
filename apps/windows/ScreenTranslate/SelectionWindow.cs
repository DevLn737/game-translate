using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ScreenTranslate.Capture.Ocr;
using Point = System.Windows.Point;
using RectI = System.Drawing.Rectangle;

namespace ScreenTranslate;

internal sealed class SelectionWindow : Window
{
    private readonly Canvas _surface = new();
    private readonly SelectionFrame _outline = new();
    private readonly RectI _monitor;
    private Point? _start;
    private Point _latest;
    private bool _renderPending;
    private double _pixelX = 1, _pixelY = 1;
    internal RectI? Selection { get; private set; }

    internal SelectionWindow(CapturedFrame frame, nint origin = 0, bool escapeAvailable = true)
    {
        _monitor = frame.Region.ScreenBounds;
        Title = "Screen Translate — выделение";
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; Topmost = true; Background = Brushes.Black;
        PassiveWindow.Configure(this);
        WindowStartupLocation = WindowStartupLocation.Manual;
        Cursor = System.Windows.Input.Cursors.Cross;
        var grid = new Grid(); Content = grid;
        grid.Children.Add(new System.Windows.Controls.Image { Source = frame.CreatePreview(), Stretch = Stretch.Fill, IsHitTestVisible = false });
        _surface.Background = Brushes.Transparent;
        grid.Children.Add(_surface);
        _surface.Children.Add(_outline);
        grid.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(25, 30, 38)), CornerRadius = new CornerRadius(9),
            Padding = new Thickness(16, 9, 16, 9), Margin = new Thickness(18),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = escapeAvailable ? "Выделите текст   ·   Esc — отмена" : "Выделите текст   ·   Правая кнопка — отмена", Foreground = Brushes.White, FontSize = 12 }
        });
        SourceInitialized += (_, _) =>
        {
            ApplyMonitorBounds();
        };
        Loaded += (_, _) =>
        {
            // WPF applies its initial layout AFTER SourceInitialized. Reassert physical
            // monitor bounds after that layout. Never enqueue repeated DPI corrections:
            // repositioning a window can itself generate another DPI transition.
            ApplyMonitorBounds();
            UpdateLayout();
            var dpi = VisualTreeHelper.GetDpi(this);
            _pixelX = 1 / dpi.DpiScaleX; _pixelY = 1 / dpi.DpiScaleY;
        };
        MouseLeftButtonDown += (_, e) =>
        {
            StopRendering();
            _start = _latest = e.GetPosition(_surface); _renderPending = true;
            _surface.CaptureMouse(); CompositionTarget.Rendering += RenderSelection;
        };
        MouseMove += (_, e) =>
        {
            if (_start is null) return;
            // Coalesce high-polling-rate mice to one update per composition frame.
            _latest = e.GetPosition(_surface); _renderPending = true;
        };
        MouseLeftButtonUp += (_, e) =>
        {
            if (_start is not { } start) return;
            var end = e.GetPosition(_surface); _start = null; StopRendering(); _surface.ReleaseMouseCapture();
            var rect = SelectionGeometry.ToPixels(start.X, start.Y, end.X, end.Y,
                _surface.ActualWidth, _surface.ActualHeight, _monitor.Size);
            if (rect.Width < 8 || rect.Height < 8) return;
            Selection = rect; Close();
        };
        MouseRightButtonDown += (_, _) => Close();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        var until = DateTime.UtcNow.AddSeconds(30);
        var expiry = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        expiry.Tick += (_, _) =>
        {
            if (DateTime.UtcNow >= until || (origin != 0 && WindowNative.GetForegroundWindow() != origin)) Close();
        };
        Loaded += (_, _) => expiry.Start();
        Closed += (_, _) => { expiry.Stop(); StopRendering(); };
    }

    private void RenderSelection(object? sender, EventArgs e)
    {
        if (!_renderPending || _start is not { } start) return;
        _renderPending = false;
        _outline.Update(new Rect(start, _latest), _pixelX, _pixelY);
    }

    private void StopRendering() { CompositionTarget.Rendering -= RenderSelection; _renderPending = false; }

    private void ApplyMonitorBounds()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        Width = _monitor.Width / dpi.DpiScaleX;
        Height = _monitor.Height / dpi.DpiScaleY;
        WindowNative.SetWindowPos(handle, new nint(-1), _monitor.X, _monitor.Y, _monitor.Width, _monitor.Height, 0x0010);
    }

}
