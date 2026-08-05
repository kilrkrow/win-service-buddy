using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WinServiceBuddy.App.ViewModels;

namespace WinServiceBuddy.App.Views;

public partial class MainWindow : Window
{
    private MainViewModel? _vm;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // Tunnel so we run before DataGrid steals the click for cell focus.
        ServicesGrid.AddHandler(
            InputElement.PointerPressedEvent,
            OnServicesGridPointerPressed,
            RoutingStrategies.Tunnel);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm.ScrollServiceIntoView = null;
            _vm.PickProfileFileAsync = null;
            _vm.OpenProfileBuilder = null;
        }

        _vm = DataContext as MainViewModel;
        if (_vm is null)
            return;

        _vm.PickProfileFileAsync = PickProfileFileAsync;
        _vm.ScrollServiceIntoView = ScrollServiceRowIntoView;
        _vm.OpenProfileBuilder = OpenProfileBuilderWindow;
        _vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OpenProfileBuilderWindow(WinServiceBuddy.Core.Profiles.ProductProfile? profile, string? path)
    {
        if (_vm is null)
            return;

        var builderVm = profile is null
            ? new ProfileBuilderViewModel()
            : new ProfileBuilderViewModel(new WinServiceBuddy.Core.Services.WindowsServiceManager(),
                new WinServiceBuddy.Core.Profiles.ProfileStore(), profile, path);

        builderVm.ProfilesChanged = () => _vm.NotifyProfilesChangedFromBuilder();

        var window = new ProfileBuilderWindow(builderVm);
        window.Show(this);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.FocusedService) && _vm?.FocusedService is { } row)
            ScrollServiceRowIntoView(row);
    }

    /// <summary>
    /// Why two clicks used to be required: DataGridCheckBoxColumn puts the grid into
    /// "current cell" mode on the first click; the CheckBox only toggles on the second.
    /// We intercept pointer presses on our template CheckBox (and Shift+click on the row)
    /// in the tunnel phase and apply selection ourselves.
    /// </summary>
    private void OnServicesGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is null)
            return;

        if (!e.GetCurrentPoint(ServicesGrid).Properties.IsLeftButtonPressed)
            return;

        var source = e.Source as Visual;
        if (source is null)
            return;

        // Don't steal clicks from Deps links / other buttons
        if (source.FindAncestorOfType<Button>() is not null &&
            source.FindAncestorOfType<CheckBox>() is null)
            return;

        var checkBox = source as CheckBox ?? source.FindAncestorOfType<CheckBox>();
        var rowControl = source.FindAncestorOfType<DataGridRow>();
        var row = checkBox?.DataContext as ServiceRowViewModel
                  ?? rowControl?.DataContext as ServiceRowViewModel;

        if (row is null)
            return;

        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        // Checkbox click (any): handle ourselves so one click is enough.
        // Row + Shift: range-check without requiring the tiny checkbox hit target.
        if (checkBox is not null || shift)
        {
            e.Handled = true;
            _vm.ApplyCheckboxInteraction(row, shift);

            // Keep grid focus/current row in sync for keyboard users
            ServicesGrid.SelectedItem = row;
        }
    }

    /// <summary>
    /// Select the row in the DataGrid and scroll it into view (table navigation from deps panel).
    /// </summary>
    private void ScrollServiceRowIntoView(ServiceRowViewModel row)
    {
        // Defer until after ItemsSource/selection bindings apply (e.g. newly added rows).
        Dispatcher.UIThread.Post(() =>
        {
            if (ServicesGrid is null)
                return;

            ServicesGrid.SelectedItem = row;
            ServicesGrid.ScrollIntoView(row, ServicesGrid.Columns.Count > 1 ? ServicesGrid.Columns[1] : null);
            // Don't Focus() the grid here — that re-creates cell currency focus chrome.
        }, DispatcherPriority.Loaded);
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
