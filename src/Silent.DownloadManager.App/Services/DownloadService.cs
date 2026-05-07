using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Silent.DownloadManager.App.Models;

namespace Silent.DownloadManager.App.Services;

public sealed class DownloadService
{
    private static readonly HttpClient Client = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        AllowAutoRedirect = true
    });

    private readonly Dictionary<Guid, CancellationTokenSource> _activeDownloads = [];

    public async Task StartAsync(DownloadItem item)
    {
        Directory.CreateDirectory(item.TargetFolder);
        item.Status = DownloadStatus.Downloading;
        item.ErrorMessage = string.Empty;

        var cts = new CancellationTokenSource();
        _activeDownloads[item.Id] = cts;

        try
        {
            var existingBytes = File.Exists(item.PartialPath) ? new FileInfo(item.PartialPath).Length : 0;
            item.BytesReceived = existingBytes;

            using var request = new HttpRequestMessage(HttpMethod.Get, item.Url);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
            request.Headers.Accept.ParseAdd("video/webm,video/mp4,video/*;q=0.9,application/octet-stream;q=0.8,*/*;q=0.7");

            if (Uri.TryCreate(item.Referrer, UriKind.Absolute, out var referrer))
            {
                request.Headers.Referrer = referrer;
            }

            if (existingBytes > 0)
            {
                request.Headers.Range = new RangeHeaderValue(existingBytes, null);
            }

            using var response = await Client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cts.Token);

            if (existingBytes > 0 && response.StatusCode != HttpStatusCode.PartialContent)
            {
                existingBytes = 0;
                item.BytesReceived = 0;
                File.Delete(item.PartialPath);
            }

            response.EnsureSuccessStatusCode();

            if (IsHtmlPage(response))
            {
                throw new UnsupportedDownloadContentException(
                    "This URL points to a web page, not a direct downloadable file.");
            }

            if (existingBytes == 0)
            {
                ApplyServerFileNameIfBetter(item, response);
            }

            var contentLength = response.Content.Headers.ContentLength;
            item.TotalBytes = contentLength.HasValue ? existingBytes + contentLength.Value : null;

            {
                await using var input = await response.Content.ReadAsStreamAsync(cts.Token);
                await using var output = new FileStream(
                    item.PartialPath,
                    existingBytes > 0 ? FileMode.Append : FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    useAsync: true);

                var buffer = new byte[81920];
                var stopwatch = Stopwatch.StartNew();
                long intervalBytes = 0;

                while (true)
                {
                    var read = await input.ReadAsync(buffer, cts.Token);
                    if (read == 0)
                    {
                        break;
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), cts.Token);
                    item.BytesReceived += read;
                    intervalBytes += read;

                    if (item.TotalBytes is > 0)
                    {
                        item.Progress = Math.Min(100, item.BytesReceived * 100d / item.TotalBytes.Value);
                    }

                    if (stopwatch.ElapsedMilliseconds >= 800)
                    {
                        item.SpeedBytesPerSecond = intervalBytes / stopwatch.Elapsed.TotalSeconds;
                        intervalBytes = 0;
                        stopwatch.Restart();
                    }
                }
            }

            item.SpeedBytesPerSecond = 0;
            item.Progress = 100;
            CompletePartialFile(item);
            item.Status = DownloadStatus.Completed;
        }
        catch (OperationCanceledException)
        {
            item.SpeedBytesPerSecond = 0;
            item.Status = item.Status == DownloadStatus.Canceled
                ? DownloadStatus.Canceled
                : DownloadStatus.Paused;
        }
        catch (UnsupportedDownloadContentException ex)
        {
            item.SpeedBytesPerSecond = 0;
            item.BytesReceived = 0;
            item.Progress = 0;
            item.ErrorMessage = ex.Message;
            item.Status = DownloadStatus.Failed;
            TryDelete(item.PartialPath);
        }
        catch (Exception ex)
        {
            item.SpeedBytesPerSecond = 0;
            item.ErrorMessage = ex.Message;
            item.Status = DownloadStatus.Failed;
        }
        finally
        {
            _activeDownloads.Remove(item.Id);
        }
    }

    public bool TryRepairCompletedPartial(DownloadItem item)
    {
        if (!File.Exists(item.PartialPath))
        {
            return false;
        }

        var partialSize = new FileInfo(item.PartialPath).Length;

        if (partialSize <= 0)
        {
            return false;
        }

        var looksComplete = item.Progress >= 99.9 ||
            item.ErrorMessage.Contains("being used by another process", StringComparison.OrdinalIgnoreCase);

        if (!looksComplete)
        {
            return false;
        }

        if (item.TotalBytes is > 0 && partialSize < item.TotalBytes.Value)
        {
            return false;
        }

        try
        {
            item.BytesReceived = partialSize;
            item.Progress = 100;
            CompletePartialFile(item);
            item.Status = DownloadStatus.Completed;
            item.ErrorMessage = string.Empty;
            item.SpeedBytesPerSecond = 0;
            return true;
        }
        catch (Exception ex)
        {
            item.ErrorMessage = ex.Message;
            item.Status = DownloadStatus.Failed;
            return false;
        }
    }

    private static void CompletePartialFile(DownloadItem item)
    {
        if (!File.Exists(item.PartialPath))
        {
            return;
        }

        if (File.Exists(item.TargetPath))
        {
            File.Delete(item.TargetPath);
        }

        File.Move(item.PartialPath, item.TargetPath);
    }

    public void Pause(DownloadItem item)
    {
        if (_activeDownloads.TryGetValue(item.Id, out var cts))
        {
            item.Status = DownloadStatus.Paused;
            cts.Cancel();
        }
    }

    public void Cancel(DownloadItem item)
    {
        item.Status = DownloadStatus.Canceled;

        if (_activeDownloads.TryGetValue(item.Id, out var cts))
        {
            cts.Cancel();
        }

        TryDelete(item.PartialPath);
    }

    private static bool IsHtmlPage(HttpResponseMessage response)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        return string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mediaType, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyServerFileNameIfBetter(DownloadItem item, HttpResponseMessage response)
    {
        var serverFileName = response.Content.Headers.ContentDisposition?.FileNameStar ??
            response.Content.Headers.ContentDisposition?.FileName;

        if (string.IsNullOrWhiteSpace(serverFileName))
        {
            return;
        }

        serverFileName = SanitizeFileName(serverFileName.Trim('"'));

        if (string.IsNullOrWhiteSpace(serverFileName))
        {
            return;
        }

        if (!Path.HasExtension(item.FileName) || item.FileName.StartsWith("download-", StringComparison.OrdinalIgnoreCase))
        {
            item.FileName = serverFileName;
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalid, '-');
        }

        return fileName;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Cleanup failure should not crash the UI.
        }
    }

    private sealed class UnsupportedDownloadContentException(string message) : Exception(message);
}

