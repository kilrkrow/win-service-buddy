using WinServiceBuddy.Core.Models;
using WinServiceBuddy.Core.Services;

namespace WinServiceBuddy.Core.Tests;

public class ServiceFilterTests
{
    private static List<ServiceInfo> Sample() =>
    [
        new() { ServiceName = "MilestoneRecording", DisplayName = "Milestone Recording Server", Status = "Running" },
        new() { ServiceName = "EverbridgeAgent", DisplayName = "Everbridge Agent", Status = "Stopped" },
        new() { ServiceName = "Spooler", DisplayName = "Print Spooler", Status = "Running" }
    ];

    [Fact]
    public void BySubstring_Matches_ServiceName_Or_DisplayName()
    {
        var result = ServiceFilter.BySubstring(Sample(), "milestone").ToList();
        Assert.Single(result);
        Assert.Equal("MilestoneRecording", result[0].ServiceName);
    }

    [Fact]
    public void BySubstring_Empty_ReturnsAll()
    {
        var result = ServiceFilter.BySubstring(Sample(), null).ToList();
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void ByNames_IsCaseInsensitive()
    {
        var result = ServiceFilter.ByNames(Sample(), ["spooler", "EVERBRIDGEAGENT"]).ToList();
        Assert.Equal(2, result.Count);
    }
}
