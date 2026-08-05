namespace WinServiceBuddy.Core.Profiles;

public readonly record struct ResolvedServicePolicy(
    string? DesiredStartup,
    string? DesiredRecovery,
    string Source);

/// <summary>Resolves desired startup/recovery for a service under a named environment.</summary>
public static class ProfileEnvironmentResolver
{
    public static ProfileEnvironment? FindEnvironment(ProductProfile profile, string? environmentKey)
    {
        if (profile.Environments.Count == 0)
            return null;

        if (string.IsNullOrWhiteSpace(environmentKey))
        {
            if (!string.IsNullOrWhiteSpace(profile.DefaultEnvironment))
                return FindEnvironment(profile, profile.DefaultEnvironment);
            return profile.Environments[0];
        }

        return profile.Environments.FirstOrDefault(e =>
                   string.Equals(e.Id, environmentKey, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(e.Name, environmentKey, StringComparison.OrdinalIgnoreCase));
    }

    public static ResolvedServicePolicy Resolve(ProductProfile profile, ProfileServiceEntry service, string? environmentKey)
    {
        var env = FindEnvironment(profile, environmentKey);

        // 1) Per-service override for this environment
        if (env is not null && service.EnvironmentOverrides.Count > 0)
        {
            if (TryGetOverride(service, env.Id, out var byId) ||
                TryGetOverride(service, env.Name, out byId))
            {
                var startup = byId.DesiredStartup ?? env.DefaultStartup ?? service.DesiredStartup;
                var recovery = byId.DesiredRecovery ?? env.DefaultRecovery ?? service.DesiredRecovery;
                return new ResolvedServicePolicy(startup, recovery, "service-override");
            }
        }

        // 2) Environment defaults
        if (env is not null &&
            (!string.IsNullOrWhiteSpace(env.DefaultStartup) || !string.IsNullOrWhiteSpace(env.DefaultRecovery)))
        {
            return new ResolvedServicePolicy(
                env.DefaultStartup ?? service.DesiredStartup,
                env.DefaultRecovery ?? service.DesiredRecovery,
                "environment-default");
        }

        // 3) Legacy v1 service fields
        if (!string.IsNullOrWhiteSpace(service.DesiredStartup) || !string.IsNullOrWhiteSpace(service.DesiredRecovery))
            return new ResolvedServicePolicy(service.DesiredStartup, service.DesiredRecovery, "legacy-service");

        return new ResolvedServicePolicy(null, null, "unset");
    }

    public static string Slugify(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "default";

        var chars = name.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        var slug = new string(chars);
        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        return slug.Trim('-');
    }

    private static bool TryGetOverride(
        ProfileServiceEntry service,
        string key,
        out ProfileServiceEnvironmentOverride value)
    {
        foreach (var kv in service.EnvironmentOverrides)
        {
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = kv.Value;
                return true;
            }
        }

        value = null!;
        return false;
    }
}
