namespace Glance.Core.Focus;

public sealed record FocusWindow(string Id, string Title, string MonitorId, bool Eligible);

/// <summary>
/// The confirmed monitor activates the first eligible window in Z order.
/// Area is not a factor. A window Glance itself owns is not eligible.
/// </summary>
public static class WindowFocusPolicy
{
    public static bool IsTaskbarWindow(
        bool visible,
        bool iconic,
        bool cloaked,
        bool hasOwner,
        bool toolWindow,
        bool appWindow,
        bool ownProcess,
        string className)
    {
        if (!visible || iconic || cloaked || ownProcess)
        {
            return false;
        }

        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
        {
            return false;
        }

        if (hasOwner)
        {
            return appWindow;
        }

        return !toolWindow;
    }

    public static FocusWindow? ChooseTopmost(IReadOnlyList<FocusWindow> zOrderTopFirst, string monitorId)
    {
        foreach (var window in zOrderTopFirst)
        {
            if (!window.Eligible)
            {
                continue;
            }

            if (string.Equals(window.MonitorId, monitorId, StringComparison.OrdinalIgnoreCase))
            {
                return window;
            }
        }

        return null;
    }
}
