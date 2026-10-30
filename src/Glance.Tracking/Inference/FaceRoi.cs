namespace Glance.Tracking.Inference;

/// <summary>
/// Square crop around a detector box. Side is 1.5 times the long side.
/// Roll is the angle of detector eye keypoints 0 and 1, in degrees.
/// </summary>
public readonly record struct FaceRoi(float CenterX, float CenterY, float Side, float AngleDegrees)
{
    public const float Margin = 0.25f;

    public const int LandmarkerSize = 256;

    public static FaceRoi FromBox(
        float x1,
        float y1,
        float x2,
        float y2,
        ImagePoint? eye0,
        ImagePoint? eye1,
        float margin = Margin)
    {
        var side = (1f + 2f * margin) * MathF.Max(x2 - x1, y2 - y1);
        var angle = 0f;
        if (eye0 is ImagePoint first && eye1 is ImagePoint second)
        {
            angle = MathF.Atan2(second.Y - first.Y, second.X - first.X) * (180f / MathF.PI);
        }

        return new FaceRoi((x1 + x2) / 2f, (y1 + y2) / 2f, side, angle);
    }

    public static FaceRoi FromFace(DetectedFace face) =>
        FromBox(
            face.X1,
            face.Y1,
            face.X2,
            face.Y2,
            face.Keypoints.Length > 1 ? face.Keypoints[0] : null,
            face.Keypoints.Length > 1 ? face.Keypoints[1] : null);
}
