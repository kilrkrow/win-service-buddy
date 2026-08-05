using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
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

        DiscoverGrid.AddHandler(InputElement.PointerPressedEvent, OnDiscoverPointerPressed, RoutingStrategies.Tunnel);
        ProfileServicesGrid.AddHandler(InputElement.PointerPressedEvent, OnProfilePointerPressed, RoutingStrategies.Tunnel);
    }

    private void OnDiscoverPointerPressed(object? sender, PointerPressedEventArgs e) =>
        HandleGridCheckboxClick(e, isDiscover: true);

    private void OnProfilePointerPressed(object? sender, PointerPressedEventArgs e) =>
        HandleGridCheckboxClick(e, isDiscover: false);

    private void HandleGridCheckboxClick(PointerPressedEventArgs e, bool isDiscover)
    {
        if (DataContext is not ProfileBuilderViewModel vm)
            return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var source = e.Source as Visual;
        if (source is null)
            return;

        var checkBox = source as CheckBox ?? source.FindAncestorOfType<CheckBox>();
        if (checkBox is null)
            return;

        // Don't steal ComboBox clicks in the profile grid
        if (source.FindAncestorOfType<ComboBox>() is not null)
            return;

        object? rowVm = checkBox.DataContext;
        if (rowVm is null)
            return;

        e.Handled = true;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        vm.ApplyCheckboxInteraction(rowVm, shift, isDiscover);
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
