using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenTranslate;
using Xunit;

public class InterfaceTests
{
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(2.0)]
    public void SelectionFrameUsesThinEdgesAtDifferentDpi(double dpi)
    {
        var area = new Rect(40, 60, 1800, 900);
        var edges = SelectionFrame.EdgeBounds(area, 1 / dpi, 1 / dpi);
        Assert.All(edges, edge => Assert.True(area.Contains(edge)));
        Assert.Equal(area.Top, edges[0].Top);
        Assert.Equal(area.Bottom, edges[1].Bottom);
        Assert.Equal(area.Left, edges[2].Left);
        Assert.Equal(area.Right, edges[3].Right);
        Assert.True(edges.Sum(edge => edge.Width * edge.Height) < area.Width * area.Height / 100);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint SendMessage(nint hwnd, uint message, nint wParam, nint lParam);

    [Fact]
    public void SelectionRejectsMouseActivationAtNativeWindowBoundary()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
                using var bitmap = new System.Drawing.Bitmap(640, 360);
                using var frame = new ScreenTranslate.Capture.Ocr.CapturedFrame(bitmap,
                    new ScreenTranslate.Capture.Ocr.CaptureRegion(new System.Drawing.Rectangle(0, 0, 640, 360)));
                var window = new SelectionWindow(frame);
                Assert.False(window.ShowActivated);
                var handle = new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
                Assert.False(window.IsVisible);
                Assert.NotEqual(0, WindowNative.GetStyle(handle, -20).ToInt64() & PassiveWindow.NoActivate);
                Assert.Equal(new nint(3), SendMessage(handle, 0x0021, 0, 0));
                window.Close();
            }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        Assert.Null(failure);
    }

    [Theory]
    [InlineData("Home", true)]
    [InlineData("End", true)]
    [InlineData("F8", true)]
    [InlineData("Alt+T", true)]
    [InlineData("Ctrl+Alt+F8", true)]
    [InlineData("T", false)]
    [InlineData("F12", false)]
    [InlineData("Escape", false)]
    [InlineData("", false)]
    public void SingleKeysAreDeliberatelyRestricted(string text, bool accepted) => Assert.Equal(accepted, ShortcutBinding.TryParse(text, out _));

    [Fact]
    public void RenderSettingsAndCardWithoutCreatingDesktopWindows()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
                var folder = new DirectoryInfo(AppContext.BaseDirectory);
                while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "ScreenTranslate.slnx"))) folder = folder.Parent;
                Assert.NotNull(folder);
                var output = Path.Combine(folder.FullName, "artifacts", "screen-translate-preview");
                Directory.CreateDirectory(output);
                var settings = new SettingsWindow(new Preferences(), "Английский → русский", _ => null, () => { });
                var settingsContent = (FrameworkElement)settings.Content; settings.Content = null;
                settingsContent.Resources = settings.Resources;
                Render(settingsContent, 430, Path.Combine(output, "settings.png"), Theme.Window);
                settings.Close();
                var popup = new TranslationPopup("Сокровище за дверью.", null, new System.Drawing.Rectangle(0, 0, 100, 50), 7, 0);
                var popupContent = (FrameworkElement)popup.Content; popup.Content = null;
                Render(popupContent, 340, Path.Combine(output, "translation.png"), Brushes.Transparent);
                popup.Close();
            }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Offscreen render timed out");
        Assert.Null(failure);
    }

    private static void Render(FrameworkElement element, double width, string path, Brush background)
    {
        var root = new Border { Width = width, Background = background, Child = element };
        System.Windows.Documents.TextElement.SetFontFamily(root, new FontFamily("Segoe UI"));
        System.Windows.Documents.TextElement.SetFontSize(root, 13);
        System.Windows.Documents.TextElement.SetForeground(root, Theme.Text);
        root.Measure(new Size(width, double.PositiveInfinity));
        var height = Math.Ceiling(root.DesiredSize.Height);
        Assert.InRange(height, 25, 650);
        root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
}

