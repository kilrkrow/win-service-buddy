using CommunityToolkit.Mvvm.ComponentModel;
using WinServiceBuddy.Core.Models;

namespace WinServiceBuddy.App.ViewModels;

public partial class ServiceRowViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public string ServiceName { get; }
    public string DisplayName { get; }

    [ObservableProperty]
    public partial string Status { get; set; }

    [ObservableProperty]
    public partial string StartupType { get; set; }

    [ObservableProperty]
    public partial string RecoverySummary { get; set; }

    public string StatusColor => Status.Equals("Running", StringComparison.OrdinalIgnoreCase)
        ? "#3DDC97"
        : Status.Equals("Stopped", StringComparison.OrdinalIgnoreCase)
            ? "#FF6B6B"
            : "#F0C929";

    public ServiceRowViewModel(ServiceInfo info)
    {
        ServiceName = info.ServiceName;
        DisplayName = info.DisplayName;
        Status = info.Status;
        StartupType = info.StartupType.ToString();
        RecoverySummary = info.RecoverySummary;
    }

    public void UpdateFrom(ServiceInfo info)
    {
        Status = info.Status;
        StartupType = info.StartupType.ToString();
        RecoverySummary = info.RecoverySummary;
        OnPropertyChanged(nameof(StatusColor));
    }
}
