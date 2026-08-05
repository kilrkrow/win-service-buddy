using WinServiceBuddy.Core.Models;

namespace WinServiceBuddy.Core.Services;

public static class ServiceFilter
{
    public static IEnumerable<ServiceInfo> BySubstring(
        IEnumerable<ServiceInfo> services,
        string? substring)
    {
        if (string.IsNullOrWhiteSpace(substring))
            return services;

        return services.Where(s =>
            s.ServiceName.Contains(substring, StringComparison.OrdinalIgnoreCase) ||
            s.DisplayName.Contains(substring, StringComparison.OrdinalIgnoreCase));
    }

    public static IEnumerable<ServiceInfo> ByNames(
        IEnumerable<ServiceInfo> services,
        IEnumerable<string> serviceNames)
    {
        var set = new HashSet<string>(serviceNames, StringComparer.OrdinalIgnoreCase);
        return services.Where(s => set.Contains(s.ServiceName));
    }
}
