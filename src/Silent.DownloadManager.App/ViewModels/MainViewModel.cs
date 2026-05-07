using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using Silent.DownloadManager.App.Models;
using Silent.DownloadManager.App.Services;
using Microsoft.Win32;

namespace Silent.DownloadManager.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly DownloadService _downloadService = new();
    private readonly YtDlpService _ytDlpService = new();
    private readonly StorageService _storageService = new();
    private readonly IncomingRequestService _incomingRequestService = new();
    private readonly Func<DownloadItem, DeleteChoice> _confirmDelete;
    private string _newUrl = string.Empty;
    private string _downloadFolder = AppPaths.DefaultDownloadFolder;
    private DownloadItem? _selectedDownload;
    private string? _pendingReferrer;
    private string _statusText = "Ready";

    public MainViewModel(Func<DownloadItem, DeleteChoice>? confirmDelete = null)
    {
        _confirmDelete = confirmDelete ?? (_ => DeleteChoice.RemoveFromListOnly);
        Downloads = [];
        AddDownloadCommand = new RelayCommand(AddDownload);
        ChooseFolderCommand = new RelayCommand(ChooseFolder);
        PauseCommand = new RelayCommand(PauseSelected, () => SelectedDownload is not null);
        ResumeCommand = new RelayCommand(ResumeSelected, () => SelectedDownload is not null);
        CancelCommand = new RelayCommand(CancelSelected, () => SelectedDownload is not null);
        DeleteCommand = new RelayCommand(DeleteSelected, () => SelectedDownload is not null);
        OpenFolderCommand = new RelayCommand(OpenSelectedFolder, () => SelectedDownload is not null);
        OpenFileCommand = new RelayCommand(OpenSelectedFile, () => SelectedDownload is not null);
        CopyUrlCommand = new RelayCommand(CopySelectedUrl, () => SelectedDownload is not null);
        ClearCompletedCommand = new RelayCommand(ClearCompleted, () => Downloads.Any(item => item.Status == DownloadStatus.Completed));

        _incomingRequestService.UrlReceived += OnUrlReceived;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DownloadItem> Downloads { get; }

    public RelayCommand AddDownloadCommand { get; }
    public RelayCommand ChooseFolderCommand { get; }
    public RelayCommand PauseCommand { get; }
    public RelayCommand ResumeCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand OpenFileCommand { get; }
    public RelayCommand CopyUrlCommand { get; }
    public RelayCommand ClearCompletedCommand { get; }

    public string NewUrl
    {
        get => _newUrl;
        set => SetField(ref _newUrl, value);
    }

    public string DownloadFolder
    {
        get => _downloadFolder;
        set => SetField(ref _downloadFolder, value);
    }

    public DownloadItem? SelectedDownload
    {
        get => _selectedDownload;
        set
        {
            if (SetField(ref _selectedDownload, value))
            {
                PauseCommand.RaiseCanExecuteChanged();
                ResumeCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
                DeleteCommand.RaiseCanExecuteChanged();
                OpenFolderCommand.RaiseCanExecuteChanged();
                OpenFileCommand.RaiseCanExecuteChanged();
                CopyUrlCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(SelectedFileName));
                OnPropertyChanged(nameof(SelectedStatusText));
                OnPropertyChanged(nameof(SelectedProgressText));
                OnPropertyChanged(nameof(SelectedTargetPath));
                OnPropertyChanged(nameof(SelectedUrl));
                OnPropertyChanged(nameof(IsSelectedYtDlp));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    public int TotalCount => Downloads.Count;
    public int ActiveCount => Downloads.Count(item => item.Status == DownloadStatus.Downloading);
    public int CompletedCount => Downloads.Count(item => item.Status == DownloadStatus.Completed);
    public int FailedCount => Downloads.Count(item => item.Status == DownloadStatus.Failed);

    public string FooterText => $"{TotalCount} total | {ActiveCount} active | {CompletedCount} completed | {FailedCount} failed";

    public string SelectedFileName => SelectedDownload?.FileName ?? "No download selected";
    public string SelectedStatusText => SelectedDownload?.Status.ToString() ?? "-";
    public string SelectedProgressText => SelectedDownload?.ProgressLabel ?? "-";
    public string SelectedTargetPath => SelectedDownload?.TargetPath ?? "-";
    public string SelectedUrl => SelectedDownload?.Url ?? "-";

    /// <summary>True when the selected item is/was handled by yt-dlp (video site).</summary>
    public bool IsSelectedYtDlp => SelectedDownload is not null && YtDlpService.IsSupported(SelectedDownload.Url);

    // -----------------------------------------------------------------------
    // Initialization
    // -----------------------------------------------------------------------

    public async Task InitializeAsync()
    {
        AppPaths.Ensure();
        Directory.CreateDirectory(DownloadFolder);

        foreach (var item in await _storageService.LoadAsync())
        {
            NormalizeLoadedItem(item);
            AttachDownloadItem(item);
            Downloads.Add(item);
        }

        _incomingRequestService.Start();
        OnPropertyChanged(nameof(FooterText));
        await SaveAsync();
    }

    public void Dispose()
    {
        _incomingRequestService.Dispose();
    }

    private void NormalizeLoadedItem(DownloadItem item)
    {
        if (item.Status == DownloadStatus.Failed)
        {
            _downloadService.TryRepairCompletedPartial(item);
        }

        if (File.Exists(item.TargetPath) &&
            (item.Progress >= 99.9 ||
             item.Status is DownloadStatus.Completed or DownloadStatus.Failed or DownloadStatus.Canceled))
        {
            var length = new FileInfo(item.TargetPath).Length;
            item.BytesReceived = length;
            item.TotalBytes = length;
            item.Progress = 100;
            item.SpeedBytesPerSecond = 0;
            item.ErrorMessage = string.Empty;
            item.Status = DownloadStatus.Completed;
            return;
        }

        if (item.Status == DownloadStatus.Downloading)
        {
            item.Status = DownloadStatus.Paused;
            item.SpeedBytesPerSecond = 0;
        }
    }

    // -----------------------------------------------------------------------
    // Commands
    // -----------------------------------------------------------------------

    private void AddDownload()
    {
        if (!Uri.TryCreate(NewUrl.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            StatusText = "Paste a valid HTTP/HTTPS URL";
            return;
        }

        var urlString = uri.ToString();
        var isVideoSite = YtDlpService.IsSupported(urlString);

        var item = new DownloadItem
        {
            Url = urlString,
            Referrer = _pendingReferrer,
            FileName = isVideoSite
                ? CreateVideoFileName(uri)
                : CreateFileName(uri),
            TargetFolder = DownloadFolder,
            IsYtDlp = isVideoSite
        };

        _pendingReferrer = null;
        AttachDownloadItem(item);
        Downloads.Insert(0, item);
        SelectedDownload = item;
        NewUrl = string.Empty;
        StatusText = isVideoSite ? "Video download started (yt-dlp)" : "Download started";
        OnCountsChanged();
        _ = RunDownloadAsync(item);
    }

    private void ChooseFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose download folder",
            InitialDirectory = Directory.Exists(DownloadFolder)
                ? DownloadFolder
                : AppPaths.DefaultDownloadFolder
        };

        if (dialog.ShowDialog() == true)
        {
            DownloadFolder = dialog.FolderName;
            Directory.CreateDirectory(DownloadFolder);
        }
    }

    private void PauseSelected()
    {
        if (SelectedDownload is null)
        {
            return;
        }

        if (SelectedDownload.IsYtDlp)
        {
            _ytDlpService.Pause(SelectedDownload);
        }
        else
        {
            _downloadService.Pause(SelectedDownload);
        }

        StatusText = "Download paused";
    }

    private void ResumeSelected()
    {
        if (SelectedDownload is null ||
            SelectedDownload.Status is DownloadStatus.Downloading or DownloadStatus.Completed)
        {
            return;
        }

        StatusText = "Download resumed";
        _ = RunDownloadAsync(SelectedDownload);
    }

    private void CancelSelected()
    {
        if (SelectedDownload is null)
        {
            return;
        }

        if (SelectedDownload.IsYtDlp)
        {
            _ytDlpService.Cancel(SelectedDownload);
        }
        else
        {
            _downloadService.Cancel(SelectedDownload);
        }

        StatusText = "Download canceled";
        _ = SaveAsync();
    }

    private void DeleteSelected()
    {
        if (SelectedDownload is null)
        {
            return;
        }

        var item = SelectedDownload;
        var deleteChoice = _confirmDelete(item);

        if (deleteChoice == DeleteChoice.Cancel)
        {
            StatusText = "Delete canceled";
            return;
        }

        if (item.Status == DownloadStatus.Downloading)
        {
            if (item.IsYtDlp)
            {
                _ytDlpService.Cancel(item);
            }
            else
            {
                _downloadService.Cancel(item);
            }
        }

        if (deleteChoice == DeleteChoice.DeleteFileToo)
        {
            DeleteDownloadedFiles(item);
        }

        Downloads.Remove(item);
        SelectedDownload = Downloads.FirstOrDefault();
        StatusText = deleteChoice == DeleteChoice.DeleteFileToo
            ? "Removed from list and deleted file"
            : "Removed from list";
        OnCountsChanged();
        _ = SaveAsync();
    }

    private static void DeleteDownloadedFiles(DownloadItem item)
    {
        TryDeleteFile(item.TargetPath);
        TryDeleteFile(item.PartialPath);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch { }
    }

    private void OpenSelectedFolder()
    {
        if (SelectedDownload is null || !Directory.Exists(SelectedDownload.TargetFolder))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = SelectedDownload.TargetFolder,
            UseShellExecute = true
        });
    }

    private void OpenSelectedFile()
    {
        if (SelectedDownload is null || !File.Exists(SelectedDownload.TargetPath))
        {
            StatusText = "Downloaded file was not found";
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = SelectedDownload.TargetPath,
            UseShellExecute = true
        });
    }

    private void CopySelectedUrl()
    {
        if (SelectedDownload is null)
        {
            return;
        }

        Clipboard.SetText(SelectedDownload.Url);
        StatusText = "URL copied";
    }

    private void ClearCompleted()
    {
        var completed = Downloads
            .Where(item => item.Status == DownloadStatus.Completed)
            .ToList();

        foreach (var item in completed)
        {
            Downloads.Remove(item);
        }

        SelectedDownload = Downloads.FirstOrDefault();
        StatusText = completed.Count > 0 ? "Completed downloads cleared" : "No completed downloads";
        OnCountsChanged();
        _ = SaveAsync();
    }

    // -----------------------------------------------------------------------
    // Download runner â€” routes to the right engine
    // -----------------------------------------------------------------------

    private async Task RunDownloadAsync(DownloadItem item)
    {
        if (item.IsYtDlp)
        {
            await _ytDlpService.StartAsync(item);
        }
        else
        {
            await _downloadService.StartAsync(item);
        }

        StatusText = item.Status switch
        {
            DownloadStatus.Completed => "Download completed",
            DownloadStatus.Failed    => $"Failed: {item.ErrorMessage}",
            DownloadStatus.Paused    => "Paused",
            DownloadStatus.Canceled  => "Canceled",
            _                        => StatusText
        };

        OnCountsChanged();
        await SaveAsync();
    }

    private async Task SaveAsync() => await _storageService.SaveAsync(Downloads);

    private void OnUrlReceived(object? sender, IncomingRequestService.IncomingDownloadRequest request)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _pendingReferrer = request.Referrer;
            NewUrl = request.Url;
            AddDownload();
        });
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string CreateFileName(Uri uri)
    {
        var name = Path.GetFileName(Uri.UnescapeDataString(uri.AbsolutePath));

        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"download-{DateTime.Now:yyyyMMdd-HHmmss}.bin";
        }

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '-');
        }

        return name;
    }

    private static string CreateVideoFileName(Uri uri)
    {
        // yt-dlp will rename the file once the title is known;
        // this is just a placeholder shown in the list while it starts.
        var host = uri.Host.Replace("www.", "").Replace("m.", "");
        return $"video-{host}-{DateTime.Now:yyyyMMdd-HHmmss}.mkv";
    }

    private void AttachDownloadItem(DownloadItem item)
    {
        item.PropertyChanged += OnDownloadItemPropertyChanged;
    }

    private void OnDownloadItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DownloadItem.Status) or nameof(DownloadItem.Progress) or nameof(DownloadItem.ErrorMessage))
        {
            OnCountsChanged();

            if (ReferenceEquals(sender, SelectedDownload))
            {
                OnPropertyChanged(nameof(SelectedStatusText));
                OnPropertyChanged(nameof(SelectedProgressText));
            }
        }

        // FileName update from yt-dlp destination line
        if (e.PropertyName == nameof(DownloadItem.FileName) && ReferenceEquals(sender, SelectedDownload))
        {
            OnPropertyChanged(nameof(SelectedFileName));
            OnPropertyChanged(nameof(SelectedTargetPath));
        }
    }

    private void OnCountsChanged()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(FailedCount));
        OnPropertyChanged(nameof(FooterText));
        ClearCompletedCommand.RaiseCanExecuteChanged();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public enum DeleteChoice
{
    Cancel,
    RemoveFromListOnly,
    DeleteFileToo
}

