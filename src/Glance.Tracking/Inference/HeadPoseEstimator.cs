using Glance.Core.Geometry;
using OpenCvSharp;

namespace Glance.Tracking.Inference;

/// <summary>
/// Head orientation from a rigid face model and SolvePnP.
/// This is not an eye-gaze ray. Yaw and pitch use the camera-frame formulas in math.md
/// on the head's forward axis only.
/// </summary>
public static class HeadPoseEstimator
{
    /// <summary>
    /// Subject's right is +X, up is +Y, and the face looks toward +Z.
    /// Units are arbitrary and only the shape matters.
    /// </summary>
    public static readonly Point3f[] Model =
    [
        new(0, 0, 0),
        new(0, -330, -65),
        new(-225, 170, -135),
        new(225, 170, -135),
        new(-150, -150, -125),
        new(150, -150, -125),
    ];

    public static readonly int[] Indices =
    [
        FaceMeshIndices.NoseTip,
        FaceMeshIndices.Chin,
        FaceMeshIndices.LeftEyeOuter,
        FaceMeshIndices.RightEyeOuter,
        FaceMeshIndices.MouthLeft,
        FaceMeshIndices.MouthRight,
    ];

    public static bool TryEstimate(
        ReadOnlySpan<float> landmarks,
        int imageWidth,
        int imageHeight,
        out double yawRadians,
        out double pitchRadians,
        out HeadTranslation head,
        out string? failure)
    {
        yawRadians = 0;
        pitchRadians = 0;
        head = default;
        failure = null;
        if (imageWidth < 2 || imageHeight < 2)
        {
            failure = "image-too-small";
            return false;
        }

        var image = new Point2f[Model.Length];
        for (var i = 0; i < Indices.Length; i++)
        {
            if (!TryPoint(landmarks, Indices[i], out var x, out var y))
            {
                failure = "head-landmarks-missing";
                return false;
            }

            image[i] = new Point2f((float)x, (float)y);
        }

        var focal = (double)Math.Max(imageWidth, imageHeight);
        using var camera = new Mat(3, 3, MatType.CV_64FC1, Scalar.All(0));
        camera.Set(0, 0, focal);
        camera.Set(1, 1, focal);
        camera.Set(0, 2, imageWidth / 2.0);
        camera.Set(1, 2, imageHeight / 2.0);
        camera.Set(2, 2, 1);
        using var distortion = new Mat(4, 1, MatType.CV_64FC1, Scalar.All(0));
        using var objectPoints = new Mat(Model.Length, 1, MatType.CV_32FC3);
        using var imagePoints = new Mat(image.Length, 1, MatType.CV_32FC2);
        for (var i = 0; i < Model.Length; i++)
        {
            objectPoints.Set(i, Model[i]);
            imagePoints.Set(i, image[i]);
        }

        using var rvec = new Mat();
        using var tvec = new Mat();
        Cv2.SolvePnP(objectPoints, imagePoints, camera, distortion, rvec, tvec, false, SolvePnPMethod.Iterative);
        var depth = tvec.At<double>(2);
        if (tvec.Empty() || !double.IsFinite(depth) || Math.Abs(depth) < 1e-6)
        {
            failure = "head-pose-failed";
            return false;
        }

        using var rotation = new Mat();
        Cv2.Rodrigues(rvec, rotation);
        var axisX = rotation.At<double>(0, 2);
        var axisY = rotation.At<double>(1, 2);
        var axisZ = rotation.At<double>(2, 2);
        var gazeX = -axisX;
        var gazeY = -axisY;
        var gazeZ = -axisZ;
        var planar = Math.Sqrt(gazeX * gazeX + gazeZ * gazeZ);
        yawRadians = Math.Atan2(gazeX, gazeZ);
        pitchRadians = Math.Atan2(-gazeY, planar);

        if (!TryPoint(landmarks, FaceMeshIndices.LeftEyeOuter, out var leftX, out var leftY)
            || !TryPoint(landmarks, FaceMeshIndices.RightEyeOuter, out var rightX, out var rightY)
            || !TryPoint(landmarks, FaceMeshIndices.NoseTip, out var noseX, out var noseY))
        {
            failure = "head-landmarks-missing";
            return false;
        }

        var eyeDistance = Math.Sqrt((rightX - leftX) * (rightX - leftX) + (rightY - leftY) * (rightY - leftY));
        if (eyeDistance < 1)
        {
            failure = "eyes-too-small";
            return false;
        }

        head = new HeadTranslation(
            (noseX - imageWidth / 2.0) / imageWidth,
            (noseY - imageHeight / 2.0) / imageHeight,
            eyeDistance / imageWidth);
        return true;
    }

    public static bool TryHeadArrow(ReadOnlySpan<float> landmarks, int imageWidth, int imageHeight, out DirectionArrow arrow)
    {
        arrow = default;
        if (!TryEstimate(landmarks, imageWidth, imageHeight, out var yawRadians, out var pitchRadians, out var head, out _)
            || !TryPoint(landmarks, FaceMeshIndices.NoseTip, out var noseX, out var noseY)
            || imageWidth < 2)
        {
            return false;
        }

        var length = Math.Max(head.Scale * imageWidth, 1);
        arrow = new DirectionArrow(
            noseX,
            noseY,
            Math.Sin(yawRadians) * length,
            -Math.Sin(pitchRadians) * length);
        return true;
    }

    private static bool TryPoint(ReadOnlySpan<float> landmarks, int index, out double x, out double y)
    {
        var offset = index * 3;
        if (offset + 1 >= landmarks.Length || !float.IsFinite(landmarks[offset]) || !float.IsFinite(landmarks[offset + 1]))
        {
            x = 0;
            y = 0;
            return false;
        }

        x = landmarks[offset];
        y = landmarks[offset + 1];
        return true;
    }
}
