using Glance.Core.Geometry;

namespace Glance.Core.Switching;

public enum SwitchState
{
    TrackingUnavailable,
    TrackingStable,
    TransitionCandidate,
    SwitchConfirmed,
    InputSuppressed,
}

public sealed class SwitchPolicy
{
    public TimeSpan Dwell { get; init; } = TimeSpan.FromMilliseconds(120);

    public TimeSpan Cooldown { get; init; } = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// While the largest feature rate is above this, a new monitor cannot be confirmed.
    /// The rate does not choose the destination.
    /// </summary>
    public double MaximumFeatureSpeed { get; init; } = 2.5;
}

public readonly record struct SwitchObservation(
    MonotonicTimestamp Timestamp,
    bool TrackingAvailable,
    string? PredictedMonitorId,
    bool PredictionAccepted,
    double? FeatureSpeed,
    MonotonicTimestamp? SuppressInputUntil);

public readonly record struct SwitchDecision(
    SwitchState State,
    string? CurrentMonitorId,
    string? CandidateMonitorId,
    bool SwitchNow,
    string? SwitchToMonitorId,
    TimeSpan? SinceLastConfirmation);

public interface IMonitorSwitchController
{
    SwitchState State { get; }

    string? CurrentMonitorId { get; }

    SwitchDecision Evaluate(in SwitchObservation observation);

    void Reset();
}

/// <summary>
/// Confirms a monitor only after the position classifier holds the same id.
/// A fast feature change can open a candidate, but it cannot select a different monitor.
/// </summary>
public sealed class MonitorSwitchStateMachine : IMonitorSwitchController
{
    private readonly SwitchPolicy _policy;
    private SwitchDecision _previous;
    private long _lastTicks = long.MinValue;
    private MonotonicTimestamp? _candidateSince;
    private MonotonicTimestamp _cooldownUntil;
    private MonotonicTimestamp? _lastConfirmation;
    private string? _candidate;

    public MonitorSwitchStateMachine()
        : this(new SwitchPolicy())
    {
    }

    public MonitorSwitchStateMachine(SwitchPolicy policy) => _policy = policy;

    public SwitchState State => _previous.State;

    public string? CurrentMonitorId { get; private set; }

    public void Reset()
    {
        _previous = default;
        _lastTicks = long.MinValue;
        _candidateSince = null;
        _cooldownUntil = default;
        _lastConfirmation = null;
        _candidate = null;
        CurrentMonitorId = null;
    }

    public SwitchDecision Evaluate(in SwitchObservation observation)
    {
        if (observation.Timestamp.Ticks < _lastTicks)
        {
            return _previous with { SwitchNow = false, SwitchToMonitorId = null };
        }

        _lastTicks = observation.Timestamp.Ticks;
        var since = _lastConfirmation is { } confirmed
            ? observation.Timestamp.ElapsedSince(confirmed)
            : (TimeSpan?)null;

        if (!observation.TrackingAvailable)
        {
            return Store(ClearCandidate(SwitchState.TrackingUnavailable, since));
        }

        if (observation.SuppressInputUntil is { } suppressUntil
            && observation.Timestamp.Ticks < suppressUntil.Ticks)
        {
            _candidateSince = null;
            return Store(Decision(SwitchState.InputSuppressed, since, switchNow: false, switchTo: null));
        }

        if (observation.PredictedMonitorId is not null
            && observation.PredictionAccepted
            && string.Equals(observation.PredictedMonitorId, CurrentMonitorId, StringComparison.Ordinal))
        {
            return Store(ClearCandidate(SwitchState.TrackingStable, since));
        }

        if (CurrentMonitorId is not null && observation.Timestamp.Ticks < _cooldownUntil.Ticks)
        {
            return Store(ClearCandidate(SwitchState.TrackingStable, since));
        }

        if (observation.PredictedMonitorId is null || !observation.PredictionAccepted)
        {
            var state = CurrentMonitorId is null ? SwitchState.TrackingUnavailable : SwitchState.TrackingStable;
            return Store(ClearCandidate(state, since));
        }

        var movingFast = observation.FeatureSpeed is double speed && speed > _policy.MaximumFeatureSpeed;
        if (!string.Equals(_candidate, observation.PredictedMonitorId, StringComparison.Ordinal) || _candidateSince is null)
        {
            _candidate = observation.PredictedMonitorId;
            _candidateSince = movingFast ? null : observation.Timestamp;
            return Store(Decision(SwitchState.TransitionCandidate, since, switchNow: false, switchTo: null));
        }

        if (movingFast)
        {
            _candidateSince = null;
            return Store(Decision(SwitchState.TransitionCandidate, since, switchNow: false, switchTo: null));
        }

        if (observation.Timestamp.ElapsedSince(_candidateSince.Value) < _policy.Dwell)
        {
            return Store(Decision(SwitchState.TransitionCandidate, since, switchNow: false, switchTo: null));
        }

        CurrentMonitorId = observation.PredictedMonitorId;
        _candidate = null;
        _candidateSince = null;
        _cooldownUntil = observation.Timestamp.Add(_policy.Cooldown);
        _lastConfirmation = observation.Timestamp;
        return Store(Decision(
            SwitchState.SwitchConfirmed,
            TimeSpan.Zero,
            switchNow: true,
            switchTo: CurrentMonitorId));
    }

    private SwitchDecision ClearCandidate(SwitchState state, TimeSpan? since)
    {
        _candidate = null;
        _candidateSince = null;
        return Decision(state, since, switchNow: false, switchTo: null);
    }

    private SwitchDecision Decision(SwitchState state, TimeSpan? since, bool switchNow, string? switchTo) =>
        new(state, CurrentMonitorId, _candidate, switchNow, switchTo, since);

    private SwitchDecision Store(SwitchDecision decision)
    {
        _previous = decision;
        return decision;
    }
}
