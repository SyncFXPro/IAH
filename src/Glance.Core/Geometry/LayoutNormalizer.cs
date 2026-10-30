namespace Glance.Core.Geometry;

public readonly record struct NormalizedRect(double X, double Y, double Width, double Height);

/// <summary>
/// Maps Windows virtual-desktop rectangles into a drawing box.
/// Y grows downward, matching the virtual desktop, so a monitor with a smaller Y is drawn above.
/// The result is a picture of the desktop layout, not an estimate of where the user is looking.
/// </summary>
public static class LayoutNormalizer
{
    public static IReadOnlyList<NormalizedRect> Normalize(
        IReadOnlyList<PixelRect> rects,
        double targetWidth,
        double targetHeight,
        double padding)
    {
        if (rects.Count == 0 || targetWidth <= (padding * 2) || targetHeight <= (padding * 2))
        {
            return [];
        }

        var minX = rects.Min(rect => rect.X);
        var minY = rects.Min(rect => rect.Y);
        var maxX = rects.Max(rect => rect.Right);
        var maxY = rects.Max(rect => rect.Bottom);
        var spanX = maxX - minX;
        var spanY = maxY - minY;
        if (spanX <= 0 || spanY <= 0)
        {
            return [];
        }

        var scale = Math.Min((targetWidth - (padding * 2)) / spanX, (targetHeight - (padding * 2)) / spanY);
        var drawnWidth = spanX * scale;
        var drawnHeight = spanY * scale;
        var offsetX = ((targetWidth - drawnWidth) / 2) - (minX * scale);
        var offsetY = ((targetHeight - drawnHeight) / 2) - (minY * scale);

        var normalized = new NormalizedRect[rects.Count];
        for (var i = 0; i < rects.Count; i++)
        {
            var rect = rects[i];
            normalized[i] = new NormalizedRect(
                (rect.X * scale) + offsetX,
                (rect.Y * scale) + offsetY,
                rect.Width * scale,
                rect.Height * scale);
        }

        return normalized;
    }
}
