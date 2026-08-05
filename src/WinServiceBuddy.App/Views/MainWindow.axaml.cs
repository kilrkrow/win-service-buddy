using Avalonia.Controls;
using Avalonia.Platform.Storage;
using WinServiceBuddy.App.ViewModels;

namespace WinServiceBuddy.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.PickProfileFileAsync = PickProfileFileAsync;
    }

    private async Task<string?> PickProfileFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Win Service Buddy profile",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("WSB profiles")
                {
                    Patterns = ["*.wsb.json", "*.json"]
                },
                new FilePickerFileType("All files")
                {
                    Patterns = ["*.*"]
                }
            ]
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
