using ScreenTranslate;
using Xunit;

public class TranslationCompletionTests
{
    [Fact]
    public async Task CompletionApiHoldsProviderUntilReturnAndStopsFurtherChunksOnCancel()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var translator = new GameTranslator(new Dictionary<string, Func<string, Task<string>>>
        {
            ["Яндекс"] = _ => { calls++; entered.TrySetResult(); return release.Task; },
            ["Bing"] = _ => { calls++; return Task.FromResult("unexpected fallback"); }
        });
        using var cancellation = new CancellationTokenSource();
        var task = translator.TranslateToCompletionAsync(string.Join(" ", Enumerable.Repeat("word", 300)), false, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        cancellation.Cancel();
        Assert.False(task.IsCompleted);
        release.SetResult("first chunk");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
        Assert.Equal(1, calls);
    }
}
