using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Silent.DownloadManager.App.Models;

public sealed class DownloadItem : INotifyPropertyChanged
{
    private string _fileName = string.Empty;
    private string _targetFolder = string.Empty;
    private long _bytesReceived;
    private long? _totalBytes;
    private double _speedBytesPerSecond;
    private double _progress;
    private DownloadStatus _status = DownloadStatus.Queued;
    private string _errorMessage = string.Empty;
    private string _qualityLabel = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Url { get; init; }

    public string? Referrer { get; init; }

    /// <summary>
    /// True when this download should be handled by YtDlpService (YouTube, Facebook, etc.)
    /// instead of the plain HTTP DownloadService.
    /// </summary>
    public bool IsYtDlp { get; set; }

    /// <summary>
    /// The yt-dlp format string used for this download, e.g. "bestvideo[height=1080]+bestaudio/best".
    /// Empty means "bestvideo+bestaudio/best" (auto best).
    /// </summary>
    public string YtDlpFormat { get; set; } = "bestvideo+bestaudio/best";

    public required string FileName
    {
        get => _fileName;
        set
        {
            if (SetField(ref _fileName, value))
            {
                OnPropertyChanged(nameof(TargetPath));
                OnPropertyChanged(nameof(PartialPath));
            }
        }
    }

    public required string TargetFolder
    {
        get => _targetFolder;
        set
        {
            if (SetField(ref _targetFolder, value))
            {
                OnPropertyChanged(nameof(TargetPath));
                OnPropertyChanged(nameof(PartialPath));
            }
        }
    }

    public string TargetPath => Path.Combine(TargetFolder, FileName);

    public string PartialPath => TargetPath + ".part";

    public long BytesReceived
    {
        get => _bytesReceived;
        set
        {
            if (SetField(ref _bytesReceived, value))
            {
                OnPropertyChanged(nameof(ProgressLabel));
                OnPropertyChanged(nameof(SizeLabel));
            }
        }
    }

    public long? TotalBytes
    {
        get => _totalBytes;
        set
        {
            if (SetField(ref _totalBytes, value))
            {
                OnPropertyChanged(nameof(ProgressLabel));
                OnPropertyChanged(nameof(SizeLabel));
            }
        }
    }

    public double SpeedBytesPerSecond
    {
        get => _speedBytesPerSecond;
        set
        {
            if (SetField(ref _speedBytesPerSecond, value))
                OnPropertyChanged(nameof(SpeedLabel));
        }
    }

    public double Progress
    {
        get => _progress;
        set
        {
            if (SetField(ref _progress, value))
                OnPropertyChanged(nameof(ProgressLabel));
        }
    }

    public DownloadStatus Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetField(ref _errorMessage, value);
    }

    /// <summary>Human-readable quality label, e.g. "1080p (mp4)"</summary>
    public string QualityLabel
    {
        get => _qualityLabel;
        set => SetField(ref _qualityLabel, value);
    }

    public string ProgressLabel => TotalBytes is > 0
        ? $"{FormatBytes(BytesReceived)} / {FormatBytes(TotalBytes.Value)} ({Progress:0.0}%)"
        : $"{FormatBytes(BytesReceived)} downloaded";

    public string SpeedLabel => Status == DownloadStatus.Downloading
        ? $"{FormatBytes((long)SpeedBytesPerSecond)}/s"
        : "-";

    public string SizeLabel => TotalBytes is > 0 ? FormatBytes(TotalBytes.Value) : "Unknown";

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
