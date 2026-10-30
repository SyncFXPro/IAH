using Glance.Core.Configuration;
using Glance.Core.Contracts;
using Glance.Core.Geometry;

namespace Glance.Windows.Cursor;

public sealed class Win32CursorNative : ICursorNative
{
    public PixelPoint GetPosition()
    {
        if (!Displays.NativeMethods.GetCursorPos(out var point))
        {
            return new PixelPoint(0, 0);
        }

        return new PixelPoint(point.X, point.Y);
    }

    public bool SetPosition(int x, int y) => Displays.NativeMethods.SetCursorPos(x, y);
}

public sealed class WindowsCursorService : ICursorService
{
    private readonly ICursorNative _native;
    private readonly IDisplayService _displays;
    private readonly Dictionary<string, PixelPoint> _remembered = new(StringComparer.Ordinal);

    public WindowsCursorService(ICursorNative native, IDisplayService displays)
    {
        _native = native;
        _displays = displays;
    }

    public PixelPoint GetPosition() => _native.GetPosition();

    public bool TryRememberCurrentPosition()
    {
        var position = _native.GetPosition();
        var display = _displays.GetDisplays().FirstOrDefault(item => item.WorkArea.Contains(position.X, position.Y))
            ?? _displays.GetDisplays().FirstOrDefault(item => item.Bounds.Contains(position.X, position.Y));
        if (display is null)
        {
            return false;
        }

        _remembered[display.DeviceName] = position;
        return true;
    }

    public bool TryGetRememberedPosition(string monitorId, out PixelPoint position) =>
        _remembered.TryGetValue(monitorId, out position);

    public bool TryRelocate(string monitorId, CursorRelocationRequest request, out string? blockReason)
    {
        blockReason = CursorRelocationPolicy.BlockReason(request);
        if (blockReason is not null)
        {
            return false;
        }

        var display = _displays.GetDisplays().FirstOrDefault(item =>
            string.Equals(item.DeviceName, monitorId, StringComparison.Ordinal));
        if (display is null)
        {
            blockReason = "The confirmed monitor is not connected.";
            return false;
        }

        var target = _remembered.TryGetValue(monitorId, out var remembered)
            ? display.WorkArea.Clamp(remembered.X, remembered.Y)
            : display.WorkArea.Clamp(display.WorkArea.Center.X, display.WorkArea.Center.Y);
        if (!_native.SetPosition(target.X, target.Y))
        {
            blockReason = "Windows did not accept the cursor position.";
            return false;
        }

        _remembered[monitorId] = new PixelPoint(target.X, target.Y);
        return true;
    }
}
