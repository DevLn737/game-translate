using System.Drawing;
using System.Net.Http;

namespace ScreenTranslate;

// Deliberately local to one UI thread, not a general job framework. Cancellation
// invalidates ownership immediately; native resources stay alive until completion.
internal sealed class RequestCoordinator(IRequestPlatform platform, Func<Preferences> preferences, TimeProvider? clock = null) : IDisposable
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly SemaphoreSlim _nativeGate = new(1, 1);
    private readonly SemaphoreSlim _translationGate = new(1, 1);
    private RequestSession? _current;
    private bool _stopped;
    internal CaptureAnchor? Anchor { get; private set; }
    internal RequestText? LastText { get; private set; }
    internal string? LastTranslation { get; private set; }
    internal RequestStage? Stage => _current?.Stage;

    internal void Cancel() { var old = _current; _current = null; old?.Cancel(); }
    public void Dispose() { _stopped = true; Cancel(); }
    private bool Owns(RequestSession session) => !_stopped && _current == session && !session.Token.IsCancellationRequested;
    private RequestContext Guard(RequestSession session)
    {
        if (!Owns(session)) throw new OperationCanceledException(session.Token);
        var context = platform.Context();
        if (context.Origin != session.Origin) { Anchor = null; session.Cancel(); throw new OperationCanceledException(session.Token); }
        return context;
    }
    private Task Delay(double seconds, CancellationToken token) => Task.Delay(TimeSpan.FromSeconds(seconds), _clock, token);

    private async Task WatchOrigin(RequestSession session)
    {
        try
        {
            while (Owns(session)) { await Delay(.15, session.Token); Guard(session); }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (Owns(session)) session.Cancel(); }
    }

    internal async Task CaptureAsync(bool repeat)
    {
        if (_stopped) return;
        Cancel(); LastText = null;
        if (!repeat) Anchor = null;
        var session = new RequestSession(platform.Context().Origin); _current = session;
        _ = WatchOrigin(session);
        Rectangle area = default;
        try
        {
            session.Stage = RequestStage.Capture;
            await Delay(.05, session.Token);
            IRequestFrame frame;
            RequestContext capturedContext;
            await _nativeGate.WaitAsync(session.Token);
            try
            {
                // Do not trust a context sampled before waiting for a slow driver.
                capturedContext = Guard(session);
                if (repeat)
                {
                    if (Anchor is not { } saved || !saved.CanRepeat(capturedContext.Origin, capturedContext.Bounds, capturedContext.Monitors))
                    {
                        Anchor = null; platform.Notify("Сначала выделите область заново: окно или его положение изменилось."); return;
                    }
                    area = saved.Area;
                }
                else area = platform.SelectMonitor();
                if (!capturedContext.Monitors.Any(m => m.Contains(area)))
                { Anchor = null; session.Cancel(); throw new OperationCanceledException(session.Token); }
                frame = await platform.CaptureAsync(area);
                try
                {
                    var after = Guard(session);
                    if (after.Bounds != capturedContext.Bounds || !after.Monitors.Any(m => m.Contains(area)))
                    {
                        Anchor = null; session.Cancel();
                        throw new OperationCanceledException(session.Token);
                    }
                }
                catch { frame.Dispose(); throw; }
            }
            finally { _nativeGate.Release(); }

            IRequestFrame crop;
            if (!repeat)
            {
                using (frame)
                {
                    session.Stage = RequestStage.Selection;
                    var selected = await platform.SelectAsync(frame, session.Origin, session.Token);
                    Guard(session);
                    if (selected is not { } relative) return;
                    if (relative.Width <= 0 || relative.Height <= 0 || !new Rectangle(Point.Empty, frame.Area.Size).Contains(relative)) return;
                    area = new Rectangle(frame.Area.X + relative.X, frame.Area.Y + relative.Y, relative.Width, relative.Height);
                    var afterSelection = Guard(session);
                    // A moved window may still translate the static snapshot, but never repeat it.
                    var candidate = new CaptureAnchor(area, session.Origin, capturedContext.Bounds);
                    Anchor = candidate.CanRepeat(afterSelection.Origin, afterSelection.Bounds, afterSelection.Monitors) ? candidate : null;
                    crop = frame.Crop(relative);
                }
            }
            else crop = frame;
            string? text;
            using (crop)
            {
                text = area.Width > platform.MaxImageDimension || area.Height > platform.MaxImageDimension
                    ? null : await Recognize(session, crop, area);
            }
            // The full screenshot dies after cropping; the crop dies after actual OCR,
            // never after the network request or a potentially long sequence of pages.
            Guard(session);
            if (text is null) { await Message(session, "Область слишком большая. Выделите только нужную фразу.", area, 5); return; }
            if (text.Length == 0) { await Message(session, "Текст не найден. Выделите фразу чуть шире.", area, 5); return; }
            if (text.Length > 4000) { await Message(session, "Слишком много текста. Выделите один диалог.", area, 5); return; }
            LastText = new RequestText(text, area, session.Origin);
            await TranslateAndPresent(session, LastText, false);
        }
        catch (OperationCanceledException) { }
        catch (TimeoutException) { if (Owns(session)) await Message(session, "Переводчик не ответил за 24 секунды. Повторите попытку.", area, 6); }
        catch (HttpRequestException) { if (Owns(session)) await Message(session, "Нет ответа от переводчика. Проверьте интернет и повторите горячей клавишей.", area, 6); }
        catch (PopupLayoutException) { if (Owns(session)) platform.Notify("Карточка не помещается на экране. Выделите меньше текста или измените масштаб экрана."); }
        catch (Exception ex)
        {
            platform.Diagnostic(ex.GetType().Name);
            if (Owns(session)) platform.Notify("Не удалось захватить или распознать экран. Попробуйте оконный режим без рамки.");
        }
        finally { if (_current == session) _current = null; session.Dispose(); }
    }

    private async Task<string> Recognize(RequestSession session, IRequestFrame frame, Rectangle area)
    {
        Guard(session); session.Stage = RequestStage.Ocr;
        Present(session, new PopupPage("Распознаём…"), area, 15);
        string recognized;
        await _nativeGate.WaitAsync(session.Token);
        try { Guard(session); recognized = await platform.RecognizeAsync(frame); }
        finally { _nativeGate.Release(); }
        // No WaitAsync(token) around OCR: it still owns the frame after cancellation.
        Guard(session);
        return TranslationPolicy.Normalize(recognized);
    }

    internal async Task AlternativeAsync()
    {
        if (_stopped) return;
        if (LastText is not { } last) { platform.Notify("Сначала выделите текст для перевода."); return; }
        if (platform.Context().Origin != last.Origin) { platform.Notify("Вернитесь в окно, где был выделен текст."); return; }
        Cancel(); var session = new RequestSession(last.Origin); _current = session;
        _ = WatchOrigin(session);
        try { await TranslateAndPresent(session, last, true); }
        catch (OperationCanceledException) { }
        catch (TimeoutException) { if (Owns(session)) await Message(session, "Переводчик не ответил за 24 секунды. Повторите попытку.", last.Area, 6); }
        catch (PopupLayoutException) { if (Owns(session)) platform.Notify("Карточка не помещается на экране. Выделите меньше текста."); }
        catch (Exception ex) { platform.Diagnostic(ex.GetType().Name); if (Owns(session)) platform.Notify("Нет ответа от переводчика. Повторите попытку."); }
        finally { if (_current == session) _current = null; session.Dispose(); }
    }

    private async Task TranslateAndPresent(RequestSession session, RequestText source, bool alternative)
    {
        Guard(session); session.Stage = RequestStage.Translation;
        Present(session, new PopupPage(alternative ? "Переводим · Google…" : "Переводим · Яндекс…"), source.Area, 24);
        GameTranslation result;
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(24), _clock);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(session.Token, budget.Token);
        try
        {
            await _translationGate.WaitAsync(cancellation.Token);
            try
            {
                Guard(session);
                result = await platform.TranslateAsync(source.Text, alternative, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
            }
            finally { _translationGate.Release(); }
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested && !session.Token.IsCancellationRequested)
        { throw new TimeoutException("Translation time budget expired."); }
        Guard(session);
        LastTranslation = result.Text;
        var prefs = preferences();
        var pages = platform.Pages(result.Text, prefs.ShowOriginal ? source.Text : null, source.Area,
            result.Provider + (alternative ? "" : $" · {prefs.AlternativeKey} — Google"));
        session.Stage = RequestStage.Presentation;
        foreach (var page in pages)
        {
            Guard(session);
            var duration = TranslationPolicy.ReadingSeconds(page.ReadingText, prefs.Seconds);
            Present(session, page, source.Area, duration);
            await Delay(duration, session.Token);
        }
    }

    private void Present(RequestSession session, PopupPage page, Rectangle area, double seconds)
    {
        Guard(session);
        session.Presentation?.Dispose(); session.Presentation = null;
        session.Presentation = platform.Present(page, area, seconds, session.Origin, () =>
        {
            if (Owns(session)) Anchor = null;
            session.Cancel();
        });
    }
    private async Task Message(RequestSession session, string text, Rectangle area, double seconds)
    {
        try { Present(session, new PopupPage(text), area, seconds); await Delay(seconds, session.Token); }
        catch (OperationCanceledException) { }
        catch (PopupLayoutException) { if (Owns(session)) platform.Notify(text); }
    }
}
