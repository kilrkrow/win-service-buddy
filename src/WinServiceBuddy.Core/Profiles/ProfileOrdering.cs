namespace WinServiceBuddy.Core.Profiles;

public static class ProfileOrdering
{
    /// <summary>Services sorted for start (ascending order). Entries with equal order keep relative stability by name.</summary>
    public static IReadOnlyList<ProfileServiceEntry> ForStart(IEnumerable<ProfileServiceEntry> services) =>
        services
            .OrderBy(s => s.Order)
            .ThenBy(s => s.ServiceName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Services sorted for stop (reverse of start).</summary>
    public static IReadOnlyList<ProfileServiceEntry> ForStop(IEnumerable<ProfileServiceEntry> services) =>
        ForStart(services).Reverse().ToList();

    /// <summary>Renumber order to 10, 20, 30… in current sequence.</summary>
    public static void Renumber(IList<ProfileServiceEntry> services, int step = 10)
    {
        var n = step;
        foreach (var s in services)
        {
            s.Order = n;
            n += step;
        }
    }

    /// <summary>Order live service names by profile order; unknown names sort last alphabetically.</summary>
    public static IReadOnlyList<string> OrderServiceNames(
        IEnumerable<string> serviceNames,
        ProductProfile profile,
        bool forStop)
    {
        var orderMap = profile.Services
            .GroupBy(s => s.ServiceName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Order, StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> sorted = serviceNames
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => orderMap.TryGetValue(n, out var o) ? o : int.MaxValue)
            .ThenBy(n => n, StringComparer.OrdinalIgnoreCase);

        if (forStop)
            sorted = sorted.Reverse();

        return sorted.ToList();
    }
}
