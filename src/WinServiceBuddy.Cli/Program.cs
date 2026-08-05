using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;
using WinServiceBuddy.Core.Models;
using WinServiceBuddy.Core.Prerequisites;
using WinServiceBuddy.Core.Profiles;
using WinServiceBuddy.Core.Services;

var manager = new WindowsServiceManager();
var profiles = new ProfileStore();

var root = new RootCommand("Win Service Buddy — manage product Windows Services and documented prerequisites.");

var jsonOption = new Option<bool>("--json") { Description = "Emit JSON instead of tables" };
var substringOption = new Option<string?>("--substring", "-s") { Description = "Filter by service/display name substring" };
var profileOption = new Option<string?>("--profile", "-p") { Description = "Profile id or path (.wsb.json)" };
var roleOption = new Option<string?>("--role", "-r")
{
    Description = "Profile-defined role name (from the profile’s roles list; not a fixed client/server enum)"
};
var environmentOption = new Option<string?>("--environment", "-e")
{
    Description = "Profile environment name/id (e.g. Production, Acceptance)"
};

// list
var listCmd = new Command("list", "List services (simple substring or profile mode)");
listCmd.Options.Add(substringOption);
listCmd.Options.Add(profileOption);
listCmd.Options.Add(roleOption);
listCmd.Options.Add(environmentOption);
listCmd.Options.Add(jsonOption);
listCmd.SetAction(parse =>
{
    var services = ResolveServices(manager, profiles, parse.GetValue(substringOption), parse.GetValue(profileOption), parse.GetValue(roleOption), parse.GetValue(environmentOption), forStop: false);
    PrintServices(services, parse.GetValue(jsonOption));
    return 0;
});

// status (alias of list with emphasis)
var statusCmd = new Command("status", "Show status for matching services");
statusCmd.Options.Add(substringOption);
statusCmd.Options.Add(profileOption);
statusCmd.Options.Add(roleOption);
statusCmd.Options.Add(environmentOption);
statusCmd.Options.Add(jsonOption);
statusCmd.SetAction(parse =>
{
    var services = ResolveServices(manager, profiles, parse.GetValue(substringOption), parse.GetValue(profileOption), parse.GetValue(roleOption), parse.GetValue(environmentOption), forStop: false);
    PrintServices(services, parse.GetValue(jsonOption));
    return 0;
});

// start / stop / restart
root.Add(BuildLifecycleCommand("start", "Start services", (m, names) => m.StartMany(names), forStop: false));
root.Add(BuildLifecycleCommand("stop", "Stop services", (m, names) => m.StopMany(names), forStop: true));
root.Add(BuildLifecycleCommand("restart", "Restart services", (m, names) => m.RestartMany(names), forStop: false));

// set-startup
var setStartupCmd = new Command("set-startup", "Bulk-set service startup type");
var startupTypeArg = new Argument<string>("type") { Description = "automatic | delayed | manual | disabled" };
setStartupCmd.Arguments.Add(startupTypeArg);
setStartupCmd.Options.Add(substringOption);
setStartupCmd.Options.Add(profileOption);
setStartupCmd.Options.Add(roleOption);
setStartupCmd.Options.Add(environmentOption);
setStartupCmd.Options.Add(jsonOption);
setStartupCmd.SetAction(parse =>
{
    if (!Elevation.IsElevated())
    {
        Console.Error.WriteLine("Administrator rights required. Re-run elevated or use: wsbuddy elevate -- ...");
        return 3;
    }

    if (!TryParseStartup(parse.GetValue(startupTypeArg)!, out var startup))
    {
        Console.Error.WriteLine("type must be automatic | delayed | manual | disabled");
        return 2;
    }

    var services = ResolveServices(manager, profiles, parse.GetValue(substringOption), parse.GetValue(profileOption), parse.GetValue(roleOption), parse.GetValue(environmentOption), forStop: false);
    var result = manager.SetStartupTypeMany(services.Select(s => s.ServiceName), startup);
    PrintBulk(result, parse.GetValue(jsonOption));
    return result.AllSucceeded ? 0 : 1;
});

