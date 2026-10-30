namespace Glance.Core.Calibration;

public enum CalibrationPoint
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    Center,
}

public static class CalibrationPoints
{
    public static readonly CalibrationPoint[] Order =
    [
        CalibrationPoint.TopLeft,
        CalibrationPoint.TopRight,
        CalibrationPoint.BottomLeft,
        CalibrationPoint.BottomRight,
        CalibrationPoint.Center,
    ];

    public static string Id(CalibrationPoint point) => point switch
    {
        CalibrationPoint.TopLeft => "top-left",
        CalibrationPoint.TopRight => "top-right",
        CalibrationPoint.BottomLeft => "bottom-left",
        CalibrationPoint.BottomRight => "bottom-right",
        _ => "center",
    };

    public static string Label(CalibrationPoint point) => point switch
    {
        CalibrationPoint.TopLeft => "Top left",
        CalibrationPoint.TopRight => "Top right",
        CalibrationPoint.BottomLeft => "Bottom left",
        CalibrationPoint.BottomRight => "Bottom right",
        _ => "Center",
    };

    /// <summary>
    /// Fraction of the panel, inset from the bezel. Adjacent monitors are close, so a corner
    /// flush with the shared edge is almost the same glance as the neighbor's corner.
    /// </summary>
    public const double EdgeMargin = 0.25;

    /// <summary>0 is the left or top edge, 1 is the right or bottom edge, 0.5 is the center.</summary>
    public static (double X, double Y) Anchor(CalibrationPoint point) => point switch
    {
        CalibrationPoint.TopLeft => (EdgeMargin, EdgeMargin),
        CalibrationPoint.TopRight => (1 - EdgeMargin, EdgeMargin),
        CalibrationPoint.BottomLeft => (EdgeMargin, 1 - EdgeMargin),
        CalibrationPoint.BottomRight => (1 - EdgeMargin, 1 - EdgeMargin),
        _ => (0.5, 0.5),
    };
}
