namespace Glance.Core.Geometry;

/// <summary>
/// Signed angle from vector a to vector b in a 2D plane, from math.md.
/// Positive follows the atan2 orientation. This is not an unsigned acos angle,
/// and it is not defined for 3D gaze vectors.
/// </summary>
public static class SignedAngle
{
    public static bool TryRadians(
        double ax,
        double ay,
        double bx,
        double by,
        out double radians)
    {
        radians = 0;
        if (!double.IsFinite(ax) || !double.IsFinite(ay) || !double.IsFinite(bx) || !double.IsFinite(by))
        {
            return false;
        }

        var aLength = Math.Sqrt((ax * ax) + (ay * ay));
        var bLength = Math.Sqrt((bx * bx) + (by * by));
        if (aLength < GazeDirection.MinimumLength || bLength < GazeDirection.MinimumLength)
        {
            return false;
        }

        var cross = (ax * by) - (ay * bx);
        var dot = (ax * bx) + (ay * by);
        radians = Math.Atan2(cross, dot);
        return true;
    }
}
