using Glance.Core.Calibration;
using Glance.Core.Classification;
using Glance.Core.Configuration;
using Glance.Core.Contracts;
using Glance.Core.Geometry;
using Glance.Core.Switching;
using Glance.Core.Tracking;
using Glance.Tracking.Inference;
using Glance.Tracking.Processing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Glance.App.Services;

public interface ITrackingStatusSource
{
    TrackingStatus Current { get; }

    event EventHandler<TrackingStatus>? StatusChanged;
}

public sealed class TrackingCoordinator : BackgroundService, ITrackingStatusSource
{
    private readonly ISettingsStore _settings;
    private readonly IDisplayService _displays;
    private readonly ICursorService _cursor;
    private readonly IWindowFocus _windows;
    private readonly ILogger<TrackingCoordinator> _logger;
    private readonly LiveFaceTracker _live;
    private readonly IMonitorClassifier _classifier;
    private readonly GazeProfileStore _profiles;
    private readonly CalibrationSession _calibration;
    private readonly ScriptedMonitorPreview _preview = new();
    private readonly object _gate = new();
    private MonitorSwitchStateMachine _machine = new();
    private int _dwellMilliseconds = -1;
    private int _cursorPoll;
    private long _lastSequence = -1;
    private GazeFeatureVector? _previousFeatures;
    private MonotonicTimestamp _previousFeatureTime;

    public TrackingCoordinator(
        ISettingsStore settings,
        IDisplayService displays,
        ICursorService cursor,
        IWindowFocus windows,
        ILogger<TrackingCoordinator> logger,
        LiveFaceTracker live,
        IMonitorClassifier classifier,
        GazeProfileStore profiles,
        CalibrationSession calibration)
    {
        _settings = settings;
        _displays = displays;
        _cursor = cursor;
        _windows = windows;
        _logger = logger;
        _live = live;
        _classifier = classifier;
        _profiles = profiles;
        _calibration = calibration;
        Current = TrackingStatus.Off;
    }

    public TrackingStatus Current { get; private set; }

