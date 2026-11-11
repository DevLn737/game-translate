using ScreenTranslate;
using Xunit;

public class CacheEvictionTests
{
    [Fact]
    public async Task EvictionIsFifoAcrossRepeatedRemovalsAndCacheHits()
    {
        var calls = new List<string>();
        using var translator = new GameTranslator(new()
        {
            ["Яндекс"] = text => { calls.Add(text); return Task.FromResult("translated " + text); }
        });
        var token = TestContext.Current.CancellationToken;
        for (var i = 0; i < 128; i++)
            await translator.TranslateAsync($"record {i}", false, token);

        // A hit must not promote the oldest entry to the back of the queue.
        await translator.TranslateAsync("record 0", false, token);
        for (var i = 128; i < 132; i++)
            await translator.TranslateAsync($"record {i}", false, token);
        Assert.Equal(132, calls.Count);

        // Every survivor, including newly inserted entries, stays cached.
        for (var i = 4; i < 132; i++)
            await translator.TranslateAsync($"record {i}", false, token);
        Assert.Equal(132, calls.Count);
        for (var i = 0; i < 4; i++)
            await translator.TranslateAsync($"record {i}", false, token);
        Assert.Equal(new[] { "record 0", "record 1", "record 2", "record 3" }, calls.Skip(132));
        await translator.TranslateAsync("record 131", false, token);
        Assert.Equal(136, calls.Count);
        await translator.TranslateAsync("record 4", false, token);
        Assert.Equal(137, calls.Count);
    }
}
