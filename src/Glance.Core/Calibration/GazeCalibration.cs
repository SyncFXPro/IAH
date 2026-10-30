using Glance.Core.Geometry;

namespace Glance.Core.Calibration;

public sealed class MonitorCalibration
{
    public required string MonitorId { get; init; }

    public required double[] Mean { get; init; }

    /// <summary>Per-feature sample standard deviation, floored so a constant channel cannot divide by zero.</summary>
    public required double[] Scales { get; init; }

    public required double[] CovarianceRowMajor { get; init; }

    public int SampleCount { get; init; }

    /// <summary>
    /// Mean feature vector at each calibration point. Empty for a center-only profile.
    /// The classifier uses these five means as a simplex, not as an axis-aligned box.
    /// </summary>
    public List<CalibrationPointSample> Points { get; set; } = [];
}

public sealed class CalibrationPointSample
{
    public required string Point { get; init; }

    public required double[] Mean { get; init; }

    public int SampleCount { get; init; }

    /// <summary>Every accepted frame. The mean is what the region uses. These rows are the variance record.</summary>
    public List<CalibrationFrameRecord> Frames { get; set; } = [];
}

public sealed class CalibrationFrameRecord
{
    public required double[] Features { get; init; }

    public double HeadX { get; init; }

    public double HeadY { get; init; }

    public double HeadScale { get; init; }
}

/// <summary>
/// One reference per monitor in feature space. Screen rectangles are not stored here.
/// </summary>
public sealed class GazeCalibrationProfile
{
    public const string GazeKind = "gaze-profile";

    public int SchemaVersion { get; set; } = 1;

    public string Kind { get; set; } = GazeKind;

    public bool ContainsGazeSamples { get; set; }

    public bool IsSimulated { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<MonitorCalibration> Monitors { get; set; } = [];
}

public sealed class DisplayAcknowledgement
{
    public required string DeviceName { get; init; }

    public required string FriendlyName { get; init; }

    public required string Fingerprint { get; init; }

    public required string Bounds { get; init; }

    public DateTimeOffset AcknowledgedAt { get; init; }
}

/// <summary>
/// Records that the calibration window was shown on each display.
/// It contains no gaze samples and cannot be used as a classifier profile.
/// </summary>
public sealed class DisplayAddressingRecord
{
    public const string AddressingKind = "display-addressing-check";

    public int SchemaVersion { get; set; } = 1;

    public string Kind { get; set; } = AddressingKind;

    public bool ContainsGazeSamples { get; set; }

    public DateTimeOffset CompletedAt { get; set; }

    public List<DisplayAcknowledgement> Displays { get; set; } = [];
}

public sealed class MonitorSampleAccumulator
{
    private readonly List<double[]> _rows = [];

    public int Count => _rows.Count;

    public void Add(GazeFeatureVector features) => _rows.Add(features.ToArray());

    public bool TryBuild(string monitorId, double scaleFloor, out MonitorCalibration? calibration)
    {
        calibration = null;
        if (_rows.Count < 2 || string.IsNullOrWhiteSpace(monitorId))
        {
            return false;
        }

        var mean = MatrixMath.Mean(_rows);
        calibration = new MonitorCalibration
        {
            MonitorId = monitorId,
            Mean = mean,
            Scales = MatrixMath.SampleScales(_rows, mean, scaleFloor),
            CovarianceRowMajor = MatrixMath.CovarianceRowMajor(_rows, mean),
            SampleCount = _rows.Count,
        };
        return true;
    }
}
