using Glance.Core.Geometry;

namespace Glance.Core.Calibration;

/// <summary>
/// Collects repeated frames at each of the five calibration points and stores one mean per point.
/// </summary>
public sealed class MonitorRegionBuilder
{
    private readonly Dictionary<string, List<CalibrationFrameRecord>> _frames = new(StringComparer.Ordinal);

    public int Count(CalibrationPoint point) =>
        _frames.TryGetValue(CalibrationPoints.Id(point), out var frames) ? frames.Count : 0;

    public void Add(CalibrationPoint point, GazeFeatureVector features, HeadTranslation head = default)
    {
        var id = CalibrationPoints.Id(point);
        if (!_frames.TryGetValue(id, out var frames))
        {
            frames = [];
            _frames[id] = frames;
        }

        frames.Add(new CalibrationFrameRecord
        {
            Features = features.ToArray(),
            HeadX = head.X,
            HeadY = head.Y,
            HeadScale = head.Scale,
        });
    }

    public bool TryBuild(string monitorId, int minimumPerPoint, double scaleFloor, out MonitorCalibration? calibration)
    {
        calibration = null;
        if (string.IsNullOrWhiteSpace(monitorId) || minimumPerPoint < 2)
        {
            return false;
        }

        var points = new List<CalibrationPointSample>();
        var all = new List<double[]>();
        foreach (var point in CalibrationPoints.Order)
        {
            if (!_frames.TryGetValue(CalibrationPoints.Id(point), out var frames) || frames.Count < minimumPerPoint)
            {
                return false;
            }

            var rows = frames.Select(frame => frame.Features).ToArray();
            points.Add(new CalibrationPointSample
            {
                Point = CalibrationPoints.Id(point),
                Mean = MatrixMath.Mean(rows),
                SampleCount = frames.Count,
                Frames = frames.ToList(),
            });
            all.AddRange(rows);
        }

        var mean = MatrixMath.Mean(all);
        calibration = new MonitorCalibration
        {
            MonitorId = monitorId,
            Mean = mean,
            Scales = MatrixMath.SampleScales(all, mean, scaleFloor),
            CovarianceRowMajor = MatrixMath.CovarianceRowMajor(all, mean),
            SampleCount = all.Count,
            Points = points,
        };
        return true;
    }
}
