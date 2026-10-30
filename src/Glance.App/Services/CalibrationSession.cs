namespace Glance.App.Services;

/// <summary>
/// Keeps the webcam thread alive while calibration is collecting samples,
/// including when the dashboard tracking switch is off.
/// </summary>
public sealed class CalibrationSession
{
    public bool IsCollecting { get; set; }
}
