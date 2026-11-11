using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using ScreenTranslate;
using Xunit;
using Rectangle = System.Drawing.Rectangle;

public class PopupPaginationTests
{
    [Theory]
    [InlineData(1.0, 800, 600)]
    [InlineData(1.25, 800, 600)]
    [InlineData(1.5, 800, 600)]
    [InlineData(2.0, 800, 600)]
    [InlineData(2.0, 420, 420)]
    public void PagesConserveBothTextsAndFitMeasuredWorkArea(double dpi, int width, int height)
    {
        StaTest.Run(() =>
        {
            var layout = new PopupLayout(new Rectangle(-width, -height, width, height), dpi, dpi);
            var translation = string.Concat(Enumerable.Repeat("Длинная фраза с переводом. ", 36));
            var original = string.Concat(Enumerable.Repeat("Full original English dialogue. ", 130))[..4000];
            var pages = PopupPagination.Paginate(translation, original, "Яндекс · Ctrl+Alt+F8 — Google", layout);
            Assert.True(pages.Count > 1);
            Assert.Equal(translation, string.Concat(pages.Where(p => p.Heading == "Перевод").Select(p => p.Text)));
            Assert.Equal(original, string.Concat(pages.Where(p => p.Heading == "Оригинал").Select(p => p.Text)));
            var originalStarted = false;
            for (var i = 0; i < pages.Count; i++)
            {
                var page = pages[i];
                originalStarted |= page.Heading == "Оригинал";
                if (originalStarted) Assert.Equal("Оригинал", page.Heading);
                Assert.Equal(i + 1, page.Number); Assert.Equal(pages.Count, page.Count);
                Assert.True(PopupPagination.Fits(page, layout));
                var content = PopupPagination.Content(page);
                VisualTreeHelper.SetRootDpi(content, new DpiScale(dpi, dpi));
                content.Measure(new Size(layout.Width, double.PositiveInfinity));
                content.Arrange(new Rect(0, 0, layout.Width, content.DesiredSize.Height));
                Assert.InRange(content.DesiredSize.Width, 1, layout.Width + .01);
                Assert.InRange(content.DesiredSize.Height, 1, layout.Height);
                var pixels = new System.Drawing.Size((int)Math.Ceiling(layout.Width * dpi), (int)Math.Ceiling(content.DesiredSize.Height * dpi));
                var point = TranslationPolicy.Place(new Rectangle(-30, -30, 20, 20), layout.WorkArea, pixels);
                Assert.True(layout.WorkArea.Contains(new Rectangle(point, pixels)));
            }
            if (dpi == 2) Assert.True(pages.Count >= 10);
        });
    }

    [Fact]
    public void ShortTranslationKeepsTheEntireShortOriginalInline()
    {
        StaTest.Run(() =>
        {
            var pages = PopupPagination.Paginate("За дверью.", "Behind the door.", "Google", new PopupLayout(new Rectangle(0, 0, 1920, 1080), 1, 1));
            var page = Assert.Single(pages);
            Assert.Equal("Behind the door.", page.Original);
            Assert.Null(page.Heading);
            Assert.Contains("Behind the door.", page.ReadingText);
            Assert.Contains("Google", page.ReadingText);
        });
    }

    [Fact]
    public void LongUnbrokenUnicodeSplitsOnlyAtTextElementBoundaries()
    {
        StaTest.Run(() =>
        {
            var text = string.Concat(Enumerable.Repeat("😀👩‍🚀á漢字", 160));
            var layout = new PopupLayout(new Rectangle(0, 0, 440, 440), 2, 2);
            var pages = PopupPagination.Paginate(text, null, "hint", layout);
            Assert.Equal(text, string.Concat(pages.Select(p => p.Text)));
            var boundaries = StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToHashSet();
            var offset = 0;
            foreach (var page in pages)
            {
                Assert.Contains(offset, boundaries); offset += page.Text.Length; Assert.Contains(offset, boundaries);
                Assert.False(char.IsHighSurrogate(page.Text[^1]));
                Assert.False(char.IsLowSurrogate(page.Text[0]));
                Assert.True(PopupPagination.Fits(page, layout));
            }
        });
    }

    [Fact]
    public void TinyAreaFailsExplicitlyRatherThanLoopingOrClipping()
    {
        StaTest.Run(() =>
        {
            Assert.Throws<PopupLayoutException>(() => PopupPagination.Paginate("text", "original", "hint", new PopupLayout(new Rectangle(0, 0, 100, 60), 2, 2)));
            Assert.Throws<PopupLayoutException>(() => PopupPagination.Paginate("text", null, new string('H', 800), new PopupLayout(new Rectangle(0, 0, 400, 250), 2, 2)));
        });
    }

    [Fact]
    public void PassiveCardCanBeConstructedAndMeasuredWithoutNativeWindow()
    {
        StaTest.Run(() =>
        {
            var layout = new PopupLayout(new Rectangle(0, 0, 800, 600), 1.5, 1.5);
            using var popup = new TranslationPopup(new PopupPage("Текст", "Original", "hint"), new Rectangle(100, 100, 100, 50), 5, 0, layout);
            Assert.False(popup.ShowActivated); Assert.False(popup.ShowInTaskbar); Assert.False(popup.IsVisible);
            Assert.True(popup.Topmost);
            Assert.Equal(0, new System.Windows.Interop.WindowInteropHelper(popup).Handle);
            Assert.InRange(popup.Height, 1, layout.Height);
        });
    }
}

internal static class StaTest
{
    internal static void Run(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly; action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(45)), "STA test did not finish.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
