using WinServiceBuddy.Core.Models;
using WinServiceBuddy.Core.Services;

namespace WinServiceBuddy.Core.Tests;

public class ServiceFilterTests
{
    private static List<ServiceInfo> Sample() =>
    [
        new() { ServiceName = "ProductRecording", DisplayName = "Product Recording Server", Status = "Running" },
        new() { ServiceName = "ProductAgent", DisplayName = "Product Agent", Status = "Stopped" },
        new() { ServiceName = "Spooler", DisplayName = "Print Spooler", Status = "Running" }
    ];

    [Fact]
    public void BySubstring_Matches_ServiceName_Or_DisplayName()
    {
        var result = ServiceFilter.BySubstring(Sample(), "recording").ToList();
        Assert.Single(result);
        Assert.Equal("ProductRecording", result[0].ServiceName);
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
        var result = ServiceFilter.ByNames(Sample(), ["spooler", "PRODUCTAGENT"]).ToList();
        Assert.Equal(2, result.Count);
    }
}
