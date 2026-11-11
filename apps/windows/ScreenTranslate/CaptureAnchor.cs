using System.Drawing;

namespace ScreenTranslate;

// Absolute screen coordinates are only reusable while their source window is unchanged.
internal sealed record CaptureAnchor(Rectangle Area, nint Origin, Rectangle? WindowBounds)
{
    internal bool CanRepeat(nint origin, Rectangle? bounds, IEnumerable<Rectangle> monitors)
        => Origin != 0 && origin == Origin && WindowBounds is { Width: > 0, Height: > 0 } window
            && Area.Width > 0 && Area.Height > 0 && window.Contains(Area)
            && bounds == WindowBounds && monitors.Any(monitor => monitor.Contains(Area));
}
