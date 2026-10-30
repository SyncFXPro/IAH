using Glance.Core.Geometry;

namespace Glance.Core.Calibration;

public enum SampleRejection
{
    None,
    NoFace,
    MultipleFaces,
    EyesUnreliable,
    Blink,
    LowConfidence,
    MovingTooFast,
    NonPositiveInterval,
}

public readonly record struct ObservationSample(
    MonotonicTimestamp Timestamp,
    GazeFeatureVector Features,
    double Confidence,
    bool FacePresent,
    bool MultipleFaces,
    bool EyesReliable,
    bool BlinkSuspected);

public sealed class SampleQualityOptions
{
    public double MinimumConfidence { get; init; } = 0.5;

    /// <summary>
    /// Maximum absolute feature-component rate, in feature units per second.
    /// This is a gate, not a physical eye speed.
    /// </summary>
    public double MaximumFeatureSpeed { get; init; } = 2.5;

    public bool YawAndPitchAreRadians { get; init; } = true;
}

public static class SampleQualityGate
{
    public static SampleRejection Evaluate(
        in ObservationSample sample,
        ObservationSample? previous,
        SampleQualityOptions options)
    {
        if (!sample.FacePresent)
        {
            return SampleRejection.NoFace;
        }

        if (sample.MultipleFaces)
        {
            return SampleRejection.MultipleFaces;
        }

        if (!sample.EyesReliable)
        {
            return SampleRejection.EyesUnreliable;
        }

        if (sample.BlinkSuspected)
        {
            return SampleRejection.Blink;
        }

        if (sample.Confidence < options.MinimumConfidence)
        {
            return SampleRejection.LowConfidence;
        }

        if (previous is not { } prior)
        {
            return SampleRejection.None;
        }

        var elapsed = sample.Timestamp.ElapsedSince(prior.Timestamp);
        if (!FeatureVelocity.TryCompute(
                prior.Features,
                sample.Features,
                elapsed,
                out var velocity))
        {
            return SampleRejection.NonPositiveInterval;
        }

        if (FeatureVelocity.MaxAbsComponent(velocity) > options.MaximumFeatureSpeed)
        {
            return SampleRejection.MovingTooFast;
        }

        return SampleRejection.None;
    }
}
