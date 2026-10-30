using Glance.Core.Contracts;
using Glance.Core.Geometry;
using Glance.Tracking.Camera;
using Glance.Tracking.Models;
using OpenCvSharp;

namespace Glance.Tracking.Inference;

public sealed record LiveFaceReading(
    string CameraName,
    int FaceCount,
    float DetectorScore,
    float Presence,
    float PresenceLogit,
    int LandmarkCount,
    GazeFeatureVector? Features,
    HeadTranslation? Head,
    double? HeadYawRadians,
    double? HeadPitchRadians,
    string? FeatureNote,
    double InspectMilliseconds,
    int FrameWidth,
    int FrameHeight,
    long Sequence,
    MonotonicTimestamp Timestamp);

/// <summary>
/// One camera thread. It keeps the newest measurement and drops the rest.
/// </summary>
public sealed class LiveFaceTracker : IDisposable
{
    private readonly IGazeFeatureExtractor _features;
    private readonly object _gate = new();
    private CancellationTokenSource? _cancellation;
    private Thread? _thread;
    private int _cameraIndex = -1;
    private int _captureWidth;
    private int _captureHeight;
    private long _sequence;
    private bool _disposed;

    public LiveFaceTracker(IGazeFeatureExtractor features) => _features = features;

    public LiveFaceReading? Latest { get; private set; }

    public string? Fault { get; private set; }

    public string ExecutionProvider { get; private set; } = "not loaded";

    public void EnsureRunning(int cameraIndex, string cameraName, int captureWidth, int captureHeight)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            if (_cameraIndex == cameraIndex
                && _captureWidth == captureWidth
                && _captureHeight == captureHeight
                && _thread is { IsAlive: true })
            {
                return;
            }
        }

        Stop();
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _cameraIndex = cameraIndex;
            _captureWidth = captureWidth;
            _captureHeight = captureHeight;
            Latest = null;
            Fault = null;
            _cancellation = new CancellationTokenSource();
            var token = _cancellation.Token;
            _thread = new Thread(() => Run(cameraIndex, cameraName, captureWidth, captureHeight, token))
            {
                IsBackground = true,
                Name = "Glance.LiveFace",
            };
            _thread.Start();
        }
    }

    public void Read(out LiveFaceReading? latest, out string? fault)
    {
        lock (_gate)
        {
            latest = Latest;
            fault = Fault;
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cancellation;
        Thread? thread;
        lock (_gate)
        {
            cancellation = _cancellation;
            thread = _thread;
            _cancellation = null;
            _thread = null;
            _cameraIndex = -1;
        }

        cancellation?.Cancel();
        thread?.Join(TimeSpan.FromSeconds(2));
        cancellation?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }

    private void Run(
        int cameraIndex,
        string cameraName,
        int captureWidth,
        int captureHeight,
        CancellationToken cancellationToken)
    {
        if (!ModelLocations.TryFind(UpstreamFaceModel.DetectorFile, out var detector)
            || !ModelLocations.TryFind(UpstreamFaceModel.PreferredLandmarkerFile, out var landmarker))
        {
            PublishFault("The ONNX files are missing, so tracking stays on the scripted preview.");
            return;
        }

        OnnxFacePipeline? pipeline = null;
        VideoCapture? capture = null;
        try
        {
            pipeline = OnnxFacePipeline.Load(detector, landmarker);
            ExecutionProvider = pipeline.ExecutionProvider;
            capture = Open(cameraIndex, captureWidth, captureHeight);
            if (capture is null)
            {
                PublishFault("Could not open " + cameraName + ".");
                return;
            }

            using var frame = new Mat();
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!capture.Read(frame) || frame.Empty())
                {
                    PublishFault(cameraName + " stopped returning frames.");
                    if (cancellationToken.WaitHandle.WaitOne(200))
                    {
                        break;
                    }

                    continue;
                }

                var measurement = pipeline.Measure(frame);
                GazeFeatureVector? features = null;
                HeadTranslation? head = null;
                double? headYaw = null;
                double? headPitch = null;
                string? note = null;
                var count = 0;
                if (measurement.Landmarks is not null)
                {
                    if (HeadPoseEstimator.TryEstimate(
                            measurement.Landmarks,
                            frame.Width,
                            frame.Height,
                            out var yawRadians,
                            out var pitchRadians,
                            out var pose,
                            out _))
                    {
                        head = pose;
                        headYaw = yawRadians;
                        headPitch = pitchRadians;
                    }

                    var observation = new LandmarkObservation(
                        UpstreamFaceModel.PreferredLandmarkerFile,
                        FaceMeshIndices.Count,
                        true,
                        measurement.Presence,
                        measurement.Landmarks,
                        frame.Width,
                        frame.Height,
                        measurement.FaceCount);
                    if (_features.TryExtract(in observation, out var vector, out var translation, out var failure))
                    {
                        features = vector;
                        head = translation;
                        count = FaceMeshIndices.Count;
                    }
                    else
                    {
                        note = failure;
                    }
                }

                lock (_gate)
                {
                    _sequence++;
                    Latest = new LiveFaceReading(
                        cameraName,
                        measurement.FaceCount,
                        measurement.DetectorScore,
                        measurement.Presence,
                        measurement.PresenceLogit,
                        count,
                        features,
                        head,
                        headYaw,
                        headPitch,
                        note,
                        measurement.Milliseconds,
                        frame.Width,
                        frame.Height,
                        _sequence,
                        MonotonicTimestamp.Now);
                    Fault = null;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            PublishFault(ex.Message);
        }
        finally
        {
            capture?.Dispose();
            pipeline?.Dispose();
        }
    }

    private void PublishFault(string fault)
    {
        lock (_gate)
        {
            Fault = fault;
        }
    }

    private static VideoCapture? Open(int cameraIndex, int captureWidth, int captureHeight)
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

            CameraCaptureSetup.Prepare(capture, captureWidth, captureHeight);
            return capture;
        }
        catch
        {
            capture?.Dispose();
            return null;
        }
    }
}
