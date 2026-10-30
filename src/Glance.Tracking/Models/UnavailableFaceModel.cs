using Glance.Core.Contracts;
using Glance.Core.Geometry;

namespace Glance.Tracking.Models;

/// <summary>
/// File names published by the upstream ONNX reference. They have not been loaded
/// with ONNX Runtime in this build, so they are labels only.
/// </summary>
public static class UpstreamFaceModel
{
    public const string SourceRepository = "https://github.com/yakhyo/mediapipe-face-mesh-onnx";

    public const string PreferredLandmarkerFile = "face_landmarker_Nx3x256x256.onnx";

    public const string DetectorFile = "face_detection_short_range.onnx";
}

public sealed class ModelNotConnectedException : NotSupportedException
{
    public ModelNotConnectedException()
        : base("No face landmark model is wired up. Image landmarks are not converted into a 3D gaze vector. See docs/MODEL.md.")
    {
    }
}

public sealed class UnavailableFaceLandmarkModel : IFaceLandmarkModel
{
    public string Name => "none";

    public bool IsLoaded => false;

    public void Load(string modelPath) => throw new ModelNotConnectedException();

    public void Dispose()
    {
    }
}

public sealed class UnavailableGazeFeatureExtractor : IGazeFeatureExtractor
{
    public bool TryExtract(
        in LandmarkObservation observation,
        out GazeFeatureVector features,
        out HeadTranslation head,
        out string? failure)
    {
        features = default;
        head = default;
        failure = "landmark-model-not-connected";
        return false;
    }
}
