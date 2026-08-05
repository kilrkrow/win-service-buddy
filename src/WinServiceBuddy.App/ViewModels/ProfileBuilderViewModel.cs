using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinServiceBuddy.Core.Models;
using WinServiceBuddy.Core.Profiles;
using WinServiceBuddy.Core.Services;

namespace WinServiceBuddy.App.ViewModels;

public partial class ProfileBuilderViewModel : ViewModelBase
{
    private readonly IWindowsServiceManager _services;
    private readonly ProfileStore _store;
    private ProductProfile _profile;
    private string? _currentPath;

    public ProfileBuilderViewModel()
        : this(new WindowsServiceManager(), new ProfileStore())
    {
    }

    public ProfileBuilderViewModel(IWindowsServiceManager services, ProfileStore store, ProductProfile? existing = null, string? path = null)
    {
        _services = services;
        _store = store;
        _profile = existing ?? ProfileStore.CreateTemplate("New Product");
        _currentPath = path;
        ProfileServices = new ObservableCollection<BuilderServiceRowViewModel>();
        DiscoverResults = new ObservableCollection<DiscoverServiceRowViewModel>();
        EnvironmentNames = new ObservableCollection<string>();
        ReloadFromProfile();
        StatusText = existing is null ? "New profile — discover services and set environments." : $"Editing {_profile.Name}";
    }

    public ObservableCollection<BuilderServiceRowViewModel> ProfileServices { get; }
    public ObservableCollection<DiscoverServiceRowViewModel> DiscoverResults { get; }
    public ObservableCollection<string> EnvironmentNames { get; }

    public IReadOnlyList<string> StartupOptions { get; } =
        ["", "Automatic", "AutomaticDelayed", "Manual", "Disabled"];

    public IReadOnlyList<string> RecoveryOptions { get; } =
        ["", "restart-3", "none"];

    public IReadOnlyList<string> EnvStartupOptions { get; } =
        ["Automatic", "AutomaticDelayed", "Manual", "Disabled"];

    public IReadOnlyList<string> EnvRecoveryOptions { get; } =
        ["restart-3", "none"];

    [ObservableProperty]
    public partial string ProductName { get; set; } = "";

    [ObservableProperty]
    public partial string ProductId { get; set; } = "";

    [ObservableProperty]
    public partial string Description { get; set; } = "";

    [ObservableProperty]
    public partial string RolesCsv { get; set; } = "";

    [ObservableProperty]
    public partial string DiscoverFilter { get; set; } = "";

    [ObservableProperty]
    public partial string? SelectedEnvironmentName { get; set; }

    [ObservableProperty]
    public partial string EnvDefaultStartup { get; set; } = "Automatic";

    [ObservableProperty]
    public partial string EnvDefaultRecovery { get; set; } = "restart-3";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    [ObservableProperty]
    public partial string NewEnvironmentName { get; set; } = "";

    public Func<Task<string?>>? PickOpenProfileAsync { get; set; }
    public Func<Task<string?>>? PickSaveProfileAsync { get; set; }
    public Action? RequestClose { get; set; }
    public Action? ProfilesChanged { get; set; }

    public ProductProfile WorkingProfile => _profile;

    partial void OnSelectedEnvironmentNameChanged(string? value)
    {
        SyncUiToProfile();
        LoadEnvironmentEditor();
        foreach (var row in ProfileServices)
            row.LoadOverridesForEnvironment(CurrentEnvironmentId());
    }

    [RelayCommand]
    private void Discover()
    {
        DiscoverResults.Clear();
        IReadOnlyList<ServiceInfo> list = string.IsNullOrWhiteSpace(DiscoverFilter)
            ? _services.GetServices()
            : _services.FindBySubstring(DiscoverFilter);

        var existing = new HashSet<string>(ProfileServices.Select(s => s.ServiceName), StringComparer.OrdinalIgnoreCase);
        foreach (var s in list.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            if (existing.Contains(s.ServiceName))
                continue;
            DiscoverResults.Add(new DiscoverServiceRowViewModel(s));
        }

        StatusText = $"{DiscoverResults.Count} service(s) match (not already in profile).";
    }

