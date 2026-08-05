using WinServiceBuddy.Core.Models;

namespace WinServiceBuddy.Core.Services;

public interface IWindowsServiceManager
{
    IReadOnlyList<ServiceInfo> GetServices();
    ServiceInfo? GetService(string serviceName);
    IReadOnlyList<ServiceInfo> FindBySubstring(string substring);
    OperationResult Start(string serviceName, TimeSpan? timeout = null);
    OperationResult Stop(string serviceName, TimeSpan? timeout = null);
    OperationResult Restart(string serviceName, TimeSpan? timeout = null);
    BulkOperationResult StartMany(IEnumerable<string> serviceNames, TimeSpan? timeout = null);
    BulkOperationResult StopMany(IEnumerable<string> serviceNames, TimeSpan? timeout = null);
    BulkOperationResult RestartMany(IEnumerable<string> serviceNames, TimeSpan? timeout = null);
    OperationResult SetStartupType(string serviceName, ServiceStartupType startupType);
    BulkOperationResult SetStartupTypeMany(IEnumerable<string> serviceNames, ServiceStartupType startupType);
    OperationResult SetRecovery(string serviceName, RecoveryPreset preset);
    BulkOperationResult SetRecoveryMany(IEnumerable<string> serviceNames, RecoveryPreset preset);
    IReadOnlyList<string> GetDependsOn(string serviceName);
    IReadOnlyDictionary<string, IReadOnlyList<string>> BuildDependencyMap(IEnumerable<string> serviceNames);
}