    public event EventHandler<TrackingStatus>? StatusChanged;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(1));
        try
        {
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            RememberCursorOccasionally();
            if (!_settings.Current.TrackingEnabled)
            {
                if (!_calibration.IsCollecting)
                {
                    _live.Stop();
                }

                if (Current.IsRunning)
                {
                    _preview.Reset();
                    _machine.Reset();
                    _previousFeatures = null;
                    _lastSequence = -1;
                    Publish(TrackingStatus.Off);
                }

                continue;
            }

            if (CameraSelection.TryParseId(_settings.Current.SelectedCameraId, out var cameraIndex, out var cameraName))
            {
                EnsurePolicy();
                _live.EnsureRunning(
                    cameraIndex,
                    cameraName,
                    _settings.Current.CaptureWidth,
                    _settings.Current.CaptureHeight);
                _live.Read(out var reading, out var fault);
                if (reading is null && fault is null)
                {
                    Publish(Opening(cameraName));
                    continue;
                }

                if (reading is not null && reading.Sequence == _lastSequence)
                {
                    continue;
                }

                if (reading is not null)
                {
                    _lastSequence = reading.Sequence;
                }

                Publish(DescribeWebcam(cameraName, reading, fault));
                continue;
            }

            _live.Stop();
            EnsurePolicy();
            var displays = _displays.GetDisplays();
            var now = MonotonicTimestamp.Now;
            var target = _preview.Advance(
                displays.Select(display => display.DeviceName).ToArray(),
                now,
                TimeSpan.FromSeconds(_settings.Current.PreviewHoldSeconds));
            var decision = _machine.Evaluate(new SwitchObservation(
                now,
                target is not null,
                target,
                target is not null,
                FeatureSpeed: 0,
                SuppressInputUntil: null));
            Publish(Describe(decision, displays.Count));
        }
        }
        finally
        {
            _live.Stop();
        }
    }

    private static TrackingStatus Opening(string cameraName) => new()
    {
        IsRunning = true,
        Source = TrackingSourceKind.Webcam,
        Headline = "Opening webcam",
        Detail = "Image-space head and iris offsets will show here. They are not angles, and the cursor does not move.",
        SwitchState = SwitchState.TrackingUnavailable,
        WebcamLine = "Opening " + cameraName + ".",
        ModelLine = "Loading the face detector and landmarker.",
        ClassifierLine = "Classifier is idle. No gaze profile is loaded.",
        LatencyLine = "No camera frames have been processed.",
    };

    private TrackingStatus DescribeWebcam(string cameraName, LiveFaceReading? reading, string? fault)
    {
        if (reading is null)
        {
            return new TrackingStatus
            {
                IsRunning = true,
                Source = TrackingSourceKind.Webcam,
                Headline = "Webcam unavailable",
                Detail = fault ?? "The webcam did not return a frame.",
                SwitchState = SwitchState.TrackingUnavailable,
                WebcamLine = cameraName,
                ModelLine = "The landmark model is not running.",
                ClassifierLine = "Classifier is idle. No gaze profile is loaded.",
                LatencyLine = "No camera frames have been processed.",
            };
        }

        var faces = reading.FaceCount;
        var features = faces == 1 ? reading.Features : null;
        var profile = _profiles.Current;
        MonitorPrediction? prediction = null;
        SwitchDecision? decision = null;
        if (features is { } current && profile is not null)
        {
            prediction = _classifier.Classify(current, profile);
            var speed = FeatureSpeed(current, reading.Timestamp);
            decision = _machine.Evaluate(new SwitchObservation(
                reading.Timestamp,
                faces == 1,
                prediction.Accepted ? prediction.MonitorId : null,
                prediction.Accepted,
                speed,
                SuppressInputUntil: null));
        }
        else if (features is { } waiting)
        {
            _previousFeatures = waiting;
            _previousFeatureTime = reading.Timestamp;
        }

        var classifierLine = prediction switch
        {
            null when profile is null => "Classifier is idle. No gaze profile is loaded.",
            null => "No feature vector in this frame.",
            _ when prediction.MonitorId is null => FormattableString.Invariant(
                $"Not accepted ({prediction.Reason})."),
            _ when prediction.Accepted => FormattableString.Invariant(
                $"{prediction.Metric} {prediction.Distance:0.00}, margin {prediction.Margin:0.00}."),
            _ => FormattableString.Invariant(
                $"Nearest {prediction.MonitorId}, distance {(double.IsFinite(prediction.Distance) ? prediction.Distance.ToString("0.00") : "n/a")}, not accepted ({prediction.Reason})."),
        };
        if (decision is { SwitchNow: true, SwitchToMonitorId: { } targetId })
        {
            classifierLine = classifierLine + " " + MoveCursor(targetId);
            classifierLine = _calibration.IsCollecting
                ? classifierLine + " Calibration is running, so the window stays put."
                : classifierLine + " " + _windows.TryActivate(targetId);
        }

        return new TrackingStatus
        {
            IsRunning = true,
            Source = TrackingSourceKind.Webcam,
            Headline = faces > 0 ? "Webcam" : "Webcam, no face",
            Detail = fault
                ?? "The monitor comes from the iris pair. When one monitor stays accepted, the cursor moves there and the topmost window on that monitor is brought forward.",
            IrisX = features?.IrisX,
            IrisY = features?.IrisY,
            HeadYawRadians = reading.HeadYawRadians,
            HeadPitchRadians = reading.HeadPitchRadians,
            HeadX = reading.Head?.X,
            HeadY = reading.Head?.Y,
            HeadScale = reading.Head?.Scale,
            HighlightedMonitorId = decision is { } chosen
                ? chosen.CandidateMonitorId ?? chosen.CurrentMonitorId
                : null,
            SwitchState = decision?.State ?? (faces > 0 ? SwitchState.TrackingStable : SwitchState.TrackingUnavailable),
            PredictedMonitorId = prediction?.MonitorId,
            ClassificationDistance = prediction is not null && double.IsFinite(prediction.Distance)
                ? prediction.Distance
                : null,
            ClassifierLine = classifierLine,
            SinceLastConfirmation = decision?.SinceLastConfirmation,
            WebcamLine = FormattableString.Invariant(
                $"Open: {reading.CameraName}. Capture {CaptureResolution.Describe(reading.FrameWidth, reading.FrameHeight, _settings.Current.CaptureWidth, _settings.Current.CaptureHeight)}."),
            ModelLine = (faces == 0
                ? "Landmarker loaded. No face in this frame."
                : FormattableString.Invariant($"Faces {faces}. Detector {reading.DetectorScore:0.00}. Presence {reading.Presence:0.00} (logit {reading.PresenceLogit:0.0}). Points {reading.LandmarkCount}."))
                + " " + _live.ExecutionProvider + "."
                + (string.IsNullOrWhiteSpace(reading.FeatureNote) ? string.Empty : " " + reading.FeatureNote),
            LatencyLine = FormattableString.Invariant($"Inspect {reading.InspectMilliseconds:0} ms. Webcam buffering is not included."),
        };
    }

    private string MoveCursor(string monitorId)
    {
        var position = _cursor.GetPosition();
        var alreadyThere = _displays.GetDisplays().Any(item =>
            string.Equals(item.DeviceName, monitorId, StringComparison.Ordinal)
            && item.Bounds.Contains(position.X, position.Y));
        if (alreadyThere)
        {
            return "Cursor is already on that monitor.";
        }

        var moved = _cursor.TryRelocate(
            monitorId,
            new CursorRelocationRequest(
                UserEnabled: _settings.Current.CursorMovementEnabled,
                PipelineIsRealWebcam: true,
                AlreadyOnMonitor: false,
                TrackingHealthy: true),
            out var blockReason);
        return moved
            ? "Cursor moved to that monitor."
            : blockReason ?? "Cursor did not move.";
    }

    private double? FeatureSpeed(GazeFeatureVector current, MonotonicTimestamp timestamp)
    {
        double? speed = null;
        if (_previousFeatures is { } prior && timestamp.Ticks > _previousFeatureTime.Ticks
            && FeatureVelocity.TryCompute(
                prior,
                current,
                timestamp.ElapsedSince(_previousFeatureTime),
                out var velocity))
        {
            speed = FeatureVelocity.MaxAbsComponent(velocity);
        }

        _previousFeatures = current;
        _previousFeatureTime = timestamp;
        return speed;
    }

    private void RememberCursorOccasionally()
    {
        _cursorPoll++;
        if (_cursorPoll % 10 != 0)
        {
            return;
        }

        try
        {
            _cursor.TryRememberCurrentPosition();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Cursor position was not remembered.");
        }
    }

    private void EnsurePolicy()
    {
        var dwell = _settings.Current.SwitchingDwellMilliseconds;
        if (dwell == _dwellMilliseconds)
        {
            return;
        }

        _dwellMilliseconds = dwell;
        _machine = new MonitorSwitchStateMachine(new SwitchPolicy
        {
            Dwell = TimeSpan.FromMilliseconds(dwell),
        });
        _preview.Reset();
    }

    private static TrackingStatus Describe(SwitchDecision decision, int displayCount)
    {
        var highlighted = decision.CandidateMonitorId ?? decision.CurrentMonitorId;
        return new TrackingStatus
        {
            IsRunning = true,
            Source = TrackingSourceKind.ScriptedPreview,
            Headline = "Scripted preview",
            Detail = displayCount switch
            {
                0 => "Windows did not report a display.",
                1 => "One display is connected, so the preview stays there. This is not gaze.",
                _ => "The highlight moves between displays on a timer. Yaw, pitch, and iris position are not measured.",
            },
            HighlightedMonitorId = highlighted,
            SwitchState = decision.State,
            PredictedMonitorId = null,
            ClassificationDistance = null,
            ClassifierLine = "Classifier is not running. No gaze features are being produced.",
            WebcamLine = "No webcam is open.",
            ModelLine = "No landmark model is loaded.",
            SinceLastConfirmation = decision.SinceLastConfirmation,
            LatencyLine = "No camera frames have been processed.",
        };
    }

    private MonotonicTimestamp _lastPublish;

    private void Publish(TrackingStatus status)
    {
        lock (_gate)
        {
            var unchanged = Same(Current, status);
            if (unchanged && MonotonicTimestamp.Now.ElapsedSince(_lastPublish) < TimeSpan.FromMilliseconds(500))
            {
                return;
            }

            Current = status;
            _lastPublish = MonotonicTimestamp.Now;
        }

        StatusChanged?.Invoke(this, status);
    }

    private static bool Same(TrackingStatus left, TrackingStatus right) =>
        left.IsRunning == right.IsRunning
        && left.SwitchState == right.SwitchState
        && left.HighlightedMonitorId == right.HighlightedMonitorId
        && left.Headline == right.Headline;
}
