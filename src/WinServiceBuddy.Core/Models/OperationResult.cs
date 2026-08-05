namespace WinServiceBuddy.Core.Models;

public sealed class OperationResult
{
    public required string ServiceName { get; init; }
    public required bool Success { get; init; }
    public string? Message { get; init; }

    public static OperationResult Ok(string serviceName, string? message = null) =>
        new() { ServiceName = serviceName, Success = true, Message = message };

    public static OperationResult Fail(string serviceName, string message) =>
        new() { ServiceName = serviceName, Success = false, Message = message };
}

public sealed class BulkOperationResult
{
    public IReadOnlyList<OperationResult> Results { get; init; } = Array.Empty<OperationResult>();

    public int Succeeded => Results.Count(r => r.Success);
    public int Failed => Results.Count(r => !r.Success);
    public bool AllSucceeded => Results.Count > 0 && Failed == 0;
}
