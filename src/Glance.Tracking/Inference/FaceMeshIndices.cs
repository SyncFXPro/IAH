namespace Glance.Tracking.Inference;

/// <summary>
/// MediaPipe 478-point layout. Iris indices are the published hypothesis and must be
/// checked on the landmark overlay before any feature math uses them.
/// </summary>
public static class FaceMeshIndices
{
    public const int Count = 478;

    public const int NoseTip = 1;

    public const int Chin = 152;

    public const int MouthLeft = 61;

    public const int MouthRight = 291;

    public const int LeftEyeOuter = 33;

    public const int LeftEyeInner = 133;

    public const int RightEyeInner = 263;

    public const int RightEyeOuter = 362;

    public const int LeftLidUpper = 159;

    public const int LeftLidLower = 145;

    public const int RightLidUpper = 386;

    public const int RightLidLower = 374;

    public const int LeftIrisStart = 468;

    public const int RightIrisStart = 473;

    public const int IrisPointsPerEye = 5;

    public static readonly int[] EyeCornerHypothesis = [33, 133, 263, 362];

    public static bool IsIris(int index) =>
        index >= LeftIrisStart && index < RightIrisStart + IrisPointsPerEye;
}
