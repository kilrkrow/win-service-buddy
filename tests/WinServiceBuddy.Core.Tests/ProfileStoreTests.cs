using WinServiceBuddy.Core.Models;
using WinServiceBuddy.Core.Profiles;

namespace WinServiceBuddy.Core.Tests;

public class ProfileStoreTests
{
    [Fact]
    public void Validate_Requires_Id_And_Name()
    {
        var store = new ProfileStore();
        var result = store.Validate(new ProductProfile());
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("id", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Save_And_Load_RoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wsbuddy-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new ProfileStore(dir, dir);
            var profile = new ProductProfile
            {
                Id = "sample-product",
                Name = "Sample Product",
                Roles = ["Application Server", "Client Workstation"],
                DefaultRoles = ["Application Server"],
                Services =
                [
                    new ProfileServiceEntry
                    {
                        ServiceName = "Example.AppCore",
                        Roles = ["Application Server"],
                        DesiredStartup = "Automatic",
                        DesiredRecovery = "restart-3"
                    }
                ],
                MatchRules =
                [
                    new ProfileMatchRule { Type = "substring", Value = "Example", Roles = ["Application Server", "Client Workstation"] }
                ],
                Prerequisites =
                [
                    new ProfilePrerequisite
                    {
                        Id = "msmq",
                        Title = "Message Queuing (MSMQ)",
                        Roles = ["Application Server", "Client Workstation"],
                        Checks =
                        [
                            new ProfileCheck { Type = "serviceExists", ServiceName = "MSMQ" }
                        ],
                        DocRef = "Install guide: MSMQ when required by role"
                    }
                ]
            };

            var path = Path.Combine(dir, "ecc.wsb.json");
            store.Save(profile, path);
            var loaded = store.Load(path);

            Assert.Equal(profile.Id, loaded.Id);
            Assert.Single(loaded.Services);
            Assert.Single(loaded.Prerequisites);
            Assert.Equal("msmq", loaded.Prerequisites[0].Id);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ResolveServiceNames_Uses_Explicit_And_MatchRules()
    {
        var store = new ProfileStore();
        var profile = new ProductProfile
        {
            Id = "t",
            Name = "t",
            Services =
            [
                new ProfileServiceEntry { ServiceName = "ExplicitSvc", Roles = ["Application Server"] }
            ],
            MatchRules =
            [
                new ProfileMatchRule { Type = "substring", Value = "Prod", Roles = ["Application Server"] }
            ]
        };

        var live = new List<ServiceInfo>
        {
            new() { ServiceName = "ExplicitSvc", DisplayName = "Explicit" },
            new() { ServiceName = "ProductX", DisplayName = "X" },
            new() { ServiceName = "Other", DisplayName = "Other" }
        };

        var names = store.ResolveServiceNames(profile, "Application Server", live).OrderBy(n => n).ToList();
        Assert.Equal(["ExplicitSvc", "ProductX"], names);
    }
}
