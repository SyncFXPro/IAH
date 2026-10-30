using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Glance.App.Services;
using Glance.Core.Calibration;
using Glance.Core.Configuration;
using Glance.Core.Contracts;
using Glance.Core.Geometry;
using Glance.Tracking.Inference;

namespace Glance.App.ViewModels;

public partial class CalibrationStepViewModel : ObservableObject
{
    public required string Title { get; init; }

    public required string Detail { get; init; }
}

public partial class CalibrationViewModel : ObservableObject
{
    public const int FramesPerPoint = 8;

    private static readonly SampleQualityOptions Gate = new()
    {
        YawAndPitchAreRadians = false,
        MinimumConfidence = 0.5,
        MaximumFeatureSpeed = 2.5,
    };

    private readonly IDisplayService _displays;
    private readonly DisplayAddressingStore _addressing;
    private readonly GazeProfileStore _profiles;
    private readonly ISettingsStore _settings;
    private readonly LiveFaceTracker _live;
    private readonly CalibrationSession _session;
    private readonly IUiInvoker _ui;
    private readonly CalibrationTargetPresenter _presenter;
    private DispatcherTimer? _timer;
    private readonly Dictionary<string, MonitorRegionBuilder> _regions = new(StringComparer.Ordinal);
    private List<string> _order = [];
    private ObservationSample? _previousAccepted;
    private MonotonicTimestamp _acceptAfter;
    private long _lastSequence = -1;
    private int _step = -1;
    private int _point;
    private bool _finishing;
    private bool _sealed;

    public CalibrationViewModel(
        IDisplayService displays,
        DisplayAddressingStore addressing,
        GazeProfileStore profiles,
        ISettingsStore settings,
        LiveFaceTracker live,
        CalibrationSession session,
        IUiInvoker ui,
        CalibrationTargetPresenter presenter)
    {
        _displays = displays;
        _addressing = addressing;
        _profiles = profiles;
        _settings = settings;
        _live = live;
        _session = session;
        _ui = ui;
        _presenter = presenter;
        displays.DisplaysChanged += (_, _) => _ui.Invoke(Refresh);
        addressing.Changed += (_, _) => _ui.Invoke(Refresh);
        profiles.Changed += (_, _) => _ui.Invoke(Refresh);
        Refresh();
    }

    public ObservableCollection<CalibrationStepViewModel> Displays { get; } = [];

    [ObservableProperty]
    private string _summary = "No gaze profile has been saved.";

    [ObservableProperty]
    private string _warning = string.Empty;

    [ObservableProperty]
    private string _prompt = "Each monitor is five points: the corners and the center. Glance keeps several frames at each point. The cursor does not move.";

    [ObservableProperty]
    private bool _walkthroughActive;

    [RelayCommand]
    private void Start()
    {
        if (!CameraSelection.TryParseId(_settings.Current.SelectedCameraId, out var cameraIndex, out var cameraName))
        {
            Prompt = "Save a webcam on the dashboard before calibrating.";
            return;
        }

        var displays = _displays.GetDisplays();
        if (displays.Count == 0)
        {
            Prompt = "Windows did not report a display.";
            return;
        }

        StopCollection(stopCamera: false);
        _regions.Clear();
        _order = displays.Select(display => display.DeviceName).ToList();
        foreach (var name in _order)
        {
            _regions[name] = new MonitorRegionBuilder();
        }

        _previousAccepted = null;
        _lastSequence = -1;
        _step = 0;
        _point = 0;
        _sealed = false;
        _session.IsCollecting = true;
        WalkthroughActive = true;
        _live.EnsureRunning(
            cameraIndex,
            cameraName,
            _settings.Current.CaptureWidth,
            _settings.Current.CaptureHeight);
        ShowCurrent();
    }

    [RelayCommand]
    private void ShowTarget() => ShowCurrent();

    [RelayCommand]
    private void Cancel()
    {
        _presenter.Dismiss();
        StopCollection(stopCamera: true);
        Prompt = "Walkthrough cancelled. No gaze profile was saved.";
    }

    private void ShowCurrent()
    {
        if (_step < 0 || _step >= _order.Count)
        {
            return;
        }

        var displays = _displays.GetDisplays();
        var display = displays.FirstOrDefault(item =>
            string.Equals(item.DeviceName, _order[_step], StringComparison.Ordinal));
        if (display is null)
        {
            Abort("A display changed during calibration. Start again. Nothing was saved.");
            return;
        }

        _point = 0;
        _presenter.Show(display, OnMonitorFinished, OnDismissed, FinishPoint);
        BeginPoint();
    }

