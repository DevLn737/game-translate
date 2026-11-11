using System.Drawing;
using ScreenTranslate;
using Xunit;

public class RequestCoordinatorTests
{
    [Fact]
    public void CancellationDuringCaptureKeepsGateUntilNativeCompletion()
    {
        using var h = new Harness();
        var capture = new TaskCompletionSource<IRequestFrame>();
        var firstFrame = new Frame(Harness.Monitor);
        h.Platform.Capture = _ => capture.Task;
        var first = h.Start();
        h.Platform.Capture = r => Task.FromResult<IRequestFrame>(new Frame(r));
        var second = h.Start();
        Assert.Equal(1, h.Platform.Captures);
        capture.SetResult(firstFrame); h.PumpUntil(() => h.Coordinator.Stage == RequestStage.Presentation);
        Assert.True(firstFrame.Disposed);
        Assert.Equal(2, h.Platform.Captures);
        Assert.Equal("translated", h.Coordinator.LastTranslation);
        Assert.True(first.IsCompletedSuccessfully);
        h.Finish(second);
    }

    [Fact]
    public void CancelledOcrRetainsFrameAndPreventsOverlappingCapture()
    {
        using var h = new Harness();
        var ocr = new TaskCompletionSource<string>();
        h.Platform.Ocr = _ => ocr.Task;
        var first = h.Start();
        var crop = h.Platform.RecognizedFrames.Single();
        h.Platform.Ocr = _ => Task.FromResult("new text");
        var second = h.Start();
        Assert.False(crop.Disposed);
        Assert.Equal(1, h.Platform.Captures);
        ocr.SetResult("obsolete"); h.PumpUntil(() => h.Coordinator.Stage == RequestStage.Presentation);
        Assert.True(crop.Disposed);
        Assert.Equal("new text", h.Coordinator.LastText?.Text);
        Assert.Equal(2, h.Platform.Captures);
        Assert.True(first.IsCompletedSuccessfully);
        h.Finish(second);
    }

    [Fact]
    public void LateTranslationAndOldPopupCallbackCannotChangeNewSession()
    {
        using var h = new Harness();
        var translation = new TaskCompletionSource<GameTranslation>();
        h.Platform.Translate = (_, _) => translation.Task;
        var first = h.Start();
        var oldCallback = h.Platform.Presentations.Last().Lost;
        h.Platform.Translate = (_, _) => Task.FromResult(new GameTranslation("new result", "fake"));
        var second = h.Start();
        Assert.Equal(1, h.Platform.Translations);
        oldCallback(); h.Pump();
        translation.SetResult(new GameTranslation("obsolete", "fake")); h.PumpUntil(() => h.Coordinator.Stage == RequestStage.Presentation);
        Assert.Equal("new result", h.Coordinator.LastTranslation);
        Assert.DoesNotContain(h.Platform.Presentations, p => p.Page.Text == "obsolete");
        oldCallback(); h.Pump();
        Assert.Equal(RequestStage.Presentation, h.Coordinator.Stage);
        Assert.True(first.IsCompletedSuccessfully);
        h.Finish(second);
    }

    [Fact]
    public void CancellationDuringSelectionIgnoresLateSelection()
    {
        using var h = new Harness();
        var selection = new TaskCompletionSource<Rectangle?>();
        h.Platform.Selection = (_, _) => selection.Task;
        var first = h.Start();
        h.Platform.Selection = (_, _) => Task.FromResult<Rectangle?>(new Rectangle(120, 120, 80, 30));
        var second = h.Start();
        var anchor = h.Coordinator.Anchor;
        selection.SetResult(new Rectangle(150, 150, 40, 40)); h.Pump();
        Assert.Equal(anchor, h.Coordinator.Anchor);
        Assert.Single(h.Platform.RecognizedFrames);
        Assert.True(first.IsCompletedSuccessfully);
        h.Finish(second);
    }

    [Fact]
    public void ShutdownDuringOcrDisposesAfterCompletionWithoutPresentation()
    {
        using var h = new Harness();
        var ocr = new TaskCompletionSource<string>(); h.Platform.Ocr = _ => ocr.Task;
        var task = h.Start(); var frame = h.Platform.RecognizedFrames.Single();
        h.Coordinator.Dispose(); h.Pump();
        Assert.False(frame.Disposed);
        ocr.SetResult("late"); h.Pump();
        Assert.True(frame.Disposed);
        Assert.True(task.IsCompletedSuccessfully);
        Assert.Null(h.Coordinator.LastText);
        Assert.All(h.Platform.Presentations, p => Assert.True(p.Disposed));
        Assert.True(h.Coordinator.CaptureAsync(false).IsCompletedSuccessfully);
    }

