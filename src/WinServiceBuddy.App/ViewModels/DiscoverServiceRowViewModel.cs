using CommunityToolkit.Mvvm.ComponentModel;
using WinServiceBuddy.Core.Models;

namespace WinServiceBuddy.App.ViewModels;

public partial class DiscoverServiceRowViewModel : ViewModelBase
{
    public DiscoverServiceRowViewModel(ServiceInfo info)
    {
        ServiceName = info.ServiceName;
        DisplayName = info.DisplayName;
        Status = info.Status;
    }

    public string ServiceName { get; }
    public string DisplayName { get; }
    public string Status { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public string Title => string.IsNullOrWhiteSpace(DisplayName) ? ServiceName : DisplayName;
}
