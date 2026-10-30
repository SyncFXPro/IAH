using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace Glance.Tracking.Inference;

public readonly record struct FaceMeasurement(
    int FaceCount,
    float DetectorScore,
    float Presence,
    float PresenceLogit,
    float[]? Landmarks,
    double Milliseconds);

public sealed class FaceInspection
{
    public required byte[] Bgra { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public required int FaceCount { get; init; }

    public required float TopDetectionScore { get; init; }

    public required float TopPresence { get; init; }

    public required float TopPresenceLogit { get; init; }

    public required int LandmarkCount { get; init; }

    public required double Milliseconds { get; init; }

    public required string Status { get; set; }
}

public readonly record struct ZeroTensorShapes(int[] Regressors, int[] Scores, int[] Landmarks, int[] Score);

/// <summary>
/// ONNX detector plus 478-point landmarker. The session uses the GPU through DirectML when that provider can run both graphs, and the CPU otherwise. Draws the face box and direction arrows.
/// Does not build a gaze vector and does not choose a monitor.
/// </summary>
public sealed class OnnxFacePipeline : IDisposable
{
    private const int MaxLandmarkFaces = 3;

    private readonly InferenceSession _detector;
    private readonly InferenceSession _landmarker;
    private readonly (float X, float Y)[] _anchors = BlazeFaceAnchors.Create();
    private bool _disposed;

    private OnnxFacePipeline(InferenceSession detector, InferenceSession landmarker, string executionProvider)
    {
        _detector = detector;
        _landmarker = landmarker;
        ExecutionProvider = executionProvider;
    }

    public string ExecutionProvider { get; }

    public static OnnxFacePipeline Load(string detectorPath, string landmarkerPath)
    {
        try
        {
            return Open(detectorPath, landmarkerPath, useGpu: true);
        }
        catch (Exception ex) when (ex is OnnxRuntimeException or DllNotFoundException or EntryPointNotFoundException)
        {
            return Open(detectorPath, landmarkerPath, useGpu: false);
        }
    }

    private static OnnxFacePipeline Open(string detectorPath, string landmarkerPath, bool useGpu)
    {
        InferenceSession? detector = null;
        InferenceSession? landmarker = null;
        OnnxFacePipeline? pipeline = null;
        try
        {
            detector = CreateSession(detectorPath, useGpu);
            landmarker = CreateSession(landmarkerPath, useGpu);
            OnnxTensorContract.RequireDetector(detector);
            OnnxTensorContract.RequireLandmarker(landmarker);
            pipeline = new OnnxFacePipeline(detector, landmarker, useGpu ? "GPU (DirectML)" : "CPU");
            detector = null;
            landmarker = null;
            pipeline.RunZeroInputs();
            return pipeline;
        }
        catch
        {
            pipeline?.Dispose();
            detector?.Dispose();
            landmarker?.Dispose();
            throw;
        }
    }

    public ZeroTensorShapes RunZeroInputs()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var detectorInput = new DenseTensor<float>(new[] { 1, 3, BlazeFaceAnchors.InputSize, BlazeFaceAnchors.InputSize });
        var landmarkerInput = new DenseTensor<float>(new[] { 1, 3, FaceRoi.LandmarkerSize, FaceRoi.LandmarkerSize });
        using var detectorResults = Run(_detector, detectorInput);
        using var landmarkerResults = Run(_landmarker, landmarkerInput);
        return new ZeroTensorShapes(
            Shape(detectorResults, "regressors"),
            Shape(detectorResults, "scores"),
            Shape(landmarkerResults, "landmarks"),
            Shape(landmarkerResults, "score"));
    }

    public FaceInspection Inspect(Mat bgr)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (bgr.Empty() || bgr.Channels() != 3)
        {
            throw new ArgumentException("Inspect expects a BGR frame.", nameof(bgr));
        }

        var clock = Stopwatch.StartNew();
        using var drawn = bgr.Clone();
        var faces = DetectFaces(bgr);
        foreach (var face in faces)
        {
            DrawBox(drawn, face);
        }

        var landmarkCount = 0;
        var topPresence = 0f;
        var topLogit = 0f;
        var meshed = 0;
        foreach (var face in faces)
        {
            if (meshed >= MaxLandmarkFaces)
            {
                break;
            }

            if (!TryLandmarks(bgr, face, out var points, out var presence, out var logit))
            {
                continue;
            }

            if (meshed == 0)
            {
                DrawEyeDirections(drawn, points);
                DrawHeadDirection(drawn, points, bgr.Width, bgr.Height);
                landmarkCount = points.Length / 3;
                topPresence = presence;
                topLogit = logit;
            }

            meshed++;
        }

