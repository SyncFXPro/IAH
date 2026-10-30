using System.Windows;
using System.Windows.Threading;
using Glance.App.Services;
using Glance.App.ViewModels;
using Glance.App.Views;
using Glance.Core.Calibration;
using Glance.Core.Classification;
using Glance.Core.Configuration;
using Glance.Core.Contracts;
using Glance.Tracking.Camera;
using Glance.Tracking.Inference;
using Glance.Tracking.Models;
using Glance.Windows.Cursor;
using Glance.Windows.Displays;
using Glance.Windows.Focus;
using Glance.Windows.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wpf.Ui;
using Wpf.Ui.Abstractions;

namespace Glance.App;

public partial class App : Application
{
    private readonly SingleInstance _single = new();
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        if (!_single.TryAcquire())
        {
            _single.SignalExisting();
            Shutdown();
            return;
        }

        var settings = new JsonSettingsStore(GlancePaths.SettingsFile);
        var addressing = new DisplayAddressingStore(GlancePaths.AddressingFile);
        var profiles = new GazeProfileStore(GlancePaths.GazeProfileFile);
        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug();
                logging.AddProvider(new GlanceFileLoggerProvider(
                    () => settings.Current.FileLoggingEnabled,
                    GlancePaths.LogFile));
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .ConfigureServices(services =>
            {
                services.AddSingleton<ISettingsStore>(settings);
                services.AddSingleton(addressing);
                services.AddSingleton(profiles);
                services.AddSingleton<CalibrationSession>();
                services.AddSingleton<IDisplayService, WindowsDisplayService>();
                services.AddSingleton<ICursorNative, Win32CursorNative>();
                services.AddSingleton<ICursorService, WindowsCursorService>();
                services.AddSingleton<IWindowFocus, WindowsWindowFocus>();
                services.AddSingleton<IStartupRegistration, WindowsStartupService>();
                services.AddSingleton<ICameraCatalog, DirectShowCameraCatalog>();
                services.AddSingleton<ICameraCapture, UnavailableCameraCapture>();
                services.AddSingleton<IFaceLandmarkModel, UnavailableFaceLandmarkModel>();
                services.AddSingleton<IGazeFeatureExtractor, ImageSpaceGazeFeatureExtractor>();
                services.AddSingleton<LiveFaceTracker>();
                services.AddSingleton<IMonitorClassifier, MonitorClassifier>();
                services.AddSingleton<INavigationViewPageProvider, AppPageProvider>();
                services.AddSingleton<INavigationService, NavigationService>();
                services.AddSingleton<IUiInvoker, WpfUiInvoker>();
                services.AddSingleton<CalibrationTargetPresenter>();
                services.AddSingleton<LandmarkPreviewHost>();
                services.AddSingleton<ShellNavigator>();
                services.AddSingleton<ThemeController>();
                services.AddSingleton<ShellViewModel>();
                services.AddSingleton<DashboardViewModel>();
                services.AddSingleton<CalibrationViewModel>();
                services.AddSingleton<SettingsViewModel>();
                services.AddSingleton<MainWindow>();
                services.AddSingleton<TrayWindow>();
                services.AddSingleton<DashboardPage>();
                services.AddSingleton<CalibrationPage>();
                services.AddSingleton<SettingsPage>();
                services.AddSingleton<TrackingCoordinator>();
                services.AddHostedService(provider => provider.GetRequiredService<TrackingCoordinator>());
                services.AddHostedService<ApplicationHostService>();
            })
            .Build();

        _single.StartListening(() => Dispatcher.BeginInvoke(ShowRequestedWindow));
        _host.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            _host.StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            _host.Dispose();
        }

        _single.Dispose();
        base.OnExit(e);
    }

    private void ShowRequestedWindow()
    {
        if (_host is null)
        {
            return;
        }

        _host.Services.GetRequiredService<MainWindow>().EnsureShown();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _host?.Services.GetService<ILogger<App>>()?.LogError(e.Exception, "Unhandled UI exception");
        e.Handled = true;
    }
}
