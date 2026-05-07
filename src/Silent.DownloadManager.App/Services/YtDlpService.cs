using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Windows;
using Silent.DownloadManager.App.Models;

namespace Silent.DownloadManager.App.Services;

/// <summary>
/// Downloads videos from YouTube, Facebook, and 1000+ other sites using yt-dlp.
/// yt-dlp.exe and ffmpeg.exe are auto-downloaded on first use if missing.
/// </summary>
public sealed class YtDlpService
{
    private static readonly string YtDlpPath = Path.Combine(
        AppContext.BaseDirectory, "yt-dlp.exe");

    private static readonly string FfmpegPath = Path.Combine(
        AppContext.BaseDirectory, "ffmpeg.exe");

    // Optional: place a Netscape-format cookies.txt next to SDM.App.exe
    // for sites that require login (Facebook, Instagram, etc.)
    private static readonly string CookiesFilePath = Path.Combine(
        AppContext.BaseDirectory, "cookies.txt");

    private readonly Dictionary<Guid, CancellationTokenSource> _activeDownloads = [];

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    public static bool IsSupported(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        // FIX: Proper host normalization instead of broken TrimStart('w', '.')
        var host = NormalizeHost(uri.Host);

        string[] supported =
        [
            "youtube.com", "youtu.be",
            "facebook.com", "fb.watch", "fb.com",
            "instagram.com",
            "twitter.com", "x.com",
            "tiktok.com",
            "vimeo.com",
            "dailymotion.com",
            "twitch.tv",
            "reddit.com",
            "bilibili.com",
            "nicovideo.jp",
            "streamable.com",
            "odysee.com",
            "rumble.com",
            "9gag.com",
            "imgur.com",
            "gfycat.com",
        ];

        return supported.Any(s => host == s || host.EndsWith("." + s, StringComparison.Ordinal));
    }

    public async Task StartAsync(DownloadItem item, string qualityFormat = "bestvideo+bestaudio/best")
    {
        Directory.CreateDirectory(item.TargetFolder);
        SetOnUi(item, i =>
        {
            i.Status = DownloadStatus.Downloading;
            i.ErrorMessage = string.Empty;
        });

        var cts = new CancellationTokenSource();
        _activeDownloads[item.Id] = cts;

        try
        {
            await EnsureYtDlpAsync(cts.Token);
            await EnsureFfmpegAsync(cts.Token);

            var outputTemplate = Path.Combine(item.TargetFolder, "%(title)s.%(ext)s");
            var args = BuildArguments(item.Url, outputTemplate, qualityFormat, item.Referrer);

            await RunYtDlpAsync(item, args, cts.Token);

            if (!cts.Token.IsCancellationRequested)
            {
                SetOnUi(item, i =>
                {
                    i.Progress = 100;
                    i.SpeedBytesPerSecond = 0;
                    i.Status = DownloadStatus.Completed;
                });
            }
        }
        catch (OperationCanceledException)
        {
            SetOnUi(item, i =>
            {
                i.SpeedBytesPerSecond = 0;
                i.Status = i.Status == DownloadStatus.Canceled
                    ? DownloadStatus.Canceled
                    : DownloadStatus.Paused;
            });
        }
        catch (YtDlpNotFoundException)
        {
            SetOnUi(item, i =>
            {
                i.SpeedBytesPerSecond = 0;
                i.ErrorMessage = "yt-dlp.exe not found and could not be downloaded. Place yt-dlp.exe next to SDM.App.exe.";
                i.Status = DownloadStatus.Failed;
            });
        }
        catch (Exception ex)
        {
            SetOnUi(item, i =>
            {
                i.SpeedBytesPerSecond = 0;
                i.ErrorMessage = ex.Message;
                i.Status = DownloadStatus.Failed;
            });
        }
        finally
        {
            _activeDownloads.Remove(item.Id);
        }
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
    }

    // -----------------------------------------------------------------------
    // Internals
    // -----------------------------------------------------------------------

