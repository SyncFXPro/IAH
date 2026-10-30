using System.Text.Json;

namespace Glance.Core.Configuration;

public interface ISettingsStore
{
    AppSettings Current { get; }

    event EventHandler? Changed;

    void Save();
}

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly object _gate = new();

    public JsonSettingsStore(string path)
    {
        _path = path;
        Current = Load();
    }

    public AppSettings Current { get; private set; }

    public event EventHandler? Changed;

    public void Save()
    {
        lock (_gate)
        {
            Current.Normalize();
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Current, Options));
            File.Move(temp, _path, overwrite: true);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                var created = new AppSettings();
                created.Normalize();
                return created;
            }

            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), Options) ?? new AppSettings();
            loaded.Normalize();
            return loaded;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            var backup = _path + ".corrupt";
            try
            {
                if (File.Exists(_path))
                {
                    File.Copy(_path, backup, overwrite: true);
                }
            }
            catch (IOException)
            {
            }

            var fallback = new AppSettings();
            fallback.Normalize();
            return fallback;
        }
    }
}
