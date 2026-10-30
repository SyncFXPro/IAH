using Glance.Core.Geometry;
using Glance.Tracking.Inference;
using OpenCvSharp;

namespace Glance.Tests;

public class ImageSpaceFeatureTests
{
    [Fact]
    public void FrontalFace_RecoversSmallAngles_AndKeepsIrisInsideTheEye()
    {
        var points = Project(yaw: 0, pitch: 0, shiftX: 0, 640, 480);
        PlaceIris(points, 0, 0);

        var extracted = ImageSpaceFeatures.TryExtract(points, 640, 480, out var features, out var head, out var failure);

        Assert.True(extracted, failure);
        Assert.Equal(0, features.IrisX, 1e-6);
        Assert.Equal(0, features.IrisY, 1e-6);
        Assert.True(Math.Abs(head.X) < 0.05);
        Assert.True(HeadPoseEstimator.TryEstimate(points, 640, 480, out var yaw, out var pitch, out _, out var poseFailure), poseFailure);
        Assert.Equal(0, yaw, 2);
        Assert.Equal(0, pitch, 2);
    }

    [Fact]
    public void SidewaysShift_ChangesFacePosition_MoreThanYaw()
    {
        var still = Project(yaw: 0, pitch: 0, shiftX: 0, 640, 480);
        var shifted = Project(yaw: 0, pitch: 0, shiftX: 48, 640, 480);
        PlaceIris(still, 0, 0);
        PlaceIris(shifted, 0, 0);

        Assert.True(ImageSpaceFeatures.TryExtract(still, 640, 480, out var stillFeatures, out var stillHead, out var stillFailure), stillFailure);
        Assert.True(ImageSpaceFeatures.TryExtract(shifted, 640, 480, out var shiftedFeatures, out var shiftedHead, out var shiftedFailure), shiftedFailure);

        Assert.Equal(stillFeatures.IrisX, shiftedFeatures.IrisX, 6);
        Assert.Equal(stillFeatures.IrisY, shiftedFeatures.IrisY, 6);
        Assert.True(Math.Abs(shiftedHead.X - stillHead.X) > 0.05);
        Assert.True(HeadPoseEstimator.TryEstimate(still, 640, 480, out var stillYaw, out _, out _, out var stillPose), stillPose);
        Assert.True(HeadPoseEstimator.TryEstimate(shifted, 640, 480, out var shiftedYaw, out _, out _, out var shiftedPose), shiftedPose);
        Assert.True(Math.Abs(shiftedYaw - stillYaw) < 0.15);
    }

    [Fact]
    public void IrisOffset_IsEachEyeDividedByItsOwnWidth_ThenAveraged()
    {
        var points = new float[FaceMeshIndices.Count * 3];
        SetEye(points, FaceMeshIndices.LeftEyeOuter, FaceMeshIndices.LeftEyeInner, FaceMeshIndices.LeftIrisStart, 80, 100, 120, 100, 92, 90);
        SetEye(points, FaceMeshIndices.RightEyeOuter, FaceMeshIndices.RightEyeInner, FaceMeshIndices.RightIrisStart, 200, 100, 160, 100, 172, 90);

        var extracted = ImageSpaceFeatures.TryExtract(points, 640, 480, out var features, out _, out var failure);

        Assert.True(extracted, failure);
        Assert.Equal(0.2, features.IrisX, 1e-6);
        Assert.Equal(0.25, features.IrisY, 1e-6);
    }

    [Fact]
    public void OneEyeOutOfRange_UsesTheOther()
    {
        var points = new float[FaceMeshIndices.Count * 3];
        SetEye(points, FaceMeshIndices.LeftEyeOuter, FaceMeshIndices.LeftEyeInner, FaceMeshIndices.LeftIrisStart, 80, 100, 120, 100, 100, 200);
        SetEye(points, FaceMeshIndices.RightEyeOuter, FaceMeshIndices.RightEyeInner, FaceMeshIndices.RightIrisStart, 200, 100, 160, 100, 172, 90);

        var extracted = ImageSpaceFeatures.TryExtract(points, 640, 480, out var features, out _, out var failure);

        Assert.True(extracted, failure);
        Assert.Equal(0.2, features.IrisX, 1e-6);
        Assert.Equal(0.25, features.IrisY, 1e-6);
    }

    [Fact]
    public void IrisPastTheEye_IsRejected()
    {
        var points = Project(yaw: 0, pitch: 0, shiftX: 0, 640, 480);
        PlaceIris(points, 0, -1.3);

        var extracted = ImageSpaceFeatures.TryExtract(points, 640, 480, out _, out _, out var failure);

        Assert.False(extracted);
        Assert.Equal("iris-out-of-range", failure);
    }

    private static float[] Project(double yaw, double pitch, double shiftX, int width, int height)
    {
        var look = new Point3d(
            Math.Sin(yaw) * Math.Cos(pitch),
            -Math.Sin(pitch) * Math.Cos(pitch),
            Math.Cos(yaw) * Math.Cos(pitch));
        var objectZ = new Point3d(-look.X, -look.Y, -look.Z);
        var up = new Point3d(0, -1, 0);
        var objectX = Normalize(Cross(up, objectZ));
        var objectY = Cross(objectZ, objectX);
        var focal = (double)Math.Max(width, height);
        var points = new float[FaceMeshIndices.Count * 3];
        for (var i = 0; i < HeadPoseEstimator.Model.Length; i++)
        {
            var model = HeadPoseEstimator.Model[i];
            var cameraX = objectX.X * model.X + objectY.X * model.Y + objectZ.X * model.Z + shiftX;
            var cameraY = objectX.Y * model.X + objectY.Y * model.Y + objectZ.Y * model.Z;
            var cameraZ = objectX.Z * model.X + objectY.Z * model.Y + objectZ.Z * model.Z + 700;
            Set(
                points,
                HeadPoseEstimator.Indices[i],
                (float)(focal * cameraX / cameraZ + width / 2.0),
                (float)(focal * cameraY / cameraZ + height / 2.0));
        }

        return points;
    }

    private static void PlaceIris(float[] points, double irisX, double irisY)
    {
        PlaceSubjectEye(points, FaceMeshIndices.LeftEyeOuter, FaceMeshIndices.LeftEyeInner, 40, FaceMeshIndices.LeftIrisStart, irisX, irisY);
        PlaceSubjectEye(points, FaceMeshIndices.RightEyeOuter, FaceMeshIndices.RightEyeInner, -40, FaceMeshIndices.RightIrisStart, irisX, irisY);
    }

    private static void PlaceSubjectEye(float[] points, int outer, int inner, float innerDx, int iris, double irisX, double irisY)
    {
        var ox = points[outer * 3];
        var oy = points[outer * 3 + 1];
        var midX = ox + innerDx / 2;
        Set(points, inner, ox + innerDx, oy);
        var width = Math.Abs(innerDx);
        Set(points, iris, (float)(midX - irisX * width), (float)(oy - irisY * width));
    }

    private static void SetEye(float[] points, int outer, int inner, int iris, float outerX, float outerY, float innerX, float innerY, float irisX, float irisY)
    {
        Set(points, outer, outerX, outerY);
        Set(points, inner, innerX, innerY);
        Set(points, iris, irisX, irisY);
    }

    private static Point3d Cross(Point3d a, Point3d b) =>
        new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    private static Point3d Normalize(Point3d value)
    {
        var length = Math.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z);
        return new Point3d(value.X / length, value.Y / length, value.Z / length);
    }

    private static void Set(float[] points, int index, float x, float y)
    {
        points[index * 3] = x;
        points[index * 3 + 1] = y;
    }
}
