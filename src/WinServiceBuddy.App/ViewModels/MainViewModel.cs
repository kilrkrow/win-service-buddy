using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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

    /// <summary>Index used as the Shift+click range anchor for checkbox multi-select.</summary>
    private int _selectionAnchorIndex = -1;

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
        Services.CollectionChanged += OnServicesCollectionChanged;
        ReloadAvailableProfiles();
        UpdateProfileGuidance();
        Refresh();
    }

    /// <summary>Optional: set by the view to open a file picker. Returns full path or null.</summary>
    public Func<Task<string?>>? PickProfileFileAsync { get; set; }

    /// <summary>Optional: set by the view to scroll/select a row in the services DataGrid.</summary>
    public Action<ServiceRowViewModel>? ScrollServiceIntoView { get; set; }

    public ObservableCollection<ServiceRowViewModel> Services { get; } = new();
    public ObservableCollection<string> PrerequisiteLines { get; } = new();
    public ObservableCollection<DependencyItemViewModel> DependencyItems { get; } = new();
    public ObservableCollection<string> AvailableProfiles { get; }
    public ObservableCollection<string> AvailableRoles { get; }
    public ObservableCollection<string> AvailableEnvironments { get; } = new();

    private ProductProfile? _activeProfile;

    [ObservableProperty]
    public partial bool IsSimpleMode { get; set; } = true;

    [ObservableProperty]
    public partial string SubstringFilter { get; set; } = "";

    [ObservableProperty]
    public partial string? SelectedProfileId { get; set; }

    [ObservableProperty]
    public partial string? SelectedRole { get; set; }

    [ObservableProperty]
    public partial string? SelectedEnvironment { get; set; }

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

    public bool ShowEnvironmentPicker => !IsSimpleMode && AvailableEnvironments.Count > 0;

    public bool ShowSimpleFilter => IsSimpleMode;

    /// <summary>Set by the view to open the Profile Builder window.</summary>
    public Action<ProductProfile?, string?>? OpenProfileBuilder { get; set; }

    public bool HasPrerequisites => PrerequisiteLines.Count > 0;

    public int SelectedCount => Services.Count(s => s.IsSelected);

    public bool HasSelection => SelectedCount > 0;

    public bool HasServices => Services.Count > 0;

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
        OnPropertyChanged(nameof(ShowEnvironmentPicker));
        OnPropertyChanged(nameof(ShowNoProfilesHelp));
        UpdateProfileGuidance();
        Refresh();
    }

    partial void OnSelectedProfileIdChanged(string? value)
    {
        LoadRolesForSelectedProfile();
        LoadEnvironmentsForSelectedProfile();
        OnPropertyChanged(nameof(ShowNoProfilesHelp));
        OnPropertyChanged(nameof(ShowRolePicker));
        OnPropertyChanged(nameof(ShowEnvironmentPicker));
        UpdateProfileGuidance();
        if (!IsSimpleMode)
            Refresh();
    }

    partial void OnSelectedRoleChanged(string? value)
    {
        if (!IsSimpleMode)
            Refresh();
    }

    partial void OnSelectedEnvironmentChanged(string? value)
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
                    ClearServices();
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
                    ClearServices();
                    return;
                }

                var role = SelectedRole;
                if (string.IsNullOrWhiteSpace(role))
                {
                    ClearServices();
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
                _activeProfile = profile;

                // Honor profile start/stop order in the grid
                var ordered = ProfileOrdering.OrderServiceNames(
                    list.Select(s => s.ServiceName), profile, forStop: false);
                list = ordered
                    .Select(n => list.First(s => string.Equals(s.ServiceName, n, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                foreach (var r in _prereqs.Evaluate(profile, role))
                {
                    var mark = r.Passed ? "PASS" : "FAIL";
                    PrerequisiteLines.Add($"[{mark}] {r.Title}: {r.Message}");
                }

                OnPropertyChanged(nameof(HasPrerequisites));
            }
            else
            {
                _activeProfile = null;
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

            // Clear without leaking PropertyChanged handlers
            ClearServices();
            _selectionAnchorIndex = -1;
            foreach (var s in list)
                Services.Add(new ServiceRowViewModel(s, lookup));

            FocusedService = focusedName is null
                ? null
                : Services.FirstOrDefault(s =>
                    string.Equals(s.ServiceName, focusedName, StringComparison.OrdinalIgnoreCase));

            if (FocusedService is not null)
                _selectionAnchorIndex = Services.IndexOf(FocusedService);

            UpdateDependenciesPanelContent();
            NotifySelectionChanged();

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

    [RelayCommand(CanExecute = nameof(CanOperateOnSelection))]
    private Task StartSelectedAsync() => RunOnSelectedAsync(names => _services.StartMany(names), forStop: false);

    [RelayCommand(CanExecute = nameof(CanOperateOnSelection))]
    private Task StopSelectedAsync() => RunOnSelectedAsync(names => _services.StopMany(names), forStop: true);

    [RelayCommand(CanExecute = nameof(CanOperateOnSelection))]
    private Task RestartSelectedAsync() => RunOnSelectedAsync(names => _services.RestartMany(names), forStop: false);

    [RelayCommand(CanExecute = nameof(CanOperateOnSelection))]
    private Task ApplyStartupAsync()
    {
        if (!EnsureElevated())
            return Task.CompletedTask;

        if (!TryMapStartup(SelectedStartup, out var startup))
        {
            StatusText = "Invalid startup type.";
            return Task.CompletedTask;
        }

        return RunOnSelectedAsync(names => _services.SetStartupTypeMany(names, startup), forStop: false);
    }

    [RelayCommand(CanExecute = nameof(CanOperateOnSelection))]
    private Task ApplyRecoveryAsync()
    {
        if (!EnsureElevated())
            return Task.CompletedTask;

        if (!TryMapRecovery(SelectedRecovery, out var preset))
        {
            StatusText = "Invalid recovery preset.";
            return Task.CompletedTask;
        }

        return RunOnSelectedAsync(names => _services.SetRecoveryMany(names, preset), forStop: false);
    }

    [RelayCommand]
    private void BuildProfile()
    {
        OpenProfileBuilder?.Invoke(null, null);
    }

    [RelayCommand]
    private void EditProfile()
    {
        if (string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            OpenProfileBuilder?.Invoke(null, null);
            return;
        }

        var profile = LoadProfile(SelectedProfileId);
        var path = _profiles.FindPathById(profile?.Id ?? SelectedProfileId);
        OpenProfileBuilder?.Invoke(profile, path);
    }

    [RelayCommand(CanExecute = nameof(CanSelectAll))]
    private void SelectAll()
    {
        foreach (var s in Services)
            s.IsSelected = true;
        if (Services.Count > 0)
            _selectionAnchorIndex = 0;
        NotifySelectionChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSelectNone))]
    private void SelectNone()
    {
        foreach (var s in Services)
            s.IsSelected = false;
        NotifySelectionChanged();
    }

    /// <summary>
    /// Checkbox / Shift+click selection for bulk actions.
    /// Plain click: toggle this row and set the range anchor.
    /// Shift+click: check every row from the anchor through this row (inclusive).
    /// </summary>
    public void ApplyCheckboxInteraction(ServiceRowViewModel row, bool shift)
    {
        var index = Services.IndexOf(row);
        if (index < 0)
            return;

        if (shift && _selectionAnchorIndex >= 0 && _selectionAnchorIndex < Services.Count)
        {
            var from = Math.Min(_selectionAnchorIndex, index);
            var to = Math.Max(_selectionAnchorIndex, index);
            for (var i = from; i <= to; i++)
                Services[i].IsSelected = true;
        }
        else
        {
            row.IsSelected = !row.IsSelected;
            _selectionAnchorIndex = index;
        }

        FocusedService = row;
        NotifySelectionChanged();
        ScrollServiceIntoView?.Invoke(row);
    }

    private bool CanOperateOnSelection() => HasSelection;

    private bool CanSelectAll() => HasServices && SelectedCount < Services.Count;

    private bool CanSelectNone() => HasSelection;

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

    /// <summary>Select the dependency in the services table (when visible) and focus it.</summary>
    [RelayCommand]
    private void NavigateToDependency(DependencyItemViewModel? dep)
    {
        if (dep is null)
            return;

        if (string.Equals(dep.StatusKind, "Missing", StringComparison.OrdinalIgnoreCase))
        {
            StatusText = $"Dependency “{dep.Title}” is not installed on this machine.";
            return;
        }

        var row = Services.FirstOrDefault(s =>
            string.Equals(s.ServiceName, dep.ServiceName, StringComparison.OrdinalIgnoreCase));

        if (row is null)
        {
            // Not in current filter/profile set — still try to surface it if SCM knows it.
            var info = _services.GetService(dep.ServiceName);
            if (info is null)
            {
                StatusText = $"Dependency “{dep.Title}” is not in the current list and was not found.";
                return;
            }

            var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [info.ServiceName] = info.DisplayName
            };
            foreach (var d in info.DependsOn)
            {
                var di = _services.GetService(d);
                if (di is not null)
                    lookup[d] = di.DisplayName;
            }

            row = new ServiceRowViewModel(info, lookup);
            Services.Add(row);
            StatusText = $"Opened “{row.Title}” (was outside the current filter/profile set).";
        }
        else
        {
            StatusText = $"Selected dependency “{row.Title}”.";
        }

        // Clear multi-check noise; highlight the navigated service
        foreach (var s in Services)
            s.IsSelected = false;
        row.IsSelected = true;
        NotifySelectionChanged();

        // Force property change even if the same instance was already focused
        // so the view re-scrolls the DataGrid to this row.
        if (ReferenceEquals(FocusedService, row))
            FocusedService = null;

        FocusedService = row;
        IsDependenciesPanelOpen = true;
        UpdateDependenciesPanelContent();

        // Explicit scroll request (selection alone does not move the viewport)
        ScrollServiceIntoView?.Invoke(row);
    }

    /// <summary>Import sample profiles from the repo/examples folder into the user profile store (dev convenience).</summary>
    [RelayCommand]
    private void LoadSampleProfiles()
    {
        var imported = 0;
        foreach (var dir in EnumerateExampleProfileDirs())
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.wsb.json"))
            {
                try
                {
                    _profiles.Import(file, machineScope: false);
                    imported++;
                }
                catch
                {
                    // skip invalid
                }
            }
        }

        ReloadAvailableProfiles();
        StatusText = imported == 0
            ? "No sample profiles found to import."
            : $"Imported {imported} sample profile file(s).";
    }

    private void ReloadAvailableProfiles()
    {
        AvailableProfiles.Clear();
        _profilePathById.Clear();

        // Only installed/imported profiles — do NOT auto-list repo samples
        // (otherwise "No profiles detected" never appears during local dev).
        foreach (var (path, profile) in _profiles.ListProfiles())
        {
            if (!AvailableProfiles.Contains(profile.Id))
                AvailableProfiles.Add(profile.Id);
            _profilePathById[profile.Id] = path;
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

    private void LoadEnvironmentsForSelectedProfile()
    {
        AvailableEnvironments.Clear();
        if (string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            SelectedEnvironment = null;
            OnPropertyChanged(nameof(ShowEnvironmentPicker));
            return;
        }

        var profile = LoadProfile(SelectedProfileId);
        if (profile is null)
        {
            SelectedEnvironment = null;
            OnPropertyChanged(nameof(ShowEnvironmentPicker));
            return;
        }

        foreach (var env in profile.Environments)
            AvailableEnvironments.Add(env.Name);

        if (AvailableEnvironments.Count == 0)
        {
            SelectedEnvironment = null;
        }
        else
        {
            var preferred = profile.DefaultEnvironment;
            SelectedEnvironment = AvailableEnvironments.FirstOrDefault(e =>
                                      string.Equals(e, preferred, StringComparison.OrdinalIgnoreCase))
                                  ?? AvailableEnvironments[0];
        }

        OnPropertyChanged(nameof(ShowEnvironmentPicker));
    }

    public void NotifyProfilesChangedFromBuilder()
    {
        ReloadAvailableProfiles();
        if (!string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            LoadRolesForSelectedProfile();
            LoadEnvironmentsForSelectedProfile();
            Refresh();
        }
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
        DependencyItems.Clear();

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
        for (var i = 0; i < FocusedService.DependsOnServiceNames.Count; i++)
        {
            var svcName = FocusedService.DependsOnServiceNames[i];
            var display = i < FocusedService.DependsOnDisplayNames.Count
                ? FocusedService.DependsOnDisplayNames[i]
                : svcName;

            DependencyItems.Add(BuildDependencyItem(svcName, display));
        }
    }

    private DependencyItemViewModel BuildDependencyItem(string serviceName, string displayName)
    {
        var info = _services.GetService(serviceName);
        if (info is null)
        {
            return new DependencyItemViewModel
            {
                ServiceName = serviceName,
                DisplayName = displayName,
                StatusKind = "Missing"
            };
        }

        var kind = info.StartupType == ServiceStartupType.Disabled
            ? "Disabled"
            : info.IsRunning
                ? "Running"
                : info.IsStopped
                    ? "Stopped"
                    : info.Status;

        return new DependencyItemViewModel
        {
            ServiceName = info.ServiceName,
            DisplayName = string.IsNullOrWhiteSpace(info.DisplayName) ? displayName : info.DisplayName,
            StatusKind = kind
        };
    }

    private static Dictionary<string, string> BuildDisplayNameLookup(IEnumerable<ServiceInfo> services)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in services)
            map[s.ServiceName] = s.DisplayName;
        return map;
    }

    private async Task RunOnSelectedAsync(Func<IEnumerable<string>, BulkOperationResult> action, bool forStop)
    {
        if (!EnsureElevated())
            return;

        var names = Services.Where(s => s.IsSelected).Select(s => s.ServiceName).ToList();
        if (names.Count == 0)
        {
            StatusText = "Select one or more services first.";
            return;
        }

        if (_activeProfile is not null)
            names = ProfileOrdering.OrderServiceNames(names, _activeProfile, forStop).ToList();

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

    private void ClearServices()
    {
        foreach (var s in Services)
            s.PropertyChanged -= OnServiceRowPropertyChanged;
        Services.Clear();
        NotifySelectionChanged();
    }

    private void OnServicesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (ServiceRowViewModel row in e.OldItems)
                row.PropertyChanged -= OnServiceRowPropertyChanged;
        }

        if (e.NewItems is not null)
        {
            foreach (ServiceRowViewModel row in e.NewItems)
                row.PropertyChanged += OnServiceRowPropertyChanged;
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            // Handlers already removed in ClearServices when we clear intentionally.
        }

        NotifySelectionChanged();
    }

    private void OnServiceRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ServiceRowViewModel.IsSelected) or null)
            NotifySelectionChanged();
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasServices));
        StartSelectedCommand.NotifyCanExecuteChanged();
        StopSelectedCommand.NotifyCanExecuteChanged();
        RestartSelectedCommand.NotifyCanExecuteChanged();
        ApplyStartupCommand.NotifyCanExecuteChanged();
        ApplyRecoveryCommand.NotifyCanExecuteChanged();
        SelectAllCommand.NotifyCanExecuteChanged();
        SelectNoneCommand.NotifyCanExecuteChanged();
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
