using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Media;
using Glance.App.Services;
using Glance.Core.Calibration;
using Glance.Core.Classification;
using Glance.Core.Configuration;
using Glance.Core.Contracts;
using Glance.Core.Geometry;
using Glance.Core.Tracking;

namespace Glance.App.ViewModels;

public partial class MonitorTileViewModel : ObservableObject
{
    public required string DeviceName { get; init; }

    public required string Title { get; init; }

    public required string Detail { get; init; }

    public required string Badge { get; init; }

    public double CanvasLeft { get; init; }

    public double CanvasTop { get; init; }

    public double CanvasWidth { get; init; }

    public double CanvasHeight { get; init; }

    public bool IsHighlighted { get; init; }
}

public partial class FeatureDotViewModel : ObservableObject
{
    public required string Label { get; init; }

    public required Brush Fill { get; init; }

    public double Size { get; init; }

    public double CanvasLeft { get; init; }

    public double CanvasTop { get; init; }
}

public partial class DashboardViewModel : ObservableObject
{
    private const double MapWidth = 640;
    private const double MapHeight = 180;
    private const double PlaneWidth = 640;
    private const double PlaneHeight = 200;
    private static readonly Brush[] RegionBrushes = CreateBrushes();
    private readonly ISettingsStore _settings;
    private readonly GazeProfileStore _profiles;
    private readonly IDisplayService _displays;
    private readonly IUiInvoker _ui;
    private readonly ShellNavigator _navigator;
    private readonly TrackingCoordinator _tracking;
    private readonly LandmarkPreviewHost _landmarkPreview;
    private readonly ICameraCatalog _cameras;

    public DashboardViewModel(
        ISettingsStore settings,
        IDisplayService displays,
        IUiInvoker ui,
        ShellNavigator navigator,
        TrackingCoordinator tracking,
        LandmarkPreviewHost landmarkPreview,
        ICameraCatalog cameras,
        GazeProfileStore profiles)
    {
        _settings = settings;
        _profiles = profiles;
        _displays = displays;
        _ui = ui;
        _navigator = navigator;
        _tracking = tracking;
        _landmarkPreview = landmarkPreview;
        _cameras = cameras;
        _trackingEnabled = settings.Current.TrackingEnabled;
        ReloadCameras();
        settings.Changed += (_, _) => _ui.Invoke(PullSettings);
        displays.DisplaysChanged += (_, _) => _ui.Invoke(Rebuild);
        profiles.Changed += (_, _) => _ui.Invoke(Rebuild);
        tracking.StatusChanged += (_, _) => _ui.Invoke(Rebuild);
        Rebuild();
    }

    public ObservableCollection<MonitorTileViewModel> Tiles { get; } = [];

    public ObservableCollection<FeatureDotViewModel> FeatureDots { get; } = [];

    public ObservableCollection<CameraDescriptor> Cameras { get; } = [];

    [ObservableProperty]
    private bool _trackingEnabled;

    [ObservableProperty]
    private bool _developerView;

    [ObservableProperty]
    private string _calibrationLabel = "Not calibrated";

    [ObservableProperty]
    private string _headline = TrackingStatus.Off.Headline;

    [ObservableProperty]
    private string _detail = TrackingStatus.Off.Detail;

    [ObservableProperty]
    private string _facePosition = "Not measured";

    [ObservableProperty]
    private string _featureCaption = "Calibrate five points on each monitor to draw a region.";

    [ObservableProperty]
    private string _headYawCaption = "Head yaw";

    [ObservableProperty]
    private string _headPitchCaption = "Head pitch";

    [ObservableProperty]
    private string _headYaw = "Not measured";

    [ObservableProperty]
    private string _headPitch = "Not measured";

    [ObservableProperty]
    private string _irisX = "Not measured";

    [ObservableProperty]
    private string _irisY = "Not measured";

    [ObservableProperty]
    private string _predictedMonitor = "Not measured";

    [ObservableProperty]
    private string _distance = "Not measured";

    [ObservableProperty]
    private string _switchState = "Tracking unavailable";

    [ObservableProperty]
    private string _sinceConfirmation = "No confirmation yet";

    [ObservableProperty]
    private string _latency = TrackingStatus.Off.LatencyLine;

    [ObservableProperty]
    private string _webcam = TrackingStatus.Off.WebcamLine;

    [ObservableProperty]
    private string _model = TrackingStatus.Off.ModelLine;

    [ObservableProperty]
    private string _classifier = TrackingStatus.Off.ClassifierLine;

    [ObservableProperty]
    private CameraDescriptor? _selectedCamera;

    [ObservableProperty]
    private string _cameraMessage = "Pick a webcam, then start it.";

    [RelayCommand]
    private void RefreshCameras() => ReloadCameras();

    [RelayCommand]
    private void OpenCalibration() => _navigator.ShowCalibration();

