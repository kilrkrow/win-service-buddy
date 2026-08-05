namespace WinServiceBuddy.Core.Models;

/// <summary>Snapshot of a Windows service used by CLI and GUI.</summary>
public sealed class ServiceInfo
{
    public required string ServiceName { get; init; }
    public required string DisplayName { get; init; }
    public string Status { get; init; } = "Unknown";
    public ServiceStartupType StartupType { get; init; } = ServiceStartupType.Unknown;
    public string RecoverySummary { get; init; } = "Unknown";
    public string? Account { get; init; }
    public int? ProcessId { get; init; }
    public IReadOnlyList<string> DependsOn { get; init; } = Array.Empty<string>();
    public bool CanStop { get; init; }
    public bool CanStart { get; init; }

    public bool IsRunning =>
        string.Equals(Status, "Running", StringComparison.OrdinalIgnoreCase);

    public bool IsStopped =>
        string.Equals(Status, "Stopped", StringComparison.OrdinalIgnoreCase);
}
