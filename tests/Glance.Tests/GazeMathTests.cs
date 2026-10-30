using Glance.Core.Geometry;

namespace Glance.Tests;

public class GazeMathTests
{
    private const double Tolerance = 1e-9;

    [Fact]
    public void ForwardGaze_HasZeroYawAndPitch()
    {
        var angles = Angles(0, 0, 1);
        Assert.Equal(0, angles.Yaw, Tolerance);
        Assert.Equal(0, angles.Pitch, Tolerance);
    }

    [Fact]
    public void RightGaze_HasPositiveYaw()
    {
        var angles = Angles(1, 0, 0);
        Assert.Equal(Math.PI / 2, angles.Yaw, Tolerance);
        Assert.Equal(0, angles.Pitch, Tolerance);
    }

    [Fact]
    public void DownGaze_UsesCameraYDown_AndHasNegativePitch()
    {
        var angles = Angles(0, 1, 0);
        Assert.Equal(0, angles.Yaw, Tolerance);
        Assert.Equal(-Math.PI / 2, angles.Pitch, Tolerance);
    }

    [Fact]
    public void UpGaze_HasPositivePitch()
    {
        var angles = Angles(0, -1, 0);
        Assert.Equal(Math.PI / 2, angles.Pitch, Tolerance);
    }

    [Fact]
    public void DiagonalForward_MatchesAtan2Formulas()
    {
        var angles = Angles(0, 1, 1);
        Assert.Equal(0, angles.Yaw, Tolerance);
        Assert.Equal(-Math.PI / 4, angles.Pitch, Tolerance);

        var right = Angles(1, 0, 1);
        Assert.Equal(Math.PI / 4, right.Yaw, Tolerance);
        Assert.Equal(0, right.Pitch, Tolerance);
    }

    [Fact]
    public void ZeroVector_IsRejected()
    {
        Assert.False(GazeDirection.TryCreate(0, 0, 0, out _));
        Assert.False(GazeAngles.TryFromDirection(new GazeDirection(0, 0, 0), out _));
    }

    [Fact]
    public void PureVerticalGaze_ReportsYawZero_BecauseBothAtan2InputsAreZero()
    {
        var angles = Angles(0, -1, 0);
        Assert.Equal(0, angles.Yaw, Tolerance);
    }

    [Fact]
    public void SignedAngle_FromXAxisToYAxis_IsPositiveHalfPi()
    {
        Assert.True(SignedAngle.TryRadians(1, 0, 0, 1, out var radians));
        Assert.Equal(Math.PI / 2, radians, Tolerance);
    }

    [Fact]
    public void SignedAngle_OppositeDirection_IsNegative()
    {
        Assert.True(SignedAngle.TryRadians(1, 0, 0, -1, out var radians));
        Assert.Equal(-Math.PI / 2, radians, Tolerance);
    }

    [Fact]
    public void SignedAngle_PreservesTheSignThatAcosDrops()
    {
        Assert.True(SignedAngle.TryRadians(1, 0, 0, 1, out var left));
        Assert.True(SignedAngle.TryRadians(1, 0, 0, -1, out var right));
        Assert.Equal(Math.Abs(left), Math.Abs(right), Tolerance);
        Assert.NotEqual(Math.Sign(left), Math.Sign(right));
        Assert.Equal(-left, right, Tolerance);
    }

    [Fact]
    public void AngularRate_WrapsAcrossTheBranchCut()
    {
        Assert.True(AngularRate.TryRadiansPerSecond(3, -3, TimeSpan.FromSeconds(1), out var rate));
        Assert.InRange(rate, 0.2, 0.4);
        Assert.NotEqual(-6, rate, Tolerance);
    }

    [Fact]
    public void AngularRate_RejectsANonPositiveInterval()
    {
        Assert.False(AngularRate.TryRadiansPerSecond(0, 1, TimeSpan.Zero, out _));
    }

    [Fact]
    public void FeatureVelocity_DividesByElapsedTime_AndDoesNotUseSignedAngle()
    {
        var previous = new GazeFeatureVector(0, 0);
        var current = new GazeFeatureVector(1, 2);
        Assert.True(FeatureVelocity.TryCompute(previous, current, TimeSpan.FromSeconds(0.5), out var velocity));
        Assert.Equal(2, velocity.IrisX, Tolerance);
        Assert.Equal(4, velocity.IrisY, Tolerance);
    }

    private static GazeAngles.Radians Angles(double x, double y, double z)
    {
        Assert.True(GazeDirection.TryCreate(x, y, z, out var direction));
        Assert.True(GazeAngles.TryFromDirection(direction, out var angles));
        return angles;
    }
}
