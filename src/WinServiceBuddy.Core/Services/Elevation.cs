using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace WinServiceBuddy.Core.Services;

public static class Elevation
{
    public static bool IsElevated()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Relaunch the current process with a runas verb. Returns true if a new process was started.
    /// </summary>
    public static bool TryRelaunchElevated(string? arguments = null)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
            return false;

        var startInfo = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = true,
            Verb = "runas",
            Arguments = arguments ?? string.Join(' ', Environment.GetCommandLineArgs().Skip(1).Select(QuoteIfNeeded))
        };

        try
        {
            Process.Start(startInfo);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string QuoteIfNeeded(string arg) =>
        arg.Contains(' ') ? $"\"{arg}\"" : arg;
}
