using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using WinServiceBuddy.App.ViewModels;

namespace WinServiceBuddy.App.Views;

public partial class MainWindow : Window
{
    private MainViewModel? _vm;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm.ScrollServiceIntoView = null;
            _vm.PickProfileFileAsync = null;
        }

        _vm = DataContext as MainViewModel;
        if (_vm is null)
            return;

        _vm.PickProfileFileAsync = PickProfileFileAsync;
        _vm.ScrollServiceIntoView = ScrollServiceRowIntoView;
        _vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.FocusedService) && _vm?.FocusedService is { } row)
            ScrollServiceRowIntoView(row);
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
            ServicesGrid.Focus();
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
