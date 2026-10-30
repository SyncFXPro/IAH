using System.Windows;
using Glance.App.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Glance.App.Services;

public sealed class ShellNavigator
{
    private readonly IServiceProvider _services;

    public ShellNavigator(IServiceProvider services) => _services = services;

    public void ShowDashboard() => Show(typeof(DashboardPage));

    public void ShowCalibration() => Show(typeof(CalibrationPage));

    public void ShowSettings() => Show(typeof(SettingsPage));

    public void Exit()
    {
        var window = _services.GetRequiredService<MainWindow>();
        window.PrepareForExit();
        Application.Current.Shutdown();
    }

    private void Show(Type page)
    {
        var window = _services.GetRequiredService<MainWindow>();
        window.ShowPage(page);
    }
}
