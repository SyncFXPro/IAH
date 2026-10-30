namespace Glance.Core.Geometry;

/// <summary>
/// Change of a yaw or pitch angle over a measured interval.
/// Uses the wrapped difference, not the 2D signed-angle formula on a 3D vector.
/// </summary>
public static class AngularRate
{
    public static double WrappedDelta(double fromRadians, double toRadians)
    {
        var delta = toRadians - fromRadians;
        return Math.Atan2(Math.Sin(delta), Math.Cos(delta));
    }

    public static bool TryRadiansPerSecond(
        double fromRadians,
        double toRadians,
        TimeSpan elapsed,
        out double radiansPerSecond)
    {
        radiansPerSecond = 0;
        if (elapsed <= TimeSpan.Zero || !double.IsFinite(fromRadians) || !double.IsFinite(toRadians))
        {
            return false;
        }

        radiansPerSecond = WrappedDelta(fromRadians, toRadians) / elapsed.TotalSeconds;
        return double.IsFinite(radiansPerSecond);
    }
}
