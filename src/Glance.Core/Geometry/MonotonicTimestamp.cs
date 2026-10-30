using System.Diagnostics;

namespace Glance.Core.Geometry;

public readonly record struct MonotonicTimestamp(long Ticks)
{
    public static MonotonicTimestamp Now => new(Stopwatch.GetTimestamp());

    public static MonotonicTimestamp FromElapsed(TimeSpan elapsed) =>
        new((long)(elapsed.TotalSeconds * Stopwatch.Frequency));

    public MonotonicTimestamp Add(TimeSpan span) =>
        new(Ticks + (long)(span.TotalSeconds * Stopwatch.Frequency));

    public TimeSpan ElapsedSince(MonotonicTimestamp earlier)
    {
        var delta = Ticks - earlier.Ticks;
        return TimeSpan.FromSeconds(delta / (double)Stopwatch.Frequency);
    }
}
