using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using WinServiceBuddy.Core.Models;
using WinServiceBuddy.Core.Native;

namespace WinServiceBuddy.Core.Services;

/// <summary>Local Windows Service Control Manager adapter.</summary>
public sealed class WindowsServiceManager : IWindowsServiceManager
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    public IReadOnlyList<ServiceInfo> GetServices()
    {
        EnsureWindows();
        return ServiceController.GetServices()
            .Select(ToServiceInfo)
            .OrderBy(s => s.ServiceName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public ServiceInfo? GetService(string serviceName)
    {
        EnsureWindows();
        try
        {
            using var sc = new ServiceController(serviceName);
            _ = sc.Status; // force open
            return ToServiceInfo(sc);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    public IReadOnlyList<ServiceInfo> FindBySubstring(string substring) =>
        ServiceFilter.BySubstring(GetServices(), substring).ToList();

    public OperationResult Start(string serviceName, TimeSpan? timeout = null)
    {
        EnsureWindows();
        timeout ??= DefaultTimeout;
        try
        {
            using var sc = new ServiceController(serviceName);
            sc.Refresh();
            if (sc.Status == ServiceControllerStatus.Running)
                return OperationResult.Ok(serviceName, "Already running");

            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, timeout.Value);
            return OperationResult.Ok(serviceName, "Started");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(serviceName, ex.Message);
        }
    }

    public OperationResult Stop(string serviceName, TimeSpan? timeout = null)
    {
        EnsureWindows();
        timeout ??= DefaultTimeout;
        try
        {
            using var sc = new ServiceController(serviceName);
            sc.Refresh();
            if (sc.Status == ServiceControllerStatus.Stopped)
                return OperationResult.Ok(serviceName, "Already stopped");

            if (!sc.CanStop)
                return OperationResult.Fail(serviceName, "Service reports it cannot be stopped");

            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, timeout.Value);
            return OperationResult.Ok(serviceName, "Stopped");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(serviceName, ex.Message);
        }
    }

    public OperationResult Restart(string serviceName, TimeSpan? timeout = null)
    {
        var stop = Stop(serviceName, timeout);
        if (!stop.Success && stop.Message is not ("Already stopped"))
            return stop;

        return Start(serviceName, timeout);
    }

    public BulkOperationResult StartMany(IEnumerable<string> serviceNames, TimeSpan? timeout = null)
    {
        // Start dependencies-first (services that others depend on first is complex;
        // for bulk we start in given order; dependency-aware order is a higher layer).
        var results = serviceNames
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(n => Start(n, timeout))
            .ToList();
        return new BulkOperationResult { Results = results };
    }

    public BulkOperationResult StopMany(IEnumerable<string> serviceNames, TimeSpan? timeout = null)
    {
        var results = serviceNames
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(n => Stop(n, timeout))
            .ToList();
        return new BulkOperationResult { Results = results };
    }

    public BulkOperationResult RestartMany(IEnumerable<string> serviceNames, TimeSpan? timeout = null)
    {
        var results = serviceNames
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(n => Restart(n, timeout))
            .ToList();
        return new BulkOperationResult { Results = results };
    }

    public OperationResult SetStartupType(string serviceName, ServiceStartupType startupType)
    {
        EnsureWindows();
        if (startupType is ServiceStartupType.Unknown)
            return OperationResult.Fail(serviceName, "Unknown startup type");

        try
        {
            using var handle = ServiceHandle.Open(serviceName, NativeMethods.SERVICE_CHANGE_CONFIG | NativeMethods.SERVICE_QUERY_CONFIG);
            var startType = startupType switch
            {
                ServiceStartupType.Automatic or ServiceStartupType.AutomaticDelayed => NativeMethods.SERVICE_AUTO_START,
                ServiceStartupType.Manual => NativeMethods.SERVICE_DEMAND_START,
                ServiceStartupType.Disabled => NativeMethods.SERVICE_DISABLED,
                _ => throw new ArgumentOutOfRangeException(nameof(startupType))
            };

            if (!NativeMethods.ChangeServiceConfig(
                    handle.Handle,
                    NativeMethods.SERVICE_NO_CHANGE,
                    (uint)startType,
                    NativeMethods.SERVICE_NO_CHANGE,
                    null, null, IntPtr.Zero, null, null, null, null))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var delayed = startupType == ServiceStartupType.AutomaticDelayed;
            var info = new NativeMethods.SERVICE_DELAYED_AUTO_START_INFO { fDelayedAutostart = delayed };
            var ptr = Marshal.AllocHGlobal(Marshal.SizeOf(info));
            try
            {
                Marshal.StructureToPtr(info, ptr, false);
                // Delayed auto-start only applies when start type is Automatic; still set for consistency.
                if (!NativeMethods.ChangeServiceConfig2(handle.Handle, NativeMethods.SERVICE_CONFIG_DELAYED_AUTO_START_INFO, ptr))
                {
                    var err = Marshal.GetLastWin32Error();
                    // Some services reject delayed flag when not auto — only fail hard for delayed requests.
                    if (delayed)
                        throw new Win32Exception(err);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }

            return OperationResult.Ok(serviceName, $"Startup set to {startupType}");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(serviceName, ex.Message);
        }
    }

    public BulkOperationResult SetStartupTypeMany(IEnumerable<string> serviceNames, ServiceStartupType startupType)
    {
        var results = serviceNames
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(n => SetStartupType(n, startupType))
            .ToList();
        return new BulkOperationResult { Results = results };
    }

    public OperationResult SetRecovery(string serviceName, RecoveryPreset preset)
    {
        EnsureWindows();
        if (preset == RecoveryPreset.Unchanged)
            return OperationResult.Ok(serviceName, "Unchanged");

        try
        {
            using var handle = ServiceHandle.Open(serviceName, NativeMethods.SERVICE_CHANGE_CONFIG | NativeMethods.SERVICE_QUERY_CONFIG);
            ApplyRecoveryPreset(handle.Handle, preset);
            return OperationResult.Ok(serviceName, $"Recovery set to {preset}");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(serviceName, ex.Message);
        }
    }

    public BulkOperationResult SetRecoveryMany(IEnumerable<string> serviceNames, RecoveryPreset preset)
    {
        var results = serviceNames
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(n => SetRecovery(n, preset))
            .ToList();
        return new BulkOperationResult { Results = results };
    }

    public IReadOnlyList<string> GetDependsOn(string serviceName)
    {
        EnsureWindows();
        try
        {
            using var sc = new ServiceController(serviceName);
            return sc.ServicesDependedOn.Select(d => d.ServiceName).ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> BuildDependencyMap(IEnumerable<string> serviceNames)
    {
        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in serviceNames.Distinct(StringComparer.OrdinalIgnoreCase))
            map[name] = GetDependsOn(name);
        return map;
    }

    private static ServiceInfo ToServiceInfo(ServiceController sc)
    {
        sc.Refresh();
        string[] dependsOn;
        try
        {
            dependsOn = sc.ServicesDependedOn.Select(d => d.ServiceName).ToArray();
        }
        catch
        {
            dependsOn = Array.Empty<string>();
        }

        var startup = MapStartup(sc);
        var recovery = QueryRecoverySummary(sc.ServiceName);
        int? pid = null;
        try
        {
            // Best-effort PID via WMI-less approach: not available on ServiceController alone.
            pid = null;
        }
        catch
        {
            // ignore
        }

        return new ServiceInfo
        {
            ServiceName = sc.ServiceName,
            DisplayName = sc.DisplayName,
            Status = sc.Status.ToString(),
            StartupType = startup,
            RecoverySummary = recovery,
            DependsOn = dependsOn,
            CanStop = sc.CanStop,
            CanStart = sc.Status is ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending,
            ProcessId = pid
        };
    }

    private static ServiceStartupType MapStartup(ServiceController sc)
    {
        try
        {
            var type = sc.StartType switch
            {
                ServiceStartMode.Automatic => ServiceStartupType.Automatic,
                ServiceStartMode.Manual => ServiceStartupType.Manual,
                ServiceStartMode.Disabled => ServiceStartupType.Disabled,
                _ => ServiceStartupType.Unknown
            };

            if (type == ServiceStartupType.Automatic && IsDelayedAutoStart(sc.ServiceName))
                return ServiceStartupType.AutomaticDelayed;

            return type;
        }
        catch
        {
            return ServiceStartupType.Unknown;
        }
    }

    private static bool IsDelayedAutoStart(string serviceName)
    {
        try
        {
            using var handle = ServiceHandle.Open(serviceName, NativeMethods.SERVICE_QUERY_CONFIG);
            var size = Marshal.SizeOf<NativeMethods.SERVICE_DELAYED_AUTO_START_INFO>();
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                if (!NativeMethods.QueryServiceConfig2(
                        handle.Handle,
                        NativeMethods.SERVICE_CONFIG_DELAYED_AUTO_START_INFO,
                        ptr,
                        size,
                        out _))
                {
                    return false;
                }

                var info = Marshal.PtrToStructure<NativeMethods.SERVICE_DELAYED_AUTO_START_INFO>(ptr);
                return info.fDelayedAutostart;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch
        {
            return false;
        }
    }

    private static string QueryRecoverySummary(string serviceName)
    {
        try
        {
            using var handle = ServiceHandle.Open(serviceName, NativeMethods.SERVICE_QUERY_CONFIG);
            // First call to get required size
            NativeMethods.QueryServiceConfig2(
                handle.Handle,
                NativeMethods.SERVICE_CONFIG_FAILURE_ACTIONS,
                IntPtr.Zero,
                0,
                out var needed);

            if (needed <= 0)
                return "None";

            var ptr = Marshal.AllocHGlobal(needed);
            try
            {
                if (!NativeMethods.QueryServiceConfig2(
                        handle.Handle,
                        NativeMethods.SERVICE_CONFIG_FAILURE_ACTIONS,
                        ptr,
                        needed,
                        out _))
                {
                    return "Unknown";
                }

                var actions = Marshal.PtrToStructure<NativeMethods.SERVICE_FAILURE_ACTIONS>(ptr);
                if (actions.cActions <= 0 || actions.lpsaActions == IntPtr.Zero)
                    return "Take no action";

                var types = new List<string>();
                var actionSize = Marshal.SizeOf<NativeMethods.SC_ACTION>();
                for (var i = 0; i < actions.cActions; i++)
                {
                    var actionPtr = IntPtr.Add(actions.lpsaActions, i * actionSize);
                    var action = Marshal.PtrToStructure<NativeMethods.SC_ACTION>(actionPtr);
                    types.Add(action.Type switch
                    {
                        NativeMethods.SC_ACTION_NONE => "None",
                        NativeMethods.SC_ACTION_RESTART => "Restart",
                        NativeMethods.SC_ACTION_REBOOT => "Reboot",
                        NativeMethods.SC_ACTION_RUN_COMMAND => "Run program",
                        _ => "Other"
                    });
                }

                if (types.All(t => t == "None"))
                    return "Take no action";

                var restartCount = types.Count(t => t == "Restart");
                if (restartCount > 0 && types.All(t => t is "Restart" or "None"))
                    return $"Restart {restartCount}x";

                return string.Join(" → ", types);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch
        {
            return "Unknown";
        }
    }

    private static void ApplyRecoveryPreset(IntPtr serviceHandle, RecoveryPreset preset)
    {
        // Three failure actions — Windows classic UI exposes first/second/subsequent.
        const int actionCount = 3;
        const int restartDelayMs = 60_000; // 1 minute
        const int resetPeriodSec = 86_400; // 1 day

        var actions = new NativeMethods.SC_ACTION[actionCount];
        switch (preset)
        {
            case RecoveryPreset.RestartThreeTimes:
                for (var i = 0; i < actionCount; i++)
                {
                    actions[i] = new NativeMethods.SC_ACTION
                    {
                        Type = NativeMethods.SC_ACTION_RESTART,
                        Delay = restartDelayMs
                    };
                }
                break;
            case RecoveryPreset.TakeNoAction:
                for (var i = 0; i < actionCount; i++)
                {
                    actions[i] = new NativeMethods.SC_ACTION
                    {
                        Type = NativeMethods.SC_ACTION_NONE,
                        Delay = 0
                    };
                }
                break;
            default:
                return;
        }

        var actionsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.SC_ACTION>() * actionCount);
        try
        {
            for (var i = 0; i < actionCount; i++)
            {
                Marshal.StructureToPtr(
                    actions[i],
                    IntPtr.Add(actionsPtr, i * Marshal.SizeOf<NativeMethods.SC_ACTION>()),
                    false);
            }

            var failure = new NativeMethods.SERVICE_FAILURE_ACTIONS
            {
                dwResetPeriod = resetPeriodSec,
                lpRebootMsg = IntPtr.Zero,
                lpCommand = IntPtr.Zero,
                cActions = actionCount,
                lpsaActions = actionsPtr
            };

            var failurePtr = Marshal.AllocHGlobal(Marshal.SizeOf(failure));
            try
            {
                Marshal.StructureToPtr(failure, failurePtr, false);
                if (!NativeMethods.ChangeServiceConfig2(
                        serviceHandle,
                        NativeMethods.SERVICE_CONFIG_FAILURE_ACTIONS,
                        failurePtr))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            finally
            {
                Marshal.FreeHGlobal(failurePtr);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(actionsPtr);
        }
    }

    private static void EnsureWindows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new PlatformNotSupportedException("Win Service Buddy requires Windows.");
    }

    private sealed class ServiceHandle : IDisposable
    {
        public IntPtr Handle { get; }
        private readonly IntPtr _scm;

        private ServiceHandle(IntPtr scm, IntPtr handle)
        {
            _scm = scm;
            Handle = handle;
        }

        public static ServiceHandle Open(string serviceName, uint access)
        {
            var scm = NativeMethods.OpenSCManager(null, null, NativeMethods.SC_MANAGER_CONNECT);
            if (scm == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenSCManager failed");

            var svc = NativeMethods.OpenService(scm, serviceName, access);
            if (svc == IntPtr.Zero)
            {
                var err = Marshal.GetLastWin32Error();
                NativeMethods.CloseServiceHandle(scm);
                throw new Win32Exception(err, $"OpenService failed for '{serviceName}'");
            }

            return new ServiceHandle(scm, svc);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
                NativeMethods.CloseServiceHandle(Handle);
            if (_scm != IntPtr.Zero)
                NativeMethods.CloseServiceHandle(_scm);
        }
    }
}
