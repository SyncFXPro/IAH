namespace Glance.Core.Configuration;

public sealed class AppSettings
{
    public const string ThemeSystem = "System";
    public const string ThemeLight = "Light";
    public const string ThemeDark = "Dark";

    public int SchemaVersion { get; set; } = 1;

    public bool TrackingEnabled { get; set; }

    public bool LaunchAtStartup { get; set; }

    public bool CursorMovementEnabled { get; set; }

    public string Theme { get; set; } = ThemeSystem;

    public string? SelectedCameraId { get; set; }

    /// <summary>Zero with <see cref="CaptureHeight"/> means the camera keeps its own resolution.</summary>
    public int CaptureWidth { get; set; }

    /// <summary>Zero with <see cref="CaptureWidth"/> means the camera keeps its own resolution.</summary>
    public int CaptureHeight { get; set; }

    public int SwitchingDwellMilliseconds { get; set; } = 120;

    public bool ShowPerformanceMetrics { get; set; }

    public bool FileLoggingEnabled { get; set; }

    public double PreviewHoldSeconds { get; set; } = 2.5;

    public void Normalize()
    {
        SchemaVersion = 1;
        Theme = Theme switch
        {
            ThemeLight => ThemeLight,
            ThemeDark => ThemeDark,
            _ => ThemeSystem,
        };
        if (CaptureWidth <= 0 || CaptureHeight <= 0)
        {
            CaptureWidth = 0;
            CaptureHeight = 0;
        }

        SwitchingDwellMilliseconds = Math.Clamp(SwitchingDwellMilliseconds, 40, 2000);
        PreviewHoldSeconds = Math.Clamp(PreviewHoldSeconds, 0.5, 10);
    }
}

public static class CaptureResolution
{
    public static bool IsAutomatic(int width, int height) => width <= 0 || height <= 0;

    public static string Describe(int actualWidth, int actualHeight, int requestedWidth, int requestedHeight)
    {
        if (IsAutomatic(requestedWidth, requestedHeight)
            || (actualWidth == requestedWidth && actualHeight == requestedHeight))
        {
            return FormattableString.Invariant($"{actualWidth}x{actualHeight}");
        }

        return FormattableString.Invariant(
            $"{actualWidth}x{actualHeight} (requested {requestedWidth}x{requestedHeight})");
    }
}

public static class GlancePaths
{
    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Glance");

    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static string AddressingFile => Path.Combine(Root, "display-addressing.json");

    public static string GazeProfileFile => Path.Combine(Root, "gaze-profile.json");

    public static string LogFile => Path.Combine(Root, "logs", "glance.log");
}

public static class StartupCommand
{
    public const string RunValueName = "Glance";

    public static string Format(string executablePath) => $"\"{executablePath}\" --background";
}

public sealed record CursorRelocationRequest(
    bool UserEnabled,
    bool PipelineIsRealWebcam,
    bool AlreadyOnMonitor,
    bool TrackingHealthy);

public static class CursorRelocationPolicy
{
    public static string? BlockReason(CursorRelocationRequest request)
    {
        if (!request.UserEnabled)
        {
            return "Cursor movement is turned off in settings.";
        }

        if (!request.PipelineIsRealWebcam)
        {
            return "Only a webcam classifier may move the cursor. The scripted preview cannot.";
        }

        if (!request.TrackingHealthy)
        {
            return "Tracking is not healthy.";
        }

        if (request.AlreadyOnMonitor)
        {
            return "The cursor is already on the confirmed monitor.";
        }

        return null;
    }
}
