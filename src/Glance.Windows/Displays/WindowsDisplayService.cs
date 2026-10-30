using System.Runtime.InteropServices;
using Glance.Core.Contracts;
using Glance.Core.Geometry;
using Microsoft.Win32;

namespace Glance.Windows.Displays;

public sealed class WindowsDisplayService : IDisplayService, IDisposable
{
    private readonly object _gate = new();
    private bool _disposed;

    public WindowsDisplayService()
    {
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public event EventHandler? DisplaysChanged;

    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var found = new List<DisplayInfo>();
        NativeMethods.MonitorEnumProc callback = (IntPtr monitor, IntPtr _, IntPtr _, IntPtr _) =>
        {
            var info = new NativeMethods.MonitorInfoEx { Size = Marshal.SizeOf<NativeMethods.MonitorInfoEx>() };
            if (!NativeMethods.GetMonitorInfo(monitor, ref info))
            {
                return true;
            }

            var bounds = ToRect(info.Monitor);
            var work = ToRect(info.Work);
            var friendly = ReadFriendlyName(info.DeviceName);
            var dpi = ReadDpi(monitor);
            found.Add(new DisplayInfo(
                info.DeviceName,
                friendly,
                (info.Flags & NativeMethods.MonitorInfoPrimary) != 0,
                bounds,
                work.IsEmpty ? bounds : work,
                dpi.X,
                dpi.Y));
            return true;
        };

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return found
            .OrderBy(display => display.Bounds.X)
            .ThenBy(display => display.Bounds.Y)
            .ThenBy(display => display.DeviceName, StringComparer.Ordinal)
            .ToList();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => DisplaysChanged?.Invoke(this, EventArgs.Empty);

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            DisplaysChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static PixelRect ToRect(NativeMethods.Rect rect) =>
        new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    private static string ReadFriendlyName(string deviceName)
    {
        var monitor = new NativeMethods.DisplayDevice { Size = Marshal.SizeOf<NativeMethods.DisplayDevice>() };
        if (NativeMethods.EnumDisplayDevices(deviceName, 0, ref monitor, 0)
            && !string.IsNullOrWhiteSpace(monitor.DeviceString))
        {
            return monitor.DeviceString;
        }

        return deviceName;
    }

    private static (int X, int Y) ReadDpi(IntPtr monitor)
    {
        if (NativeMethods.GetDpiForMonitor(monitor, 0, out var dpiX, out var dpiY) == 0 && dpiX > 0 && dpiY > 0)
        {
            return ((int)dpiX, (int)dpiY);
        }

        return (96, 96);
    }
}
