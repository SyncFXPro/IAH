using Glance.Windows.Displays;

namespace Glance.Windows.Placement;

public static class WindowPlacement
{
    public static void MoveToPixels(IntPtr window, int x, int y, int width, int height)
    {
        if (window == IntPtr.Zero || width <= 0 || height <= 0)
        {
            return;
        }

        NativeMethods.SetWindowPos(window, NativeMethods.TopMost, x, y, width, height, NativeMethods.ShowWindow);
    }
}
