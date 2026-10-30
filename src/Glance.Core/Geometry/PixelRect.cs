namespace Glance.Core.Geometry;

/// <summary>
/// Virtual-desktop pixels. The origin may be negative. This rectangle places a window
/// or clamps a cursor. It is not a physical gaze angle.
/// </summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public (int X, int Y) Center => (X + (Width / 2), Y + (Height / 2));

    public bool Contains(int x, int y) => x >= X && y >= Y && x < Right && y < Bottom;

    public (int X, int Y) Clamp(int x, int y)
    {
        if (IsEmpty)
        {
            return (x, y);
        }

        return (Math.Clamp(x, X, Right - 1), Math.Clamp(y, Y, Bottom - 1));
    }
}
