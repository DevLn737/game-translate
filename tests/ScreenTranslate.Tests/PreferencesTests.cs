using System.IO;
using ScreenTranslate;
using Xunit;

public class PreferencesTests
{
    [Theory]
    [InlineData("{\"CaptureKey\":null}")]
    [InlineData("{\"CaptureKey\":\"T\"}")]
    [InlineData("{\"CaptureKey\":\"End\"}")]
    [InlineData("{\"CaptureKey\":\"Ctrl+T\",\"RepeatKey\":\"Control+t\"}")]
    [InlineData("{\"AlternativeKey\":\"F12\"}")]
    [InlineData("{\"Seconds\":100}")]
    [InlineData("null")]
    [InlineData("not json")]
    public void InvalidConfigurationFallsBackToWorkingDefaults(string json)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, json);
            Assert.Equal(new Preferences(), Preferences.Load(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MissingConfigurationUsesDefaults()
        => Assert.Equal(new Preferences(), Preferences.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")));

    [Fact]
    public void ValidConfigurationRoundTrips()
    {
        var path = Path.GetTempFileName();
        try
        {
            var prefs = new Preferences("F8", "F9", 12, true, "Ctrl+T");
            Assert.True(prefs.TrySave(path));
            Assert.Equal(prefs, Preferences.Load(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadOnlyDestinationDoesNotThrowOrOverwrite()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "original");
            File.SetAttributes(path, FileAttributes.ReadOnly);
            Assert.False(new Preferences().TrySave(path));
            Assert.Equal("original", File.ReadAllText(path));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
            File.Delete(path + ".tmp");
        }
    }

    [Fact]
    public void InvalidConfigurationDoesNotOverwriteExistingFile()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "original");
            Assert.False(new Preferences(CaptureKey: "End").TrySave(path));
            Assert.Equal("original", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }
}
