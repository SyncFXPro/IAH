namespace Glance.Core.Geometry;

/// <summary>
/// v(t) = (z(t) - z(t - 1)) / delta_t, using a real elapsed interval.
/// This is the rate of change of the feature vector, not the physical velocity of an eye.
/// Iris components are never wrapped. The 2D signed-angle formula is not used here.
/// </summary>
public static class FeatureVelocity
{
    public static bool TryCompute(
        GazeFeatureVector previous,
        GazeFeatureVector current,
        TimeSpan elapsed,
        out GazeFeatureVector perSecond)
    {
        perSecond = default;
        if (elapsed <= TimeSpan.Zero)
        {
            return false;
        }

        var seconds = elapsed.TotalSeconds;
        perSecond = new GazeFeatureVector(
            (current.IrisX - previous.IrisX) / seconds,
            (current.IrisY - previous.IrisY) / seconds);
        return true;
    }

    public static double MaxAbsComponent(GazeFeatureVector velocity) =>
        Math.Max(Math.Abs(velocity.IrisX), Math.Abs(velocity.IrisY));
}
