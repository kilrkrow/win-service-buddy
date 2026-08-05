using System.Runtime.InteropServices;

namespace WinServiceBuddy.Core.Native;

internal static class NativeMethods
{
    public const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;
    public const uint SERVICE_QUERY_CONFIG = 0x0001;
    public const uint SERVICE_CHANGE_CONFIG = 0x0002;
    public const uint SERVICE_QUERY_STATUS = 0x0004;
    public const uint SERVICE_START = 0x0010;
    public const uint SERVICE_STOP = 0x0020;
    public const uint SC_MANAGER_CONNECT = 0x0001;
    public const uint SC_MANAGER_ENUMERATE_SERVICE = 0x0004;

    public const int SERVICE_CONFIG_FAILURE_ACTIONS = 2;
    public const int SERVICE_CONFIG_DELAYED_AUTO_START_INFO = 3;

    public const int SERVICE_AUTO_START = 0x00000002;
    public const int SERVICE_DEMAND_START = 0x00000003;
    public const int SERVICE_DISABLED = 0x00000004;

    public const int SC_ACTION_NONE = 0;
    public const int SC_ACTION_RESTART = 1;
    public const int SC_ACTION_REBOOT = 2;
    public const int SC_ACTION_RUN_COMMAND = 3;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr OpenService(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseServiceHandle(IntPtr hSCObject);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ChangeServiceConfig(
        IntPtr hService,
        uint nServiceType,
        uint nStartType,
        uint nErrorControl,
        string? lpBinaryPathName,
        string? lpLoadOrderGroup,
        IntPtr lpdwTagId,
        string? lpDependencies,
        string? lpServiceStartName,
        string? lpPassword,
        string? lpDisplayName);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ChangeServiceConfig2(
        IntPtr hService,
        int dwInfoLevel,
        IntPtr lpInfo);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool QueryServiceConfig2(
        IntPtr hService,
        int dwInfoLevel,
        IntPtr lpBuffer,
        int cbBufSize,
        out int pcbBytesNeeded);

    [StructLayout(LayoutKind.Sequential)]
    public struct SERVICE_DELAYED_AUTO_START_INFO
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool fDelayedAutostart;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SC_ACTION
    {
        public int Type;
        public int Delay;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SERVICE_FAILURE_ACTIONS
    {
        public int dwResetPeriod;
        public IntPtr lpRebootMsg;
        public IntPtr lpCommand;
        public int cActions;
        public IntPtr lpsaActions;
    }
}
