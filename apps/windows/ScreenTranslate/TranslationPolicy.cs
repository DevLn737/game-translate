using System.Drawing;
using System.Text.RegularExpressions;

namespace ScreenTranslate;

internal static class TranslationPolicy
{
    internal static string Normalize(string value) => Regex.Replace(value, @"\s+", " ").Trim();

    // Keep sentences together where possible; never silently truncate a dialogue.
    internal static IEnumerable<string> Chunks(string text, int limit = 500)
    {
        text = Normalize(text);
        while (text.Length > limit)
        {
            var split = text.LastIndexOf(' ', limit);
            if (split < limit / 2) split = limit;
            yield return text[..split];
            text = text[split..].TrimStart();
        }
        if (text.Length > 0) yield return text;
    }

    internal static Point Place(Rectangle selection, Rectangle work, Size popup, int gap = 12)
    {
        var x = Math.Clamp(selection.Left, work.Left, Math.Max(work.Left, work.Right - popup.Width));
        var y = selection.Bottom + gap;
        if (y + popup.Height > work.Bottom) y = selection.Top - gap - popup.Height;
        return new Point(x, Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - popup.Height)));
    }

    internal static double ReadingSeconds(string text, int minimum) => Math.Clamp(Math.Max(minimum, text.Length / 18.0), minimum, 30);
}
