namespace ScreenTranslate;

internal sealed record StartupDecision(bool Continue, bool ActivateSettings, int ExitCode, bool SelfTest, bool Practice);

internal static class StartupPolicy
{
    internal static StartupDecision Decide(IEnumerable<string> arguments, bool ownsInstance)
    {
        var args = arguments.ToHashSet(StringComparer.Ordinal);
        var test = args.Contains("--self-test");
        return ownsInstance
            ? new(true, false, 0, test, !test && args.Contains("--practice"))
            : new(false, !test, test ? 2 : 0, false, false);
    }
}