        clock.Stop();
        var topScore = faces.Count == 0 ? 0f : faces[0].Score;
        return new FaceInspection
        {
            Bgra = ToBgra(drawn),
            Width = drawn.Width,
            Height = drawn.Height,
            FaceCount = faces.Count,
            TopDetectionScore = topScore,
            TopPresence = topPresence,
            TopPresenceLogit = topLogit,
            LandmarkCount = landmarkCount,
            Milliseconds = clock.Elapsed.TotalMilliseconds,
            Status = StatusLine(faces.Count, topScore, topPresence, topLogit, landmarkCount, clock.Elapsed.TotalMilliseconds, ExecutionProvider),
        };
    }

    public FaceMeasurement Measure(Mat bgr)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (bgr.Empty() || bgr.Channels() != 3)
        {
            throw new ArgumentException("Measure expects a BGR frame.", nameof(bgr));
        }

        var clock = Stopwatch.StartNew();
        var faces = DetectFaces(bgr);
        float[]? landmarks = null;
        var presence = 0f;
        var logit = 0f;
        if (faces.Count > 0 && TryLandmarks(bgr, faces[0], out var points, out presence, out logit))
        {
            landmarks = points;
        }

        clock.Stop();
        return new FaceMeasurement(
            faces.Count,
            faces.Count == 0 ? 0f : faces[0].Score,
            presence,
            logit,
            landmarks,
            clock.Elapsed.TotalMilliseconds);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _detector.Dispose();
        _landmarker.Dispose();
    }

    private List<DetectedFace> DetectFaces(Mat bgr)
    {
        var letterbox = LetterboxGeometry.For(bgr.Width, bgr.Height);
        using var canvas = Letterbox(bgr, letterbox);
        var detectorTensor = DetectorTensor(canvas);
        using var results = Run(_detector, detectorTensor);
        var regressors = Copy(results, "regressors");
        var logits = Copy(results, "scores");
        return BlazeFaceDecoder.Decode(regressors, logits, _anchors, letterbox);
    }

    private bool TryLandmarks(
        Mat bgr,
        DetectedFace face,
        out float[] points,
        out float presence,
        out float logit)
    {
        points = [];
        presence = 0;
        logit = 0;
        var roi = FaceRoi.FromFace(face);
        if (roi.Side < 2f)
        {
            return false;
        }

        var forward = Affine2x3.Crop(roi.CenterX, roi.CenterY, roi.Side, roi.AngleDegrees, FaceRoi.LandmarkerSize);
        using var crop = Warp(bgr, forward, FaceRoi.LandmarkerSize);
        var tensor = LandmarkerTensor(crop);
        using var results = Run(_landmarker, tensor);
        var raw = Copy(results, "landmarks");
        if (raw.Length != FaceMeshIndices.Count * 3)
        {
            return false;
        }

        logit = Copy(results, "score")[0];
        presence = (float)BlazeFaceDecoder.Sigmoid(logit);
        forward.MapLandmarks(raw, roi.Side, FaceRoi.LandmarkerSize);
        points = raw;
        return true;
    }

    private static InferenceSession CreateSession(string path, bool useGpu)
    {
        using var options = new SessionOptions
        {
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
        };
        if (useGpu)
        {
            options.AppendExecutionProvider_DML(0);
        }

        return new InferenceSession(path, options);
    }

    private static IDisposableReadOnlyCollection<DisposableNamedOnnxValue> Run(InferenceSession session, DenseTensor<float> tensor)
    {
        var input = NamedOnnxValue.CreateFromTensor("input", tensor);
        return session.Run([input]);
    }

    private static int[] Shape(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results, string name)
    {
        var dimensions = results.First(result => result.Name == name).AsTensor<float>().Dimensions;
        var shape = new int[dimensions.Length];
        for (var i = 0; i < shape.Length; i++)
        {
            shape[i] = dimensions[i];
        }

        return shape;
    }

    private static float[] Copy(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results, string name)
    {
        return results.First(result => result.Name == name).AsTensor<float>().ToArray();
    }

    private static Mat Letterbox(Mat bgr, LetterboxGeometry letterbox)
    {
        using var matrix = Matrix(letterbox.Scale, 0, letterbox.PadX, 0, letterbox.Scale, letterbox.PadY);
        var canvas = new Mat();
        Cv2.WarpAffine(
            bgr,
            canvas,
            matrix,
            new Size(letterbox.InputSize, letterbox.InputSize),
            InterpolationFlags.Linear,
            BorderTypes.Constant,
            Scalar.All(0));
        return canvas;
    }

    private static Mat Warp(Mat bgr, Affine2x3 forward, int size)
    {
        using var matrix = Matrix(forward.M00, forward.M01, forward.M02, forward.M10, forward.M11, forward.M12);
        var crop = new Mat();
        Cv2.WarpAffine(
            bgr,
            crop,
            matrix,
            new Size(size, size),
            InterpolationFlags.Linear,
            BorderTypes.Constant,
            Scalar.All(0));
        return crop;
    }

    private static Mat Matrix(double m00, double m01, double m02, double m10, double m11, double m12)
    {
        var matrix = new Mat(2, 3, MatType.CV_64FC1);
        matrix.Set(0, 0, m00);
        matrix.Set(0, 1, m01);
        matrix.Set(0, 2, m02);
        matrix.Set(1, 0, m10);
        matrix.Set(1, 1, m11);
        matrix.Set(1, 2, m12);
        return matrix;
    }

    private static DenseTensor<float> DetectorTensor(Mat bgrCanvas)
    {
        using var rgb = new Mat();
        using var floated = new Mat();
        Cv2.CvtColor(bgrCanvas, rgb, ColorConversionCodes.BGR2RGB);
        rgb.ConvertTo(floated, MatType.CV_32FC3, 1.0 / 127.5, -1.0);
        return PackNchw(floated);
    }

    private static DenseTensor<float> LandmarkerTensor(Mat bgrCrop)
    {
        using var rgb = new Mat();
        using var floated = new Mat();
        Cv2.CvtColor(bgrCrop, rgb, ColorConversionCodes.BGR2RGB);
        rgb.ConvertTo(floated, MatType.CV_32FC3, 1.0 / 255.0, 0);
        return PackNchw(floated);
    }

    private static DenseTensor<float> PackNchw(Mat rgbFloat)
    {
        var height = rgbFloat.Rows;
        var width = rgbFloat.Cols;
        var tensor = new DenseTensor<float>(new[] { 1, 3, height, width });
        var rows = rgbFloat.AsRows<Vec3f>();
        for (var y = 0; y < height; y++)
        {
            var row = rows[y];
            for (var x = 0; x < width; x++)
            {
                var pixel = row[x];
                tensor[0, 0, y, x] = pixel.Item0;
                tensor[0, 1, y, x] = pixel.Item1;
                tensor[0, 2, y, x] = pixel.Item2;
            }
        }

        return tensor;
    }

    private static void DrawBox(Mat image, DetectedFace face)
    {
        var color = new Scalar(244, 236, 232);
        Cv2.Rectangle(
            image,
            new Point((int)Math.Round(face.X1), (int)Math.Round(face.Y1)),
            new Point((int)Math.Round(face.X2), (int)Math.Round(face.Y2)),
            color,
            2);
    }

    private static void DrawEyeDirections(Mat image, float[] xyz)
    {
        var landmarks = xyz.AsSpan();
        if (ImageSpaceFeatures.TryEyeArrow(landmarks, subjectLeft: true, out var leftEye))
        {
            DrawArrow(image, leftEye, new Scalar(220, 180, 40));
        }

        if (ImageSpaceFeatures.TryEyeArrow(landmarks, subjectLeft: false, out var rightEye))
        {
            DrawArrow(image, rightEye, new Scalar(40, 140, 255));
        }
    }

    private static void DrawHeadDirection(Mat image, float[] xyz, int width, int height)
    {
        if (!HeadPoseEstimator.TryHeadArrow(xyz, width, height, out var arrow))
        {
            return;
        }

        DrawArrow(image, arrow, new Scalar(230, 230, 230));
    }

    private static void DrawArrow(Mat image, DirectionArrow arrow, Scalar color)
    {
        var origin = new Point((int)Math.Round(arrow.OriginX), (int)Math.Round(arrow.OriginY));
        var tip = new Point(
            (int)Math.Round(arrow.OriginX + arrow.DeltaX),
            (int)Math.Round(arrow.OriginY + arrow.DeltaY));
        if (tip.X != origin.X || tip.Y != origin.Y)
        {
            Cv2.ArrowedLine(image, origin, tip, color, 2, LineTypes.AntiAlias, 0, 0.35);
        }

        Cv2.Circle(image, tip, 3, color, 2, LineTypes.AntiAlias);
    }

    private static byte[] ToBgra(Mat bgr)
    {
        using var bgra = new Mat();
        Cv2.CvtColor(bgr, bgra, ColorConversionCodes.BGR2BGRA);
        Mat? clone = null;
        var source = bgra;
        if (!bgra.IsContinuous())
        {
            clone = bgra.Clone();
            source = clone;
        }

        try
        {
            var bytes = new byte[source.Rows * source.Cols * 4];
            System.Runtime.InteropServices.Marshal.Copy(source.Data, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            clone?.Dispose();
        }
    }

    private static string StatusLine(
        int faces,
        float detection,
        float presence,
        float logit,
        int landmarks,
        double milliseconds,
        string executionProvider)
    {
        if (faces == 0)
        {
            return FormattableString.Invariant($"No face above 0.50. {executionProvider}. {milliseconds:0} ms. Not gaze.");
        }

        return FormattableString.Invariant(
            $"Faces {faces}. Detector {detection:0.00}. Presence {presence:0.00} (logit {logit:0.0}). Points {landmarks}. {executionProvider}. {milliseconds:0} ms. Not gaze.");
    }
}
