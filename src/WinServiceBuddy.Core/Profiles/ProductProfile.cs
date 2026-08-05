using System.Text.Json.Serialization;

namespace WinServiceBuddy.Core.Profiles;

public sealed class ProductProfile
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? ProductVersionNote { get; set; }
    /// <summary>Preferred role(s) when the profile is loaded. Must appear in <see cref="Roles"/> when that list is non-empty.</summary>
    public List<string> DefaultRoles { get; set; } = new();

    /// <summary>
    /// Profile-defined machine functions (not a fixed client/server enum).
    /// Examples: "Application Server", "Database Host", "Web Front End", "Client Workstation".
    /// Services and prerequisites reference these by name.
    /// </summary>
    public List<string> Roles { get; set; } = new();
    public bool IncludeScmDependencies { get; set; } = true;
    public string DependencyDirection { get; set; } = "dependsOn";
    public List<ProfileServiceEntry> Services { get; set; } = new();
    public List<ProfileMatchRule> MatchRules { get; set; } = new();
    public List<ProfilePrerequisite> Prerequisites { get; set; } = new();
    public ProfileMetadata? Metadata { get; set; }
}

public sealed class ProfileServiceEntry
{
    public string ServiceName { get; set; } = "";
    public List<string> Roles { get; set; } = new();
    public bool Optional { get; set; }
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
