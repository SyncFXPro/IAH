using Glance.Core.Calibration;
using Glance.Core.Geometry;

namespace Glance.Core.Classification;

public enum ClassificationMetric
{
    NormalizedDistance,
    RegularizedMahalanobis,
    ConvexRegion,
}

public sealed record MonitorPrediction(
    string? MonitorId,
    double Distance,
    double Margin,
    ClassificationMetric Metric,
    bool Accepted,
    string Reason);

public sealed class ClassifierOptions
{
    public double MaximumNormalizedDistance { get; init; } = 3;

    public double MaximumMahalanobisDistance { get; init; } = 3;

    public double MinimumMargin { get; init; } = 0.25;

    /// <summary>
    /// Product heuristic from the calibration notes, not a statistical requirement.
    /// Tests can lower it. Below this count the classifier stays on normalized distance.
    /// </summary>
    public int MahalanobisSampleCount { get; init; } = 30;

    public double ScaleFloor { get; init; } = 1e-3;

    public double CovarianceEpsilon { get; init; } = 1e-3;

    public bool AllowSimulatedProfiles { get; init; }
}

/// <summary>
/// A five-point profile is accepted only when the live iris pair is inside that monitor's shape.
/// Older profiles, which have no points, still use distance to the sample center.
/// The destination is not feature velocity and not a screen rectangle.
/// </summary>
public interface IMonitorClassifier
{
    MonitorPrediction Classify(GazeFeatureVector features, GazeCalibrationProfile profile);
}

public sealed class MonitorClassifier : IMonitorClassifier
{
    private readonly ClassifierOptions _options;

    public MonitorClassifier()
        : this(new ClassifierOptions())
    {
    }

    public MonitorClassifier(ClassifierOptions options) => _options = options;

    public MonitorPrediction Classify(GazeFeatureVector features, GazeCalibrationProfile profile)
    {
        if (profile.IsSimulated && !_options.AllowSimulatedProfiles)
        {
            return Reject(ClassificationMetric.NormalizedDistance, "simulated-calibration");
        }

        if (!profile.ContainsGazeSamples || profile.Monitors.Count == 0)
        {
            return Reject(ClassificationMetric.NormalizedDistance, "no-calibration");
        }

        if (profile.Monitors.Any(monitor => monitor.Points.Count == CalibrationPoints.Order.Length))
        {
            return ClassifyRegion(features, profile);
        }

        var value = features.ToArray();
        var scores = new List<(MonitorCalibration Monitor, double Distance, ClassificationMetric Metric)>();
        foreach (var monitor in profile.Monitors)
        {
            if (!TryDistance(value, monitor, out var distance, out var metric))
            {
                continue;
            }

            scores.Add((monitor, distance, metric));
        }

        if (scores.Count == 0)
        {
            return Reject(ClassificationMetric.NormalizedDistance, "no-usable-monitor");
        }

        scores.Sort((left, right) => left.Distance.CompareTo(right.Distance));
        var best = scores[0];
        var second = scores.Count > 1 ? scores[1].Distance : double.PositiveInfinity;
        var margin = second - best.Distance;
        var limit = best.Metric == ClassificationMetric.RegularizedMahalanobis
            ? _options.MaximumMahalanobisDistance
            : _options.MaximumNormalizedDistance;
        var accepted = best.Distance <= limit && (double.IsPositiveInfinity(margin) || margin >= _options.MinimumMargin);
        return new MonitorPrediction(
            best.Monitor.MonitorId,
            best.Distance,
            margin,
            best.Metric,
            accepted,
            accepted ? "accepted" : "outside-gate");
    }