    /// <summary>
    /// FIX: Properly strips www. and m. prefixes from hostname.
    /// Old code used TrimStart('w', '.') which is character-based and
    /// fails for hosts like "m.youtube.com" or "m.facebook.com".
    /// </summary>
    private static string NormalizeHost(string host)
    {
        host = host.ToLowerInvariant();
        if (host.StartsWith("www.")) host = host[4..];
        else if (host.StartsWith("m.")) host = host[2..];
        return host;
    }

    private static void SetOnUi(DownloadItem item, Action<DownloadItem> update)
    {
        if (Application.Current?.Dispatcher is { } dispatcher)
            dispatcher.Invoke(() => update(item));
        else
            update(item);
    }

    private static IReadOnlyList<string> BuildArguments(string url, string outputTemplate, string format, string? referrer)
    {
        var args = new List<string>();

        // Video+audio format, fallback to best available
        args.AddRange(["-f", format]);

        // FIX: Use mp4 as merge format (better compatibility than mkv when ffmpeg is present)
        args.AddRange(["--merge-output-format", "mp4"]);

        // FIX: Tell yt-dlp where ffmpeg is (next to the exe)
        var ffmpegDir = AppContext.BaseDirectory;
        args.AddRange(["--ffmpeg-location", ffmpegDir]);

        // Output path template
        args.AddRange(["-o", outputTemplate]);

        // Machine-readable progress lines
        args.Add("--newline");

        // Single video only, no playlist
        args.Add("--no-playlist");

        // Use cookies.txt if it exists next to the exe (for Facebook, Instagram login)
        if (File.Exists(CookiesFilePath))
        {
            args.AddRange(["--cookies", CookiesFilePath]);
        }

        // Optional subtitles
        args.AddRange(["--write-subs", "--sub-langs", "en", "--embed-subs"]);

        // Referrer header
        if (!string.IsNullOrWhiteSpace(referrer))
            args.AddRange(["--referer", referrer]);

        args.AddRange(["--retries", "5", url]);

        return args;
    }

