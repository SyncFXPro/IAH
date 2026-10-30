using OpenCvSharp;

namespace Glance.Tracking.Camera;

public static class CameraCaptureSetup
{
    public static void Prepare(VideoCapture capture, int requestedWidth, int requestedHeight)
    {
        capture.Set(VideoCaptureProperties.BufferSize, 1);
        if (requestedWidth > 0 && requestedHeight > 0)
        {
            capture.Set(VideoCaptureProperties.FrameWidth, requestedWidth);
            capture.Set(VideoCaptureProperties.FrameHeight, requestedHeight);
        }
    }
}
