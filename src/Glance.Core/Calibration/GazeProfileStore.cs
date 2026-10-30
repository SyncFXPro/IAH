using System.Text.Json;

namespace Glance.Core.Calibration;

public sealed class GazeProfileStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _path;

    public GazeProfileStore(string path)
    {
        _path = path;
        Current = Read();
    }

    public GazeCalibrationProfile? Current { get; private set; }

    public event EventHandler? Changed;

    public void Save(GazeCalibrationProfile profile)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        profile.Kind = GazeCalibrationProfile.GazeKind;
        profile.ContainsGazeSamples = true;
        profile.IsSimulated = false;
        File.WriteAllText(_path, JsonSerializer.Serialize(profile, Options));
        Current = profile;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }

        Current = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private GazeCalibrationProfile? Read()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            return CalibrationDocument.TryReadGazeProfile(File.ReadAllText(_path), out var profile, out _)
                ? profile
                : null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
