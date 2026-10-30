using Microsoft.ML.OnnxRuntime;

namespace Glance.Tracking.Inference;

/// <summary>
/// Refuses to run if the loaded graph does not match the tensor names and ranks
/// checked for these two files. A zero-tensor smoke test asserts the concrete shapes.
/// </summary>
public static class OnnxTensorContract
{
    public static void RequireDetector(InferenceSession session)
    {
        RequireInput(session, BlazeFaceAnchors.InputSize);
        RequireRank3(session, "regressors", BlazeFaceAnchors.Count, BlazeFaceDecoder.RegressorWidth);
        RequireRank3(session, "scores", BlazeFaceAnchors.Count, 1);
    }

    public static void RequireLandmarker(InferenceSession session)
    {
        RequireInput(session, FaceRoi.LandmarkerSize);
        RequireRank3(session, "landmarks", FaceMeshIndices.Count, 3);
        if (!session.OutputMetadata.TryGetValue("score", out var score))
        {
            throw new InvalidDataException("The landmarker graph is missing output score.");
        }

        var dims = score.Dimensions;
        var isBatchAndOne = dims.Length == 2 && IsDim(dims[1], 1);
        var isBatchOneOne = dims.Length == 3 && IsDim(dims[1], 1) && IsDim(dims[2], 1);
        if (score.ElementType != typeof(float) || (!isBatchAndOne && !isBatchOneOne))
        {
            throw new InvalidDataException("The landmarker score output is not a float logit of shape [batch, 1].");
        }
    }

    private static void RequireInput(InferenceSession session, int size)
    {
        if (!session.InputMetadata.TryGetValue("input", out var input))
        {
            throw new InvalidDataException("The ONNX graph has no input named input.");
        }

        var dims = input.Dimensions;
        if (input.ElementType != typeof(float)
            || dims.Length != 4
            || dims[1] != 3
            || dims[2] != size
            || dims[3] != size)
        {
            throw new InvalidDataException("The ONNX input is not float32 NCHW at the checked size.");
        }
    }

    private static void RequireRank3(InferenceSession session, string name, int dim1, int dim2)
    {
        if (!session.OutputMetadata.TryGetValue(name, out var output))
        {
            throw new InvalidDataException("The ONNX graph is missing output " + name + ".");
        }

        var dims = output.Dimensions;
        if (output.ElementType != typeof(float)
            || dims.Length != 3
            || !IsDim(dims[1], dim1)
            || !IsDim(dims[2], dim2))
        {
            throw new InvalidDataException(FormattableString.Invariant(
                $"The ONNX output {name} does not have the checked shape. Rank {dims.Length}, dims [{string.Join(",", dims)}]."));
        }
    }

    /// <summary>DirectML leaves fixed output sizes as -1 until a tensor is produced.</summary>
    private static bool IsDim(int actual, int expected) => actual == expected || actual < 0;
}
