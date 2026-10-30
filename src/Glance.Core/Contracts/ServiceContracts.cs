using Glance.Core.Configuration;
using Glance.Core.Geometry;

namespace Glance.Core.Contracts;

public sealed record CameraDescriptor(string Id, string Name, int Index);

public sealed record DisplayInfo(
    string DeviceName,
    string FriendlyName,
    bool IsPrimary,
    PixelRect Bounds,
    PixelRect WorkArea,
    int DpiX,
    int DpiY)
{
    public string Fingerprint =>
        $"{DeviceName}|{Bounds.X},{Bounds.Y},{Bounds.Width},{Bounds.Height}|{DpiX}";
}

public interface IDisplayService
{
    IReadOnlyList<DisplayInfo> GetDisplays();

    event EventHandler? DisplaysChanged;
}

public interface ICursorNative
{
    PixelPoint GetPosition();

    bool SetPosition(int x, int y);
}

public readonly record struct PixelPoint(int X, int Y);

public interface ICursorService
{
    PixelPoint GetPosition();

    bool TryRememberCurrentPosition();

    bool TryGetRememberedPosition(string monitorId, out PixelPoint position);

    bool TryRelocate(string monitorId, CursorRelocationRequest request, out string? blockReason);
}

public interface IWindowFocus
{
    /// <summary>
    /// Brings forward the topmost eligible window on the monitor.
    /// Returns a short status line. Does not force focus when Windows refuses.
    /// </summary>
    string TryActivate(string monitorId);
}

public interface IStartupRegistration
{
    void Apply(bool enabled, string executablePath);
}

public interface ICameraCatalog
{
    IReadOnlyList<CameraDescriptor> List();
}

public static class CameraSelection
{
    public static CameraDescriptor? Match(string? savedId, IReadOnlyList<CameraDescriptor> cameras)
    {
        if (string.IsNullOrWhiteSpace(savedId) || cameras.Count == 0)
        {
            return null;
        }

        var exact = cameras.FirstOrDefault(camera => string.Equals(camera.Id, savedId, StringComparison.Ordinal));
        if (exact is not null)
        {
            return exact;
        }

        var separator = savedId.IndexOf('|');
        var savedName = separator >= 0 ? savedId[(separator + 1)..] : savedId;
        var byName = cameras.FirstOrDefault(camera => string.Equals(camera.Name, savedName, StringComparison.Ordinal));
        if (byName is not null)
        {
            return byName;
        }

        if (separator > 0 && int.TryParse(savedId[..separator], out var index))
        {
            return cameras.FirstOrDefault(camera => camera.Index == index);
        }

        return null;
    }

    public static bool TryParseId(string? savedId, out int index, out string name)
    {
        index = 0;
        name = string.Empty;
        if (string.IsNullOrWhiteSpace(savedId))
        {
            return false;
        }

        var separator = savedId.IndexOf('|');
        if (separator <= 0 || !int.TryParse(savedId[..separator], out index))
        {
            return false;
        }

        name = savedId[(separator + 1)..];
        return !string.IsNullOrWhiteSpace(name);
    }

    public static string Label(string? savedId)
    {
        if (string.IsNullOrWhiteSpace(savedId))
        {
            return "No webcam is selected.";
        }

        var separator = savedId.IndexOf('|');
        var name = separator >= 0 ? savedId[(separator + 1)..] : savedId;
        return string.IsNullOrWhiteSpace(name) ? "No webcam is selected." : "Selected webcam: " + name + ".";
    }
}

public interface ICameraCapture : IDisposable
{
    IReadOnlyList<CameraDescriptor> ListCameras();

    bool IsCapturing { get; }

    void Start(string? deviceId, CancellationToken cancellationToken);

    void Stop();
}

/// <summary>
/// Image-space landmarks are not a 3D gaze vector. The skeleton does not convert them.
/// </summary>
public readonly record struct LandmarkObservation(
    string ModelName,
    int LandmarkCount,
    bool HasIrisLandmarks,
    double PresenceScore,
    float[]? Points = null,
    int ImageWidth = 0,
    int ImageHeight = 0,
    int FaceCount = 0);

public interface IFaceLandmarkModel : IDisposable
{
    string Name { get; }

    bool IsLoaded { get; }

    void Load(string modelPath);
}

public interface IGazeFeatureExtractor
{
    bool TryExtract(
        in LandmarkObservation observation,
        out Geometry.GazeFeatureVector features,
        out Geometry.HeadTranslation head,
        out string? failure);
}
