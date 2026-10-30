using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Glance.App.Services;
using Glance.Core.Calibration;
using Glance.Core.Configuration;
using Glance.Core.Contracts;
using Glance.Core.Diagnostics;

namespace Glance.App.ViewModels;

public sealed record DwellChoice(string Label, int Milliseconds);

public sealed record CaptureChoice(string Label, int Width, int Height);

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly IStartupRegistration _startup;
    private readonly DisplayAddressingStore _addressing;
    private readonly GazeProfileStore _profiles;
    private readonly ThemeController _theme;
    private readonly IUiInvoker _ui;
    private readonly ICameraCatalog _cameras;
    private bool _pulling;

    public SettingsViewModel(
        ISettingsStore settings,
        IStartupRegistration startup,
        DisplayAddressingStore addressing,
        GazeProfileStore profiles,
        ThemeController theme,
        IUiInvoker ui,
        ICameraCatalog cameras)
    {
        _settings = settings;
        _startup = startup;
        _addressing = addressing;
        _profiles = profiles;
        _theme = theme;
        _ui = ui;
        _cameras = cameras;
        foreach (var camera in _cameras.List())
        {
            Cameras.Add(camera);
        }

        _selectedCamera = CameraSelection.Match(settings.Current.SelectedCameraId, Cameras.ToArray());
        _selectedCapture = MatchCapture(settings.Current.CaptureWidth, settings.Current.CaptureHeight);
        _trackingEnabled = settings.Current.TrackingEnabled;
        _launchAtStartup = settings.Current.LaunchAtStartup;
        _cursorMovementEnabled = settings.Current.CursorMovementEnabled;
        _showPerformanceMetrics = settings.Current.ShowPerformanceMetrics;
        _fileLoggingEnabled = settings.Current.FileLoggingEnabled;
        _themeName = settings.Current.Theme;
        SelectedDwell = DwellChoices.MinBy(choice =>
            Math.Abs(choice.Milliseconds - settings.Current.SwitchingDwellMilliseconds)) ?? DwellChoices[1];
        settings.Changed += (_, _) => _ui.Invoke(Pull);
        RefreshStamp();
    }

    public string[] Themes { get; } = [AppSettings.ThemeSystem, AppSettings.ThemeLight, AppSettings.ThemeDark];

    public DwellChoice[] DwellChoices { get; } =
    [
        new("Responsive (80 ms)", 80),
        new("Balanced (120 ms)", 120),
        new("Relaxed (250 ms)", 250),
    ];

    public CaptureChoice[] CaptureChoices { get; } =
    [
        new("Automatic (camera default)", 0, 0),
        new("1920 x 1080", 1920, 1080),
        new("1280 x 720", 1280, 720),
        new("960 x 540", 960, 540),
        new("640 x 480", 640, 480),
    ];

    public ObservableCollection<string> MetricLines { get; } = [];

    public ObservableCollection<CameraDescriptor> Cameras { get; } = [];

    [ObservableProperty]
    private bool _trackingEnabled;

    [ObservableProperty]
    private bool _launchAtStartup;

    [ObservableProperty]
    private bool _cursorMovementEnabled;

    [ObservableProperty]
    private bool _showPerformanceMetrics;

    [ObservableProperty]
    private bool _fileLoggingEnabled;

    [ObservableProperty]
    private string _themeName = AppSettings.ThemeSystem;

    [ObservableProperty]
    private DwellChoice? _selectedDwell;

    [ObservableProperty]
    private CameraDescriptor? _selectedCamera;

    [ObservableProperty]
    private CaptureChoice? _selectedCapture;

    [ObservableProperty]
    private string _status = "Pick a webcam. The dashboard button saves it and starts the preview.";

    partial void OnTrackingEnabledChanged(bool value) => Save(settings => settings.TrackingEnabled = value);

    partial void OnLaunchAtStartupChanged(bool value)
    {
        Save(settings => settings.LaunchAtStartup = value);
        TryApplyStartup(value);
    }

    partial void OnCursorMovementEnabledChanged(bool value) => Save(settings => settings.CursorMovementEnabled = value);

    partial void OnShowPerformanceMetricsChanged(bool value)
    {
        Save(settings => settings.ShowPerformanceMetrics = value);
        RefreshStamp();
    }

    partial void OnFileLoggingEnabledChanged(bool value) => Save(settings => settings.FileLoggingEnabled = value);

    partial void OnThemeNameChanged(string value)
    {
        Save(settings => settings.Theme = value);
        _theme.Apply(value);
    }

    partial void OnSelectedCameraChanged(CameraDescriptor? value)
    {
        if (_pulling || value is null)
        {
            return;
        }

        Save(settings => settings.SelectedCameraId = value.Id);
        Status = "Saved webcam: " + value.Name + ". The dashboard button starts it.";
    }

    partial void OnSelectedDwellChanged(DwellChoice? value)
    {
        if (value is null)
        {
            return;
        }

        Save(settings => settings.SwitchingDwellMilliseconds = value.Milliseconds);
    }

    partial void OnSelectedCaptureChanged(CaptureChoice? value)
    {
        if (_pulling || value is null)
        {
            return;
        }

        Save(settings =>
        {
            settings.CaptureWidth = value.Width;
            settings.CaptureHeight = value.Height;
        });
        Status = value.Width == 0
            ? "Capture size is automatic. The camera keeps its own resolution."
            : $"Requested capture size {value.Width} x {value.Height}. The camera may deliver a different size.";
    }

    [RelayCommand]
    private void ResetCalibration()
    {
        _addressing.Clear();
        _profiles.Clear();
        Status = "Display check and gaze profile cleared.";
    }

    private void Save(Action<AppSettings> change)
    {
        var before = Snapshot(_settings.Current);
        change(_settings.Current);
        if (Snapshot(_settings.Current) == before)
        {
            return;
        }

        _settings.Save();
    }

    private void Pull()
    {
        _pulling = true;
        var current = _settings.Current;
        if (TrackingEnabled != current.TrackingEnabled)
        {
            TrackingEnabled = current.TrackingEnabled;
        }

        if (LaunchAtStartup != current.LaunchAtStartup)
        {
            LaunchAtStartup = current.LaunchAtStartup;
        }

        if (CursorMovementEnabled != current.CursorMovementEnabled)
        {
            CursorMovementEnabled = current.CursorMovementEnabled;
        }

        if (ShowPerformanceMetrics != current.ShowPerformanceMetrics)
        {
            ShowPerformanceMetrics = current.ShowPerformanceMetrics;
        }

        if (FileLoggingEnabled != current.FileLoggingEnabled)
        {
            FileLoggingEnabled = current.FileLoggingEnabled;
        }

        if (!string.Equals(ThemeName, current.Theme, StringComparison.Ordinal))
        {
            ThemeName = current.Theme;
        }

        var saved = CameraSelection.Match(current.SelectedCameraId, Cameras.ToArray());
        if (saved?.Id != SelectedCamera?.Id)
        {
            SelectedCamera = saved;
        }

        var capture = MatchCapture(current.CaptureWidth, current.CaptureHeight);
        if (capture != SelectedCapture)
        {
            SelectedCapture = capture;
        }

        _pulling = false;
    }

    private void TryApplyStartup(bool enabled)
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            Status = "Could not resolve the application path for startup registration.";
            return;
        }

        try
        {
            _startup.Apply(enabled, path);
            Status = enabled
                ? "Glance will open to the tray at sign-in."
                : "Startup registration removed.";
        }
        catch (Exception ex)
        {
            Status = "Startup registration failed: " + ex.Message;
        }
    }

    private void RefreshStamp()
    {
        MetricLines.Clear();
        if (!_settings.Current.ShowPerformanceMetrics)
        {
            return;
        }

        var stamp = MachineStamp.CaptureCurrent();
        MetricLines.Add($"OS: {stamp.OsDescription}");
        MetricLines.Add($"Runtime: {stamp.Framework}");
        MetricLines.Add($"CPU: {stamp.ProcessorName}");
        MetricLines.Add($"Processors: {stamp.ProcessorCount}");
        MetricLines.Add($"Execution provider: {stamp.ExecutionProvider}");
        MetricLines.Add($"Model: {stamp.ModelName}");
        MetricLines.Add("Latency histograms: no samples");
        MetricLines.Add("Input frame age: not measured");
    }

    private static string Snapshot(AppSettings settings) =>
        $"{settings.TrackingEnabled}|{settings.LaunchAtStartup}|{settings.CursorMovementEnabled}|{settings.ShowPerformanceMetrics}|{settings.FileLoggingEnabled}|{settings.Theme}|{settings.SwitchingDwellMilliseconds}|{settings.SelectedCameraId}|{settings.CaptureWidth}x{settings.CaptureHeight}";

    private CaptureChoice MatchCapture(int width, int height) =>
        CaptureChoices.FirstOrDefault(choice => choice.Width == width && choice.Height == height)
        ?? CaptureChoices[0];
}
