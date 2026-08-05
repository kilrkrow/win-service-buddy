namespace WinServiceBuddy.Core.Models;

/// <summary>Named recovery configurations applied via SCM failure actions.</summary>
public enum RecoveryPreset
{
    /// <summary>Leave existing recovery configuration unchanged.</summary>
    Unchanged,

    /// <summary>Restart the service on failure, up to 3 attempts, then take no action.</summary>
    RestartThreeTimes,

    /// <summary>Take no action on all failure slots.</summary>
    TakeNoAction
}