    private void Collect()
    {
        if (!WalkthroughActive || _step < 0 || _step >= _order.Count)
        {
            return;
        }

        _live.Read(out var reading, out var fault);
        if (reading is null)
        {
            var waiting = fault ?? "Waiting for a camera frame.";
            Prompt = waiting;
            _presenter.SetPoint(CurrentPoint(), CurrentCount(), FramesPerPoint, waiting);
            return;
        }

        if (reading.Sequence == _lastSequence)
        {
            return;
        }

        _lastSequence = reading.Sequence;
        if (reading.Timestamp.Ticks < _acceptAfter.Ticks)
        {
            var settling = $"{CurrentCount()} / {FramesPerPoint}. Look at the dot.";
            Prompt = settling;
            _presenter.SetPoint(CurrentPoint(), CurrentCount(), FramesPerPoint, settling);
            return;
        }
        var monitorId = _order[_step];
        if (!_regions.TryGetValue(monitorId, out var region))
        {
            return;
        }

        var sample = new ObservationSample(
            reading.Timestamp,
            reading.Features ?? default,
            Math.Min(reading.DetectorScore, reading.Presence),
            reading.FaceCount > 0,
            reading.FaceCount > 1,
            reading.Features is not null,
            BlinkSuspected: false);
        var rejection = SampleQualityGate.Evaluate(sample, _previousAccepted, Gate);
        if (rejection == SampleRejection.None)
        {
            region.Add(CurrentPoint(), sample.Features, reading.Head ?? default);
            _previousAccepted = sample;
        }

        var count = region.Count(CurrentPoint());
        var line = $"{CalibrationPoints.Label(CurrentPoint())}. {count} / {FramesPerPoint}. Last frame: {Describe(rejection)}.";
        Prompt = line;
        _presenter.SetPoint(CurrentPoint(), count, FramesPerPoint, line);
        if (count >= FramesPerPoint)
        {
            _timer?.Stop();
            FinishPoint();
        }
    }

    private void FinishPoint()
    {
        if (_sealed || _finishing || !WalkthroughActive || _step < 0)
        {
            return;
        }

        _sealed = true;
        _finishing = true;
        try
        {
            _timer?.Stop();
            var displays = _displays.GetDisplays();
            if (!SameDisplays(displays))
            {
                Abort("A display changed during calibration. Start again. Nothing was saved.");
                return;
            }

            _point++;
            if (_point < CalibrationPoints.Order.Length)
            {
                BeginPoint();
                return;
            }

            _step++;
            if (_step >= _order.Count)
            {
                SaveProfile(displays);
                return;
            }

            _point = 0;
            ShowCurrent();
        }
        finally
        {
            _finishing = false;
        }
    }

    private void BeginPoint()
    {
        _previousAccepted = null;
        _lastSequence = -1;
        _acceptAfter = MonotonicTimestamp.Now.Add(TimeSpan.FromMilliseconds(400));
        _sealed = false;
        var display = CurrentDisplay();
        var point = CurrentPoint();
        var count = CurrentCount();
        var line = $"{CalibrationPoints.Label(point)}. {count} / {FramesPerPoint}.";
        Prompt = display is null
            ? line
            : $"Look at {CalibrationPoints.Label(point).ToLowerInvariant()} on {display.FriendlyName}. {line}";
        _presenter.SetPoint(point, count, FramesPerPoint, line);
        EnsureTimer().Start();
    }

    private void OnMonitorFinished()
    {
        if (_finishing || !WalkthroughActive)
        {
            return;
        }

        FinishPoint();
    }

    private void OnDismissed()
    {
        if (_finishing || !WalkthroughActive)
        {
            return;
        }

        StopCollection(stopCamera: true);
        Prompt = "Walkthrough closed. No gaze profile was saved.";
    }

