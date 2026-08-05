using CommunityToolkit.Mvvm.ComponentModel;
using WinServiceBuddy.Core.Profiles;

namespace WinServiceBuddy.App.ViewModels;

public partial class BuilderServiceRowViewModel : ViewModelBase
{
    public BuilderServiceRowViewModel(ProfileServiceEntry entry)
    {
        Entry = entry;
        ServiceName = entry.ServiceName;
        DisplayName = entry.DisplayNameHint ?? entry.ServiceName;
        Order = entry.Order;
        RolesText = string.Join(", ", entry.Roles);
        OverrideStartup = "";
        OverrideRecovery = "";
    }

    public ProfileServiceEntry Entry { get; }

    public string ServiceName { get; }

    [ObservableProperty]
    public partial string DisplayName { get; set; }

    [ObservableProperty]
    public partial int Order { get; set; }

    [ObservableProperty]
    public partial string RolesText { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Per-environment override currently shown in the builder (empty = inherit env default).</summary>
    [ObservableProperty]
    public partial string OverrideStartup { get; set; }

    [ObservableProperty]
    public partial string OverrideRecovery { get; set; }

    public void SyncToEntry(string? environmentId)
    {
        Entry.DisplayNameHint = DisplayName;
        Entry.Order = Order;
        Entry.Roles = string.IsNullOrWhiteSpace(RolesText)
            ? []
            : RolesText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        if (string.IsNullOrWhiteSpace(environmentId))
            return;

        var hasStartup = !string.IsNullOrWhiteSpace(OverrideStartup);
        var hasRecovery = !string.IsNullOrWhiteSpace(OverrideRecovery);
        if (!hasStartup && !hasRecovery)
        {
            Entry.EnvironmentOverrides.Remove(environmentId);
            return;
        }

        Entry.EnvironmentOverrides[environmentId] = new ProfileServiceEnvironmentOverride
        {
            DesiredStartup = hasStartup ? OverrideStartup : null,
            DesiredRecovery = hasRecovery ? OverrideRecovery : null
        };
    }

    public void LoadOverridesForEnvironment(string? environmentId)
    {
        OverrideStartup = "";
        OverrideRecovery = "";
        if (string.IsNullOrWhiteSpace(environmentId))
            return;

        if (Entry.EnvironmentOverrides.TryGetValue(environmentId, out var o) ||
            Entry.EnvironmentOverrides.Keys.Any(k => string.Equals(k, environmentId, StringComparison.OrdinalIgnoreCase)))
        {
            var match = Entry.EnvironmentOverrides.FirstOrDefault(kv =>
                string.Equals(kv.Key, environmentId, StringComparison.OrdinalIgnoreCase));
            if (match.Value is not null)
            {
                OverrideStartup = match.Value.DesiredStartup ?? "";
                OverrideRecovery = match.Value.DesiredRecovery ?? "";
            }
        }
    }
}
