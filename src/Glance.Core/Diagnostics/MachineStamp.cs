using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Glance.Core.Diagnostics;

public sealed record MachineStamp(
    string OsDescription,
    string Framework,
    int ProcessorCount,
    string ProcessorName,
    string ExecutionProvider,
    string ModelName)
{
    public static MachineStamp CaptureCurrent() =>
        new(
            RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription,
            Environment.ProcessorCount,
            ReadProcessorName(),
            "not loaded",
            "not loaded");

    private static string ReadProcessorName()
    {
    if (!System.OperatingSystem.IsWindows())
    {
        return "unavailable";
    }

    try
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return key?.GetValue("ProcessorNameString") as string ?? "unavailable";
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return "unavailable";
        }
    }
}
