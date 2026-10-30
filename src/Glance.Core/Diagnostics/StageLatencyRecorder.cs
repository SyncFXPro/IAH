using System.Diagnostics;

namespace Glance.Core.Diagnostics;

public enum PipelineStage
{
    CameraRetrieval,
    Preprocessing,
    FaceDetection,
    LandmarkInference,
    Postprocessing,
    FeatureExtraction,
    Classification,
    TemporalFiltering,
    CursorRelocation,
    EndToEnd,
}

public sealed record LatencySummary(int Count, double P50Milliseconds, double P95Milliseconds, double P99Milliseconds);

/// <summary>
/// Records durations supplied by the caller. It does not invent timings.
/// </summary>
public sealed class StageLatencyRecorder
{
    private readonly int _capacity;
    private readonly Dictionary<PipelineStage, SampleBuffer> _buffers = [];

    public StageLatencyRecorder(int capacity = 512)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _capacity = capacity;
    }

    public void Record(PipelineStage stage, long startedTimestamp, long endedTimestamp)
    {
        var milliseconds = (endedTimestamp - startedTimestamp) * 1000d / Stopwatch.Frequency;
        if (!double.IsFinite(milliseconds) || milliseconds < 0)
        {
            return;
        }

        Add(stage, milliseconds);
    }

    public void AddMilliseconds(PipelineStage stage, double milliseconds)
    {
        if (!double.IsFinite(milliseconds) || milliseconds < 0)
        {
            return;
        }

        Add(stage, milliseconds);
    }

    public LatencySummary? Summarize(PipelineStage stage)
    {
        if (!_buffers.TryGetValue(stage, out var buffer) || buffer.Count == 0)
        {
            return null;
        }

        var values = buffer.Snapshot();
        Array.Sort(values);
        return new LatencySummary(
            values.Length,
            Percentile(values, 0.50),
            Percentile(values, 0.95),
            Percentile(values, 0.99));
    }

    public static double Percentile(IReadOnlyList<double> sorted, double percentile)
    {
        if (sorted.Count == 0)
        {
            throw new ArgumentException("Percentile requires samples.", nameof(sorted));
        }

        var rank = (int)Math.Ceiling(percentile * sorted.Count);
        rank = Math.Clamp(rank, 1, sorted.Count);
        return sorted[rank - 1];
    }

    private void Add(PipelineStage stage, double milliseconds)
    {
        if (!_buffers.TryGetValue(stage, out var buffer))
        {
            buffer = new SampleBuffer(_capacity);
            _buffers.Add(stage, buffer);
        }

        buffer.Add(milliseconds);
    }

    private sealed class SampleBuffer
    {
        private readonly double[] _values;
        private int _next;
        private int _count;

        public SampleBuffer(int capacity) => _values = new double[capacity];

        public int Count => _count;

        public void Add(double value)
        {
            _values[_next] = value;
            _next = (_next + 1) % _values.Length;
            if (_count < _values.Length)
            {
                _count++;
            }
        }

        public double[] Snapshot()
        {
            var copy = new double[_count];
            var start = _count == _values.Length ? _next : 0;
            for (var i = 0; i < _count; i++)
            {
                copy[i] = _values[(start + i) % _values.Length];
            }

            return copy;
        }
    }
}
