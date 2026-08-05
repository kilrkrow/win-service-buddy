namespace WinServiceBuddy.Core.Profiles;

public sealed class ProductProfile
{
    public int SchemaVersion { get; set; } = 2;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? ProductVersionNote { get; set; }

    /// <summary>Preferred role(s) when the profile is loaded. Must appear in <see cref="Roles"/> when that list is non-empty.</summary>
    public List<string> DefaultRoles { get; set; } = new();

    /// <summary>
    /// Profile-defined machine functions (not a fixed client/server enum).
    /// Examples: "Application Server", "Database Host", "Web Front End", "Client Workstation".
    /// </summary>
    public List<string> Roles { get; set; } = new();

    /// <summary>Preferred environment name or id when the profile is loaded.</summary>
    public string? DefaultEnvironment { get; set; }

    /// <summary>
    /// Named environments (e.g. Production, Acceptance) with default startup/recovery policies.
    /// One profile file per product; environments are variants inside it.
    /// </summary>
    public List<ProfileEnvironment> Environments { get; set; } = new();

    public bool IncludeScmDependencies { get; set; } = true;
    public string DependencyDirection { get; set; } = "dependsOn";
    public List<ProfileServiceEntry> Services { get; set; } = new();
    public List<ProfileMatchRule> MatchRules { get; set; } = new();
    public List<ProfilePrerequisite> Prerequisites { get; set; } = new();
    public ProfileMetadata? Metadata { get; set; }
}

public sealed class ProfileEnvironment
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>Automatic | AutomaticDelayed | Manual | Disabled</summary>
    public string? DefaultStartup { get; set; }

    /// <summary>restart-3 | none</summary>
    public string? DefaultRecovery { get; set; }
}

public sealed class ProfileServiceEntry
{
    public string ServiceName { get; set; } = "";

    /// <summary>Optional display name captured at authoring time (live machine may differ).</summary>
    public string? DisplayNameHint { get; set; }

    /// <summary>Start order (ascending). Stop uses reverse order.</summary>
    public int Order { get; set; }

    public List<string> Roles { get; set; } = new();
    public bool Optional { get; set; }

    /// <summary>Legacy v1 fields — used when no environment override/default applies.</summary>
    public string? DesiredStartup { get; set; }

    /// <summary>Legacy v1 fields — used when no environment override/default applies.</summary>
    public string? DesiredRecovery { get; set; }

    /// <summary>Reserved for future explicit edges; v1 builder uses <see cref="Order"/> only.</summary>
    public List<string> DependsOnProfileServices { get; set; } = new();

    /// <summary>Key = environment id (preferred) or name. Per-service overrides for that environment.</summary>
    public Dictionary<string, ProfileServiceEnvironmentOverride> EnvironmentOverrides { get; set; } = new();
}

public sealed class ProfileServiceEnvironmentOverride
{
    public string? DesiredStartup { get; set; }
    public string? DesiredRecovery { get; set; }
}

public sealed class ProfileMatchRule
{
    public string Type { get; set; } = "substring";
    public string Value { get; set; } = "";
    public List<string> Roles { get; set; } = new();
    public string? Comment { get; set; }
}

public sealed class ProfilePrerequisite
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public List<string> Roles { get; set; } = new();
    public List<ProfileCheck> Checks { get; set; } = new();
    public string? DocRef { get; set; }
    public string? DocUrl { get; set; }
}

public sealed class ProfileCheck
{
    public string Type { get; set; } = "serviceExists";
    public string? ServiceName { get; set; }
    public string Severity { get; set; } = "error";
    public string? ExpectedStartup { get; set; }
}

public sealed class ProfileMetadata
{
    public string? Author { get; set; }
    public DateTimeOffset? ExportedAt { get; set; }
}

public sealed class ProfileValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
}
