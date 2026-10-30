using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Glance.App.Services;
using Glance.App.Views;
using Glance.Core.Configuration;
using Wpf.Ui.Controls;

namespace Glance.App.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly IUiInvoker _ui;
    private readonly ShellNavigator _navigator;
    private readonly TrackingCoordinator _tracking;

    public ShellViewModel(
        ISettingsStore settings,
        IUiInvoker ui,
        ShellNavigator navigator,
        TrackingCoordinator tracking)
    {
        _settings = settings;
        _ui = ui;
        _navigator = navigator;
        _tracking = tracking;
        _trackingEnabled = settings.Current.TrackingEnabled;
        MenuItems = new ObservableCollection<object>
        {
            new NavigationViewItem
            {
                Content = "Dashboard",
                Icon = new SymbolIcon { Symbol = SymbolRegular.Home24 },
                TargetPageType = typeof(DashboardPage),
            },
            new NavigationViewItem
            {
                Content = "Calibration",
                Icon = new SymbolIcon { Symbol = SymbolRegular.Target24 },
                TargetPageType = typeof(CalibrationPage),
            },
        };
        FooterMenuItems = new ObservableCollection<object>
        {
            new NavigationViewItem
            {
                Content = "Settings",
                Icon = new SymbolIcon { Symbol = SymbolRegular.Settings24 },
                TargetPageType = typeof(SettingsPage),
            },
        };
        settings.Changed += (_, _) => _ui.Invoke(PullSettings);
        tracking.StatusChanged += (_, status) => _ui.Invoke(() => ApplyStatus(status));
        ApplyStatus(tracking.Current);
    }

    public ObservableCollection<object> MenuItems { get; }

    public ObservableCollection<object> FooterMenuItems { get; }

    [ObservableProperty]
    private bool _trackingEnabled;

    [ObservableProperty]
    private string _toolTip = "Glance";

    [RelayCommand]
    private void ShowDashboard() => _navigator.ShowDashboard();

    [RelayCommand]
    private void ShowCalibration() => _navigator.ShowCalibration();

    [RelayCommand]
    private void ShowSettings() => _navigator.ShowSettings();

    [RelayCommand]
    private void Exit() => _navigator.Exit();

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
    }

    private void ApplyStatus(Core.Tracking.TrackingStatus status)
    {
        ToolTip = status.Source == Core.Tracking.TrackingSourceKind.Webcam
            ? "Glance - webcam"
            : status.IsRunning ? "Glance - scripted preview" : "Glance - eye tracking off";
        if (TrackingEnabled != _settings.Current.TrackingEnabled)
        {
            TrackingEnabled = _settings.Current.TrackingEnabled;
        }
    }
}