// set-recovery
var setRecoveryCmd = new Command("set-recovery", "Bulk-set crash recovery preset");
var recoveryArg = new Argument<string>("preset") { Description = "restart-3 | none" };
setRecoveryCmd.Arguments.Add(recoveryArg);
setRecoveryCmd.Options.Add(substringOption);
setRecoveryCmd.Options.Add(profileOption);
setRecoveryCmd.Options.Add(roleOption);
setRecoveryCmd.Options.Add(environmentOption);
setRecoveryCmd.Options.Add(jsonOption);
setRecoveryCmd.SetAction(parse =>
{
    if (!Elevation.IsElevated())
    {
        Console.Error.WriteLine("Administrator rights required.");
        return 3;
    }

    if (!TryParseRecovery(parse.GetValue(recoveryArg)!, out var preset))
    {
        Console.Error.WriteLine("preset must be restart-3 | none");
        return 2;
    }

    var services = ResolveServices(manager, profiles, parse.GetValue(substringOption), parse.GetValue(profileOption), parse.GetValue(roleOption), parse.GetValue(environmentOption), forStop: false);
    var result = manager.SetRecoveryMany(services.Select(s => s.ServiceName), preset);
    PrintBulk(result, parse.GetValue(jsonOption));
    return result.AllSucceeded ? 0 : 1;
});

