using Glance.App.Views;
using Glance.Core.Configuration;
using Glance.Core.Contracts;
using Glance.Windows.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Glance.App.Services;

public sealed class ApplicationHostService : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ISettingsStore _settings;
    private readonly IStartupRegistration _startup;
    private readonly ThemeController _theme;
    private readonly ILogger<ApplicationHostService> _logger;

    public ApplicationHostService(
        IServiceProvider services,
        ISettingsStore settings,
        IStartupRegistration startup,
        ThemeController theme,
        ILogger<ApplicationHostService> logger)
    {
        _services = services;
        _settings = settings;
        _startup = startup;
        _theme = theme;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _theme.Start();
        TryApplyStartup();
        var tray = _services.GetRequiredService<TrayWindow>();
        tray.Show();
        var hidden = Environment.GetCommandLineArgs()
            .Any(argument => string.Equals(argument, "--background", StringComparison.OrdinalIgnoreCase));
        if (!hidden)
        {
            _services.GetRequiredService<MainWindow>().EnsureShown();
        }

        _logger.LogInformation("Glance shell started.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _theme.Dispose();
        return Task.CompletedTask;
    }

    private void TryApplyStartup()
    {
        if (!_settings.Current.LaunchAtStartup)
        {
            return;
        }

        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            _startup.Apply(true, path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not refresh the Windows startup registration.");
        }
    }
}
