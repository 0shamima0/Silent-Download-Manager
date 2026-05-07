using System.Windows;
using Silent.DownloadManager.App.Models;
using Silent.DownloadManager.App.ViewModels;

namespace Silent.DownloadManager.App.Views;

public partial class DeleteDownloadDialog : Window
{
    public DeleteDownloadDialog(DownloadItem item)
    {
        InitializeComponent();
        FileNameText.Text = item.FileName;
    }

    public DeleteChoice Choice { get; private set; } = DeleteChoice.Cancel;

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        Choice = DeleteFileCheckBox.IsChecked == true
            ? DeleteChoice.DeleteFileToo
            : DeleteChoice.RemoveFromListOnly;

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Choice = DeleteChoice.Cancel;
        DialogResult = false;
    }
}

