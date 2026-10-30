using Glance.Core.Calibration;
using Glance.Core.Classification;
using Glance.Core.Geometry;

namespace Glance.Tests;

public class FeatureRegionTests
{
    [Fact]
    public void Simplex_AcceptsTheCentroid_AndRejectsABoundingBoxCorner()
    {
        double[][] vertices =
        [
            [0, 0, 0, 0],
            [1, 0, 0, 0],
            [0, 1, 0, 0],
            [0, 0, 1, 0],
            [0, 0, 0, 1],
        ];
        Span<double> weights = stackalloc double[5];

        Assert.True(FeatureSimplex.TryWeights(vertices, [0.2, 0.2, 0.2, 0.2], weights, out var inside));
        Assert.Equal(0, inside, 6);

        Assert.True(FeatureSimplex.TryWeights(vertices, [1, 1, 0, 0], weights, out var outside));
        Assert.True(outside > 0.5);
    }

    [Fact]
    public void Region_AcceptsAPointInsideOneMonitor_AndRejectsAnOverlap()
    {
        var left = Region("left", 0);
        var right = Region("right", 3);
        var overlap = Region("overlap", 0);
        var classifier = new MonitorClassifier();
        var separated = Profile(left, right);
        var both = Profile(left, overlap);

        var insideLeft = classifier.Classify(new GazeFeatureVector(0, 0.1), separated);
        var far = classifier.Classify(new GazeFeatureVector(2, 0.1), separated);
        var shared = classifier.Classify(new GazeFeatureVector(0, 0.1), both);

        Assert.True(insideLeft.Accepted);
        Assert.Equal("left", insideLeft.MonitorId);
        Assert.Equal(ClassificationMetric.ConvexRegion, insideLeft.Metric);
        Assert.False(far.Accepted);
        Assert.Equal("outside-region", far.Reason);
        Assert.False(shared.Accepted);
        Assert.Equal("ambiguous", shared.Reason);
        Assert.Null(shared.MonitorId);

        var beside = classifier.Classify(new GazeFeatureVector(0.24, 0.1), separated);
        Assert.True(beside.Accepted);
        Assert.Equal("left", beside.MonitorId);

        var neighbor = Region("neighbor", 0.40);
        var owned = classifier.Classify(new GazeFeatureVector(0.18, 0.1), Profile(left, neighbor));
        Assert.True(owned.Accepted);
        Assert.Equal("left", owned.MonitorId);
    }

    [Fact]
    public void Plane_KeepsTheTwoStrongestDirections()
    {
        double[][] rows =
        [
            [0, 0, 0, 0],
            [2, 0, 0, 0],
            [0, 1, 0, 0],
            [2, 1, 0, 0],
        ];
        var plane = FeaturePlane.Fit(rows);
        var origin = plane.Project(rows[0]);
        var alongX = plane.Project(rows[1]);
        var alongY = plane.Project(rows[2]);

        var xSpan = Math.Abs(alongX.X - origin.X) + Math.Abs(alongX.Y - origin.Y);
        var ySpan = Math.Abs(alongY.X - origin.X) + Math.Abs(alongY.Y - origin.Y);
        Assert.True(xSpan > 1);
        Assert.True(ySpan > 0.4);
    }

    private static MonitorCalibration Region(string id, double shift)
    {
        (double X, double Y)[] vertices =
        [
            (shift - 0.2, 0.3),
            (shift + 0.2, 0.3),
            (shift - 0.2, -0.1),
            (shift + 0.2, -0.1),
            (shift, 0.1),
        ];
        var builder = new MonitorRegionBuilder();
        for (var point = 0; point < CalibrationPoints.Order.Length; point++)
        {
            var vertex = vertices[point];
            builder.Add(CalibrationPoints.Order[point], new GazeFeatureVector(vertex.X, vertex.Y));
            builder.Add(CalibrationPoints.Order[point], new GazeFeatureVector(vertex.X, vertex.Y));
        }

        Assert.True(builder.TryBuild(id, 2, 1e-3, out var calibration));
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
