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
        AvailableProfiles = new ObservableCollection<string>(
            _profiles.ListProfiles().Select(p => p.Profile.Id)
                .Concat(Directory.Exists(Path.Combine(AppContext.BaseDirectory, "profiles"))
                    ? Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "profiles"), "*.wsb.json", SearchOption.AllDirectories)
                        .Select(Path.GetFileNameWithoutExtension)
                        .Select(n => n!.Replace(".wsb", ""))
                    : Array.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x));

        // Also offer example path relative to repo when developing
        var examples = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "profiles", "examples"));
        if (Directory.Exists(examples))
        {
            foreach (var f in Directory.EnumerateFiles(examples, "*.wsb.json"))
            {
                var id = Path.GetFileName(f);
                if (!AvailableProfiles.Contains(id))
                    AvailableProfiles.Add(id);
            }
        }

        Refresh();
    }

    public ObservableCollection<ServiceRowViewModel> Services { get; } = new();
    public ObservableCollection<string> PrerequisiteLines { get; } = new();
    public ObservableCollection<string> DependencyLines { get; } = new();
    public ObservableCollection<string> AvailableProfiles { get; }

    [ObservableProperty]
    public partial bool IsSimpleMode { get; set; } = true;

    [ObservableProperty]
    public partial string SubstringFilter { get; set; } = "";

    [ObservableProperty]
    public partial string? SelectedProfileId { get; set; }

    [ObservableProperty]
    public partial string SelectedRole { get; set; } = "Server";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

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

    public string ModeLabel => IsSimpleMode ? "Simple" : "Profile";

    public IReadOnlyList<string> StartupOptions { get; } =
        ["Automatic", "AutomaticDelayed", "Manual", "Disabled"];

    public IReadOnlyList<string> RecoveryOptions { get; } =
        ["RestartThreeTimes", "TakeNoAction"];

    partial void OnIsSimpleModeChanged(bool value)
    {
        OnPropertyChanged(nameof(ModeLabel));
        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        try
        {
            IsBusy = true;
            List<ServiceInfo> list;
            PrerequisiteLines.Clear();
            DependencyLines.Clear();

            if (!IsSimpleMode && !string.IsNullOrWhiteSpace(SelectedProfileId))
            {
                var profile = LoadProfile(SelectedProfileId);
                if (profile is null)
                {
                    StatusText = $"Profile not found: {SelectedProfileId}";
                    Services.Clear();
                    return;
                }

                var live = _services.GetServices();
                var names = _profiles.ResolveServiceNames(profile, SelectedRole, live).ToList();
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

                foreach (var r in _prereqs.Evaluate(profile, SelectedRole))
                {
                    var mark = r.Passed ? "PASS" : "FAIL";
                    PrerequisiteLines.Add($"[{mark}] {r.Title}: {r.Message}");
                }

                var map = _services.BuildDependencyMap(list.Select(s => s.ServiceName));
                foreach (var (svc, deps) in map)
                {
                    DependencyLines.Add(deps.Count == 0
                        ? $"{svc} → (none)"
                        : $"{svc} → {string.Join(", ", deps)}");
                }
            }
            else
            {
                list = string.IsNullOrWhiteSpace(SubstringFilter)
                    ? _services.GetServices().ToList()
                    : _services.FindBySubstring(SubstringFilter).ToList();
            }

            Services.Clear();
            foreach (var s in list)
                Services.Add(new ServiceRowViewModel(s));

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
        {
            // Caller process can exit; Avalonia lifetime will be closed by user or we request shutdown.
            StatusText = "Elevated process launched. You can close this window.";
        }
        else
        {
            StatusText = "Elevation cancelled or failed.";
        }
    }

    [RelayCommand]
    private void UseSimpleMode() => IsSimpleMode = true;

    [RelayCommand]
    private void UseProfileMode() => IsSimpleMode = false;

    [RelayCommand]
    private void SetRoleServer()
    {
        SelectedRole = "Server";
        if (!IsSimpleMode)
            Refresh();
    }

    [RelayCommand]
    private void SetRoleClient()
    {
        SelectedRole = "Client";
        if (!IsSimpleMode)
            Refresh();
    }

    private async Task RunOnSelectedAsync(Func<IEnumerable<string>, BulkOperationResult> action)
    {
        if (!EnsureElevated())
            return;

        var names = Services.Where(s => s.IsSelected).Select(s => s.ServiceName).ToList();
        if (names.Count == 0)
        {
            // If nothing selected, operate on all visible rows (ops-friendly default).
            names = Services.Select(s => s.ServiceName).ToList();
        }

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
            var found = _profiles.FindById(idOrPath);
            if (found is not null)
                return found;

            // Resolve examples from repo / beside exe
            var candidates = new[]
            {
                idOrPath,
                Path.Combine(AppContext.BaseDirectory, "profiles", "examples", idOrPath),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "profiles", "examples", idOrPath)),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "profiles", "examples", idOrPath.EndsWith(".json") ? idOrPath : idOrPath + ".wsb.json"))
            };

            foreach (var c in candidates)
            {
                if (File.Exists(c))
                    return _profiles.Load(c);
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
