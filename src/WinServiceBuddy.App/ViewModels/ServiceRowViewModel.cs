using CommunityToolkit.Mvvm.ComponentModel;
using WinServiceBuddy.Core.Models;

namespace WinServiceBuddy.App.ViewModels;

public partial class ServiceRowViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public string ServiceName { get; }
    public string DisplayName { get; }

    /// <summary>Primary label for the grid (display name, fall back to service name).</summary>
    public string Title => string.IsNullOrWhiteSpace(DisplayName) ? ServiceName : DisplayName;

    [ObservableProperty]
    public partial string Status { get; set; }

    [ObservableProperty]
    public partial string StartupType { get; set; }

    [ObservableProperty]
    public partial string RecoverySummary { get; set; }

    /// <summary>SCM depends-on entries as display names when known, else service names.</summary>
    public IReadOnlyList<string> DependsOnDisplayNames { get; private set; } = Array.Empty<string>();

    public IReadOnlyList<string> DependsOnServiceNames { get; private set; } = Array.Empty<string>();

    public bool HasDependencies => DependsOnServiceNames.Count > 0;

    public string DependenciesButtonLabel => HasDependencies
        ? $"Deps ({DependsOnServiceNames.Count})"
        : "—";

    public string StatusColor => Status.Equals("Running", StringComparison.OrdinalIgnoreCase)
        ? "#3DDC97"
        : Status.Equals("Stopped", StringComparison.OrdinalIgnoreCase)
            ? "#FF6B6B"
            : "#F0C929";

    public ServiceRowViewModel(ServiceInfo info, IReadOnlyDictionary<string, string>? displayNameLookup = null)
    {
        ServiceName = info.ServiceName;
        DisplayName = info.DisplayName;
        Status = info.Status;
        StartupType = info.StartupType.ToString();
        RecoverySummary = info.RecoverySummary;
        SetDependencies(info.DependsOn, displayNameLookup);
    }

    public void UpdateFrom(ServiceInfo info, IReadOnlyDictionary<string, string>? displayNameLookup = null)
    {
        Status = info.Status;
        StartupType = info.StartupType.ToString();
        RecoverySummary = info.RecoverySummary;
        SetDependencies(info.DependsOn, displayNameLookup);
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(HasDependencies));
        OnPropertyChanged(nameof(DependenciesButtonLabel));
    }

    private void SetDependencies(
        IReadOnlyList<string> dependsOnServiceNames,
        IReadOnlyDictionary<string, string>? displayNameLookup)
    {
        DependsOnServiceNames = dependsOnServiceNames?.ToArray() ?? Array.Empty<string>();
        DependsOnDisplayNames = DependsOnServiceNames
            .Select(n =>
                displayNameLookup is not null && displayNameLookup.TryGetValue(n, out var dn) && !string.IsNullOrWhiteSpace(dn)
                    ? dn
                    : n)
            .ToArray();
    }
}
