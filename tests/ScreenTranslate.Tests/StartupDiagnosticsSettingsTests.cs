using System.IO;
using System.Windows;
using System.Windows.Controls;
using ScreenTranslate;
using Xunit;

public class StartupDiagnosticsSettingsTests
{
    [Theory]
    [InlineData(false, false, false, false, true, 0)]
    [InlineData(false, true, false, false, false, 2)]
    [InlineData(false, true, true, false, false, 2)]
    [InlineData(false, false, true, false, true, 0)]
    [InlineData(true, false, false, true, false, 0)]
    [InlineData(true, true, true, true, false, 0)]
    [InlineData(true, false, true, true, false, 0)]
    public void StartupPolicyDoesNotActivateAnExistingInstanceForSelfTest(bool owns, bool test, bool practice, bool proceed, bool activate, int exit)
    {
        var args = new List<string>(); if (test) args.Add("--self-test"); if (practice) args.Add("--practice");
        var decision = StartupPolicy.Decide(args, owns);
        var activationCalls = 0;
        if (decision.ActivateSettings) activationCalls++;
        Assert.Equal(proceed, decision.Continue);
        Assert.Equal(activate ? 1 : 0, activationCalls);
        Assert.Equal(exit, decision.ExitCode);
        Assert.Equal(owns && test, decision.SelfTest);
        Assert.Equal(owns && !test && practice, decision.Practice);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DiagnosticFailureCannotEscapeOrLogRecursively(bool denied)
    {
        var calls = 0;
        var sink = new DiagnosticSink("unused", (_, _, _) =>
        {
            calls++;
            if (denied) throw new UnauthorizedAccessException();
            throw new IOException();
        });
        sink.Write(DiagnosticEvent.RequestFailed);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void DiagnosticFileAndEntriesRemainBounded()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ScreenTranslate-tests-" + Guid.NewGuid());
        try
        {
            var path = Path.Combine(directory, "diagnostics.txt"); var sink = new DiagnosticSink(path);
            for (var i = 0; i < 2500; i++) sink.Write(DiagnosticEvent.RequestFailed);
            Assert.InRange(new FileInfo(path).Length, 1, DiagnosticSink.MaximumBytes);
            Assert.All(File.ReadAllLines(path), line =>
            {
                Assert.EndsWith("RequestFailed", line);
                Assert.True(System.Text.Encoding.UTF8.GetByteCount(line) < DiagnosticSink.MaximumEntryBytes);
            });
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void SaveFailureRestoresPreviousHotkeysAndReportsRollbackFailure()
    {
        var previous = new Preferences(); var next = previous with { CaptureKey = "F8" };
        var applied = new List<Preferences>();
        var error = SettingsSavePolicy.Save(next, previous, value => { applied.Add(value); return value == previous ? "restore failed" : null; }, _ => false);
        Assert.Equal(new[] { next, previous }, applied);
        Assert.Contains("restore failed", error);
    }

    [Fact]
    public void InvalidDraftDoesNotApplyOrPersist()
    {
        var calls = 0;
        var error = SettingsSavePolicy.Save(new Preferences(CaptureKey: "T"), new Preferences(), _ => { calls++; return null; }, _ => { calls++; return true; });
        Assert.NotNull(error); Assert.Equal(0, calls);
    }

    [Fact]
    public void LiveStatusDoesNotOverwriteDraftAndIgnoresClosedWindow()
    {
        StaTest.Run(() =>
        {
            var window = new SettingsWindow(new Preferences(), "starting", _ => null, () => { });
            var inputs = Descendants<TextBox>((DependencyObject)window.Content).ToArray();
            inputs[0].Text = "F8";
            window.UpdateStatus("ready");
            Assert.Contains(Descendants<TextBlock>((DependencyObject)window.Content), t => t.Text == "ready");
            window.UpdateStatus("failed");
            Assert.Equal("F8", inputs[0].Text);
            Assert.Contains(Descendants<TextBlock>((DependencyObject)window.Content), t => t.Text == "failed");
            window.Close(); window.UpdateStatus("late");
            Assert.Equal("F8", inputs[0].Text);
            Assert.DoesNotContain(Descendants<TextBlock>((DependencyObject)window.Content), t => t.Text == "late");
        });
    }

    [Theory]
    [InlineData("Попробовать")]
    [InlineData("Готово")]
    public void BothButtonsSaveCurrentDraftAndPreserveItOnFailure(string label)
    {
        StaTest.Run(() =>
        {
            var practices = 0; Preferences? attempted = null; var fail = true;
            var window = new SettingsWindow(new Preferences(), "starting", next => { attempted = next; return fail ? "cannot save" : null; }, () => practices++);
            var closed = false; window.Closed += (_, _) => closed = true;
            var inputs = Descendants<TextBox>((DependencyObject)window.Content).ToArray(); inputs[0].Text = "F8";
            var button = Descendants<Button>((DependencyObject)window.Content).Single(b => Equals(b.Content, label));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("F8", attempted?.CaptureKey); Assert.Equal("F8", inputs[0].Text);
            Assert.False(closed); Assert.Equal(0, practices);
            Assert.Contains(Descendants<TextBlock>((DependencyObject)window.Content), t => t.Text == "cannot save" && t.Visibility == Visibility.Visible);
            fail = false;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(closed); Assert.Equal(label == "Попробовать" ? 1 : 0, practices);
            Assert.False(window.IsVisible);
        });
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
