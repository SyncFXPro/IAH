using System.Text.Json;
using System.Text.Json.Serialization;

namespace Glance.Core.Calibration;

public static class CalibrationDocument
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(GazeCalibrationProfile profile) => JsonSerializer.Serialize(profile, Options);

    public static string Serialize(DisplayAddressingRecord record) => JsonSerializer.Serialize(record, Options);

    public static bool TryReadGazeProfile(string json, out GazeCalibrationProfile? profile, out string? rejection)
    {
        profile = null;
        rejection = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("kind", out var kindProperty))
            {
                rejection = "missing-kind";
                return false;
            }

            var kind = kindProperty.GetString();
            if (!string.Equals(kind, GazeCalibrationProfile.GazeKind, StringComparison.Ordinal))
            {
                rejection = "not-a-gaze-profile";
                return false;
            }

            if (!document.RootElement.TryGetProperty("containsGazeSamples", out var samplesProperty)
                || samplesProperty.ValueKind != JsonValueKind.True)
            {
                rejection = "no-gaze-samples";
                return false;
            }

            profile = JsonSerializer.Deserialize<GazeCalibrationProfile>(json, Options);
            if (profile is null || profile.Monitors.Count == 0)
            {
                profile = null;
                rejection = "empty-profile";
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            rejection = "invalid-json";
            return false;
        }
    }
}
