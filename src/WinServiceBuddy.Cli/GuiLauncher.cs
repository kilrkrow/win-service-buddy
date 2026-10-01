namespace WinServiceBuddy.Cli;

/// <summary>
/// Resolves the location of the desktop GUI executable relative to the CLI.
/// </summary>
/// <remarks>
/// The GUI can sit in several different places depending on how Win Service Buddy was
/// installed, so the CLI probes a fixed list of candidates in order:
/// <list type="number">
///   <item>Combined layout — GUI next to <c>wsbuddy.exe</c> in the same folder.</item>
///   <item>Chocolatey layout — <c>tools\cli\wsbuddy.exe</c> with the GUI in <c>tools\app\</c>.</item>
///   <item>Sibling publish layout — per-project output folders under one root.</item>
///   <item>Developer tree — <c>src\*\bin\&lt;Configuration&gt;\&lt;tfm&gt;\</c>.</item>
/// </list>
/// </remarks>
public static class GuiLauncher
{
    /// <summary>File name of the desktop GUI executable.</summary>
    public const string AppExeName = "WinServiceBuddy.App.exe";

    /// <summary>Project (and output folder) name of the desktop GUI.</summary>
    public const string AppProjectName = "WinServiceBuddy.App";

    /// <summary>
    /// Target framework the GUI is built for. Must stay in sync with
    /// <c>WinServiceBuddy.App.csproj</c>; all projects target Windows-specific .NET.
    /// </summary>
    public const string AppTargetFramework = "net10.0-windows";

    private static readonly string[] BuildConfigurations = ["Debug", "Release"];

    /// <summary>
    /// Returns the GUI executable paths to probe, in priority order, for a CLI running
    /// out of <paramref name="baseDirectory"/>. Paths are normalised but not checked for
    /// existence, which keeps this method pure and testable.
    /// </summary>
    public static IReadOnlyList<string> GetCandidatePaths(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        var candidates = new List<string>
        {
            // 1. Combined layout: GUI published next to the CLI.
            Combine(baseDirectory, AppExeName),

            // 2. Chocolatey layout: chocolateyinstall.ps1 unzips to tools\cli and tools\app.
            Combine(baseDirectory, "..", "app", AppExeName),

            // 3. Sibling publish layout: one folder per project under a shared root.
            Combine(baseDirectory, "..", AppProjectName, AppExeName),
        };

        // 4. Developer tree: src\WinServiceBuddy.Cli\bin\<cfg>\<tfm> -> src\WinServiceBuddy.App\bin\<cfg>\<tfm>
        foreach (var configuration in BuildConfigurations)
        {
            candidates.Add(Combine(
                baseDirectory,
                "..", "..", "..", "..",
                AppProjectName, "bin", configuration, AppTargetFramework, AppExeName));
        }

        return candidates;
    }

    private static string Combine(string baseDirectory, params string[] parts)
    {
        var all = new string[parts.Length + 1];
        all[0] = baseDirectory;
        parts.CopyTo(all, 1);
        return Path.GetFullPath(Path.Combine(all));
    }
}
