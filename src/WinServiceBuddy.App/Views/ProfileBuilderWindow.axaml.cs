using Avalonia.Controls;
using Avalonia.Platform.Storage;
using WinServiceBuddy.App.ViewModels;

namespace WinServiceBuddy.App.Views;

public partial class ProfileBuilderWindow : Window
{
    public ProfileBuilderWindow()
        : this(new ProfileBuilderViewModel())
    {
    }

    public ProfileBuilderWindow(ProfileBuilderViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PickOpenProfileAsync = PickOpenAsync;
        viewModel.PickSaveProfileAsync = PickSaveAsync;
        viewModel.RequestClose = () => Close();
    }

    private async Task<string?> PickOpenAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open profile",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("WSB profiles") { Patterns = ["*.wsb.json", "*.json"] },
                new FilePickerFileType("All files") { Patterns = ["*.*"] }
            ]
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private async Task<string?> PickSaveAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export profile",
            SuggestedFileName = "product.wsb.json",
            FileTypeChoices =
            [
                new FilePickerFileType("WSB profile") { Patterns = ["*.wsb.json", "*.json"] }
            ]
        });
        return file?.TryGetLocalPath();
    }
}
