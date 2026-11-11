using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Size = System.Windows.Size;
using RectI = System.Drawing.Rectangle;

namespace ScreenTranslate;

internal sealed record PopupPage(string Text, string? Original = null, string? Hint = null, string? Heading = null, int Number = 0, int Count = 0)
{
    internal string ReadingText => string.Join(" ", new[] { Heading, Text, Original, Hint }.Where(s => !string.IsNullOrEmpty(s)));
}

internal sealed record PopupLayout(RectI WorkArea, double DpiX, double DpiY)
{
    // Leave a physical safety margin after rounding, including on high-DPI monitors.
    internal double Width => Math.Min(420, WorkArea.Width / DpiX - 24);
    internal double Height => WorkArea.Height / DpiY - 24;
}
internal sealed class PopupLayoutException : Exception
{
    internal PopupLayoutException() : base("Content cannot fit in the monitor work area.") { }
}

internal static class PopupPagination
{
    internal const int MaximumPages = 4096;
    private const int MaximumCharacters = 32000;

    // Rendering and measurement share the entire tree: no parallel font/spacing estimate.
    internal static FrameworkElement Content(PopupPage page)
    {
        var stack = new StackPanel();
        if (page.Heading is not null)
        {
            var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            header.ColumnDefinitions.Add(new ColumnDefinition());
            // Fixed count column reserves the 9 -> 10 (and later) transitions.
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            header.Children.Add(new TextBlock { Text = page.Heading, Foreground = Theme.Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap });
            var count = new TextBlock { Text = $"{page.Number} / {page.Count}", FontSize = 11, Foreground = Theme.Muted, TextAlignment = TextAlignment.Right };
            Grid.SetColumn(count, 1); header.Children.Add(count); stack.Children.Add(header);
        }
        stack.Children.Add(new TextBlock { Text = page.Text, Foreground = Theme.Text, FontSize = 18, TextWrapping = TextWrapping.Wrap, LineHeight = 25 });
        if (!string.IsNullOrEmpty(page.Original)) stack.Children.Add(new TextBlock { Text = page.Original, Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 193)), TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 10, 0, 0) });
        if (!string.IsNullOrEmpty(page.Hint)) stack.Children.Add(new TextBlock { Text = page.Hint, Foreground = Theme.Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(247, 25, 29, 35)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(62, 68, 76)), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 11, 16, 13), Child = stack
        };
        System.Windows.Documents.TextElement.SetFontFamily(border, new System.Windows.Media.FontFamily("Segoe UI"));
        return border;
    }

    internal static Size Measure(PopupPage page, PopupLayout layout)
    {
        if (!double.IsFinite(layout.Width) || !double.IsFinite(layout.Height) || layout.Width < 140 || layout.Height < 1 || layout.DpiX <= 0 || layout.DpiY <= 0)
            throw new PopupLayoutException();
        var content = Content(page);
        // Detached measurement must use the target monitor DPI, not the primary display.
        VisualTreeHelper.SetRootDpi(content, new DpiScale(layout.DpiX, layout.DpiY));
        content.Measure(new Size(layout.Width, double.PositiveInfinity));
        return content.DesiredSize;
    }
    internal static bool Fits(PopupPage page, PopupLayout layout)
    {
        var size = Measure(page, layout);
        return size.Width <= layout.Width + .01 && Math.Ceiling(size.Height * layout.DpiY) <= Math.Floor(layout.Height * layout.DpiY);
    }

    internal static IReadOnlyList<PopupPage> Paginate(string translation, string? original, string? hint, PopupLayout layout)
    {
        if (translation.Length > MaximumCharacters || original?.Length > MaximumCharacters || hint?.Length > 1024) throw new PopupLayoutException();
        var combined = new PopupPage(translation, original, hint);
        if (Fits(combined, layout)) return [combined];
        var pages = new List<PopupPage>();
        Add(translation, "Перевод");
        if (!string.IsNullOrEmpty(original)) Add(original, "Оригинал");
        if (pages.Count == 0) throw new PopupLayoutException();
        for (var i = 0; i < pages.Count; i++)
        {
            pages[i] = pages[i] with { Number = i + 1, Count = pages.Count };
            if (!Fits(pages[i], layout)) throw new PopupLayoutException();
        }
        return pages;

        void Add(string text, string heading)
        {
            var boundaries = StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToArray();
            var start = 0;
            while (start < boundaries.Length - 1)
            {
                if (pages.Count >= MaximumPages) throw new PopupLayoutException();
                var low = start + 1; var high = boundaries.Length - 1; var best = start;
                while (low <= high)
                {
                    var middle = low + (high - low) / 2;
                    var candidate = new PopupPage(text[boundaries[start]..boundaries[middle]], null, hint, heading, MaximumPages, MaximumPages);
                    if (Fits(candidate, layout)) { best = middle; low = middle + 1; }
                    else high = middle - 1;
                }
                if (best == start) throw new PopupLayoutException();
                // Prefer a word break, without discarding even whitespace or splitting a grapheme.
                if (best < boundaries.Length - 1)
                {
                    for (var end = best; end > start + (best - start) / 2; end--)
                        if (char.IsWhiteSpace(text[boundaries[end] - 1])) { best = end; break; }
                }
                var page = new PopupPage(text[boundaries[start]..boundaries[best]], null, hint, heading, MaximumPages, MaximumPages);
                if (!Fits(page, layout)) throw new PopupLayoutException();
                pages.Add(page); start = best;
            }
        }
    }
}
