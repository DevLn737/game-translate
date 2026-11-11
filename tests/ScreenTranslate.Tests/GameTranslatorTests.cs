using ScreenTranslate;
using Xunit;
using System.Net.Http;
using System.Text.Json;

public class GameTranslatorTests
{
    [Fact]
    public async Task AlternativeUsesSameTextAndHasSeparateCache()
    {
        var calls = new List<string>();
        using var translator = new GameTranslator(new() {
            ["Яндекс"] = text => { calls.Add("Y:" + text); return Task.FromResult("основной"); },
            ["Google"] = text => { calls.Add("G:" + text); return Task.FromResult("другой"); },
            ["Bing"] = _ => throw new Exception("Unexpected fallback") });
        Assert.Equal("основной", (await translator.TranslateAsync("phrase", false, TestContext.Current.CancellationToken)).Text);
        Assert.Equal("другой", (await translator.TranslateAsync("phrase", true, TestContext.Current.CancellationToken)).Text);
        await translator.TranslateAsync("phrase", false, TestContext.Current.CancellationToken);
        await translator.TranslateAsync("phrase", true, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "Y:phrase", "G:phrase" }, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailureUsesBingButDoesNotCacheFallback(bool alternative)
    {
        var attempts = 0;
        using var translator = new GameTranslator(new() {
            [alternative ? "Google" : "Яндекс"] = _ => ++attempts == 1 ? Task.FromException<string>(new HttpRequestException()) : Task.FromResult("recovered"),
            ["Bing"] = _ => Task.FromResult("fallback") });
        Assert.Equal("Bing", (await translator.TranslateAsync("phrase", alternative, TestContext.Current.CancellationToken)).Provider);
        Assert.Equal("recovered", (await translator.TranslateAsync("phrase", alternative, TestContext.Current.CancellationToken)).Text);
    }

    [Fact]
    public async Task CancellationDoesNotStartFallbackOrOverlapNetworkRequests()
    {
        var pending = new TaskCompletionSource<string>();
        var entered = new TaskCompletionSource();
        var calls = 0;
        using var translator = new GameTranslator(new() {
            ["Яндекс"] = _ => { calls++; entered.SetResult(); return pending.Task; },
            ["Google"] = _ => { calls++; return Task.FromResult("alternative"); },
            ["Bing"] = _ => throw new Exception("Unexpected fallback") });
        using var cancel = new CancellationTokenSource();
        var first = translator.TranslateAsync("phrase", false, cancel.Token);
        await entered.Task;
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        var second = translator.TranslateAsync("phrase", true, TestContext.Current.CancellationToken);
        Assert.Equal(1, calls);
        pending.SetResult("stale");
        Assert.Equal("alternative", (await second).Text);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void OldPreferencesGetInsertDefault()
    {
        var prefs = JsonSerializer.Deserialize<Preferences>("{\"CaptureKey\":\"Home\",\"RepeatKey\":\"End\",\"Seconds\":7,\"ShowOriginal\":false}");
        Assert.Equal("Insert", prefs!.AlternativeKey);
    }
}