    private void SaveProfile(IReadOnlyList<DisplayInfo> displays)
    {
        var monitors = new List<MonitorCalibration>();
        foreach (var display in displays)
        {
            if (!_regions.TryGetValue(display.DeviceName, out var region)
                || !region.TryBuild(display.DeviceName, FramesPerPoint, 1e-3, out var calibration)
                || calibration is null
                || calibration.Points.Count != CalibrationPoints.Order.Length)
            {
                Abort("A monitor is missing one of its five points. Nothing was saved.");
                return;
            }

            monitors.Add(calibration);
        }

        _profiles.Save(new GazeCalibrationProfile
        {
            CreatedAt = DateTimeOffset.Now,
            Monitors = monitors,
        });
        _addressing.Save(new DisplayAddressingRecord
        {
            CompletedAt = DateTimeOffset.Now,
            Displays = displays.Select(display => new DisplayAcknowledgement
            {
                DeviceName = display.DeviceName,
                FriendlyName = display.FriendlyName,
                Fingerprint = display.Fingerprint,
                Bounds = $"{display.Bounds.X},{display.Bounds.Y},{display.Bounds.Width},{display.Bounds.Height}",
                AcknowledgedAt = DateTimeOffset.Now,
            }).ToList(),
        });
        StopCollection(stopCamera: true);
        _presenter.Dismiss();
        Prompt = "Calibrated.";
    }

    private void Abort(string message)
    {
        StopCollection(stopCamera: true);
        _presenter.Dismiss();
        Prompt = message;
    }

    private void StopCollection(bool stopCamera)
    {
        _timer?.Stop();
        _session.IsCollecting = false;
        _step = -1;
        WalkthroughActive = false;
        _previousAccepted = null;
        if (stopCamera && !_settings.Current.TrackingEnabled)
        {
            _live.Stop();
        }
    }

    private DispatcherTimer EnsureTimer()
    {
        if (_timer is not null)
        {
            return _timer;
        }

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _timer.Tick += (_, _) => Collect();
        return _timer;
    }

    private CalibrationPoint CurrentPoint() =>
        CalibrationPoints.Order[Math.Clamp(_point, 0, CalibrationPoints.Order.Length - 1)];

    private DisplayInfo? CurrentDisplay()
    {
        if (_step < 0 || _step >= _order.Count)
        {
            return null;
        }

        return _displays.GetDisplays().FirstOrDefault(item =>
            string.Equals(item.DeviceName, _order[_step], StringComparison.Ordinal));
    }

    private int CurrentCount()
    {
        if (_step < 0 || _step >= _order.Count || !_regions.TryGetValue(_order[_step], out var region))
        {
            return 0;
        }

        return region.Count(CurrentPoint());
    }

    private bool SameDisplays(IReadOnlyList<DisplayInfo> displays)
    {
        if (displays.Count != _order.Count)
        {
            return false;
        }

        for (var i = 0; i < _order.Count; i++)
        {
            if (!string.Equals(displays[i].DeviceName, _order[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private void Refresh()
    {
        Displays.Clear();
        foreach (var display in _displays.GetDisplays())
        {
            Displays.Add(new CalibrationStepViewModel
            {
                Title = display.FriendlyName,
                Detail = $"{display.Bounds.Width} x {display.Bounds.Height}"
                    + (display.IsPrimary ? "  Primary" : string.Empty),
            });
        }

        var profile = _profiles.Current;
        if (profile is not null && profile.Monitors.Count > 0)
        {
            Summary = "Calibrated " + profile.CreatedAt.ToLocalTime().ToString("g");
            var saved = profile.Monitors.Select(monitor => monitor.MonitorId).ToHashSet(StringComparer.Ordinal);
            var current = _displays.GetDisplays().Select(display => display.DeviceName).ToHashSet(StringComparer.Ordinal);
            Warning = current.SetEquals(saved)
                ? string.Empty
                : "The Windows display set changed since that profile. Calibrate again before trusting a match.";
            return;
        }

        Warning = string.Empty;
        Summary = "Not calibrated";
    }

    private static string Describe(SampleRejection rejection) => rejection switch
    {
        SampleRejection.None => "kept",
        SampleRejection.NoFace => "no face",
        SampleRejection.MultipleFaces => "more than one face",
        SampleRejection.EyesUnreliable => "eyes not reliable",
        SampleRejection.Blink => "blink",
        SampleRejection.LowConfidence => "low confidence",
        SampleRejection.MovingTooFast => "moving too fast",
        SampleRejection.NonPositiveInterval => "bad interval",
        _ => "rejected",
    };
}
