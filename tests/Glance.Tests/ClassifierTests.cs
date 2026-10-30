using Glance.Core.Calibration;
using Glance.Core.Classification;
using Glance.Core.Geometry;

namespace Glance.Tests;

public class ClassifierTests
{
    [Fact]
    public void NormalizedDistance_PrefersTheInScalePoint_WhenRawEuclideanDoesTheOpposite()
    {
        double[] mean = [0, 0, 0, 0];
        double[] scales = [0.01, 0.01, 20, 20];
        double[] yawOutlier = [1, 0, 0, 0];
        double[] irisInlier = [0, 0, 10, 0];

        var normalizedOutlier = MatrixMath.NormalizedDistance(yawOutlier, mean, scales, 1e-6);
        var normalizedInlier = MatrixMath.NormalizedDistance(irisInlier, mean, scales, 1e-6);
        Assert.True(normalizedOutlier > normalizedInlier);
        Assert.True(MatrixMath.Euclidean(yawOutlier, mean) < MatrixMath.Euclidean(irisInlier, mean));
    }

    [Fact]
    public void PitchOnlyShift_SelectsTheMatchingMonitor()
    {
        var profile = Profile(
            Samples("above", 0, 0.4, 12),
            Samples("below", 0, -0.4, 12));
        var classifier = new MonitorClassifier(new ClassifierOptions { MinimumMargin = 0.2, MahalanobisSampleCount = 30 });

        var above = classifier.Classify(new GazeFeatureVector(0, 0.4), profile);
        var below = classifier.Classify(new GazeFeatureVector(0, -0.4), profile);

        Assert.True(above.Accepted);
        Assert.Equal("above", above.MonitorId);
        Assert.Equal(ClassificationMetric.NormalizedDistance, above.Metric);
        Assert.Equal("below", below.MonitorId);
    }

    [Fact]
    public void DiagonalFeatureShift_SelectsTheMatchingMonitor()
    {
        var profile = Profile(
            Samples("north-east", 0.3, 0.3, 12),
            Samples("south-west", -0.3, -0.3, 12));
        var classifier = new MonitorClassifier(new ClassifierOptions { MinimumMargin = 0.2, MahalanobisSampleCount = 100 });

        var prediction = classifier.Classify(new GazeFeatureVector(0.3, 0.3), profile);

        Assert.Equal("north-east", prediction.MonitorId);
        Assert.True(prediction.Accepted);
    }

    [Fact]
    public void Classifier_UsesMahalanobisOnlyAfterEnoughSamples()
    {
        var rows = new List<GazeFeatureVector>();
        foreach (var t in new[] { -4d, -3, -2, -1, 1, 2, 3, 4 })
        {
            rows.Add(new GazeFeatureVector(t, t));
        }

        var early = new MonitorClassifier(new ClassifierOptions
        {
            MahalanobisSampleCount = 100,
            MaximumNormalizedDistance = 100,
            MinimumMargin = 0,
        });
        var later = new MonitorClassifier(new ClassifierOptions
        {
            MahalanobisSampleCount = 8,
            MaximumMahalanobisDistance = 100,
            MinimumMargin = 0,
        });
        var profile = Profile(Build("only", rows));

        var normalizedAlong = early.Classify(new GazeFeatureVector(1, 1), profile);
        var normalizedAgainst = early.Classify(new GazeFeatureVector(1, -1), profile);
        var mahalanobisAlong = later.Classify(new GazeFeatureVector(1, 1), profile);
        var mahalanobisAgainst = later.Classify(new GazeFeatureVector(1, -1), profile);

        Assert.Equal(ClassificationMetric.NormalizedDistance, normalizedAlong.Metric);
        Assert.Equal(normalizedAlong.Distance, normalizedAgainst.Distance, 6);
        Assert.Equal(ClassificationMetric.RegularizedMahalanobis, mahalanobisAlong.Metric);
        Assert.True(mahalanobisAlong.Distance < mahalanobisAgainst.Distance);
    }

    [Fact]
    public void SimulatedProfile_IsRejectedUnlessExplicitlyAllowed()
    {
        var profile = Profile(Samples("center", 0, 0, 4));
        profile.IsSimulated = true;
        var blocked = new MonitorClassifier();
        var allowed = new MonitorClassifier(new ClassifierOptions { AllowSimulatedProfiles = true, MinimumMargin = 0 });

        Assert.Equal("simulated-calibration", blocked.Classify(new GazeFeatureVector(0, 0), profile).Reason);
        Assert.Equal("center", allowed.Classify(new GazeFeatureVector(0, 0), profile).MonitorId);
    }

    [Fact]
    public void AddressingDocument_IsNotAGazeProfile()
    {
        var record = new DisplayAddressingRecord
        {
            ContainsGazeSamples = false,
            CompletedAt = DateTimeOffset.Parse("2026-10-30T00:00:00Z"),
            Displays =
            [
                new DisplayAcknowledgement
                {
                    DeviceName = @"\\.\DISPLAY1",
                    FriendlyName = "Left",
                    Fingerprint = "fp",
                    Bounds = "0,0,1920,1080",
                    AcknowledgedAt = DateTimeOffset.Parse("2026-10-30T00:00:00Z"),
                },
            ],
        };

        var loaded = CalibrationDocument.TryReadGazeProfile(CalibrationDocument.Serialize(record), out _, out var rejection);

        Assert.False(loaded);
        Assert.Equal("not-a-gaze-profile", rejection);
    }

    [Fact]
    public void SampleGate_RejectsBlinkAndFastMotion()
    {
        var options = new SampleQualityOptions { MaximumFeatureSpeed = 1, YawAndPitchAreRadians = false };
        var first = Sample(TimeSpan.Zero, 0, blink: false);
        var blink = Sample(TimeSpan.FromSeconds(1), 0, blink: true);
        var fast = Sample(TimeSpan.FromSeconds(1), 5, blink: false);

        Assert.Equal(SampleRejection.None, SampleQualityGate.Evaluate(first, null, options));
        Assert.Equal(SampleRejection.Blink, SampleQualityGate.Evaluate(blink, first, options));
        Assert.Equal(SampleRejection.MovingTooFast, SampleQualityGate.Evaluate(fast, first, options));
    }

    private static ObservationSample Sample(TimeSpan time, double yaw, bool blink) =>
        new(
            MonotonicTimestamp.FromElapsed(time),
            new GazeFeatureVector(yaw, 0),
            1,
            true,
            false,
            true,
            blink);

    private static MonitorCalibration Samples(string id, double yaw, double pitch, int count)
    {
        var rows = new List<GazeFeatureVector>();
        for (var i = 0; i < count; i++)
        {
            var jitter = (i - (count / 2d)) * 0.002;
            rows.Add(new GazeFeatureVector(yaw + jitter, pitch + jitter));
        }

        return Build(id, rows);
    }

    private static MonitorCalibration Build(string id, IReadOnlyList<GazeFeatureVector> rows)
    {
        var accumulator = new MonitorSampleAccumulator();
        foreach (var row in rows)
        {
            accumulator.Add(row);
        }

        Assert.True(accumulator.TryBuild(id, 1e-3, out var calibration));
        return calibration!;
    }

    private static GazeCalibrationProfile Profile(params MonitorCalibration[] monitors) => new()
    {
        ContainsGazeSamples = true,
        IsSimulated = false,
        CreatedAt = DateTimeOffset.Parse("2026-10-30T00:00:00Z"),
        Monitors = monitors.ToList(),
    };
}
