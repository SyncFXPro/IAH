using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using Glance.Core.Contracts;
using Glance.Core.Focus;
using Glance.Windows.Displays;

namespace Glance.Windows.Focus;

public sealed class WindowsWindowFocus : IWindowFocus, IDisposable
{
    private readonly ConcurrentDictionary<nint, AutomationElement> _inputs = new();
    private readonly int _ownProcessId = Environment.ProcessId;
    private bool _listening;
    private bool _disposed;

    public WindowsWindowFocus()
    {
        try
        {
            Automation.AddAutomationFocusChangedEventHandler(RememberInput);
            _listening = true;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ElementNotAvailableException)
        {
            _listening = false;
        }
    }

    public string TryActivate(string monitorId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var windows = new List<FocusWindow>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (TryDescribe(hwnd, out var window))
            {
                windows.Add(window);
            }

            return true;
        }, IntPtr.Zero);

        var chosen = WindowFocusPolicy.ChooseTopmost(windows, monitorId);
        if (chosen is null)
        {
            return "No eligible window on that monitor.";
        }

        var hwnd = new IntPtr(long.Parse(chosen.Id, System.Globalization.CultureInfo.InvariantCulture));
        var title = string.IsNullOrWhiteSpace(chosen.Title) ? "the window" : chosen.Title;
        if (NativeMethods.GetForegroundWindow() == hwnd)
        {
            return title + " is already in front.";
        }

        if (!NativeMethods.SetForegroundWindow(hwnd))
        {
            return "Windows did not allow " + title + " to come forward.";
        }

        if (_inputs.TryGetValue(hwnd, out var input) && TryRestore(hwnd, input))
        {
            return "Brought " + title + " forward and restored its text input.";
        }

        return "Brought " + title + " forward.";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_listening)
        {
            Automation.RemoveAutomationFocusChangedEventHandler(RememberInput);
        }
    }

    private bool TryDescribe(IntPtr hwnd, out FocusWindow window)
    {
        window = null!;
        if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd))
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        var owner = NativeMethods.GetWindow(hwnd, NativeMethods.GwOwner);
        var exStyle = ExStyle(hwnd);
        var className = ClassName(hwnd);
        var eligible = WindowFocusPolicy.IsTaskbarWindow(
            visible: true,
            iconic: false,
            cloaked: IsCloaked(hwnd),
            hasOwner: owner != IntPtr.Zero,
            toolWindow: (exStyle & NativeMethods.WsExToolWindow) != 0,
            appWindow: (exStyle & NativeMethods.WsExAppWindow) != 0,
            ownProcess: processId == _ownProcessId,
            className);
        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNull);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var info = new NativeMethods.MonitorInfoEx { Size = Marshal.SizeOf<NativeMethods.MonitorInfoEx>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        window = new FocusWindow(
            hwnd.ToInt64().ToString(System.Globalization.CultureInfo.InvariantCulture),
            Title(hwnd),
            info.DeviceName,
            eligible);
        return true;
    }

    private void RememberInput(object sender, AutomationFocusChangedEventArgs args)
    {
        if (sender is not AutomationElement element)
        {
            return;
        }

        try
        {
            if (!IsEditable(element) || !TryRootWindow(element, out var hwnd))
            {
                return;
            }

            NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
            if (processId == _ownProcessId)
            {
                return;
            }

            _inputs[hwnd] = element;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException)
        {
        }
    }

    private bool TryRestore(nint hwnd, AutomationElement element)
    {
        try
        {
            element.SetFocus();
            return true;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException)
        {
            _inputs.TryRemove(hwnd, out _);
            return false;
        }
    }

    private static bool IsEditable(AutomationElement element)
    {
        if (!element.Current.IsKeyboardFocusable)
        {
            return false;
        }

        var type = element.Current.ControlType;
        return type == ControlType.Edit || type == ControlType.Document || type == ControlType.ComboBox;
    }

    private static bool TryRootWindow(AutomationElement element, out nint hwnd)
    {
        var current = element;
        while (current is not null)
        {
            var raw = current.GetCurrentPropertyValue(AutomationElement.NativeWindowHandleProperty, true);
            if (raw is int handle && handle != 0)
            {
                var root = NativeMethods.GetAncestor(new IntPtr(handle), NativeMethods.GaRoot);
                if (root != IntPtr.Zero)
                {
                    hwnd = root;
                    return true;
                }
            }

            current = TreeWalker.ControlViewWalker.GetParent(current);
        }

        hwnd = 0;
        return false;
    }

    private static bool IsCloaked(IntPtr hwnd)
    {
        var result = NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DwmwaCloaked, out var cloaked, sizeof(int));
        return result == 0 && cloaked != 0;
    }

    private static long ExStyle(IntPtr hwnd) =>
        IntPtr.Size == 8
            ? NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64()
            : NativeMethods.GetWindowLong(hwnd, NativeMethods.GwlExStyle);

    private static string ClassName(IntPtr hwnd)
    {
        var buffer = new char[256];
        var length = NativeMethods.GetClassName(hwnd, buffer, buffer.Length);
        return length <= 0 ? string.Empty : new string(buffer, 0, length);
    }

    private static string Title(IntPtr hwnd)
    {
        var buffer = new char[512];
        var length = NativeMethods.GetWindowText(hwnd, buffer, buffer.Length);
        return length <= 0 ? string.Empty : new string(buffer, 0, length);
    }
}