    private static MonitorPrediction ClassifyRegion(GazeFeatureVector features, GazeCalibrationProfile profile)
    {
        var value = features.ToArray();
        var inside = new List<string>();
        var margin = new List<(string MonitorId, double Amount)>();
        var outside = new List<(string MonitorId, double Amount)>();
        var considered = 0;
        foreach (var monitor in profile.Monitors)
        {
            if (!TryVertices(monitor, out var vertices))
            {
                continue;
            }

            considered++;
            if (!IrisRegion.TryMeasure(vertices, value, out var amount))
            {
                outside.Add((monitor.MonitorId, double.PositiveInfinity));
                continue;
            }

            if (amount <= IrisRegion.NumericalTolerance)
            {
                inside.Add(monitor.MonitorId);
            }
            else if (amount <= IrisRegion.OutsideTolerance)
            {
                margin.Add((monitor.MonitorId, amount));
            }
            else
            {
                outside.Add((monitor.MonitorId, amount));
            }
        }

        if (inside.Count > 1 || (inside.Count == 0 && margin.Count > 1))
        {
            return new MonitorPrediction(null, 0, 0, ClassificationMetric.ConvexRegion, false, "ambiguous");
        }

        if (inside.Count == 1)
        {
            var next = NearestOther(margin, outside);
            return new MonitorPrediction(inside[0], 0, next, ClassificationMetric.ConvexRegion, true, "accepted");
        }

        if (margin.Count == 1)
        {
            var next = NearestOther([], outside);
            return new MonitorPrediction(margin[0].MonitorId, margin[0].Amount, next, ClassificationMetric.ConvexRegion, true, "accepted");
        }

        if (considered == 0)
        {
            return Reject(ClassificationMetric.ConvexRegion, "recalibrate");
        }

        if (outside.Count == 0 || outside.All(item => double.IsPositiveInfinity(item.Amount)))
        {
            return Reject(ClassificationMetric.ConvexRegion, "region-degenerate");
        }

        outside.Sort((left, right) => left.Amount.CompareTo(right.Amount));
        var best = outside[0];
        var gap = outside.Count > 1 ? outside[1].Amount - best.Amount : double.PositiveInfinity;
        return new MonitorPrediction(best.MonitorId, best.Amount, gap, ClassificationMetric.ConvexRegion, false, "outside-region");
    }

    private static double NearestOther(
        List<(string MonitorId, double Amount)> margin,
        List<(string MonitorId, double Amount)> outside)
    {
        var nearest = double.PositiveInfinity;
        foreach (var item in margin)
        {
            nearest = Math.Min(nearest, item.Amount);
        }

        foreach (var item in outside)
        {
            nearest = Math.Min(nearest, item.Amount);
        }

        return nearest;
    }

    private static bool TryVertices(MonitorCalibration monitor, out double[][] vertices)
    {
        vertices = new double[CalibrationPoints.Order.Length][];
        if (monitor.Points.Count != CalibrationPoints.Order.Length)
        {
            return false;
        }

        for (var i = 0; i < CalibrationPoints.Order.Length; i++)
        {
            var id = CalibrationPoints.Id(CalibrationPoints.Order[i]);
            var sample = monitor.Points.FirstOrDefault(point => string.Equals(point.Point, id, StringComparison.Ordinal));
            if (sample is null || sample.Mean.Length != GazeFeatureVector.Dimension)
            {
                return false;
            }

            vertices[i] = sample.Mean;
        }

        return true;
    }

    private bool TryDistance(
        double[] value,
        MonitorCalibration monitor,
        out double distance,
        out ClassificationMetric metric)
    {
        distance = 0;
        metric = ClassificationMetric.NormalizedDistance;
        if (monitor.SampleCount < 2
            || monitor.Mean.Length != GazeFeatureVector.Dimension
            || monitor.Scales.Length != GazeFeatureVector.Dimension)
        {
            return false;
        }

        var useMahalanobis = monitor.SampleCount >= _options.MahalanobisSampleCount
            && monitor.CovarianceRowMajor.Length == GazeFeatureVector.Dimension * GazeFeatureVector.Dimension;
        if (useMahalanobis)
        {
            var regularized = MatrixMath.Regularize(
                monitor.CovarianceRowMajor,
                GazeFeatureVector.Dimension,
                _options.CovarianceEpsilon);
            Span<double> inverse = stackalloc double[GazeFeatureVector.Dimension * GazeFeatureVector.Dimension];
            if (MatrixMath.TryInvert(regularized, GazeFeatureVector.Dimension, inverse))
            {
                metric = ClassificationMetric.RegularizedMahalanobis;
                distance = MatrixMath.Mahalanobis(value, monitor.Mean, inverse, GazeFeatureVector.Dimension);
                return double.IsFinite(distance);
            }
        }

        distance = MatrixMath.NormalizedDistance(value, monitor.Mean, monitor.Scales, _options.ScaleFloor);
        return double.IsFinite(distance);
    }

    private static MonitorPrediction Reject(ClassificationMetric metric, string reason) =>
        new(null, double.PositiveInfinity, 0, metric, false, reason);
}