    [RelayCommand]
    private void AddSelectedDiscoveries()
    {
        var selected = DiscoverResults.Where(d => d.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusText = "Select one or more discovered services to add.";
            return;
        }

        var order = ProfileServices.Count == 0 ? 10 : ProfileServices.Max(s => s.Order) + 10;
        foreach (var d in selected)
        {
            if (ProfileServices.Any(p => string.Equals(p.ServiceName, d.ServiceName, StringComparison.OrdinalIgnoreCase)))
                continue;

            var entry = new ProfileServiceEntry
            {
                ServiceName = d.ServiceName,
                DisplayNameHint = d.DisplayName,
                Order = order,
                Roles = ParseRoles(RolesCsv)
            };
            order += 10;
            var row = new BuilderServiceRowViewModel(entry);
            row.LoadOverridesForEnvironment(CurrentEnvironmentId());
            ProfileServices.Add(row);
        }

        foreach (var d in selected)
            DiscoverResults.Remove(d);

        RenumberOrders();
        StatusText = $"Added services. Profile now has {ProfileServices.Count}.";
    }

    [RelayCommand]
    private void RemoveSelectedProfileServices()
    {
        var selected = ProfileServices.Where(s => s.IsSelected).ToList();
        foreach (var s in selected)
            ProfileServices.Remove(s);
        RenumberOrders();
        StatusText = $"Removed {selected.Count}. {ProfileServices.Count} remain.";
    }

    [RelayCommand]
    private void MoveUp()
    {
        var idx = ProfileServices.ToList().FindIndex(s => s.IsSelected);
        if (idx <= 0)
            return;
        ProfileServices.Move(idx, idx - 1);
        RenumberOrders();
    }

    [RelayCommand]
    private void MoveDown()
    {
        var idx = ProfileServices.ToList().FindIndex(s => s.IsSelected);
        if (idx < 0 || idx >= ProfileServices.Count - 1)
            return;
        ProfileServices.Move(idx, idx + 1);
        RenumberOrders();
    }

    [RelayCommand]
    private void ApplyEnvironmentDefaultsToAll()
    {
        SyncUiToProfile();
        var envId = CurrentEnvironmentId();
        if (envId is null)
        {
            StatusText = "Select an environment first.";
            return;
        }

        foreach (var row in ProfileServices)
        {
            row.Entry.EnvironmentOverrides[envId] = new ProfileServiceEnvironmentOverride
            {
                DesiredStartup = EnvDefaultStartup,
                DesiredRecovery = EnvDefaultRecovery
            };
            row.OverrideStartup = EnvDefaultStartup;
            row.OverrideRecovery = EnvDefaultRecovery;
        }

        StatusText = $"Applied {EnvDefaultStartup}/{EnvDefaultRecovery} overrides to all services for {SelectedEnvironmentName}.";
    }

    [RelayCommand]
    private void ClearServiceOverrides()
    {
        var envId = CurrentEnvironmentId();
        if (envId is null)
            return;

        foreach (var row in ProfileServices.Where(s => s.IsSelected))
        {
            row.Entry.EnvironmentOverrides.Remove(envId);
            row.OverrideStartup = "";
            row.OverrideRecovery = "";
        }

        StatusText = "Cleared overrides on selected services (they inherit environment defaults).";
    }

