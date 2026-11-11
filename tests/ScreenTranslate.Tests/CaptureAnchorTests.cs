using System.Drawing;
using ScreenTranslate;
using Xunit;

public class CaptureAnchorTests
{
    private static readonly Rectangle Monitor = new(0, 0, 1920, 1080);
    private static readonly Rectangle Window = new(100, 100, 800, 600);
    private static readonly CaptureAnchor Anchor = new(new Rectangle(120, 120, 200, 50), (nint)42, Window);

    [Fact]
    public void UnchangedWindowCanRepeat() => Assert.True(Anchor.CanRepeat((nint)42, Window, [Monitor]));

    [Theory]
    [InlineData(150, 100, 800, 600)]
    [InlineData(100, 150, 800, 600)]
    [InlineData(100, 100, 900, 600)]
    [InlineData(100, 100, 800, 700)]
    public void MovedOrResizedWindowRequiresReselection(int x, int y, int width, int height)
        => Assert.False(Anchor.CanRepeat((nint)42, new Rectangle(x, y, width, height), [Monitor]));

    [Fact]
    public void DifferentWindowCannotRepeat() => Assert.False(Anchor.CanRepeat((nint)43, Window, [Monitor]));

    [Fact]
    public void UnknownWindowBoundsCannotRepeat()
    {
        Assert.False(Anchor.CanRepeat((nint)42, null, [Monitor]));
        Assert.False((Anchor with { WindowBounds = null }).CanRepeat((nint)42, Window, [Monitor]));
        Assert.False((Anchor with { Origin = 0 }).CanRepeat(0, Window, [Monitor]));
    }

    [Fact]
    public void DisconnectedMonitorCannotRepeat() => Assert.False(Anchor.CanRepeat((nint)42, Window, []));
    [Theory]
    [InlineData(50, 120, 100, 50)]
    [InlineData(0, 0, 50, 50)]
    [InlineData(120, 120, 0, 50)]
    [InlineData(120, 120, 50, 0)]
    public void OutsidePartialOrEmptyAreaCannotRepeat(int x, int y, int width, int height)
        => Assert.False((Anchor with { Area = new Rectangle(x, y, width, height) }).CanRepeat((nint)42, Window, [Monitor]));

    [Fact]
    public void ExactNegativeMonitorAndWindowBoundariesCanRepeat()
    {
        var bounds = new Rectangle(-1920, -1080, 1920, 1080);
        Assert.True(new CaptureAnchor(bounds, 42, bounds).CanRepeat(42, bounds, [bounds]));
    }
}
