using WinServiceBuddy.Cli;

namespace WinServiceBuddy.Core.Tests;

public class GuiLauncherTests
{
    [Fact]
    public void GetCandidatePaths_IncludesCombinedLayoutFirst()
    {
        var candidates = GuiLauncher.GetCandidatePaths(@"C:\tools\wsbuddy");

        Assert.Equal(
            Path.GetFullPath(@"C:\tools\wsbuddy\WinServiceBuddy.App.exe"),
            candidates[0]);
    }

    [Fact]
    public void GetCandidatePaths_IncludesChocolateySiblingAppFolder()
    {
        // chocolateyinstall.ps1 unzips the CLI to tools\cli and the GUI to tools\app.
        var candidates = GuiLauncher.GetCandidatePaths(@"C:\ProgramData\chocolatey\lib\wsbuddy\tools\cli");

        Assert.Contains(
            Path.GetFullPath(@"C:\ProgramData\chocolatey\lib\wsbuddy\tools\app\WinServiceBuddy.App.exe"),
            candidates);
    }

    [Fact]
    public void GetCandidatePaths_IncludesSiblingPublishLayout()
    {
        var candidates = GuiLauncher.GetCandidatePaths(@"C:\publish\WinServiceBuddy.Cli");

        Assert.Contains(
            Path.GetFullPath(@"C:\publish\WinServiceBuddy.App\WinServiceBuddy.App.exe"),
            candidates);
    }

    [Theory]
    [InlineData("Debug")]
    [InlineData("Release")]
    public void GetCandidatePaths_IncludesDeveloperTreeForWindowsTargetFramework(string configuration)
    {
        var baseDirectory = $@"C:\repo\src\WinServiceBuddy.Cli\bin\{configuration}\net10.0-windows";

        var candidates = GuiLauncher.GetCandidatePaths(baseDirectory);

        Assert.Contains(
            Path.GetFullPath(
                $@"C:\repo\src\WinServiceBuddy.App\bin\{configuration}\net10.0-windows\WinServiceBuddy.App.exe"),
            candidates);
    }

    [Fact]
    public void GetCandidatePaths_DoesNotProbeFrameworkAgnosticTargetFramework()
    {
        // Regression guard: the dev-tree probe used to hardcode "net10.0" while every
        // project targets "net10.0-windows", which made the fallback dead code.
        var candidates = GuiLauncher.GetCandidatePaths(
            @"C:\repo\src\WinServiceBuddy.Cli\bin\Debug\net10.0-windows");

        Assert.DoesNotContain(
            candidates,
            path => path.Contains(@"\net10.0\", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetCandidatePaths_ReturnsNormalisedRootedPaths()
    {
        var candidates = GuiLauncher.GetCandidatePaths(@"C:\tools\wsbuddy\tools\cli");

        Assert.NotEmpty(candidates);
        Assert.All(candidates, path =>
        {
            Assert.True(Path.IsPathFullyQualified(path), $"not fully qualified: {path}");
            Assert.DoesNotContain("..", path);
            Assert.EndsWith(GuiLauncher.AppExeName, path);
        });
    }

    [Fact]
    public void GetCandidatePaths_RejectsEmptyBaseDirectory()
    {
        Assert.Throws<ArgumentException>(() => GuiLauncher.GetCandidatePaths("  "));
    }
}