    [RelayCommand]
    private void OpenSettings() => _navigator.ShowSettings();

    [RelayCommand]
    private void OpenLandmarkPreview()
    {
        if (SelectedCamera is null)
        {
            CameraMessage = Cameras.Count == 0
                ? "No webcams were found. Refresh the list and try again."
                : "Pick a webcam first.";
            return;
        }

        _landmarkPreview.Start(
            SelectedCamera.Index,
            SelectedCamera.Name,
            _settings.Current.CaptureWidth,
            _settings.Current.CaptureHeight);
        CameraMessage = "Preview started for " + SelectedCamera.Name + ".";
        Rebuild();
    }

    partial void OnSelectedCameraChanged(CameraDescriptor? value)
    {
        if (value is null || _settings.Current.SelectedCameraId == value.Id)
        {
            return;
        }

        _settings.Current.SelectedCameraId = value.Id;
        _settings.Save();
    }

    partial void OnTrackingEnabledChanged(bool value)
    {
        if (_settings.Current.TrackingEnabled == value)
        {
            return;
        }

        _settings.Current.TrackingEnabled = value;
        _settings.Save();
    }

    private void PullSettings()
    {
        if (TrackingEnabled != _settings.Current.TrackingEnabled)
        {
            TrackingEnabled = _settings.Current.TrackingEnabled;
        }

        var saved = CameraSelection.Match(_settings.Current.SelectedCameraId, Cameras.ToArray());
        if (saved?.Id != SelectedCamera?.Id)
        {
            SelectedCamera = saved;
        }
    }

    private void Rebuild()
    {
        var status = _tracking.Current;
        var displays = _displays.GetDisplays();
        Headline = status.Headline;
        Detail = status.Detail;
        var profile = _profiles.Current;
        CalibrationLabel = profile is { Monitors.Count: > 0 }
            ? "Calibrated " + profile.CreatedAt.ToLocalTime().ToString("g")
            : "Not calibrated";
        var live = status.Source == Core.Tracking.TrackingSourceKind.Webcam;
        HeadYawCaption = "Head yaw";
        HeadPitchCaption = "Head pitch";
        HeadYaw = FormatAngle(status.HeadYawRadians);
        HeadPitch = FormatAngle(status.HeadPitchRadians);
        FacePosition = status.HeadX is double faceX && status.HeadY is double faceY && status.HeadScale is double faceScale
            ? FormattableString.Invariant($"X {faceX:0.00}    Y {faceY:0.00}    scale {faceScale:0.00}")
            : "Not measured";
        IrisX = FormatOptional(status.IrisX);
        IrisY = FormatOptional(status.IrisY);
        PredictedMonitor = status.PredictedMonitorId ?? "Not measured";
        Distance = status.ClassificationDistance is double distance ? distance.ToString("0.00") : "Not measured";
        SwitchState = FormatState(status.SwitchState);
        SinceConfirmation = status.SinceLastConfirmation is TimeSpan since
            ? live ? $"{since.TotalSeconds:0.0} s" : $"{since.TotalSeconds:0.0} s (scripted preview)"
            : "No confirmation yet";
        Latency = status.LatencyLine;
        Webcam = live ? status.WebcamLine : CameraSelection.Label(_settings.Current.SelectedCameraId);
        Model = status.ModelLine;
        Classifier = status.ClassifierLine;
        if (TrackingEnabled != _settings.Current.TrackingEnabled)
        {
            TrackingEnabled = _settings.Current.TrackingEnabled;
        }

        var drawn = LayoutNormalizer.Normalize(
            displays.Select(display => display.Bounds).ToArray(),
            MapWidth,
            MapHeight,
            16);
        Tiles.Clear();
        for (var i = 0; i < displays.Count; i++)
        {
            var display = displays[i];
            var rect = drawn[i];
            var highlighted = string.Equals(display.DeviceName, status.HighlightedMonitorId, StringComparison.Ordinal);
            Tiles.Add(new MonitorTileViewModel
            {
                DeviceName = display.DeviceName,
                Title = Trim(display.FriendlyName, 28),
                Detail = $"{display.Bounds.Width} x {display.Bounds.Height}",
                Badge = highlighted
                    ? live ? "Match" : "Preview"
                    : display.IsPrimary ? "Primary" : string.Empty,
                CanvasLeft = rect.X,
                CanvasTop = rect.Y,
                CanvasWidth = Math.Max(72, rect.Width),
                CanvasHeight = Math.Max(56, rect.Height),
                IsHighlighted = highlighted,
            });
        }

        RebuildFeaturePlane(status);
    }

