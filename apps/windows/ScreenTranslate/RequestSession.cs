using System.Drawing;

namespace ScreenTranslate;

internal enum RequestStage { Capture, Selection, Ocr, Translation, Presentation, Finished }

// A callback always keeps this identity, never a mutable "current CTS" field.
internal sealed class RequestSession : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private bool _disposed;
    internal RequestSession(nint origin) { Origin = origin; Token = _cancellation.Token; }
    internal CancellationToken Token { get; }
    internal nint Origin { get; }
    internal RequestStage Stage { get; set; }
    internal IDisposable? Presentation { get; set; }
    internal void Cancel()
    {
        if (_disposed) return;
        _cancellation.Cancel();
        Presentation?.Dispose(); Presentation = null;
    }
    public void Dispose()
    {
        if (_disposed) return;
        Cancel(); _disposed = true; Stage = RequestStage.Finished; _cancellation.Dispose();
    }
}

internal sealed record RequestContext(nint Origin, Rectangle? Bounds, IReadOnlyList<Rectangle> Monitors);
internal sealed record RequestText(string Text, Rectangle Area, nint Origin);

internal interface IRequestFrame : IDisposable
{
    Rectangle Area { get; }
    IRequestFrame Crop(Rectangle relativeArea);
}

// UI entry points and continuations use the owning UI synchronization context.
// Native tasks MUST finish before completing, even if the request was cancelled.
internal interface IRequestPlatform
{
    RequestContext Context();
    Rectangle SelectMonitor();
    Task<IRequestFrame> CaptureAsync(Rectangle area);
    Task<Rectangle?> SelectAsync(IRequestFrame frame, nint origin, CancellationToken token);
    uint MaxImageDimension { get; }
    Task<string> RecognizeAsync(IRequestFrame frame);
    Task<GameTranslation> TranslateAsync(string text, bool alternative, CancellationToken token);
    IReadOnlyList<PopupPage> Pages(string translation, string? original, Rectangle area, string hint);
    IDisposable Present(PopupPage page, Rectangle area, double seconds, nint origin, Action originLost);
    void Notify(string message);
    void Diagnostic(string code);
}
