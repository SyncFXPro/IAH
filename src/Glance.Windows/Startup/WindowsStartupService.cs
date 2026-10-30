using Glance.Core.Configuration;
using Glance.Core.Contracts;
using Microsoft.Win32;

namespace Glance.Windows.Startup;

public sealed class WindowsStartupService : IStartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public void Apply(bool enabled, string executablePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Could not open the Windows Run registry key.");
        if (!enabled)
        {
            key.DeleteValue(StartupCommand.RunValueName, throwOnMissingValue: false);
            return;
        }

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("An executable path is required.", nameof(executablePath));
        }

        key.SetValue(StartupCommand.RunValueName, StartupCommand.Format(executablePath));
    }
}
