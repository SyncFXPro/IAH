using System.Text.Json;

namespace Glance.Core.Calibration;

public sealed class DisplayAddressingStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _path;

    public DisplayAddressingStore(string path)
    {
        _path = path;
        Current = Read();
    }

    public DisplayAddressingRecord? Current { get; private set; }

    public event EventHandler? Changed;

    public void Save(DisplayAddressingRecord record)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        record.Kind = DisplayAddressingRecord.AddressingKind;
        record.ContainsGazeSamples = false;
        File.WriteAllText(_path, JsonSerializer.Serialize(record, Options));
        Current = record;
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

    private DisplayAddressingRecord? Read()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<DisplayAddressingRecord>(File.ReadAllText(_path), Options);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }
}
