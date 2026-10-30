using Glance.Core.Configuration;
using Glance.Core.Contracts;
using Glance.Core.Diagnostics;
using Glance.Core.Geometry;
using Glance.Core.Switching;
using Glance.Windows.Cursor;

namespace Glance.Tests;

public class SwitchingAndLayoutTests
{
    [Fact]
    public void FastFeatureChange_DoesNotConfirmUntilThePositionSettles()
    {
        var machine = new MonitorSwitchStateMachine(new SwitchPolicy
        {
            Dwell = TimeSpan.FromMilliseconds(100),
            Cooldown = TimeSpan.Zero,
            MaximumFeatureSpeed = 2,
        });

        var first = machine.Evaluate(Observation(0, "A", speed: 0));
        var locked = machine.Evaluate(Observation(150, "A", speed: 0));
        var moving = machine.Evaluate(Observation(200, "B", speed: 10));
        var stillMoving = machine.Evaluate(Observation(400, "B", speed: 10));
        var settledStart = machine.Evaluate(Observation(500, "B", speed: 0));
        var tooSoon = machine.Evaluate(Observation(550, "B", speed: 0));
        var confirmed = machine.Evaluate(Observation(700, "B", speed: 0));

        Assert.Equal(SwitchState.TransitionCandidate, first.State);
        Assert.True(locked.SwitchNow);
        Assert.Equal("A", locked.SwitchToMonitorId);
        Assert.False(moving.SwitchNow);
        Assert.Equal("A", moving.CurrentMonitorId);
        Assert.Equal("B", moving.CandidateMonitorId);
        Assert.False(stillMoving.SwitchNow);
        Assert.Equal(SwitchState.TransitionCandidate, settledStart.State);
        Assert.False(tooSoon.SwitchNow);
        Assert.True(confirmed.SwitchNow);
        Assert.Equal("B", confirmed.SwitchToMonitorId);
    }

    [Fact]
    public void PositionClassifier_IsTheOnlyDestination()
    {
        var machine = new MonitorSwitchStateMachine(new SwitchPolicy
        {
            Dwell = TimeSpan.FromMilliseconds(50),
            Cooldown = TimeSpan.Zero,
        });

        machine.Evaluate(Observation(0, "B", speed: 0));
        var decision = machine.Evaluate(Observation(80, "B", speed: 0));

        Assert.Equal("B", decision.SwitchToMonitorId);
    }

    [Fact]
    public void InputSuppression_BlocksConfirmation()
    {
        var machine = new MonitorSwitchStateMachine(new SwitchPolicy
        {
            Dwell = TimeSpan.FromMilliseconds(50),
            Cooldown = TimeSpan.Zero,
        });
        var suppressedUntil = MonotonicTimestamp.FromElapsed(TimeSpan.FromMilliseconds(500));

        var decision = machine.Evaluate(new SwitchObservation(
            MonotonicTimestamp.FromElapsed(TimeSpan.FromMilliseconds(200)),
            true,
            "A",
            true,
            0,
            suppressedUntil));

        Assert.Equal(SwitchState.InputSuppressed, decision.State);
        Assert.False(decision.SwitchNow);
    }

    [Fact]
    public void Layout_DrawsAHigherMonitorAbove_WithoutBecomingAGazeAngle()
    {
        PixelRect[] rects =
        [
            new(0, 0, 100, 100),
            new(0, -200, 100, 100),
        ];

        var drawn = LayoutNormalizer.Normalize(rects, 400, 400, 20);

        Assert.True(drawn[1].Y < drawn[0].Y);
    }

    [Fact]
    public void Clamp_SupportsANegativeVirtualDesktop()
    {
        var work = new PixelRect(-1920, 0, 1920, 1080);
        var clamped = work.Clamp(-5000, 5000);
        Assert.Equal((-1920, 1079), clamped);
    }

    [Fact]
    public void CursorPolicy_BlocksAnythingOtherThanARealWebcamClassifier()
    {
        var reason = CursorRelocationPolicy.BlockReason(new CursorRelocationRequest(
            UserEnabled: true,
            PipelineIsRealWebcam: false,
            AlreadyOnMonitor: false,
            TrackingHealthy: true));

        Assert.NotNull(reason);
    }

    [Fact]
    public void CursorRelocation_ClampsWithAFakeNative_AndDoesNotRequireTheRealPointer()
    {
        var native = new FakeCursor();
        var displays = new FakeDisplays(new DisplayInfo(
            @"\\.\DISPLAY2",
            "Left",
            false,
            new PixelRect(-100, -50, 200, 100),
            new PixelRect(-100, -40, 200, 80),
            96,
            96));
        var service = new WindowsCursorService(native, displays);

        var moved = service.TryRelocate(
            @"\\.\DISPLAY2",
            new CursorRelocationRequest(true, true, false, true),
            out var reason);

        Assert.True(moved);
        Assert.Null(reason);
        Assert.Equal(new PixelPoint(0, 0), native.LastSet);
    }

    [Fact]
    public void LatencyRecorder_ReportsNearestRankPercentiles_AndStaysEmptyWithoutSamples()
    {
        var recorder = new StageLatencyRecorder();
        Assert.Null(recorder.Summarize(PipelineStage.LandmarkInference));

        for (var i = 1; i <= 100; i++)
        {
            recorder.AddMilliseconds(PipelineStage.LandmarkInference, i);
        }

        var summary = recorder.Summarize(PipelineStage.LandmarkInference);
        Assert.NotNull(summary);
        Assert.Equal(100, summary.Count);
        Assert.Equal(50, summary.P50Milliseconds);
        Assert.Equal(95, summary.P95Milliseconds);
        Assert.Equal(99, summary.P99Milliseconds);
    }

    [Fact]
    public void StartupCommand_QuotesTheExecutable()
    {
        Assert.Equal("\"C:\\Glance\\Glance.exe\" --background", StartupCommand.Format(@"C:\Glance\Glance.exe"));
    }

    [Fact]
    public void ConnectedDisplays_HavePositiveBounds()
    {
        using var displays = new Glance.Windows.Displays.WindowsDisplayService();
        var found = displays.GetDisplays();
        Assert.NotEmpty(found);
        Assert.Contains(found, display => display.IsPrimary);
        Assert.All(found, display => Assert.True(display.Bounds.Width > 0 && display.Bounds.Height > 0));
    }

    private static SwitchObservation Observation(double milliseconds, string monitor, double speed) =>
        new(
            MonotonicTimestamp.FromElapsed(TimeSpan.FromMilliseconds(milliseconds)),
            true,
            monitor,
            true,
            speed,
            null);

    private sealed class FakeCursor : ICursorNative
    {
        public PixelPoint LastSet { get; private set; }

        public PixelPoint GetPosition() => new(0, 0);

        public bool SetPosition(int x, int y)
        {
            LastSet = new PixelPoint(x, y);
            return true;
        }
    }

    private sealed class FakeDisplays : IDisplayService
    {
        private readonly DisplayInfo[] _displays;

        public FakeDisplays(params DisplayInfo[] displays) => _displays = displays;

        public event EventHandler? DisplaysChanged
        {
            add { }
            remove { }
        }

        public IReadOnlyList<DisplayInfo> GetDisplays() => _displays;
    }
}
