using System.Drawing;
using ScreenTranslate;
using Xunit;

public class TranslationPolicyTests
{
    [Theory]
    [InlineData(1920, 1080, 1.0)]
    [InlineData(2560, 1440, 1.25)]
    [InlineData(1920, 1080, 1.5)]
    [InlineData(1280, 1024, 1.0)]
    public void SelectionUsesActualSurfaceOnMixedDpiMonitors(int width, int height, double dpi)
    {
        var surfaceWidth = width / dpi;
        var surfaceHeight = height / dpi;
        var rect = SelectionGeometry.ToPixels(surfaceWidth, surfaceHeight, 0, 0, surfaceWidth, surfaceHeight, new Size(width, height));
        Assert.Equal(new Rectangle(0, 0, width, height), rect);
        var center = SelectionGeometry.ToPixels(surfaceWidth * .25, surfaceHeight * .25,
            surfaceWidth * .75, surfaceHeight * .75, surfaceWidth, surfaceHeight, new Size(width, height));
        Assert.Equal(new Rectangle(width / 4, height / 4, width / 2, height / 2), center);
    }
    [Fact]
    public void LongDialogueKeepsEveryWordWithinProviderLimit()
    {
        var text = string.Join(" ", Enumerable.Repeat("A very long dialogue about the hidden treasure.", 200));
        var chunks = TranslationPolicy.Chunks(text).ToArray();
        Assert.All(chunks, chunk => Assert.InRange(chunk.Length, 1, 500));
        Assert.Equal(text, string.Join(" ", chunks));
    }
    [Fact]
    public void UnbrokenTextCannotExceedLimit()
    {
        var text = new string('x', 1501);
        var chunks = TranslationPolicy.Chunks(text).ToArray();
        Assert.All(chunks, chunk => Assert.InRange(chunk.Length, 1, 500));
        Assert.Equal(text, string.Concat(chunks));
    }
    [Theory]
    [InlineData(-1920, 0)]
    [InlineData(0, 0)]
    [InlineData(1920, -1080)]
    public void PopupStaysOnChosenMonitorAtBottomRight(int x, int y)
    {
        var monitor = new Rectangle(x, y, 1920, 1080);
        var selection = new Rectangle(x + 1800, y + 1000, 110, 60);
        var size = new Size(420, 240);
        var point = TranslationPolicy.Place(selection, monitor, size);
        Assert.True(monitor.Contains(new Rectangle(point, size)));
        Assert.True(point.Y + size.Height < selection.Top);
    }
    [Fact]
    public void EmptyAndMultilineRecognitionAreHandled()
    {
        Assert.Empty(TranslationPolicy.Chunks(" \r\n\t "));
        Assert.Equal("Hello there friend.", TranslationPolicy.Normalize(" Hello\r\nthere\tfriend. "));
    }
    [Fact]
    public void LongTranslationsGetTimeToReadButRemainBounded()
    {
        Assert.Equal(7, TranslationPolicy.ReadingSeconds("Hi", 7));
        Assert.Equal(20, TranslationPolicy.ReadingSeconds(new string('a', 360), 7));
        Assert.Equal(30, TranslationPolicy.ReadingSeconds(new string('a', 5000), 7));
    }
}
