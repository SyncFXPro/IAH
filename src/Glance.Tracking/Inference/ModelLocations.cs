namespace Glance.Tracking.Inference;

/// <summary>
/// Finds the gitignored ONNX files beside the executable or in the repo's assets folder.
/// A clean checkout has neither, and callers stay on the scripted preview.
/// </summary>
public static class ModelLocations
{
    public static bool TryFind(string fileName, out string path)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 8 && directory is not null; depth++, directory = directory.Parent)
        {
            var besideOutput = Path.Combine(directory.FullName, "models", fileName);
            if (File.Exists(besideOutput))
            {
                path = besideOutput;
                return true;
            }

            var assets = Path.Combine(directory.FullName, "assets", "models", fileName);
            if (File.Exists(assets))
            {
                path = assets;
                return true;
            }
        }

        path = string.Empty;
        return false;
    }
}