    private static async Task RunYtDlpAsync(DownloadItem item, IReadOnlyList<string> args, CancellationToken token)
    {
        var psi = new ProcessStartInfo
        {
            FileName = YtDlpPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.Start();

        // Read stderr fully for error reporting
        var stderrTask = process.StandardError.ReadToEndAsync(token);

        // Read stdout line by line and update progress on UI thread
        var readTask = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync(token) is { } line)
            {
                ParseProgressLine(item, line);
            }
        }, token);

        await using var reg = token.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); }
            catch { /* already exited */ }
        });

        await readTask;
        await process.WaitForExitAsync(token);

        if (process.ExitCode != 0 && !token.IsCancellationRequested)
        {
            var error = await stderrTask;
            throw new Exception(ExtractUserFriendlyError(error));
        }
    }

    private static readonly Regex ProgressRegex = new(
        @"\[download\]\s+(?<pct>[\d.]+)%\s+of\s+(?<size>[\d.]+)(?<unit>\w+)\s+at\s+(?<speed>[\d.]+)(?<su>\w+)/s",
        RegexOptions.Compiled);

    private static readonly Regex DestinationRegex = new(
        @"\[(?:download|Merger|ffmpeg)\] (?:Destination|Merging formats into): (.+)$",
        RegexOptions.Compiled);

    private static void ParseProgressLine(DownloadItem item, string line)
    {
        var pm = ProgressRegex.Match(line);
        if (pm.Success)
        {
            double pct = 0;
            if (double.TryParse(pm.Groups["pct"].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var p))
                pct = Math.Min(100, p);

            var totalBytes = ParseSize(pm.Groups["size"].Value, pm.Groups["unit"].Value);
            var speed = ParseSize(pm.Groups["speed"].Value, pm.Groups["su"].Value);

            SetOnUi(item, i =>
            {
                i.Progress = pct;
                if (totalBytes > 0)
                {
                    i.TotalBytes = totalBytes;
                    i.BytesReceived = (long)(totalBytes * pct / 100.0);
                }
                i.SpeedBytesPerSecond = speed;
            });
            return;
        }

        var dm = DestinationRegex.Match(line);
        if (dm.Success)
        {
            var filePath = dm.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                var fileName = Path.GetFileName(filePath);
                SetOnUi(item, i => i.FileName = fileName);
            }
        }
    }

    private static long ParseSize(string value, string unit)
    {
        if (!double.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var num))
            return 0;

        return unit.ToUpperInvariant() switch
        {
            "KIB" or "KB" => (long)(num * 1024),
            "MIB" or "MB" => (long)(num * 1024 * 1024),
            "GIB" or "GB" => (long)(num * 1024 * 1024 * 1024),
            "B"           => (long)num,
            _             => (long)num
        };
    }

    private static string ExtractUserFriendlyError(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
            return "yt-dlp exited with an error.";

        var errorLine = stderr
            .Split('\n')
            .LastOrDefault(l => l.TrimStart().StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase));

        return errorLine?.Trim()
            ?? stderr.Split('\n').LastOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim()
            ?? "yt-dlp exited with an error.";
    }

    // -----------------------------------------------------------------------
    // Auto-download yt-dlp.exe
    // -----------------------------------------------------------------------

    private static readonly SemaphoreSlim _dlLock = new(1, 1);
    private static readonly SemaphoreSlim _ffmpegLock = new(1, 1);

    private static async Task EnsureYtDlpAsync(CancellationToken token)
    {
        if (File.Exists(YtDlpPath))
            return;

        await _dlLock.WaitAsync(token);

        try
        {
            if (File.Exists(YtDlpPath))
                return;

            const string ReleaseUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";

            using var client = new System.Net.Http.HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SDM/1.0");
            client.Timeout = TimeSpan.FromMinutes(5);

            var bytes = await client.GetByteArrayAsync(ReleaseUrl, token);
            await File.WriteAllBytesAsync(YtDlpPath, bytes, token);
        }
        catch (Exception ex)
        {
            throw new YtDlpNotFoundException(ex.Message);
        }
        finally
        {
            _dlLock.Release();
        }
    }

    /// <summary>
    /// FIX: Auto-download ffmpeg.exe so video+audio merge works for Facebook/Instagram etc.
    /// Without ffmpeg, yt-dlp cannot merge separate video and audio streams.
    /// Downloads the Windows ffmpeg binary from GitHub releases.
    /// </summary>
    private static async Task EnsureFfmpegAsync(CancellationToken token)
    {
        if (File.Exists(FfmpegPath))
            return;

        await _ffmpegLock.WaitAsync(token);

        try
        {
            if (File.Exists(FfmpegPath))
                return;

            // yt-dlp ships a self-contained ffmpeg build specifically for yt-dlp use
            const string FfmpegUrl = "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";

            using var client = new System.Net.Http.HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SDM/1.0");
            client.Timeout = TimeSpan.FromMinutes(10);

            var zipBytes = await client.GetByteArrayAsync(FfmpegUrl, token);
            var zipPath = Path.Combine(AppContext.BaseDirectory, "_ffmpeg_tmp.zip");

            await File.WriteAllBytesAsync(zipPath, zipBytes, token);

            try
            {
                // Extract only ffmpeg.exe from the zip
                using var zip = ZipFile.OpenRead(zipPath);
                var ffmpegEntry = zip.Entries.FirstOrDefault(e =>
                    e.Name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase));

                if (ffmpegEntry is not null)
                {
                    ffmpegEntry.ExtractToFile(FfmpegPath, overwrite: true);
                }
            }
            finally
            {
                TryDeleteFile(zipPath);
            }
        }
        catch
        {
            // ffmpeg download failure is non-fatal — yt-dlp will fall back to
            // single-stream download. Silently ignore so the download still starts.
        }
        finally
        {
            _ffmpegLock.Release();
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private sealed class YtDlpNotFoundException(string message) : Exception(message);
}
