using Glance.Core.Switching;

namespace Glance.Core.Tracking;

public enum TrackingSourceKind
{
    None,
    ScriptedPreview,
    Webcam,
}

/// <summary>
/// Status published to the UI. Null measurement fields mean the pipeline did not produce them.
/// </summary>
public sealed record TrackingStatus
{
    public bool IsRunning { get; init; }

    public TrackingSourceKind Source { get; init; }

    public string Headline { get; init; } = "Eye tracking is off";

    public string Detail { get; init; } = "Camera inference is not connected.";

    public string? HighlightedMonitorId { get; init; }

    public SwitchState SwitchState { get; init; }

    public double? HeadYawRadians { get; init; }

    public double? HeadPitchRadians { get; init; }

    public double? IrisX { get; init; }

    public double? IrisY { get; init; }

    public double? HeadX { get; init; }

    public double? HeadY { get; init; }

    public double? HeadScale { get; init; }

    public string? PredictedMonitorId { get; init; }

    public double? ClassificationDistance { get; init; }

    public string ClassifierLine { get; init; } = "Classifier is not running.";

    public string WebcamLine { get; init; } = "No webcam is open.";

    public string ModelLine { get; init; } = "No landmark model is loaded.";

    public TimeSpan? SinceLastConfirmation { get; init; }

    public string LatencyLine { get; init; } = "No camera frames have been processed.";

    public static TrackingStatus Off { get; } = new()
    {
        Headline = "Eye tracking is off",
        Detail = "Turn it on to run a scripted monitor preview. No webcam frames are read.",
        SwitchState = SwitchState.TrackingUnavailable,
    };
}
