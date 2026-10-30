using Glance.Core.Contracts;

namespace Glance.Tracking.Camera;

public sealed class CameraCaptureUnavailableException : InvalidOperationException
{
    public CameraCaptureUnavailableException()
        : base("Camera capture is not implemented in this build. No frames are read from a webcam.")
    {
    }
}

public sealed class UnavailableCameraCapture : ICameraCapture
{
    public IReadOnlyList<CameraDescriptor> ListCameras() => [];

    public bool IsCapturing => false;

    public void Start(string? deviceId, CancellationToken cancellationToken) =>
        throw new CameraCaptureUnavailableException();

    public void Stop()
    {
    }

    public void Dispose()
    {
    }
}
