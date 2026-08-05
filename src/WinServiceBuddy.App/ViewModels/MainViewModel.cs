using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinServiceBuddy.Core.Models;
using WinServiceBuddy.Core.Prerequisites;
using WinServiceBuddy.Core.Profiles;
using WinServiceBuddy.Core.Services;

namespace WinServiceBuddy.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IWindowsServiceManager _services;
    private readonly ProfileStore _profiles;
    private readonly PrerequisiteEvaluator _prereqs;
    private readonly Dictionary<string, string> _profilePathById = new(StringComparer.OrdinalIgnoreCase);

    public MainViewModel()
        : this(new WindowsServiceManager(), new ProfileStore())
    {
    }

    public MainViewModel(IWindowsServiceManager services, ProfileStore profiles)
    {
        _services = services;
        _profiles = profiles;
        _prereqs = new PrerequisiteEvaluator(services);
        IsElevated = Elevation.IsElevated();
        HostName = Environment.MachineName;
        AvailableProfiles = new ObservableCollection<string>();
        AvailableRoles = new ObservableCollection<string>();
        ReloadAvailableProfiles();
        UpdateProfileGuidance();
        Refresh();
    }

    /// <summary>Optional: set by the view to open a file picker. Returns full path or null.</summary>
    public Func<Task<string?>>? PickProfileFileAsync { get; set; }

    public ObservableCollection<ServiceRowViewModel> Services { get; } = new();
    public ObservableCollection<string> PrerequisiteLines { get; } = new();
    public ObservableCollection<string> DependencyLines { get; } = new();
    public ObservableCollection<string> AvailableProfiles { get; }
    public ObservableCollection<string> AvailableRoles { get; }

    [ObservableProperty]
    public partial bool IsSimpleMode { get; set; } = true;

    [ObservableProperty]
    public partial string SubstringFilter { get; set; } = "";

    [ObservableProperty]
    public partial string? SelectedProfileId { get; set; }

    [ObservableProperty]
    public partial string? SelectedRole { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    [ObservableProperty]
    public partial string ProfileGuidance { get; set; } = "";

    [ObservableProperty]
    public partial bool IsElevated { get; set; }

    [ObservableProperty]
    public partial string HostName { get; set; } = "";

    [ObservableProperty]
    public partial string SelectedStartup { get; set; } = "Automatic";

    [ObservableProperty]
    public partial string SelectedRecovery { get; set; } = "RestartThreeTimes";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsDependenciesPanelOpen { get; set; }

    [ObservableProperty]
    public partial ServiceRowViewModel? FocusedService { get; set; }

    [ObservableProperty]
    public partial string DependenciesHeader { get; set; } = "Dependencies";

    [ObservableProperty]
    public partial string DependenciesEmptyMessage { get; set; } = "Select a service to see dependencies.";

    [ObservableProperty]
    public partial bool ShowDependenciesEmptyMessage { get; set; } = true;

    public bool HasProfiles => AvailableProfiles.Count > 0;

    public bool ShowNoProfilesHelp => !IsSimpleMode && !HasProfiles && string.IsNullOrWhiteSpace(SelectedProfileId);

    public bool ShowProfilePicker => !IsSimpleMode;

    public bool ShowRolePicker => !IsSimpleMode && AvailableRoles.Count > 0;

    public bool ShowSimpleFilter => IsSimpleMode;

    public bool HasPrerequisites => PrerequisiteLines.Count > 0;

    public string ModeLabel => IsSimpleMode ? "Simple" : "Profile";

    public string MainGridColumnDefinitions => IsDependenciesPanelOpen ? "260,*,300" : "260,*";

    public IReadOnlyList<string> StartupOptions { get; } =
        ["Automatic", "AutomaticDelayed", "Manual", "Disabled"];

    public IReadOnlyList<string> RecoveryOptions { get; } =
        ["RestartThreeTimes", "TakeNoAction"];

    partial void OnIsSimpleModeChanged(bool value)
    {
        OnPropertyChanged(nameof(ModeLabel));
        OnPropertyChanged(nameof(ShowSimpleFilter));
        OnPropertyChanged(nameof(ShowProfilePicker));
        OnPropertyChanged(nameof(ShowRolePicker));
        OnPropertyChanged(nameof(ShowNoProfilesHelp));
        UpdateProfileGuidance();
        Refresh();
    }

    partial void OnSelectedProfileIdChanged(string? value)
    {
        LoadRolesForSelectedProfile();
        OnPropertyChanged(nameof(ShowNoProfilesHelp));
        OnPropertyChanged(nameof(ShowRolePicker));
        UpdateProfileGuidance();
        if (!IsSimpleMode)
            Refresh();
    }

    partial void OnSelectedRoleChanged(string? value)
    {
        if (!IsSimpleMode)
            Refresh();
    }

    partial void OnFocusedServiceChanged(ServiceRowViewModel? value)
    {
        UpdateDependenciesPanelContent();
    }

    partial void OnIsDependenciesPanelOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(MainGridColumnDefinitions));
        UpdateDependenciesPanelContent();
    }

    [RelayCommand]
    private void Refresh()
    {
        try
        {
            IsBusy = true;
            List<ServiceInfo> list;
            PrerequisiteLines.Clear();

            // Keep focused service name across rebuild
            var focusedName = FocusedService?.ServiceName;

            if (!IsSimpleMode)
            {
                if (string.IsNullOrWhiteSpace(SelectedProfileId))
                {
                    Services.Clear();
                    FocusedService = null;
                    UpdateDependenciesPanelContent();
                    StatusText = HasProfiles
                        ? "Choose a profile and role to load services."
                        : "No profiles detected — browse to a .wsb.json or switch to Simple mode.";
                    return;
                }

                var profile = LoadProfile(SelectedProfileId);
                if (profile is null)
                {
                    StatusText = $"Profile not found: {SelectedProfileId}";
                    Services.Clear();
                    return;
                }

                var role = SelectedRole;
                if (string.IsNullOrWhiteSpace(role))
                {
                    Services.Clear();
                    StatusText = "Select a role defined by this profile.";
                    return;
                }

                var live = _services.GetServices();
                var names = _profiles.ResolveServiceNames(profile, role, live).ToList();
                if (profile.IncludeScmDependencies)
                {
                    var expanded = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
                    foreach (var n in names.ToList())
                    {
                        foreach (var dep in _services.GetDependsOn(n))
                            expanded.Add(dep);
                    }

                    names = expanded.ToList();
                }

                list = ServiceFilter.ByNames(live, names).ToList();

                foreach (var r in _prereqs.Evaluate(profile, role))
                {
                    var mark = r.Passed ? "PASS" : "FAIL";
                    PrerequisiteLines.Add($"[{mark}] {r.Title}: {r.Message}");
                }

                OnPropertyChanged(nameof(HasPrerequisites));
            }
            else
            {
                OnPropertyChanged(nameof(HasPrerequisites));
                list = string.IsNullOrWhiteSpace(SubstringFilter)
                    ? _services.GetServices().ToList()
                    : _services.FindBySubstring(SubstringFilter).ToList();
            }

            var lookup = BuildDisplayNameLookup(list);
            // Enrich lookup with depends-on services that may not be in the filtered list
            foreach (var s in list)
            {
                foreach (var dep in s.DependsOn)
                {
                    if (lookup.ContainsKey(dep))
                        continue;
                    var depInfo = _services.GetService(dep);
                    if (depInfo is not null)
                        lookup[dep] = depInfo.DisplayName;
                }
            }

            Services.Clear();
            foreach (var s in list)
                Services.Add(new ServiceRowViewModel(s, lookup));

            FocusedService = focusedName is null
                ? null
                : Services.FirstOrDefault(s =>
                    string.Equals(s.ServiceName, focusedName, StringComparison.OrdinalIgnoreCase));

            UpdateDependenciesPanelContent();

            var running = list.Count(s => s.IsRunning);
            StatusText =
                $"{list.Count} services · {running} running · {(IsElevated ? "elevated" : "NOT elevated")} · {HostName}";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task StartSelectedAsync() => RunOnSelectedAsync(names => _services.StartMany(names));

    [RelayCommand]
    private Task StopSelectedAsync() => RunOnSelectedAsync(names => _services.StopMany(names));

    [RelayCommand]
    private Task RestartSelectedAsync() => RunOnSelectedAsync(names => _services.RestartMany(names));

    [RelayCommand]
    private Task ApplyStartupAsync()
    {
        if (!EnsureElevated())
            return Task.CompletedTask;

        if (!TryMapStartup(SelectedStartup, out var startup))
        {
            StatusText = "Invalid startup type.";
            return Task.CompletedTask;
        }

        return RunOnSelectedAsync(names => _services.SetStartupTypeMany(names, startup));
    }

    [RelayCommand]
    private Task ApplyRecoveryAsync()
    {
        if (!EnsureElevated())
            return Task.CompletedTask;

        if (!TryMapRecovery(SelectedRecovery, out var preset))
        {
            StatusText = "Invalid recovery preset.";
            return Task.CompletedTask;
        }

        return RunOnSelectedAsync(names => _services.SetRecoveryMany(names, preset));
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var s in Services)
            s.IsSelected = true;
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var s in Services)
            s.IsSelected = false;
    }

    [RelayCommand]
    private void RelaunchElevated()
    {
        if (Elevation.IsElevated())
        {
            StatusText = "Already elevated.";
            return;
        }

        if (Elevation.TryRelaunchElevated())
            StatusText = "Elevated process launched. You can close this window.";
        else
            StatusText = "Elevation cancelled or failed.";
    }

    [RelayCommand]
    private void UseSimpleMode() => IsSimpleMode = true;

    [RelayCommand]
    private void UseProfileMode()
    {
        IsSimpleMode = false;
        UpdateProfileGuidance();
    }

    [RelayCommand]
    private async Task BrowseProfileAsync()
    {
        if (PickProfileFileAsync is null)
        {
            StatusText = "File picker is not available.";
            return;
        }

        var path = await PickProfileFileAsync();
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            var profile = _profiles.Load(path);
            // Import into user store so it shows up next launch
            _profiles.Import(path, machineScope: false);
            ReloadAvailableProfiles();

            if (!AvailableProfiles.Contains(profile.Id))
                AvailableProfiles.Add(profile.Id);

            _profilePathById[profile.Id] = path;
            IsSimpleMode = false;
            SelectedProfileId = profile.Id;
            StatusText = $"Loaded profile '{profile.Name}'.";
        }
        catch (Exception ex)
        {
            StatusText = $"Could not load profile: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ShowDependenciesFor(ServiceRowViewModel? row)
    {
        if (row is null)
            return;

        FocusedService = row;
        IsDependenciesPanelOpen = true;
        UpdateDependenciesPanelContent();
    }

    [RelayCommand]
    private void ToggleDependenciesFor(ServiceRowViewModel? row)
    {
        if (row is null)
            return;

        if (IsDependenciesPanelOpen &&
            FocusedService is not null &&
            string.Equals(FocusedService.ServiceName, row.ServiceName, StringComparison.OrdinalIgnoreCase))
        {
            IsDependenciesPanelOpen = false;
            return;
        }

        ShowDependenciesFor(row);
    }

    [RelayCommand]
    private void CloseDependenciesPanel()
    {
        IsDependenciesPanelOpen = false;
    }

    private void ReloadAvailableProfiles()
    {
        AvailableProfiles.Clear();
        _profilePathById.Clear();

        foreach (var (path, profile) in _profiles.ListProfiles())
        {
            if (!AvailableProfiles.Contains(profile.Id))
                AvailableProfiles.Add(profile.Id);
            _profilePathById[profile.Id] = path;
        }

        // Dev-time examples next to repo
        foreach (var dir in EnumerateExampleProfileDirs())
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.wsb.json"))
            {
                try
                {
                    var profile = _profiles.Load(file);
                    if (!AvailableProfiles.Contains(profile.Id))
                        AvailableProfiles.Add(profile.Id);
                    _profilePathById[profile.Id] = file;
                }
                catch
                {
                    // skip invalid samples
                }
            }
        }

        OnPropertyChanged(nameof(HasProfiles));
        OnPropertyChanged(nameof(ShowNoProfilesHelp));
        UpdateProfileGuidance();
    }

    private static IEnumerable<string> EnumerateExampleProfileDirs()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "profiles", "examples"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "profiles", "examples")),
            Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "profiles", "examples"))
        };

        foreach (var c in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(c))
                yield return c;
        }
    }

    private void LoadRolesForSelectedProfile()
    {
        AvailableRoles.Clear();
        if (string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            SelectedRole = null;
            OnPropertyChanged(nameof(ShowRolePicker));
            return;
        }

        var profile = LoadProfile(SelectedProfileId);
        if (profile is null)
        {
            SelectedRole = null;
            OnPropertyChanged(nameof(ShowRolePicker));
            return;
        }

        var roles = profile.Roles.Count > 0
            ? profile.Roles
            : profile.DefaultRoles;

        foreach (var role in roles.Distinct(StringComparer.OrdinalIgnoreCase))
            AvailableRoles.Add(role);

        if (AvailableRoles.Count == 0)
        {
            // Profile with no roles: treat as single implicit role covering all entries
            AvailableRoles.Add("(all)");
        }

        var preferred = profile.DefaultRoles.FirstOrDefault(r =>
            AvailableRoles.Any(a => string.Equals(a, r, StringComparison.OrdinalIgnoreCase)));

        SelectedRole = preferred
                       ?? AvailableRoles.FirstOrDefault();

        OnPropertyChanged(nameof(ShowRolePicker));
    }

    private void UpdateProfileGuidance()
    {
        if (IsSimpleMode)
        {
            ProfileGuidance = "Simple mode: filter services by name substring on this machine.";
            return;
        }

        if (!HasProfiles && string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            ProfileGuidance =
                "No profiles detected. Switch to Simple mode, or Browse to import a .wsb.json profile. Roles (e.g. Application Server, Database Host) come from the profile — they are not fixed.";
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            ProfileGuidance = "Select a profile. Roles listed are defined by that profile.";
            return;
        }

        if (AvailableRoles.Count == 0)
        {
            ProfileGuidance = "This profile defines no roles yet.";
            return;
        }

        ProfileGuidance =
            $"Profile role “{SelectedRole ?? "—"}”: services and prerequisites for this machine’s function. Roles are profile-specific.";
    }

    private void UpdateDependenciesPanelContent()
    {
        DependencyLines.Clear();

        if (!IsDependenciesPanelOpen)
        {
            ShowDependenciesEmptyMessage = false;
            return;
        }

        if (FocusedService is null)
        {
            DependenciesHeader = "Dependencies";
            DependenciesEmptyMessage = "Select a service to see dependencies.";
            ShowDependenciesEmptyMessage = true;
            return;
        }

        DependenciesHeader = $"Dependencies — {FocusedService.Title}";

        if (!FocusedService.HasDependencies)
        {
            DependenciesEmptyMessage = "No dependencies.";
            ShowDependenciesEmptyMessage = true;
            return;
        }

        ShowDependenciesEmptyMessage = false;
        foreach (var name in FocusedService.DependsOnDisplayNames)
            DependencyLines.Add(name);
    }

    private static Dictionary<string, string> BuildDisplayNameLookup(IEnumerable<ServiceInfo> services)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in services)
            map[s.ServiceName] = s.DisplayName;
        return map;
    }

    private async Task RunOnSelectedAsync(Func<IEnumerable<string>, BulkOperationResult> action)
    {
        if (!EnsureElevated())
            return;

        var names = Services.Where(s => s.IsSelected).Select(s => s.ServiceName).ToList();
        if (names.Count == 0)
            names = Services.Select(s => s.ServiceName).ToList();

        if (names.Count == 0)
        {
            StatusText = "No services in view.";
            return;
        }

        try
        {
            IsBusy = true;
            var result = await Task.Run(() => action(names));
            StatusText = $"{result.Succeeded} succeeded, {result.Failed} failed.";
            Refresh();
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool EnsureElevated()
    {
        if (Elevation.IsElevated())
            return true;
        StatusText = "Administrator rights required. Click 'Run elevated'.";
        return false;
    }

    private ProductProfile? LoadProfile(string idOrPath)
    {
        try
        {
            if (_profilePathById.TryGetValue(idOrPath, out var mapped) && File.Exists(mapped))
                return _profiles.Load(mapped);

            var found = _profiles.FindById(idOrPath);
            if (found is not null)
                return found;

            if (File.Exists(idOrPath))
                return _profiles.Load(idOrPath);

            foreach (var dir in EnumerateExampleProfileDirs())
            {
                var candidate = Path.Combine(dir, idOrPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    ? idOrPath
                    : idOrPath + ".wsb.json");
                if (File.Exists(candidate))
                    return _profiles.Load(candidate);
            }
        }
        catch
        {
            // fall through
        }

        return null;
    }

    private static bool TryMapStartup(string value, out ServiceStartupType type)
    {
        type = value switch
        {
            "Automatic" => ServiceStartupType.Automatic,
            "AutomaticDelayed" => ServiceStartupType.AutomaticDelayed,
            "Manual" => ServiceStartupType.Manual,
            "Disabled" => ServiceStartupType.Disabled,
            _ => ServiceStartupType.Unknown
        };
        return type != ServiceStartupType.Unknown;
    }

    private static bool TryMapRecovery(string value, out RecoveryPreset preset)
    {
        preset = value switch
        {
            "RestartThreeTimes" => RecoveryPreset.RestartThreeTimes,
            "TakeNoAction" => RecoveryPreset.TakeNoAction,
            _ => RecoveryPreset.Unchanged
        };
        return preset != RecoveryPreset.Unchanged;
    }
}
