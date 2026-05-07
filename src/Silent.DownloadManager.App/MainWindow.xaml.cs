using System.Windows;
using Silent.DownloadManager.App.ViewModels;
using Silent.DownloadManager.App.Views;

namespace Silent.DownloadManager.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        _viewModel = new MainViewModel(ConfirmDelete);
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.Dispose();
    }

    private DeleteChoice ConfirmDelete(Models.DownloadItem item)
    {
        var dialog = new DeleteDownloadDialog(item)
        {
            Owner = this
        };

        return dialog.ShowDialog() == true
            ? dialog.Choice
            : DeleteChoice.Cancel;
    }
}

