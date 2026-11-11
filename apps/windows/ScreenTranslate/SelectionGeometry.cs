using System.Drawing;

namespace ScreenTranslate;

internal static class SelectionGeometry
{
    internal static Rectangle ToPixels(double x1, double y1, double x2, double y2,
        double surfaceWidth, double surfaceHeight, Size pixels)
    {
        if (surfaceWidth <= 0 || surfaceHeight <= 0) return Rectangle.Empty;
        var scaleX = pixels.Width / surfaceWidth;
        var scaleY = pixels.Height / surfaceHeight;
        return Rectangle.FromLTRB(
            Math.Clamp((int)Math.Floor(Math.Min(x1, x2) * scaleX), 0, pixels.Width),
            Math.Clamp((int)Math.Floor(Math.Min(y1, y2) * scaleY), 0, pixels.Height),
            Math.Clamp((int)Math.Ceiling(Math.Max(x1, x2) * scaleX), 0, pixels.Width),
            Math.Clamp((int)Math.Ceiling(Math.Max(y1, y2) * scaleY), 0, pixels.Height));
    }
}
