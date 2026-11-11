using GTranslate.Translators;
using System.Net.Http;

namespace ScreenTranslate;

internal sealed record GameTranslation(string Text, string Provider);

internal sealed class GameTranslator : IDisposable
{
    private readonly Dictionary<string, Func<string, Task<string>>> _clients;
    private readonly List<IDisposable> _owned = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<(string Text, bool Alternative), GameTranslation> _cache = [];
    private readonly Queue<(string Text, bool Alternative)> _cacheOrder = new();

    internal GameTranslator()
    {
        _clients = [];
        foreach (var name in new[] { "Яндекс", "Google", "Bing" })
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            ITranslator translator = name switch { "Яндекс" => new YandexTranslator(http), "Google" => new GoogleTranslator(http), _ => new BingTranslator(http) };
            _owned.Add(http);
            if (translator is IDisposable disposable) _owned.Add(disposable);
            _clients[name] = async text => (await translator.TranslateAsync(text, "ru", "en").ConfigureAwait(false)).Translation;
        }
    }

    internal GameTranslator(Dictionary<string, Func<string, Task<string>>> clients) => _clients = clients;

    internal Task<GameTranslation> TranslateAsync(string text, bool alternative, CancellationToken token)
        => TranslateCoreAsync(text, alternative, token).WaitAsync(token);

    // Lifecycle owner needs actual provider completion, not only cancellation of its wait.
    internal Task<GameTranslation> TranslateToCompletionAsync(string text, bool alternative, CancellationToken token)
        => TranslateCoreAsync(text, alternative, token);

    private async Task<GameTranslation> TranslateCoreAsync(string text, bool alternative, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            var key = (text, alternative);
            if (_cache.TryGetValue(key, out var cached)) return cached;
            foreach (var name in new[] { alternative ? "Google" : "Яндекс", "Bing" })
            {
                try
                {
                    var pieces = new List<string>();
                    foreach (var chunk in TranslationPolicy.Chunks(text))
                    {
                        token.ThrowIfCancellationRequested();
                        var piece = await _clients[name](chunk).ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                        if (string.IsNullOrWhiteSpace(piece)) throw new HttpRequestException("Empty translation");
                        pieces.Add(piece.Trim());
                    }
                    var result = new GameTranslation(string.Join(" ", pieces), name);
                    // A transient fallback must not permanently replace the preferred engine.
                    if (name != "Bing")
                    {
                        // Dictionary slots can be reused after removal; enumeration is not FIFO.
                        // Both structures are accessed only while holding _gate.
                        if (_cache.Count >= 128) _cache.Remove(_cacheOrder.Dequeue());
                        _cache.Add(key, result);
                        _cacheOrder.Enqueue(key);
                    }
                    return result;
                }
                catch (Exception) when (!token.IsCancellationRequested) { }
            }
            throw new HttpRequestException("Translation services unavailable");
        }
        finally { _gate.Release(); }
    }

    public void Dispose() { foreach (var item in _owned) item.Dispose(); }
}