    [RelayCommand]
    private void AddEnvironment()
    {
        if (string.IsNullOrWhiteSpace(NewEnvironmentName))
        {
            StatusText = "Enter an environment name.";
            return;
        }

        SyncUiToProfile();
        var name = NewEnvironmentName.Trim();
        var id = ProfileEnvironmentResolver.Slugify(name);
        if (_profile.Environments.Any(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            StatusText = $"Environment '{name}' already exists.";
            return;
        }

        _profile.Environments.Add(new ProfileEnvironment
        {
            Id = id,
            Name = name,
            DefaultStartup = "Manual",
            DefaultRecovery = "none"
        });
        EnvironmentNames.Add(name);
        SelectedEnvironmentName = name;
        NewEnvironmentName = "";
        StatusText = $"Added environment {name}.";
    }

    [RelayCommand]
    private void NewProfile()
    {
        _profile = ProfileStore.CreateTemplate("New Product");
        _currentPath = null;
        ReloadFromProfile();
        StatusText = "Started a new product profile.";
    }

    [RelayCommand]
    private async Task OpenProfileAsync()
    {
        if (PickOpenProfileAsync is null)
            return;
        var path = await PickOpenProfileAsync();
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            _profile = _store.Load(path);
            _currentPath = path;
            ReloadFromProfile();
            StatusText = $"Opened {_profile.Name}";
        }
        catch (Exception ex)
        {
            StatusText = $"Open failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Save()
    {
        try
        {
            SyncUiToProfile();
            _store.SaveToUserStore(_profile);
            _currentPath = _store.FindPathById(_profile.Id) ?? _currentPath;
            ProfilesChanged?.Invoke();
            StatusText = $"Saved '{_profile.Name}' to user profile store.";
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveAsAsync()
    {
        if (PickSaveProfileAsync is null)
            return;
        var path = await PickSaveProfileAsync();
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            SyncUiToProfile();
            if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                path += ".wsb.json";
            _store.Save(_profile, path);
            _store.SaveToUserStore(_profile);
            _currentPath = path;
            ProfilesChanged?.Invoke();
            StatusText = $"Exported to {path}";
        }
        catch (Exception ex)
        {
            StatusText = $"Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Validate()
    {
        SyncUiToProfile();
        var v = _store.Validate(_profile);
        foreach (var w in v.Warnings)
            StatusText = "WARN: " + w;
        if (!v.IsValid)
            StatusText = "Invalid: " + string.Join("; ", v.Errors);
        else
            StatusText = "Profile is valid.";
    }

    [RelayCommand]
    private void Close() => RequestClose?.Invoke();

    private void ReloadFromProfile()
    {
        ProductName = _profile.Name;
        ProductId = _profile.Id;
        Description = _profile.Description ?? "";
        RolesCsv = string.Join(", ", _profile.Roles);

        EnvironmentNames.Clear();
        foreach (var e in _profile.Environments)
            EnvironmentNames.Add(e.Name);

        SelectedEnvironmentName = _profile.DefaultEnvironment
            ?? _profile.Environments.FirstOrDefault()?.Name;

        ProfileServices.Clear();
        foreach (var s in ProfileOrdering.ForStart(_profile.Services))
        {
            var row = new BuilderServiceRowViewModel(s);
            row.LoadOverridesForEnvironment(CurrentEnvironmentId());
            ProfileServices.Add(row);
        }

        LoadEnvironmentEditor();
    }

    private void LoadEnvironmentEditor()
    {
        var env = ProfileEnvironmentResolver.FindEnvironment(_profile, SelectedEnvironmentName);
        if (env is null)
        {
            EnvDefaultStartup = "Automatic";
            EnvDefaultRecovery = "restart-3";
            return;
        }

        EnvDefaultStartup = env.DefaultStartup ?? "Automatic";
        EnvDefaultRecovery = env.DefaultRecovery ?? "restart-3";
    }

    private void SyncUiToProfile()
    {
        _profile.Name = ProductName.Trim();
        _profile.Id = string.IsNullOrWhiteSpace(ProductId)
            ? ProfileEnvironmentResolver.Slugify(ProductName)
            : ProductId.Trim();
        ProductId = _profile.Id;
        _profile.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
        _profile.Roles = ParseRoles(RolesCsv);
        if (_profile.Roles.Count > 0 && _profile.DefaultRoles.Count == 0)
            _profile.DefaultRoles = [_profile.Roles[0]];

        var env = ProfileEnvironmentResolver.FindEnvironment(_profile, SelectedEnvironmentName);
        if (env is not null)
        {
            env.DefaultStartup = EnvDefaultStartup;
            env.DefaultRecovery = EnvDefaultRecovery;
            _profile.DefaultEnvironment = env.Name;
        }

        var envId = CurrentEnvironmentId();
        foreach (var row in ProfileServices)
            row.SyncToEntry(envId);

        _profile.Services = ProfileServices.Select(r => r.Entry).ToList();
        ProfileOrdering.Renumber(_profile.Services);
        for (var i = 0; i < ProfileServices.Count; i++)
        {
            ProfileServices[i].Order = _profile.Services[i].Order;
        }
    }

    private string? CurrentEnvironmentId()
    {
        var env = ProfileEnvironmentResolver.FindEnvironment(_profile, SelectedEnvironmentName);
        return env?.Id;
    }

    private void RenumberOrders()
    {
        for (var i = 0; i < ProfileServices.Count; i++)
        {
            ProfileServices[i].Order = (i + 1) * 10;
            ProfileServices[i].Entry.Order = ProfileServices[i].Order;
        }
    }

    private static List<string> ParseRoles(string csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? []
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
