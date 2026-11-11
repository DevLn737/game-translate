using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ScreenTranslate;

// Four small retained visuals keep damage confined to the old/new edges.
// A single screen-sized path or outlined rectangle invalidates its entire bounds.
internal sealed class SelectionFrame : Canvas
{
    private readonly Border[] _edges = Enumerable.Range(0, 4).Select(_ => new Border
    {
        Background = Theme.Accent, IsHitTestVisible = false
    }).ToArray();
    private Rect _previous = Rect.Empty;

    internal SelectionFrame()
    {
        IsHitTestVisible = false;
        foreach (var edge in _edges) Children.Add(edge);
    }

    internal void Update(Rect area, double pixelX, double pixelY)
    {
        var snapped = new Rect(
            Math.Round(area.X / pixelX) * pixelX, Math.Round(area.Y / pixelY) * pixelY,
            Math.Round(area.Width / pixelX) * pixelX, Math.Round(area.Height / pixelY) * pixelY);
        if (snapped == _previous) return;
        _previous = snapped;
        var bounds = EdgeBounds(snapped, pixelX, pixelY);
        for (var i = 0; i < _edges.Length; i++)
        {
            var edge = _edges[i]; var rect = bounds[i];
            SetLeft(edge, rect.X); SetTop(edge, rect.Y);
            edge.Width = rect.Width; edge.Height = rect.Height;
        }
    }

    internal static Rect[] EdgeBounds(Rect area, double pixelX, double pixelY)
    {
        var x = Math.Min(pixelX, area.Width); var y = Math.Min(pixelY, area.Height);
        return [new(area.Left, area.Top, area.Width, y), new(area.Left, area.Bottom - y, area.Width, y),
            new(area.Left, area.Top, x, area.Height), new(area.Right - x, area.Top, x, area.Height)];
    }
}
