using Glance.Core.Contracts;
using Glance.Core.Geometry;

namespace Glance.Tracking.Inference;

/// <summary>
/// An arrow in the camera image. Delta is in pixels. Positive X is right and positive Y is down.
/// </summary>
public readonly record struct DirectionArrow(double OriginX, double OriginY, double DeltaX, double DeltaY);

/// <summary>
/// Head yaw and pitch come from SolvePnP. They are radians and they are not an eye-gaze ray.
/// Each eye's iris offset is divided by that eye's own corner-to-corner width, then the valid eyes are averaged. Positive X is the person's right. Positive Y is up. The preview arrow ends on the iris landmark. Head pose is not part of the vector.
/// Face position and scale are returned separately and are not part of the feature vector.
/// </summary>
public static class ImageSpaceFeatures
{
    public const double MaximumIrisOffset = 0.65;

    public static bool TryExtract(
        ReadOnlySpan<float> landmarks,
        int imageWidth,
        int imageHeight,
        out GazeFeatureVector features,
        out HeadTranslation head,
        out string? failure)
    {
        features = default;
        head = default;
        if (landmarks.Length < FaceMeshIndices.Count * 3 || imageWidth < 2 || imageHeight < 2)
        {
            failure = "landmarks-missing";
            return false;
        }

        if (!TryCombineIris(landmarks, out var irisX, out var irisY, out failure))
        {
            return false;
        }

        if (HeadPoseEstimator.TryEstimate(landmarks, imageWidth, imageHeight, out _, out _, out var pose, out _))
        {
            head = pose;
        }

        features = new GazeFeatureVector(irisX, irisY);
        failure = null;
        return true;
    }

    public static bool TryEyeArrow(ReadOnlySpan<float> landmarks, bool subjectLeft, out DirectionArrow arrow)
    {
        arrow = default;
        var outer = subjectLeft ? FaceMeshIndices.LeftEyeOuter : FaceMeshIndices.RightEyeOuter;
        var inner = subjectLeft ? FaceMeshIndices.LeftEyeInner : FaceMeshIndices.RightEyeInner;
        var iris = subjectLeft ? FaceMeshIndices.LeftIrisStart : FaceMeshIndices.RightIrisStart;
        if (!TryEye(landmarks, outer, inner, iris, out var centerX, out var centerY, out var irisX, out var irisY, out _))
        {
            return false;
        }

        arrow = new DirectionArrow(centerX, centerY, irisX - centerX, irisY - centerY);
        return true;
    }

    private static bool TryCombineIris(ReadOnlySpan<float> landmarks, out double irisX, out double irisY, out string? failure)
    {
        irisX = irisY = 0;
        var left = TryEye(
            landmarks,
            FaceMeshIndices.LeftEyeOuter,
            FaceMeshIndices.LeftEyeInner,
            FaceMeshIndices.LeftIrisStart,
            out var leftCenterX,
            out var leftCenterY,
            out var leftIrisX,
            out var leftIrisY,
            out var leftWidth);
        var right = TryEye(
            landmarks,
            FaceMeshIndices.RightEyeOuter,
            FaceMeshIndices.RightEyeInner,
            FaceMeshIndices.RightIrisStart,
            out var rightCenterX,
            out var rightCenterY,
            out var rightIrisX,
            out var rightIrisY,
            out var rightWidth);
        if (!left && !right)
        {
            failure = "iris-missing";
            return false;
        }

        var leftOk = left && InsideEye(leftIrisX - leftCenterX, leftIrisY - leftCenterY, leftWidth);
        var rightOk = right && InsideEye(rightIrisX - rightCenterX, rightIrisY - rightCenterY, rightWidth);
        double sumX = 0;
        double sumY = 0;
        var count = 0;
        if (leftOk)
        {
            sumX += -(leftIrisX - leftCenterX) / leftWidth;
            sumY += -(leftIrisY - leftCenterY) / leftWidth;
            count++;
        }

        if (rightOk)
        {
            sumX += -(rightIrisX - rightCenterX) / rightWidth;
            sumY += -(rightIrisY - rightCenterY) / rightWidth;
            count++;
        }

        if (count == 0)
        {
            failure = "iris-out-of-range";
            return false;
        }

        irisX = sumX / count;
        irisY = sumY / count;
        failure = null;
        return true;
    }

    private static bool TryEye(
        ReadOnlySpan<float> landmarks,
        int outer,
        int inner,
        int iris,
        out double centerX,
        out double centerY,
        out double irisX,
        out double irisY,
        out double width)
    {
        centerX = centerY = irisX = irisY = width = 0;
        if (!TryPoint(landmarks, outer, out var outerX, out var outerY)
            || !TryPoint(landmarks, inner, out var innerX, out var innerY)
            || !TryPoint(landmarks, iris, out irisX, out irisY))
        {
            return false;
        }

        var dx = innerX - outerX;
        var dy = innerY - outerY;
        width = Math.Sqrt(dx * dx + dy * dy);
        if (width < 1)
        {
            return false;
        }

        centerX = (outerX + innerX) / 2.0;
        centerY = (outerY + innerY) / 2.0;
        return true;
    }

    private static bool InsideEye(double offsetX, double offsetY, double width) =>
        offsetX * offsetX + offsetY * offsetY <= MaximumIrisOffset * MaximumIrisOffset * width * width;

    private static bool TryPoint(ReadOnlySpan<float> landmarks, int index, out double x, out double y)
    {
        var offset = index * 3;
        x = landmarks[offset];
        y = landmarks[offset + 1];
        return float.IsFinite(landmarks[offset]) && float.IsFinite(landmarks[offset + 1]);
    }
}

public sealed class ImageSpaceGazeFeatureExtractor : IGazeFeatureExtractor
{
    public bool TryExtract(
        in LandmarkObservation observation,
        out GazeFeatureVector features,
        out HeadTranslation head,
        out string? failure)
    {
        features = default;
        head = default;
        if (observation.Points is null)
        {
            failure = "landmarks-missing";
            return false;
        }

        return ImageSpaceFeatures.TryExtract(
            observation.Points,
            observation.ImageWidth,
            observation.ImageHeight,
            out features,
            out head,
            out failure);
    }
}