    [Fact]
    public void ForegroundChangeCancelsEvenWhileNativeOperationIsPending()
    {
        using var h = new Harness();
        var ocr = new TaskCompletionSource<string>(); h.Platform.Ocr = _ => ocr.Task;
        var task = h.Start();
        h.Platform.Current = h.Platform.Current with { Origin = 99 };
        h.Advance(.2);
        Assert.Null(h.Coordinator.Anchor);
        Assert.All(h.Platform.Presentations, p => Assert.True(p.Disposed));
        h.Platform.Current = h.Platform.Current with { Origin = 42 };
        ocr.SetResult("late"); h.Pump();
        Assert.True(task.IsCompletedSuccessfully);
        Assert.Null(h.Coordinator.LastText);
    }

    [Fact]
    public void ManualOutsideSelectionStillTranslatesButClearsPreviousAnchor()
    {
        using var h = new Harness();
        var first = h.Start(); Assert.NotNull(h.Coordinator.Anchor); h.Finish(first);
        h.Platform.Selection = (_, _) => Task.FromResult<Rectangle?>(new Rectangle(0, 0, 200, 200));
        var second = h.Start();
        Assert.Null(h.Coordinator.Anchor);
        Assert.Equal("translated", h.Coordinator.LastTranslation);
        h.Finish(second);
    }

    [Fact]
    public void MovementDuringSelectionTranslatesSnapshotButInvalidatesAnchor()
    {
        using var h = new Harness();
        h.Platform.Selection = (_, _) =>
        {
            h.Platform.Current = h.Platform.Current with { Bounds = new Rectangle(110, 100, 800, 600) };
            return Task.FromResult<Rectangle?>(new Rectangle(120, 120, 80, 30));
        };
        var task = h.Start();
        Assert.Null(h.Coordinator.Anchor);
        Assert.Equal("translated", h.Coordinator.LastTranslation);
        h.Finish(task);
    }

    [Fact]
    public void RepeatRevalidatesAfterWaitingForGateAndClearsStaleAnchor()
    {
        using var h = new Harness();
        var ocr = new TaskCompletionSource<string>(); h.Platform.Ocr = _ => ocr.Task;
        var first = h.Start(); Assert.NotNull(h.Coordinator.Anchor);
        var second = h.Coordinator.CaptureAsync(true); h.Advance(.05);
        h.Platform.Current = h.Platform.Current with { Bounds = new Rectangle(200, 100, 800, 600) };
        ocr.SetResult("old"); h.PumpUntil(() => second.IsCompleted);
        Assert.Equal(1, h.Platform.Captures);
        Assert.Null(h.Coordinator.Anchor);
        Assert.True(first.IsCompletedSuccessfully);
        Assert.True(second.IsCompletedSuccessfully);
    }

