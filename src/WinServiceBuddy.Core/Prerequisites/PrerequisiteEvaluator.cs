using WinServiceBuddy.Core.Models;
using WinServiceBuddy.Core.Profiles;
using WinServiceBuddy.Core.Services;

namespace WinServiceBuddy.Core.Prerequisites;

public sealed class PrerequisiteResult
{
    public required string PrerequisiteId { get; init; }
    public required string Title { get; init; }
    public required bool Passed { get; init; }
    public string Severity { get; init; } = "error";
    public string? Message { get; init; }
    public string? DocRef { get; init; }
}

public sealed class PrerequisiteEvaluator
{
    private readonly IWindowsServiceManager _services;

    public PrerequisiteEvaluator(IWindowsServiceManager services)
    {
        _services = services;
    }

    public IReadOnlyList<PrerequisiteResult> Evaluate(ProductProfile profile, string role)
    {
        var results = new List<PrerequisiteResult>();
        foreach (var prereq in profile.Prerequisites)
        {
            if (prereq.Roles.Count > 0 &&
                !prereq.Roles.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            foreach (var check in prereq.Checks)
            {
                results.Add(EvaluateCheck(prereq, check));
            }
        }

        return results;
    }

    private PrerequisiteResult EvaluateCheck(ProfilePrerequisite prereq, ProfileCheck check)
    {
        switch (check.Type)
        {
            case "serviceExists":
            {
                var name = check.ServiceName ?? "";
                var exists = _services.GetService(name) is not null;
                return new PrerequisiteResult
                {
                    PrerequisiteId = prereq.Id,
                    Title = prereq.Title,
                    Passed = exists,
                    Severity = check.Severity,
                    Message = exists
                        ? $"Service '{name}' is registered."
                        : $"Service '{name}' was not found.",
                    DocRef = prereq.DocRef
                };
            }
            case "serviceRunning":
            {
                var name = check.ServiceName ?? "";
                var svc = _services.GetService(name);
                var running = svc?.IsRunning == true;
                return new PrerequisiteResult
                {
                    PrerequisiteId = prereq.Id,
                    Title = prereq.Title,
                    Passed = running,
                    Severity = check.Severity,
                    Message = running
                        ? $"Service '{name}' is running."
                        : $"Service '{name}' is not running (status: {svc?.Status ?? "missing"}).",
                    DocRef = prereq.DocRef
                };
            }
            case "serviceStartup":
            {
                var name = check.ServiceName ?? "";
                var svc = _services.GetService(name);
                var expected = check.ExpectedStartup ?? "Automatic";
                var actual = svc?.StartupType.ToString() ?? "missing";
                var ok = svc is not null &&
                         string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
                return new PrerequisiteResult
                {
                    PrerequisiteId = prereq.Id,
                    Title = prereq.Title,
                    Passed = ok,
                    Severity = check.Severity,
                    Message = ok
                        ? $"Service '{name}' startup is {actual}."
                        : $"Service '{name}' startup is {actual}, expected {expected}.",
                    DocRef = prereq.DocRef
                };
            }
            default:
                return new PrerequisiteResult
                {
                    PrerequisiteId = prereq.Id,
                    Title = prereq.Title,
                    Passed = false,
                    Severity = "warn",
                    Message = $"Unsupported check type '{check.Type}'.",
                    DocRef = prereq.DocRef
                };
        }
    }
}
