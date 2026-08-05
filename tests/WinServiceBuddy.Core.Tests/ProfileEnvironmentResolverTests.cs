using WinServiceBuddy.Core.Profiles;

namespace WinServiceBuddy.Core.Tests;

public class ProfileEnvironmentResolverTests
{
    private static ProductProfile Sample() => new()
    {
        Id = "prod",
        Name = "Product",
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
        Services =
        [
            new ProfileServiceEntry
            {
                ServiceName = "SvcA",
                Order = 10,
                DesiredStartup = "Disabled", // legacy fallback
                EnvironmentOverrides =
                {
                    ["production"] = new ProfileServiceEnvironmentOverride
                    {
                        DesiredStartup = "AutomaticDelayed"
                    }
                }
            },
            new ProfileServiceEntry
            {
                ServiceName = "SvcB",
                Order = 20
            }
        ]
    };

    [Fact]
    public void Resolve_Uses_Service_Override_Then_Env_Default()
    {
        var p = Sample();
        var a = ProfileEnvironmentResolver.Resolve(p, p.Services[0], "Production");
        Assert.Equal("AutomaticDelayed", a.DesiredStartup);
        Assert.Equal("restart-3", a.DesiredRecovery);
        Assert.Equal("service-override", a.Source);

        var b = ProfileEnvironmentResolver.Resolve(p, p.Services[1], "Acceptance");
        Assert.Equal("Manual", b.DesiredStartup);
        Assert.Equal("none", b.DesiredRecovery);
        Assert.Equal("environment-default", b.Source);
    }

    [Fact]
    public void Resolve_Falls_Back_To_Legacy_When_No_Env()
    {
        var p = new ProductProfile
        {
            Id = "x",
            Name = "x",
            Services =
            [
                new ProfileServiceEntry
                {
                    ServiceName = "Only",
                    DesiredStartup = "Manual",
                    DesiredRecovery = "none"
                }
            ]
        };

        var r = ProfileEnvironmentResolver.Resolve(p, p.Services[0], null);
        Assert.Equal("Manual", r.DesiredStartup);
        Assert.Equal("legacy-service", r.Source);
    }

    [Fact]
    public void Ordering_Start_And_Stop()
    {
        var services = new List<ProfileServiceEntry>
        {
            new() { ServiceName = "C", Order = 30 },
            new() { ServiceName = "A", Order = 10 },
            new() { ServiceName = "B", Order = 20 }
        };

        Assert.Equal(["A", "B", "C"], ProfileOrdering.ForStart(services).Select(s => s.ServiceName));
        Assert.Equal(["C", "B", "A"], ProfileOrdering.ForStop(services).Select(s => s.ServiceName));
    }

    [Fact]
    public void V2_RoundTrip_Save_Load()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wsbuddy-env-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new ProfileStore(dir, dir);
            var profile = Sample();
            var path = Path.Combine(dir, "p.wsb.json");
            store.Save(profile, path);
            var loaded = store.Load(path);
            Assert.Equal(2, loaded.Environments.Count);
            Assert.Equal(2, loaded.Services.Count);
            Assert.True(loaded.Services[0].Order < loaded.Services[1].Order);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void V1_Profile_Gets_Default_Environment_On_Load()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wsbuddy-env-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "legacy.wsb.json");
            File.WriteAllText(path, """
            {
              "schemaVersion": 1,
              "id": "legacy",
              "name": "Legacy",
              "services": [ { "serviceName": "Spooler", "desiredStartup": "Automatic" } ],
              "matchRules": [],
              "prerequisites": []
            }
            """);

            var store = new ProfileStore(dir, dir);
            var loaded = store.Load(path);
            Assert.NotEmpty(loaded.Environments);
            Assert.Equal("Spooler", loaded.Services[0].ServiceName);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