    [Fact]
    public void GeometryChangeDuringActualCaptureRejectsSnapshot()
    {
        using var h = new Harness();
        var capture = new TaskCompletionSource<IRequestFrame>(); h.Platform.Capture = _ => capture.Task;
        var task = h.Start(); var frame = new Frame(Harness.Monitor);
        h.Platform.Current = h.Platform.Current with { Bounds = new Rectangle(150, 100, 800, 600) };
        capture.SetResult(frame); h.Pump();
        Assert.True(frame.Disposed);
        Assert.Empty(h.Platform.RecognizedFrames);
        Assert.Null(h.Coordinator.Anchor);
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public void FakeClockAdvancesAllPagesUsingDisplayedTextDuration()
    {
        using var h = new Harness();
        h.Platform.ResultPages = [new PopupPage("first", "full original", "hint"), new PopupPage("second", Heading: "Оригинал")];
        var task = h.Start();
        Assert.Equal("first", h.Platform.Presentations.Last().Page.Text);
        var duration = TranslationPolicy.ReadingSeconds(h.Platform.ResultPages[0].ReadingText, 3);
        h.Advance(duration - .01);
        Assert.Equal("first", h.Platform.Presentations.Last().Page.Text);
        h.Advance(.01);
        Assert.Equal("second", h.Platform.Presentations.Last().Page.Text);
        h.Advance(TranslationPolicy.ReadingSeconds(h.Platform.ResultPages[1].ReadingText, 3));
        Assert.True(task.IsCompletedSuccessfully);
        Assert.All(h.Platform.Presentations, p => Assert.True(p.Disposed));
    }

    [Fact]
    public void CancellingInitialDelayNeverCaptures()
    {
        using var h = new Harness();
        var task = h.Coordinator.CaptureAsync(false);
        h.Coordinator.Cancel(); h.Advance(1);
        Assert.True(task.IsCompletedSuccessfully);
        Assert.Equal(0, h.Platform.Captures);
    }

    [Fact]
    public void ScreenshotsAreDisposedBeforeTranslationStarts()
    {
        using var h = new Harness();
        var frame = new Frame(Harness.Monitor);
        h.Platform.Capture = _ => Task.FromResult<IRequestFrame>(frame);
        h.Platform.Translate = (_, _) =>
        {
            Assert.True(frame.Disposed);
            Assert.True(h.Platform.RecognizedFrames.Single().Disposed);
            return Task.FromResult(new GameTranslation("result", "fake"));
        };
        var task = h.Start(); Assert.Equal("result", h.Coordinator.LastTranslation); h.Finish(task);
    }

    [Fact]
    public void TimeoutCancelsProviderButWaitsForActualCompletionAndShowsFailure()
    {
        using var h = new Harness();
        var provider = new TaskCompletionSource<GameTranslation>();
        h.Platform.Translate = (_, _) => provider.Task;
        var task = h.Start();
        h.Advance(24);
        Assert.True(h.Platform.TranslationToken.IsCancellationRequested);
        Assert.False(task.IsCompleted);
        provider.SetResult(new GameTranslation("too late", "fake")); h.Pump();
        Assert.Null(h.Coordinator.LastTranslation);
        Assert.Contains("24", h.Platform.Presentations.Last().Page.Text);
        h.Advance(6);
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public void NewRequestCancelsProviderTokenWhileOldProviderKeepsGate()
    {
        using var h = new Harness();
        var provider = new TaskCompletionSource<GameTranslation>();
        h.Platform.Translate = (_, _) => provider.Task;
        var first = h.Start(); var oldToken = h.Platform.TranslationToken;
        var second = h.Start();
        Assert.True(oldToken.IsCancellationRequested);
        Assert.Equal(1, h.Platform.Translations);
        h.Coordinator.Cancel(); provider.SetResult(new GameTranslation("obsolete", "fake")); h.PumpUntil(() => first.IsCompleted && second.IsCompleted);
        Assert.True(first.IsCompletedSuccessfully); Assert.True(second.IsCompletedSuccessfully);
        Assert.Equal(1, h.Platform.Translations);
    }

    private sealed class Harness : IDisposable
    {
        internal static readonly Rectangle Monitor = new(0, 0, 1920, 1080);
        private readonly SynchronizationContext? _previous = SynchronizationContext.Current;
        private readonly PumpContext _context = new();
        internal readonly ManualClock Clock = new();
        internal readonly FakePlatform Platform = new();
        internal RequestCoordinator Coordinator { get; }
        internal Harness()
        {
            SynchronizationContext.SetSynchronizationContext(_context);
            Coordinator = new RequestCoordinator(Platform, () => new Preferences(Seconds: 3), Clock);
        }
        internal Task Start() { var task = Coordinator.CaptureAsync(false); Advance(.05); return task; }
        internal void Pump() => _context.Drain();
        internal void PumpUntil(Func<bool> condition) => _context.Until(condition);
        internal void Advance(double seconds) { Clock.Advance(TimeSpan.FromSeconds(seconds), Pump); Pump(); }
        internal void Finish(Task task) { Coordinator.Cancel(); Pump(); Assert.True(task.IsCompletedSuccessfully); }
        public void Dispose() { Coordinator.Dispose(); Pump(); SynchronizationContext.SetSynchronizationContext(_previous); }
    }
    private sealed class PumpContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();
        private readonly AutoResetEvent _ready = new(false);
        public override void Post(SendOrPostCallback d, object? state) { lock (_queue) _queue.Enqueue((d, state)); _ready.Set(); }
        internal void Until(Func<bool> condition)
        {
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                Drain(); if (condition()) return;
                Assert.True(timeout.Elapsed < TimeSpan.FromSeconds(3), "Expected continuation did not complete.");
                _ready.WaitOne(TimeSpan.FromMilliseconds(50));
            }
        }
        internal void Drain()
        {
            for (var i = 0; i < 10000; i++)
            {
                (SendOrPostCallback Callback, object? State) item;
                lock (_queue) { if (_queue.Count == 0) return; item = _queue.Dequeue(); }
                item.Callback(item.State);
            }
            throw new InvalidOperationException("Continuation loop did not settle.");
        }
    }
    private sealed class Frame(Rectangle area) : IRequestFrame
    {
        internal bool Disposed;
        public Rectangle Area => area;
        public IRequestFrame Crop(Rectangle relativeArea) => new Frame(new Rectangle(area.X + relativeArea.X, area.Y + relativeArea.Y, relativeArea.Width, relativeArea.Height));
        public void Dispose() => Disposed = true;
    }
    private sealed class Presentation(PopupPage page, Action lost) : IDisposable
    {
        internal PopupPage Page = page;
        internal Action Lost = lost;
        internal bool Disposed;
        public void Dispose() => Disposed = true;
    }
    private sealed class FakePlatform : IRequestPlatform
    {
        internal RequestContext Current = new(42, new Rectangle(100, 100, 800, 600), [Harness.Monitor]);
        internal Func<Rectangle, Task<IRequestFrame>> Capture = r => Task.FromResult<IRequestFrame>(new Frame(r));
        internal Func<IRequestFrame, CancellationToken, Task<Rectangle?>> Selection = (_, _) => Task.FromResult<Rectangle?>(new Rectangle(120, 120, 80, 30));
        internal Func<IRequestFrame, Task<string>> Ocr = _ => Task.FromResult("source");
        internal Func<string, bool, Task<GameTranslation>> Translate = (_, _) => Task.FromResult(new GameTranslation("translated", "fake"));
        internal List<Frame> RecognizedFrames = [];
        internal List<Presentation> Presentations = [];
        internal IReadOnlyList<PopupPage>? ResultPages;
        internal int Captures, Translations;
        internal CancellationToken TranslationToken;
        public RequestContext Context() => Current;
        public Rectangle SelectMonitor() => Harness.Monitor;
        public Task<IRequestFrame> CaptureAsync(Rectangle area) { Captures++; return Capture(area); }
        public Task<Rectangle?> SelectAsync(IRequestFrame frame, nint origin, CancellationToken token) => Selection(frame, token);
        public uint MaxImageDimension => 4000;
        public Task<string> RecognizeAsync(IRequestFrame frame) { RecognizedFrames.Add((Frame)frame); return Ocr(frame); }
        public Task<GameTranslation> TranslateAsync(string text, bool alternative, CancellationToken token) { Translations++; TranslationToken = token; return Translate(text, alternative); }
        public IReadOnlyList<PopupPage> Pages(string translation, string? original, Rectangle area, string hint) => ResultPages ?? [new PopupPage(translation, original, hint)];
        public IDisposable Present(PopupPage page, Rectangle area, double seconds, nint origin, Action originLost)
        { var item = new Presentation(page, originLost); Presentations.Add(item); return item; }
        public void Notify(string message) { }
        public void Diagnostic(string code) { }
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;
        private readonly List<Timer> _timers = [];
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(_ticks);
        public override long GetTimestamp() => _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { var timer = new Timer(this, callback, state); _timers.Add(timer); timer.Change(dueTime, period); return timer; }
        internal void Advance(TimeSpan duration, Action pump)
        {
            var target = _ticks + duration.Ticks;
            while (true)
            {
                var next = _timers.Where(t => t.Due <= target).OrderBy(t => t.Due).FirstOrDefault();
                if (next is null) break;
                _ticks = next.Due; next.Fire(); pump();
            }
            _ticks = target;
        }
        private sealed class Timer(ManualClock owner, TimerCallback callback, object? state) : ITimer
        {
            internal long Due = long.MaxValue;
            private long _period;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            { Due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : owner._ticks + dueTime.Ticks; _period = period.Ticks; return true; }
            internal void Fire() { Due = _period > 0 ? Due + _period : long.MaxValue; callback(state); }
            public void Dispose() => Due = long.MaxValue;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
