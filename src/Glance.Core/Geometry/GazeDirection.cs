namespace Glance.Core.Geometry;

/// <summary>
/// A direction in the camera frame from math.md: X right, Y down, Z forward.
/// This is not an iris coordinate and not a Windows monitor vector.
/// </summary>
public readonly record struct GazeDirection(double X, double Y, double Z)
{
    public const double MinimumLength = 1e-8;

    public static bool TryCreate(double x, double y, double z, out GazeDirection direction)
    {
        direction = default;
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
        {
            return false;
        }

        var lengthSquared = (x * x) + (y * y) + (z * z);
        if (lengthSquared < MinimumLength * MinimumLength)
        {
            return false;
        }

        direction = new GazeDirection(x, y, z);
        return true;
    }
}
