namespace Glance.Core.Geometry;

/// <summary>
/// Yaw and pitch from math.md. Both formulas require a 3D gaze direction.
/// Raw image-space iris points are not a valid input.
/// </summary>
public static class GazeAngles
{
    public readonly record struct Radians(double Yaw, double Pitch);

    /// <summary>
    /// yaw = atan2(gx, gz), pitch = atan2(-gy, sqrt(gx^2 + gz^2)).
    /// A pure vertical direction has gz = gx = 0, and atan2(0, 0) is 0, so yaw is reported as 0.
    /// </summary>
    public static bool TryFromDirection(GazeDirection direction, out Radians angles)
    {
        angles = default;
        if (!GazeDirection.TryCreate(direction.X, direction.Y, direction.Z, out var valid))
        {
            return false;
        }

        var yaw = Math.Atan2(valid.X, valid.Z);
        var horizontal = Math.Sqrt((valid.X * valid.X) + (valid.Z * valid.Z));
        var pitch = Math.Atan2(-valid.Y, horizontal);
        angles = new Radians(yaw, pitch);
        return true;
    }
}
