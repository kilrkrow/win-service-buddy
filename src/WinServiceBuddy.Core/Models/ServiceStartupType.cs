namespace WinServiceBuddy.Core.Models;

/// <summary>Windows service start type as exposed to CLI/GUI.</summary>
public enum ServiceStartupType
{
    Automatic,
    AutomaticDelayed,
    Manual,
    Disabled,
    Unknown
}
