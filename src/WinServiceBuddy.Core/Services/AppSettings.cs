using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinServiceBuddy.Core.Services;

/// <summary>User preferences for the desktop app (not part of shareable profiles).</summary>
public sealed class AppSettings
{
    /// <summary>When set, launch in Profile mode and select this profile id (if it exists).</summary>
    public string? DefaultProfileId { get; set; }

    /// <summary>When true and <see cref="DefaultProfileId"/> resolves, start in Profile mode.</summary>
    public bool LaunchInProfileMode { get; set; }

    public string? DefaultEnvironment { get; set; }
    public string? DefaultRole { get; set; }
}

public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string SettingsPath { get; }

    public AppSettingsStore(string? settingsPath = null)
    {
        SettingsPath = settingsPath
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinServiceBuddy",
                "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();

            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var dir = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }
}
