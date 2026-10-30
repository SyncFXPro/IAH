using Glance.Core.Configuration;
using Glance.Tracking.Camera;
using Glance.Tracking.Models;
using OpenCvSharp;

namespace Glance.Tracking.Inference;

/// <summary>
/// One-shot preview loop: open a camera, run the detector and landmarker, and hand
/// each drawn frame to the caller. This is not the tracking pipeline.
/// </summary>
public static class WebcamLandmarkPreview
{
    public static void Run(
        int cameraIndex,
        string cameraName,
        int requestedWidth,
        int requestedHeight,
        CancellationToken cancellationToken,
        Action<FaceInspection> onFrame,
        Action<string> onStatus)
    {
        if (!ModelLocations.TryFind(UpstreamFaceModel.DetectorFile, out var detector)
            || !ModelLocations.TryFind(UpstreamFaceModel.PreferredLandmarkerFile, out var landmarker))
        {
            onStatus("The ONNX files are not next to the app or under assets/models. The dashboard stays on the scripted preview.");
            return;
        }

        OnnxFacePipeline? pipeline = null;
        VideoCapture? capture = null;
        try
        {
            onStatus("Loading the detector and the landmarker on the GPU.");
            pipeline = OnnxFacePipeline.Load(detector, landmarker);
            onStatus("Landmarker is using " + pipeline.ExecutionProvider + ".");
            cancellationToken.ThrowIfCancellationRequested();
            onStatus("Opening " + cameraName + ".");
            capture = OpenCamera(cameraIndex, requestedWidth, requestedHeight);
            if (capture is null)
            {
                onStatus("Could not open " + cameraName + ". Pick another camera and try again.");
                return;
            }

            using var frame = new Mat();
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!capture.Read(frame) || frame.Empty())
                {
                    onStatus("The webcam stopped returning frames.");
                    if (cancellationToken.WaitHandle.WaitOne(200))
                    {
                        break;
                    }

                    continue;
                }

                var inspection = pipeline.Inspect(frame);
                var delivered = CaptureResolution.Describe(
                    frame.Width,
                    frame.Height,
                    requestedWidth,
                    requestedHeight);
                inspection.Status = FormattableString.Invariant($"Capture {delivered}. {inspection.Status}");
                onFrame(inspection);
            }
        }
        finally
        {
            capture?.Dispose();
            pipeline?.Dispose();
        }
    }

    private static VideoCapture? OpenCamera(int cameraIndex, int requestedWidth, int requestedHeight)
    {
        VideoCapture? capture = null;
        try
        {
            capture = new VideoCapture(cameraIndex, VideoCaptureAPIs.DSHOW);
            if (!capture.IsOpened())
            {
                capture.Dispose();
                return null;
            }

            CameraCaptureSetup.Prepare(capture, requestedWidth, requestedHeight);
            return capture;
        }
        catch
        {
            capture?.Dispose();
            return null;
        }
    }
}