// prereq check
var prereqCmd = new Command("prereq", "Prerequisite operations");
var prereqCheckCmd = new Command("check", "Evaluate profile prerequisites for a role");
prereqCheckCmd.Options.Add(profileOption);
prereqCheckCmd.Options.Add(roleOption);
prereqCheckCmd.Options.Add(jsonOption);
prereqCheckCmd.SetAction(parse =>
{
    var profileId = parse.GetValue(profileOption);
    if (string.IsNullOrWhiteSpace(profileId))
    {
        Console.Error.WriteLine("--profile is required");
        return 2;
    }

    var profile = profiles.FindById(profileId);
    if (profile is null)
    {
        Console.Error.WriteLine($"Profile not found: {profileId}");
        return 4;
    }

    var role = parse.GetValue(roleOption)
               ?? profile.DefaultRoles.FirstOrDefault()
               ?? profile.Roles.FirstOrDefault()
               ?? "(all)";
    var evaluator = new PrerequisiteEvaluator(manager);
    var results = evaluator.Evaluate(profile, role);
    if (parse.GetValue(jsonOption))
    {
        Console.WriteLine(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    }
    else
    {
        foreach (var r in results)
        {
            var mark = r.Passed ? "PASS" : "FAIL";
            Console.WriteLine($"[{mark}] {r.Title}: {r.Message}");
            if (!string.IsNullOrWhiteSpace(r.DocRef))
                Console.WriteLine($"       doc: {r.DocRef}");
        }
    }

    return results.Any(r => !r.Passed && r.Severity == "error") ? 1 : 0;
});
prereqCmd.Subcommands.Add(prereqCheckCmd);

// profile commands
var profileCmd = new Command("profile", "Import/export/list/validate profiles");
var profileListCmd = new Command("list", "List known profiles");
profileListCmd.SetAction(_ =>
{
    foreach (var (path, p) in profiles.ListProfiles())
        Console.WriteLine($"{p.Id,-40} {p.Name,-40} {path}");
    return 0;
});

var profileShowCmd = new Command("show", "Show a profile");
var profileIdArg = new Argument<string>("id");
profileShowCmd.Arguments.Add(profileIdArg);
profileShowCmd.SetAction(parse =>
{
    var p = profiles.FindById(parse.GetValue(profileIdArg)!);
    if (p is null)
    {
        Console.Error.WriteLine("Profile not found");
        return 4;
    }

    Console.WriteLine(JsonSerializer.Serialize(p, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
});

var profileImportCmd = new Command("import", "Import a .wsb.json profile");
var importPathArg = new Argument<string>("path");
profileImportCmd.Arguments.Add(importPathArg);
profileImportCmd.SetAction(parse =>
{
    try
    {
        profiles.Import(parse.GetValue(importPathArg)!);
        Console.WriteLine("Imported.");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 4;
    }
});

var profileExportCmd = new Command("export", "Export a profile by id");
var exportIdArg = new Argument<string>("id");
var exportPathArg = new Argument<string>("path");
profileExportCmd.Arguments.Add(exportIdArg);
profileExportCmd.Arguments.Add(exportPathArg);
profileExportCmd.SetAction(parse =>
{
    try
    {
        profiles.Export(parse.GetValue(exportIdArg)!, parse.GetValue(exportPathArg)!);
        Console.WriteLine("Exported.");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 4;
    }
});

var profileValidateCmd = new Command("validate", "Validate a profile file");
var validatePathArg = new Argument<string>("path");
profileValidateCmd.Arguments.Add(validatePathArg);
profileValidateCmd.SetAction(parse =>
{
    try
    {
        var p = profiles.Load(parse.GetValue(validatePathArg)!);
        var v = profiles.Validate(p);
        foreach (var w in v.Warnings)
            Console.WriteLine($"WARN: {w}");
        foreach (var e in v.Errors)
            Console.WriteLine($"ERROR: {e}");
        Console.WriteLine(v.IsValid ? "Valid." : "Invalid.");
        return v.IsValid ? 0 : 4;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 4;
    }
});

profileCmd.Subcommands.Add(profileListCmd);
profileCmd.Subcommands.Add(profileShowCmd);
profileCmd.Subcommands.Add(profileImportCmd);
profileCmd.Subcommands.Add(profileExportCmd);
profileCmd.Subcommands.Add(profileValidateCmd);

// elevate
var elevateCmd = new Command("elevate", "Relaunch elevated (optional args after --)");
elevateCmd.SetAction(_ =>
{
    if (Elevation.IsElevated())
    {
        Console.WriteLine("Already elevated.");
        return 0;
    }

    return Elevation.TryRelaunchElevated() ? 0 : 1;
});

// whoami elevation helper
var infoCmd = new Command("info", "Show runtime info");
infoCmd.SetAction(_ =>
{
    Console.WriteLine($"Elevated: {Elevation.IsElevated()}");
    Console.WriteLine($"User: {Environment.UserName}");
    Console.WriteLine($"Machine: {Environment.MachineName}");
    Console.WriteLine($"User profiles: {profiles.UserProfilesDirectory}");
    Console.WriteLine($"Machine profiles: {profiles.MachineProfilesDirectory}");
    return 0;
});

// gui launcher
var guiCmd = new Command("gui", "Launch the graphical UI (WinServiceBuddy.App)");
guiCmd.SetAction(_ =>
{
    var candidates = new[]
    {
        Path.Combine(AppContext.BaseDirectory, "WinServiceBuddy.App.exe"),
        Path.Combine(AppContext.BaseDirectory, "..", "WinServiceBuddy.App", "WinServiceBuddy.App.exe"),
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "WinServiceBuddy.App", "bin", "Debug", "net10.0", "WinServiceBuddy.App.exe")),
    };

    foreach (var path in candidates)
    {
        if (!File.Exists(path))
            continue;
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return 0;
    }

    Console.Error.WriteLine("Could not locate WinServiceBuddy.App.exe. Build the App project and run it directly, or publish a combined layout.");
    return 1;
});

root.Subcommands.Add(listCmd);
root.Subcommands.Add(statusCmd);
root.Subcommands.Add(setStartupCmd);
root.Subcommands.Add(setRecoveryCmd);
root.Subcommands.Add(prereqCmd);
root.Subcommands.Add(profileCmd);
root.Subcommands.Add(elevateCmd);
root.Subcommands.Add(infoCmd);
root.Subcommands.Add(guiCmd);

return root.Parse(args).Invoke();

Command BuildLifecycleCommand(string name, string description, Func<IWindowsServiceManager, IEnumerable<string>, BulkOperationResult> action, bool forStop)
{
    var cmd = new Command(name, description);
    cmd.Options.Add(substringOption);
    cmd.Options.Add(profileOption);
    cmd.Options.Add(roleOption);
    cmd.Options.Add(environmentOption);
    cmd.Options.Add(jsonOption);
    cmd.SetAction(parse =>
    {
        if (!Elevation.IsElevated())
        {
            Console.Error.WriteLine("Administrator rights required for service control.");
            return 3;
        }

        var services = ResolveServices(manager, profiles, parse.GetValue(substringOption), parse.GetValue(profileOption), parse.GetValue(roleOption), parse.GetValue(environmentOption), forStop);
        if (services.Count == 0)
        {
            Console.Error.WriteLine("No services matched.");
            return 1;
        }

        var result = action(manager, services.Select(s => s.ServiceName));
        PrintBulk(result, parse.GetValue(jsonOption));
        return result.AllSucceeded ? 0 : 1;
    });
    return cmd;
}

static List<ServiceInfo> ResolveServices(
    IWindowsServiceManager manager,
    ProfileStore store,
    string? substring,
    string? profileId,
    string? role,
    string? environment,
    bool forStop)
{
    if (!string.IsNullOrWhiteSpace(profileId))
    {
        var profile = store.FindById(profileId) ?? store.Load(profileId);
        var live = manager.GetServices();
        var resolvedRole = role
                           ?? profile.DefaultRoles.FirstOrDefault()
                           ?? profile.Roles.FirstOrDefault()
                           ?? "(all)";
        var names = store.ResolveServiceNames(profile, resolvedRole, live).ToList();

        if (profile.IncludeScmDependencies)
        {
            var expanded = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            foreach (var n in names)
            {
                foreach (var dep in manager.GetDependsOn(n))
                    expanded.Add(dep);
            }
            names = expanded.ToList();
        }

        names = ProfileOrdering.OrderServiceNames(names, profile, forStop).ToList();
        var byName = ServiceFilter.ByNames(live, names).ToDictionary(s => s.ServiceName, StringComparer.OrdinalIgnoreCase);
        return names.Where(byName.ContainsKey).Select(n => byName[n]).ToList();
    }

    if (!string.IsNullOrWhiteSpace(substring))
        return manager.FindBySubstring(substring).ToList();

    return manager.GetServices().ToList();
}

static void PrintServices(IReadOnlyList<ServiceInfo> services, bool json)
{
    if (json)
    {
        Console.WriteLine(JsonSerializer.Serialize(services, new JsonSerializerOptions { WriteIndented = true }));
        return;
    }

    Console.WriteLine($"{"Status",-14} {"Startup",-18} {"Recovery",-18} {"ServiceName",-40} DisplayName");
    Console.WriteLine(new string('-', 120));
    foreach (var s in services)
    {
        Console.WriteLine($"{s.Status,-14} {s.StartupType,-18} {s.RecoverySummary,-18} {s.ServiceName,-40} {s.DisplayName}");
    }

    Console.WriteLine();
    Console.WriteLine($"{services.Count} service(s); {services.Count(s => s.IsRunning)} running.");
}

static void PrintBulk(BulkOperationResult result, bool json)
{
    if (json)
    {
        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        return;
    }

    foreach (var r in result.Results)
    {
        var mark = r.Success ? "OK" : "FAIL";
        Console.WriteLine($"[{mark}] {r.ServiceName}: {r.Message}");
    }

    Console.WriteLine($"{result.Succeeded} succeeded, {result.Failed} failed.");
}

static bool TryParseStartup(string value, out ServiceStartupType type)
{
    type = value.ToLowerInvariant() switch
    {
        "automatic" or "auto" => ServiceStartupType.Automatic,
        "delayed" or "automaticdelayed" or "auto-delayed" => ServiceStartupType.AutomaticDelayed,
        "manual" or "demand" => ServiceStartupType.Manual,
        "disabled" => ServiceStartupType.Disabled,
        _ => ServiceStartupType.Unknown
    };
    return type != ServiceStartupType.Unknown;
}

static bool TryParseRecovery(string value, out RecoveryPreset preset)
{
    switch (value.ToLowerInvariant())
    {
        case "restart-3":
        case "restart3":
        case "retry-3":
        case "retry3":
            preset = RecoveryPreset.RestartThreeTimes;
            return true;
        case "none":
        case "no-action":
        case "take-no-action":
            preset = RecoveryPreset.TakeNoAction;
            return true;
        default:
            preset = RecoveryPreset.Unchanged;
            return false;
    }
}
