using System.Windows.Media;
using Glance.Core.Configuration;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Glance.App.Services;

public sealed class ThemeController : IDisposable
{
    private readonly ISettingsStore _settings;
    private bool _started;

    public ThemeController(ISettingsStore settings) => _settings = settings;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        Apply(_settings.Current.Theme);
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
    }

    public void Apply(string themeName)
    {
        var theme = themeName switch
        {
            AppSettings.ThemeLight => ApplicationTheme.Light,
            AppSettings.ThemeDark => ApplicationTheme.Dark,
            _ => MapSystem(),
        };
        ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, updateAccent: true);
    }

    public void Dispose()
    {
        if (_started)
        {
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
        }
    }

    private void OnPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (Microsoft.Win32.UserPreferenceCategory.General
            or Microsoft.Win32.UserPreferenceCategory.Color
            or Microsoft.Win32.UserPreferenceCategory.VisualStyle))
        {
            return;
        }

        if (string.Equals(_settings.Current.Theme, AppSettings.ThemeSystem, StringComparison.Ordinal))
        {
            Apply(AppSettings.ThemeSystem);
        }
    }

    private static ApplicationTheme MapSystem()
    {
        if (ApplicationThemeManager.IsSystemHighContrast())
        {
            return ApplicationTheme.HighContrast;
        }

        return ApplicationThemeManager.GetSystemTheme() == SystemTheme.Light
            ? ApplicationTheme.Light
            : ApplicationTheme.Dark;
    }
}
