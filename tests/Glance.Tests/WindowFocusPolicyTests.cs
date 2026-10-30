using Glance.Core.Focus;

namespace Glance.Tests;

public class WindowFocusPolicyTests
{
    [Fact]
    public void ChooseTopmost_UsesStackingOrder_NotALaterWindowOnTheSameMonitor()
    {
        FocusWindow[] windows =
        [
            new("1", "Front", @"\\.\DISPLAY2", Eligible: true),
            new("2", "Behind", @"\\.\DISPLAY2", Eligible: true),
            new("3", "Other monitor", @"\\.\DISPLAY1", Eligible: true),
        ];

        var chosen = WindowFocusPolicy.ChooseTopmost(windows, @"\\.\DISPLAY2");

        Assert.Equal("1", chosen?.Id);
        Assert.Equal("Front", chosen?.Title);
    }

    [Fact]
    public void ChooseTopmost_SkipsIneligibleWindowsAboveTheTarget()
    {
        FocusWindow[] windows =
        [
            new("1", "Glance", @"\\.\DISPLAY2", Eligible: false),
            new("2", "Chrome", @"\\.\DISPLAY1", Eligible: true),
            new("3", "Chrome", @"\\.\DISPLAY2", Eligible: true),
        ];

        var chosen = WindowFocusPolicy.ChooseTopmost(windows, @"\\.\DISPLAY2");

        Assert.Equal("3", chosen?.Id);
    }

    [Fact]
    public void TaskbarRule_KeepsAnOwnedAppWindow_AndDropsAToolWindow()
    {
        Assert.True(WindowFocusPolicy.IsTaskbarWindow(true, false, false, true, false, true, false, "Chrome_WidgetWin_1"));
        Assert.False(WindowFocusPolicy.IsTaskbarWindow(true, false, false, false, true, false, false, "tool"));
        Assert.False(WindowFocusPolicy.IsTaskbarWindow(true, false, false, false, false, false, true, "Glance"));
        Assert.False(WindowFocusPolicy.IsTaskbarWindow(true, true, false, false, false, false, false, "Notepad"));
    }
}
