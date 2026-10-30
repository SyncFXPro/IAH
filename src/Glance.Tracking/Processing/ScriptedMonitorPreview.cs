using Glance.Core.Geometry;

namespace Glance.Tracking.Processing;

/// <summary>
/// Cycles connected monitor ids on a timer so the shell can be exercised.
/// It does not read a camera and does not emit yaw, pitch, or iris values.
/// </summary>
public sealed class ScriptedMonitorPreview
{
    private int _index = -1;
    private MonotonicTimestamp _nextSwitchAt;
    private bool _started;

    public string? CurrentTarget { get; private set; }

    public void Reset()
    {
        _index = -1;
        _started = false;
        CurrentTarget = null;
    }

    public string? Advance(IReadOnlyList<string> monitorIds, MonotonicTimestamp now, TimeSpan hold)
    {
        if (monitorIds.Count == 0)
        {
            Reset();
            return null;
        }

        if (!_started || CurrentTarget is null || !monitorIds.Contains(CurrentTarget, StringComparer.Ordinal))
        {
            _started = true;
            _index = 0;
            CurrentTarget = monitorIds[0];
            _nextSwitchAt = now.Add(hold);
            return CurrentTarget;
        }

        if (now.Ticks >= _nextSwitchAt.Ticks && monitorIds.Count > 1)
        {
            _index = (_index + 1) % monitorIds.Count;
            CurrentTarget = monitorIds[_index];
            _nextSwitchAt = now.Add(hold);
        }

        return CurrentTarget;
    }
}
