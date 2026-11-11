using System.Drawing;
using System.Windows.Interop;
using ScreenTranslate.Capture;
using ScreenTranslate.Capture.Ocr;

namespace ScreenTranslate;

internal sealed class WindowsRequestPlatform(SystemOcrRunner ocr, GameTranslator translator, Func<nint> host,
    Func<bool> diagnostics, Action<string> notify, Action<string> trace) : IRequestPlatform
{
    private sealed class Frame(CapturedFrame value) : IRequestFrame
    {
        internal CapturedFrame Value { get; } = value;
        public Rectangle Area => Value.Region.ScreenBounds;
        public IRequestFrame Crop(Rectangle relativeArea) => new Frame(Value.Crop(relativeArea));
        public void Dispose() => Value.Dispose();
    }
    public RequestContext Context()
    {
        var origin = WindowNative.GetForegroundWindow();
        return new RequestContext(origin, WindowNative.GetBounds(origin), System.Windows.Forms.Screen.AllScreens.Select(s => s.Bounds).ToArray());
    }
    public Rectangle SelectMonitor() => new ScreenCapture().SelectMonitor().ScreenBounds;
    public async Task<IRequestFrame> CaptureAsync(Rectangle area)
    {
        if (area.Width <= 0 || area.Height <= 0 || (long)area.Width * area.Height > 34_000_000)
            throw new InvalidOperationException("Capture exceeds the memory budget.");
        return new Frame(await Task.Run(() => new ScreenCapture().CaptureMonitor(new CaptureRegion(area))));
    }
    public async Task<Rectangle?> SelectAsync(IRequestFrame frame, nint origin, CancellationToken token)
    {
        using var escape = new GlobalHotkey(host(), 0x5356);
        var available = escape.TryRegister(new HotkeyBinding(HotkeyModifiers.NoRepeat, System.Windows.Input.Key.Escape));
        var overlay = new SelectionWindow(((Frame)frame).Value, origin, available) { ShowInTaskbar = diagnostics() };
        var completion = new TaskCompletionSource<Rectangle?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = false;
        overlay.Closed += (_, _) => { closed = true; completion.TrySetResult(overlay.Selection); };
        escape.Triggered += (_, _) => { if (!closed) overlay.Close(); };
        using var cancellation = token.Register(() =>
        {
            if (overlay.Dispatcher.CheckAccess()) { if (!closed) overlay.Close(); }
            else if (!overlay.Dispatcher.HasShutdownStarted) overlay.Dispatcher.BeginInvoke(() => { if (!closed) overlay.Close(); });
        });
        try
        {
            token.ThrowIfCancellationRequested();
            overlay.Show();
            return await completion.Task;
        }
        finally { if (!closed) overlay.Close(); }
    }
    public uint MaxImageDimension => ocr.MaxImageDimension;
    public async Task<string> RecognizeAsync(IRequestFrame frame) => OcrText.Context(await ocr.RecognizeAsync(((Frame)frame).Value));
    // Cancellation stops subsequent chunks, but never releases the gate while a provider is in flight.
    public Task<GameTranslation> TranslateAsync(string text, bool alternative, CancellationToken token)
        => translator.TranslateToCompletionAsync(text, alternative, token);
    public IReadOnlyList<PopupPage> Pages(string translation, string? original, Rectangle area, string hint)
        => PopupPagination.Paginate(translation, original, hint, MonitorLayout.ForSelection(area));
    public IDisposable Present(PopupPage page, Rectangle area, double seconds, nint origin, Action originLost)
    {
        var popup = new TranslationPopup(page, area, seconds, origin, MonitorLayout.ForSelection(area));
        popup.OriginLost += originLost;
        try { popup.ShowPrepared(); return popup; }
        catch { popup.Dispose(); throw; }
    }
    public void Notify(string message) => notify(message);
    public void Diagnostic(string code) => trace(code);
}
