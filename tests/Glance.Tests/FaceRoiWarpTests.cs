using Glance.Tracking.Inference;
using OpenCvSharp;

namespace Glance.Tests;

public class FaceRoiWarpTests
{
    private const double Tolerance = 1e-6;

    [Fact]
    public void Roi_IsOneAndAHalfTimesTheLongSide_AndRollFollowsTheEyes()
    {
        var roi = FaceRoi.FromBox(0, 0, 40, 20, new ImagePoint(0, 0), new ImagePoint(10, 10));

        Assert.Equal(60, roi.Side, 1e-4);
        Assert.Equal(20, roi.CenterX, 1e-4);
        Assert.Equal(10, roi.CenterY, 1e-4);
        Assert.Equal(45, roi.AngleDegrees, 1e-3);
    }

    [Fact]
    public void InverseWarp_OfTheCropCenter_LandsOnTheRoiCenter()
    {
        const double centerX = 100;
        const double centerY = 80;
        const double side = 40;
        const int output = 256;
        var forward = Affine2x3.Crop(centerX, centerY, side, angleDegrees: 30, output);
        var cropCenter = forward.Invert().Apply(output / 2.0, output / 2.0);
        var imageCenter = forward.Apply(centerX, centerY);

        Assert.Equal(centerX, cropCenter.X, Tolerance);
        Assert.Equal(centerY, cropCenter.Y, Tolerance);
        Assert.Equal(output / 2.0, imageCenter.X, Tolerance);
        Assert.Equal(output / 2.0, imageCenter.Y, Tolerance);
    }

    [Fact]
    public void LandmarkZ_ScalesByRoiSideOverInputSize()
    {
        var forward = Affine2x3.Crop(10, 20, side: 40, angleDegrees: 0, outputSize: 256);
        var points = new float[3];
        points[0] = 128;
        points[1] = 128;
        points[2] = 10;

        forward.MapLandmarks(points, roiSide: 40, inputSize: 256);

        Assert.Equal(10, points[0], 1e-4);
        Assert.Equal(20, points[1], 1e-4);
        Assert.Equal(10f * 40f / 256f, points[2], 1e-4);
    }

    [Fact]
    public void CropMatrix_MatchesOpenCv_ForAnOffCenterPoint()
    {
        const double centerX = 40;
        const double centerY = 20;
        const double side = 20;
        const int output = 32;
        const double angle = 15;
        var forward = Affine2x3.Crop(centerX, centerY, side, angle, output);
        var scale = output / side;
        using var opencv = Cv2.GetRotationMatrix2D(new Point2f((float)centerX, (float)centerY), angle, scale);
        opencv.Set(0, 2, opencv.At<double>(0, 2) + output / 2.0 - centerX);
        opencv.Set(1, 2, opencv.At<double>(1, 2) + output / 2.0 - centerY);

        AssertNear(opencv.At<double>(0, 0), forward.M00);
        AssertNear(opencv.At<double>(0, 1), forward.M01);
        AssertNear(opencv.At<double>(0, 2), forward.M02);
        AssertNear(opencv.At<double>(1, 0), forward.M10);
        AssertNear(opencv.At<double>(1, 1), forward.M11);
        AssertNear(opencv.At<double>(1, 2), forward.M12);

        var imagePoint = (X: centerX + 8, Y: centerY - 3);
        var ours = forward.Apply(imagePoint.X, imagePoint.Y);
        var theirs = Apply(opencv, imagePoint.X, imagePoint.Y);
        AssertNear(theirs.X, ours.X);
        AssertNear(theirs.Y, ours.Y);
        Assert.NotEqual(output / 2.0, ours.X);
        Assert.NotEqual(output / 2.0, ours.Y);
    }

    [Fact]
    public void PositiveAngle_MovesAPointRightOfCenterUpward()
    {
        const double centerX = 100;
        const double centerY = 80;
        const int output = 256;
        var forward = Affine2x3.Crop(centerX, centerY, side: output, angleDegrees: 90, output);
        var right = forward.Apply(centerX + 10, centerY);

        Assert.Equal(output / 2.0, right.X, 1e-9);
        Assert.Equal(output / 2.0 - 10, right.Y, 1e-9);
        Assert.True(right.Y < output / 2.0);
    }

    [Fact]
    public void WarpAffine_KeepsTheMarkedCenterOnTheCropCenter()
    {
        const double centerX = 40;
        const double centerY = 20;
        const int output = 32;
        var forward = Affine2x3.Crop(centerX, centerY, side: 20, angleDegrees: 15, output);
        using var image = new Mat(64, 64, MatType.CV_8UC3, Scalar.All(0));
        Cv2.Circle(image, new Point((int)centerX, (int)centerY), 2, Scalar.All(255), -1);
        using var matrix = new Mat(2, 3, MatType.CV_64FC1);
        matrix.Set(0, 0, forward.M00);
        matrix.Set(0, 1, forward.M01);
        matrix.Set(0, 2, forward.M02);
        matrix.Set(1, 0, forward.M10);
        matrix.Set(1, 1, forward.M11);
        matrix.Set(1, 2, forward.M12);
        using var crop = new Mat();
        Cv2.WarpAffine(image, crop, matrix, new Size(output, output), InterpolationFlags.Linear, BorderTypes.Constant, Scalar.All(0));

        var center = crop.At<Vec3b>(output / 2, output / 2);
        Assert.True(center.Item0 > 200);
    }

    private static (double X, double Y) Apply(Mat matrix, double x, double y) =>
        (
            matrix.At<double>(0, 0) * x + matrix.At<double>(0, 1) * y + matrix.At<double>(0, 2),
            matrix.At<double>(1, 0) * x + matrix.At<double>(1, 1) * y + matrix.At<double>(1, 2));

    private static void AssertNear(double expected, double actual) =>
        Assert.Equal(expected, actual, 1e-9);
}
