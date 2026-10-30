using Glance.Tracking.Inference;
using Glance.Tracking.Models;
using OpenCvSharp;

namespace Glance.Tests;

public class OnnxModelContractTests
{
    [Fact]
    public void MissingFile_IsNotFound()
    {
        Assert.False(ModelLocations.TryFind("not-a-real-model.onnx", out _));
    }

    [Fact]
    public void BothSessions_AcceptAZeroTensor_AndReturnTheCheckedShapes()
    {
        using var pipeline = TryLoad();
        if (pipeline is null)
        {
            return;
        }

        Assert.Equal("GPU (DirectML)", pipeline.ExecutionProvider);
        var shapes = pipeline.RunZeroInputs();
        Assert.Equal(new[] { 1, 896, 16 }, shapes.Regressors);
        Assert.Equal(new[] { 1, 896, 1 }, shapes.Scores);
        Assert.Equal(new[] { 1, 478, 3 }, shapes.Landmarks);
        Assert.True(shapes.Score is [1, 1] or [1, 1, 1]);
    }

    [Fact]
    public void BlankFrame_RunsDetectionAndReturnsAnImage()
    {
        using var pipeline = TryLoad();
        if (pipeline is null)
        {
            return;
        }

        using var image = new Mat(480, 640, MatType.CV_8UC3, Scalar.All(0));
        var inspection = pipeline.Inspect(image);
        Assert.Equal(640, inspection.Width);
        Assert.Equal(480, inspection.Height);
        Assert.Equal(640 * 480 * 4, inspection.Bgra.Length);
        Assert.Contains("Not gaze", inspection.Status, StringComparison.Ordinal);
    }

    private static OnnxFacePipeline? TryLoad()
    {
        if (!ModelLocations.TryFind(UpstreamFaceModel.DetectorFile, out var detector)
            || !ModelLocations.TryFind(UpstreamFaceModel.PreferredLandmarkerFile, out var landmarker))
        {
            return null;
        }

        return OnnxFacePipeline.Load(detector, landmarker);
    }
}
