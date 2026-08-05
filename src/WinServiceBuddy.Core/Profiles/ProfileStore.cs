using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinServiceBuddy.Core.Profiles;

public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true
    };

    public string UserProfilesDirectory { get; }
    public string MachineProfilesDirectory { get; }

    public ProfileStore(string? userProfilesDirectory = null, string? machineProfilesDirectory = null)
    {
        UserProfilesDirectory = userProfilesDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinServiceBuddy",
                "Profiles");

        MachineProfilesDirectory = machineProfilesDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "WinServiceBuddy",
                "Profiles");
    }

    public ProfileValidationResult Validate(ProductProfile profile)
    {
        var result = new ProfileValidationResult();
        if (string.IsNullOrWhiteSpace(profile.Id))
            result.Errors.Add("Profile id is required.");
        if (string.IsNullOrWhiteSpace(profile.Name))
            result.Errors.Add("Profile name is required.");
        if (profile.SchemaVersion < 1)
            result.Errors.Add("schemaVersion must be >= 1.");

        var serviceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var svc in profile.Services)
        {
            if (string.IsNullOrWhiteSpace(svc.ServiceName))
            {
                result.Errors.Add("A profile service entry is missing serviceName.");
                continue;
            }

            if (!serviceNames.Add(svc.ServiceName))
                result.Errors.Add($"Duplicate serviceName '{svc.ServiceName}'.");
        }

        var envIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var env in profile.Environments)
        {
            if (string.IsNullOrWhiteSpace(env.Id) && string.IsNullOrWhiteSpace(env.Name))
            {
                result.Errors.Add("An environment is missing both id and name.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(env.Id))
                env.Id = ProfileEnvironmentResolver.Slugify(env.Name);

            if (string.IsNullOrWhiteSpace(env.Name))
                env.Name = env.Id;

            if (!envIds.Add(env.Id))
                result.Errors.Add($"Duplicate environment id '{env.Id}'.");
        }

        if (!string.IsNullOrWhiteSpace(profile.DefaultEnvironment) && profile.Environments.Count > 0)
        {
            var found = ProfileEnvironmentResolver.FindEnvironment(profile, profile.DefaultEnvironment);
            if (found is null)
                result.Errors.Add($"defaultEnvironment '{profile.DefaultEnvironment}' was not found in environments.");
        }

        foreach (var rule in profile.MatchRules)
        {
            if (!string.Equals(rule.Type, "substring", StringComparison.OrdinalIgnoreCase))
                result.Warnings.Add($"Unsupported match rule type '{rule.Type}' (only substring in v1).");
            if (string.IsNullOrWhiteSpace(rule.Value))
                result.Errors.Add("Match rule value is required.");
        }

        foreach (var prereq in profile.Prerequisites)
        {
            if (string.IsNullOrWhiteSpace(prereq.Id))
                result.Errors.Add("Prerequisite id is required.");
            foreach (var check in prereq.Checks)
            {
                if (check.Type is not ("serviceExists" or "serviceRunning" or "serviceStartup"))
                    result.Warnings.Add($"Prerequisite '{prereq.Id}' has unsupported check type '{check.Type}'.");
            }
        }

        return result;
    }

    public ProductProfile Load(string path)
    {
        var json = File.ReadAllText(path);
        var profile = JsonSerializer.Deserialize<ProductProfile>(json, JsonOptions)
                      ?? throw new InvalidDataException($"Could not parse profile: {path}");
        NormalizeAfterLoad(profile);
        var validation = Validate(profile);
        if (!validation.IsValid)
            throw new InvalidDataException($"Invalid profile '{path}': {string.Join("; ", validation.Errors)}");
        return profile;
    }

    public void Save(ProductProfile profile, string path)
    {
        NormalizeBeforeSave(profile);
        var validation = Validate(profile);
        if (!validation.IsValid)
            throw new InvalidDataException(string.Join("; ", validation.Errors));

        profile.Metadata ??= new ProfileMetadata();
        profile.Metadata.ExportedAt = DateTimeOffset.UtcNow;

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(profile, JsonOptions);
        File.WriteAllText(path, json);
    }

    public void SaveToUserStore(ProductProfile profile)
    {
        Directory.CreateDirectory(UserProfilesDirectory);
        var dest = Path.Combine(UserProfilesDirectory, $"{SanitizeFileName(profile.Id)}.wsb.json");
        Save(profile, dest);
    }

    public void Import(string sourcePath, bool machineScope = false)
    {
        var profile = Load(sourcePath);
        var targetDir = machineScope ? MachineProfilesDirectory : UserProfilesDirectory;
        Directory.CreateDirectory(targetDir);
        var dest = Path.Combine(targetDir, $"{SanitizeFileName(profile.Id)}.wsb.json");
        Save(profile, dest);
    }

    public void Export(string profileId, string destinationPath)
    {
        var profile = FindById(profileId)
                      ?? throw new FileNotFoundException($"Profile '{profileId}' not found in known directories.");
        Save(profile, destinationPath);
    }

    public ProductProfile? FindById(string profileId)
    {
        foreach (var path in EnumerateProfileFiles())
        {
            try
            {
                var p = Load(path);
                if (string.Equals(p.Id, profileId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path)), profileId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(path), profileId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(path, profileId, StringComparison.OrdinalIgnoreCase))
                {
                    return p;
                }
            }
            catch
            {
                // skip invalid
            }
        }

        if (File.Exists(profileId))
            return Load(profileId);

        return null;
    }

    public string? FindPathById(string profileId)
    {
        foreach (var path in EnumerateProfileFiles())
        {
            try
            {
                var p = Load(path);
                if (string.Equals(p.Id, profileId, StringComparison.OrdinalIgnoreCase))
                    return path;
            }
            catch
            {
                // skip
            }
        }

        return null;
    }

    public IReadOnlyList<(string Path, ProductProfile Profile)> ListProfiles()
    {
        var list = new List<(string, ProductProfile)>();
        foreach (var path in EnumerateProfileFiles())
        {
            try
            {
                list.Add((path, Load(path)));
            }
            catch
            {
                // skip
            }
        }

        return list;
    }

    public IEnumerable<string> ResolveServiceNames(ProductProfile profile, string role, IEnumerable<Models.ServiceInfo> liveServices)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in profile.Services)
        {
            if (RoleMatches(entry.Roles, role))
                names.Add(entry.ServiceName);
        }

        foreach (var rule in profile.MatchRules)
        {
            if (!RoleMatches(rule.Roles, role))
                continue;
            if (!string.Equals(rule.Type, "substring", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var svc in liveServices)
            {
                if (svc.ServiceName.Contains(rule.Value, StringComparison.OrdinalIgnoreCase) ||
                    svc.DisplayName.Contains(rule.Value, StringComparison.OrdinalIgnoreCase))
                {
                    names.Add(svc.ServiceName);
                }
            }
        }

        return names;
    }

    /// <summary>Create a starter product profile with Production + Acceptance environments.</summary>
    public static ProductProfile CreateTemplate(string name, string? id = null)
    {
        var profileId = string.IsNullOrWhiteSpace(id)
            ? ProfileEnvironmentResolver.Slugify(name)
            : id.Trim();

        return new ProductProfile
        {
            SchemaVersion = 2,
            Id = profileId,
            Name = name.Trim(),
            DefaultRoles = ["Application Server"],
            Roles = ["Application Server", "Client Workstation"],
            DefaultEnvironment = "Production",
            Environments =
            [
                new ProfileEnvironment
                {
                    Id = "production",
                    Name = "Production",
                    DefaultStartup = "Automatic",
                    DefaultRecovery = "restart-3"
                },
                new ProfileEnvironment
                {
                    Id = "acceptance",
                    Name = "Acceptance",
                    DefaultStartup = "Manual",
                    DefaultRecovery = "none"
                }
            ],
            IncludeScmDependencies = true,
            Services = [],
            MatchRules = [],
            Prerequisites = [],
            Metadata = new ProfileMetadata { Author = Environment.UserName }
        };
    }

    private static void NormalizeAfterLoad(ProductProfile profile)
    {
        // v1 profiles: no environments → synthesize a Default so UI has something to bind.
        if (profile.Environments.Count == 0)
        {
            profile.Environments.Add(new ProfileEnvironment
            {
                Id = "default",
                Name = "Default",
                DefaultStartup = null,
                DefaultRecovery = null
            });
            profile.DefaultEnvironment ??= "Default";
        }

        foreach (var env in profile.Environments)
        {
            if (string.IsNullOrWhiteSpace(env.Id) && !string.IsNullOrWhiteSpace(env.Name))
                env.Id = ProfileEnvironmentResolver.Slugify(env.Name);
            if (string.IsNullOrWhiteSpace(env.Name) && !string.IsNullOrWhiteSpace(env.Id))
                env.Name = env.Id;
        }

        // Ensure orders exist
        if (profile.Services.Any(s => s.Order == 0) && profile.Services.Select(s => s.Order).Distinct().Count() == 1)
            ProfileOrdering.Renumber(profile.Services);
    }

    private static void NormalizeBeforeSave(ProductProfile profile)
    {
        if (profile.SchemaVersion < 2)
            profile.SchemaVersion = 2;

        foreach (var env in profile.Environments)
        {
            if (string.IsNullOrWhiteSpace(env.Id))
                env.Id = ProfileEnvironmentResolver.Slugify(env.Name);
        }

        if (string.IsNullOrWhiteSpace(profile.DefaultEnvironment) && profile.Environments.Count > 0)
            profile.DefaultEnvironment = profile.Environments[0].Name;

        profile.Services = ProfileOrdering.ForStart(profile.Services).ToList();
        ProfileOrdering.Renumber(profile.Services);
    }

    private IEnumerable<string> EnumerateProfileFiles()
    {
        foreach (var dir in new[] { UserProfilesDirectory, MachineProfilesDirectory })
        {
            if (!Directory.Exists(dir))
                continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.wsb.json"))
                yield return file;
            foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
                yield return file;
        }
    }

    private static bool RoleMatches(IReadOnlyList<string> roles, string activeRole)
    {
        if (roles.Count == 0)
            return true;
        if (string.Equals(activeRole, "(all)", StringComparison.OrdinalIgnoreCase))
            return true;
        return roles.Any(r => string.Equals(r, activeRole, StringComparison.OrdinalIgnoreCase));
    }

    private static string SanitizeFileName(string id)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            id = id.Replace(c, '_');
        return id;
    }
}