    private void RebuildFeaturePlane(TrackingStatus status)
    {
        FeatureDots.Clear();
        var labeled = new List<(string Label, double[] Mean, int Monitor)>();
        var profile = _profiles.Current;
        if (profile is not null)
        {
            for (var monitorIndex = 0; monitorIndex < profile.Monitors.Count; monitorIndex++)
            {
                foreach (var point in profile.Monitors[monitorIndex].Points)
                {
                    if (point.Mean.Length != GazeFeatureVector.Dimension)
                    {
                        continue;
                    }

                    labeled.Add(($"{monitorIndex + 1} {ShortPoint(point.Point)}", point.Mean, monitorIndex));
                }
            }
        }

        if (labeled.Count < 2)
        {
            FeatureCaption = "Calibrate five points on each monitor. The saved profile is from head pose, so collect the dots again.";
            return;
        }

        var projected = labeled.Select(item => (item.Label, item.Monitor, Point: (X: item.Mean[0], Y: item.Mean[1]))).ToList();
        var minX = projected.Min(item => item.Point.X);
        var maxX = projected.Max(item => item.Point.X);
        var minY = projected.Min(item => item.Point.Y);
        var maxY = projected.Max(item => item.Point.Y);
        var spanX = Math.Max(maxX - minX, 0.05);
        var spanY = Math.Max(maxY - minY, 0.05);
        var midX = (minX + maxX) / 2;
        var midY = (minY + maxY) / 2;
        minX = midX - spanX / 2 - spanX * 0.15;
        maxX = midX + spanX / 2 + spanX * 0.15;
        minY = midY - spanY / 2 - spanY * 0.15;
        maxY = midY + spanY / 2 + spanY * 0.15;
        const double pad = 18;
        double MapX(double value) => pad + (Math.Clamp(value, minX, maxX) - minX) / (maxX - minX) * (PlaneWidth - pad * 2);
        double MapY(double value) => pad + (maxY - Math.Clamp(value, minY, maxY)) / (maxY - minY) * (PlaneHeight - pad * 2);
        var liveOutside = false;
        (double X, double Y)? livePoint = null;
        if (status.IrisX is double irisX && status.IrisY is double irisY)
        {
            liveOutside = irisX < minX || irisX > maxX || irisY < minY || irisY > maxY;
            livePoint = (irisX, irisY);
        }

        foreach (var item in projected)
        {
            FeatureDots.Add(new FeatureDotViewModel
            {
                Label = item.Label,
                Fill = RegionBrushes[item.Monitor % RegionBrushes.Length],
                Size = 12,
                CanvasLeft = MapX(item.Point.X),
                CanvasTop = MapY(item.Point.Y),
            });
        }

        if (livePoint is { } current)
        {
            FeatureDots.Add(new FeatureDotViewModel
            {
                Label = liveOutside ? "Now outside" : "Now",
                Fill = Brushes.WhiteSmoke,
                Size = 16,
                CanvasLeft = MapX(current.X),
                CanvasTop = MapY(current.Y),
            });
        }

        FeatureCaption = "The calibration dots stay on a fixed scale. Horizontal is iris X, toward your right. Vertical is iris Y, toward the upper lid. Now outside means the live pair left that scale.";
    }

    private static string ShortPoint(string point) => point switch
    {
        "top-left" => "TL",
        "top-right" => "TR",
        "bottom-left" => "BL",
        "bottom-right" => "BR",
        "center" => "C",
        _ => point,
    };

    private static Brush[] CreateBrushes()
    {
        Brush[] brushes =
        [
            new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0xFF)),
            new SolidColorBrush(Color.FromRgb(0xF0, 0xB4, 0x29)),
            new SolidColorBrush(Color.FromRgb(0x3D, 0xDC, 0x97)),
            new SolidColorBrush(Color.FromRgb(0xE8, 0x5D, 0x75)),
        ];
        foreach (var brush in brushes)
        {
            brush.Freeze();
        }

        return brushes;
    }

    private void ReloadCameras()
    {
        var selectedId = SelectedCamera?.Id ?? _settings.Current.SelectedCameraId;
        Cameras.Clear();
        foreach (var camera in _cameras.List())
        {
            Cameras.Add(camera);
        }

        SelectedCamera = CameraSelection.Match(selectedId, Cameras.ToArray());
        CameraMessage = Cameras.Count == 0
            ? "No webcams were found."
            : string.Empty;
    }

    private static string FormatAngle(double? radians) =>
        radians is double value ? $"{value * 180 / Math.PI:0.0} deg" : "Not measured";

    private static string FormatOptional(double? value) =>
        value is double number ? number.ToString("0.00") : "Not measured";

    private static string FormatState(Core.Switching.SwitchState state) => state switch
    {
        Core.Switching.SwitchState.TrackingStable => "Tracking stable",
        Core.Switching.SwitchState.TransitionCandidate => "Transition candidate",
        Core.Switching.SwitchState.SwitchConfirmed => "Switch confirmed",
        Core.Switching.SwitchState.InputSuppressed => "Input suppressed",
        _ => "Tracking unavailable",
    };

    private static string Trim(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "...";
}
