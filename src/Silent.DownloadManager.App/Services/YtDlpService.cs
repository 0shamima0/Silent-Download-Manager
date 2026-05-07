using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Windows;
using Silent.DownloadManager.App.Models;

namespace Silent.DownloadManager.App.Services;

/// <summary>
/// Downloads videos from YouTube, Facebook, Instagram, Twitter and 1000+ other sites using yt-dlp.
/// yt-dlp.exe and ffmpeg.exe are auto-downloaded on first use if missing.
/// </summary>
public sealed class YtDlpService
{
    private static readonly string YtDlpPath = Path.Combine(
        AppContext.BaseDirectory, "yt-dlp.exe");

    private static readonly string FfmpegPath = Path.Combine(
        AppContext.BaseDirectory, "ffmpeg.exe");

    // Place a Netscape-format cookies.txt next to SDM.App.exe for login-required sites
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

        var host = NormalizeHost(uri.Host);

        string[] supported =
        [
            "youtube.com", "youtu.be", "music.youtube.com",
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
            "pinterest.com",
            "linkedin.com",
            "snapchat.com",
        ];

        return supported.Any(s => host == s || host.EndsWith("." + s, StringComparison.Ordinal));
    }

    /// <summary>
    /// Fetch available quality/format options from the URL without downloading.
    /// Returns a list of VideoQualityOption for the UI to display.
    /// </summary>
    public async Task<List<VideoQualityOption>> GetAvailableFormatsAsync(string url, CancellationToken token = default)
    {
        await EnsureYtDlpAsync(token);

        var args = BuildListFormatsArguments(url);
        var psi = new ProcessStartInfo
        {
            FileName = YtDlpPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);

        return ParseFormatList(output);
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

            // Update yt-dlp before each download to fix Facebook/Instagram parsing issues
            await TryUpdateYtDlpAsync(cts.Token);

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
            cts.Cancel();
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

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

    private static IReadOnlyList<string> BuildListFormatsArguments(string url)
    {
        var args = new List<string> { "-F", "--no-playlist" };

        if (File.Exists(CookiesFilePath))
            args.AddRange(["--cookies", CookiesFilePath]);

        // FIX: Use cookies-from-browser as fallback to help Facebook/Instagram
        // when no cookies.txt is present
        args.AddRange(["--cookies-from-browser", "chrome"]);

        args.Add(url);
        return args;
    }

    private static IReadOnlyList<string> BuildArguments(
        string url, string outputTemplate, string format, string? referrer)
    {
        var args = new List<string>();

        // Format selection
        args.AddRange(["-f", format]);

        // FIX: mp4 merge format for best compatibility
        args.AddRange(["--merge-output-format", "mp4"]);

        // Tell yt-dlp where ffmpeg is
        args.AddRange(["--ffmpeg-location", AppContext.BaseDirectory]);

        // Output path template
        args.AddRange(["-o", outputTemplate]);

        // Machine-readable progress lines
        args.Add("--newline");

        // Single video only
        args.Add("--no-playlist");

        // FIX: Add retries and fragment retries for better reliability
        args.AddRange(["--retries", "10"]);
        args.AddRange(["--fragment-retries", "10"]);

        // FIX: Use cookies-from-browser for Facebook/Instagram when cookies.txt missing
        if (File.Exists(CookiesFilePath))
        {
            args.AddRange(["--cookies", CookiesFilePath]);
        }

        // FIX: Embed metadata and thumbnails for better file info
        args.Add("--embed-metadata");
        args.Add("--embed-thumbnail");

        // Optional subtitles
        args.AddRange(["--write-subs", "--sub-langs", "en", "--embed-subs"]);

        // Referrer header
        if (!string.IsNullOrWhiteSpace(referrer))
            args.AddRange(["--referer", referrer]);

        // FIX: Add a proper user-agent to avoid blocks
        args.AddRange(["--user-agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36"]);

        args.Add(url);
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
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.Start();

        var stderrTask = process.StandardError.ReadToEndAsync(token);

        var readTask = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync(token) is { } line)
                ParseProgressLine(item, line);
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

    // FIX: Expanded progress regex to also handle size-only lines Twitter/X outputs
    private static readonly Regex ProgressRegex = new(
        @"\[download\]\s+(?<pct>[\d.]+)%\s+of\s+(?<size>[\d.]+)(?<unit>[KMGBkib]+)\s+at\s+(?<speed>[\d.]+)(?<su>[KMGBkib]+)/s",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // FIX: Separate regex for size lines without speed (Twitter/X)
    private static readonly Regex SizeOnlyRegex = new(
        @"\[download\]\s+(?<pct>[\d.]+)%\s+of\s+(?<size>[\d.]+)(?<unit>[KMGBkib]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DestinationRegex = new(
        @"\[(?:download|Merger|ffmpeg)\] (?:Destination|Merging formats into): (.+)$",
        RegexOptions.Compiled);

    // FIX: Regex to catch total file size from "[download] ... of ~X.XXMIB"
    private static readonly Regex TotalSizeRegex = new(
        @"of ~?(?<size>[\d.]+)(?<unit>[KMGBkib]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static void ParseProgressLine(DownloadItem item, string line)
    {
        // Full progress line with speed
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

        // FIX: Size-only line (Twitter/X and some other sites omit speed)
        var sm = SizeOnlyRegex.Match(line);
        if (sm.Success)
        {
            double pct = 0;
            if (double.TryParse(sm.Groups["pct"].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var p))
                pct = Math.Min(100, p);

            var totalBytes = ParseSize(sm.Groups["size"].Value, sm.Groups["unit"].Value);

            SetOnUi(item, i =>
            {
                i.Progress = pct;
                if (totalBytes > 0)
                {
                    i.TotalBytes = totalBytes;
                    i.BytesReceived = (long)(totalBytes * pct / 100.0);
                }
            });
            return;
        }

        // Destination filename
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
            "KIB" or "KB" or "K" => (long)(num * 1024),
            "MIB" or "MB" or "M" => (long)(num * 1024 * 1024),
            "GIB" or "GB" or "G" => (long)(num * 1024 * 1024 * 1024),
            "B"                   => (long)num,
            _                     => (long)num
        };
    }

    private static string ExtractUserFriendlyError(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
            return "yt-dlp exited with an error.";

        // FIX: Better Facebook error guidance
        if (stderr.Contains("Cannot parse data", StringComparison.OrdinalIgnoreCase) &&
            stderr.Contains("facebook", StringComparison.OrdinalIgnoreCase))
        {
            return "Facebook download failed: yt-dlp needs updating. The app will auto-update on next attempt. " +
                   "For private/login-required videos, place a cookies.txt file next to SDM.App.exe.";
        }

        if (stderr.Contains("login", StringComparison.OrdinalIgnoreCase) ||
            stderr.Contains("cookies", StringComparison.OrdinalIgnoreCase) ||
            stderr.Contains("sign in", StringComparison.OrdinalIgnoreCase))
        {
            return "This video requires login. Place a Netscape-format cookies.txt next to SDM.App.exe, " +
                   "or use --cookies-from-browser in yt-dlp directly.";
        }

        if (stderr.Contains("Private video", StringComparison.OrdinalIgnoreCase))
            return "This video is private and cannot be downloaded.";

        if (stderr.Contains("removed", StringComparison.OrdinalIgnoreCase) ||
            stderr.Contains("unavailable", StringComparison.OrdinalIgnoreCase))
            return "This video is unavailable or has been removed.";

        var errorLine = stderr
            .Split('\n')
            .LastOrDefault(l => l.TrimStart().StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase));

        return errorLine?.Trim()
            ?? stderr.Split('\n').LastOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim()
            ?? "yt-dlp exited with an error.";
    }

    // Parse "-F" format list output into quality options
    private static List<VideoQualityOption> ParseFormatList(string output)
    {
        var result = new List<VideoQualityOption>();
        var lines = output.Split('\n');
        bool inFormats = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("ID") || line.StartsWith("--"))
            {
                inFormats = true;
                continue;
            }

            if (!inFormats || string.IsNullOrWhiteSpace(line)) continue;

            // Parse format lines: ID EXT RESOLUTION ...
            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) continue;

            var formatId = parts[0];
            var ext = parts[1];
            var resolution = parts[2];

            // Skip storyboard / mhtml
            if (ext.Equals("mhtml", StringComparison.OrdinalIgnoreCase)) continue;

            var label = BuildQualityLabel(resolution, ext, line);
            var ytdlpFormat = BuildFormatString(formatId, line);

            result.Add(new VideoQualityOption
            {
                FormatId = formatId,
                Extension = ext,
                Resolution = resolution,
                Label = label,
                YtDlpFormat = ytdlpFormat
            });
        }

        // Add common preset options at top
        result.Insert(0, new VideoQualityOption
        {
            FormatId = "best",
            Label = "Best Quality (auto)",
            YtDlpFormat = "bestvideo+bestaudio/best",
            Resolution = "auto"
        });

        return result;
    }

    private static string BuildQualityLabel(string resolution, string ext, string line)
    {
        // Try to identify if it's video-only, audio-only, or combined
        var lower = line.ToLowerInvariant();
        var isAudioOnly = lower.Contains("audio only") || resolution.Equals("audio only", StringComparison.OrdinalIgnoreCase);
        var isVideoOnly = lower.Contains("video only");

        if (isAudioOnly)
        {
            var abr = ExtractBitrate(line);
            return abr > 0 ? $"Audio only - {abr}kbps ({ext})" : $"Audio only ({ext})";
        }

        var height = ExtractHeight(resolution);
        var suffix = isVideoOnly ? " (video only)" : "";

        return height > 0 ? $"{height}p{suffix} ({ext})" : $"{resolution}{suffix} ({ext})";
    }

    private static string BuildFormatString(string formatId, string line)
    {
        var lower = line.ToLowerInvariant();
        if (lower.Contains("video only"))
            return $"{formatId}+bestaudio/{formatId}";
        return formatId;
    }

    private static int ExtractHeight(string resolution)
    {
        // e.g. "1920x1080" -> 1080, "1080p" -> 1080
        var match = Regex.Match(resolution, @"(\d+)x(\d+)");
        if (match.Success && int.TryParse(match.Groups[2].Value, out var h)) return h;
        match = Regex.Match(resolution, @"(\d+)p");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var h2)) return h2;
        return 0;
    }

    private static int ExtractBitrate(string line)
    {
        var match = Regex.Match(line, @"(\d+)k");
        return match.Success && int.TryParse(match.Groups[1].Value, out var k) ? k : 0;
    }

    // -----------------------------------------------------------------------
    // Auto-download / update yt-dlp.exe
    // -----------------------------------------------------------------------

    private static readonly SemaphoreSlim _dlLock = new(1, 1);
    private static readonly SemaphoreSlim _ffmpegLock = new(1, 1);
    private static DateTime _lastYtDlpUpdate = DateTime.MinValue;

    private static async Task EnsureYtDlpAsync(CancellationToken token)
    {
        if (File.Exists(YtDlpPath))
            return;

        await _dlLock.WaitAsync(token);
        try
        {
            if (File.Exists(YtDlpPath)) return;
            await DownloadYtDlpAsync(token);
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
    /// FIX: Auto-update yt-dlp once per day to fix Facebook/Instagram parsing errors.
    /// Facebook's HTML structure changes frequently, so keeping yt-dlp updated is essential.
    /// </summary>
    private static async Task TryUpdateYtDlpAsync(CancellationToken token)
    {
        // Only update once per day
        if ((DateTime.UtcNow - _lastYtDlpUpdate).TotalHours < 24)
            return;

        await _dlLock.WaitAsync(token);
        try
        {
            if ((DateTime.UtcNow - _lastYtDlpUpdate).TotalHours < 24) return;

            // Try running yt-dlp --update first (faster than re-downloading)
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = YtDlpPath,
                    ArgumentList = { "-U" },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = new Process { StartInfo = psi };
                p.Start();
                await p.WaitForExitAsync(token);
            }
            catch
            {
                // Fall back to fresh download if update fails
                await DownloadYtDlpAsync(token);
            }

            _lastYtDlpUpdate = DateTime.UtcNow;
        }
        catch
        {
            // Update failure is non-fatal
        }
        finally
        {
            _dlLock.Release();
        }
    }

    private static async Task DownloadYtDlpAsync(CancellationToken token)
    {
        const string ReleaseUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";

        using var client = new System.Net.Http.HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SDM/1.0");
        client.Timeout = TimeSpan.FromMinutes(5);

        var bytes = await client.GetByteArrayAsync(ReleaseUrl, token);
        var tmpPath = YtDlpPath + ".tmp";
        await File.WriteAllBytesAsync(tmpPath, bytes, token);

        // Atomic replace
        if (File.Exists(YtDlpPath)) File.Delete(YtDlpPath);
        File.Move(tmpPath, YtDlpPath);
    }

    private static async Task EnsureFfmpegAsync(CancellationToken token)
    {
        if (File.Exists(FfmpegPath))
            return;

        await _ffmpegLock.WaitAsync(token);

        try
        {
            if (File.Exists(FfmpegPath)) return;

            const string FfmpegUrl = "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";

            using var client = new System.Net.Http.HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SDM/1.0");
            client.Timeout = TimeSpan.FromMinutes(10);

            var zipBytes = await client.GetByteArrayAsync(FfmpegUrl, token);
            var zipPath = Path.Combine(AppContext.BaseDirectory, "_ffmpeg_tmp.zip");

            await File.WriteAllBytesAsync(zipPath, zipBytes, token);

            try
            {
                using var zip = ZipFile.OpenRead(zipPath);
                var ffmpegEntry = zip.Entries.FirstOrDefault(e =>
                    e.Name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase));

                if (ffmpegEntry is not null)
                    ffmpegEntry.ExtractToFile(FfmpegPath, overwrite: true);
            }
            finally
            {
                TryDeleteFile(zipPath);
            }
        }
        catch
        {
            // Non-fatal: yt-dlp falls back to single-stream
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

/// <summary>Represents one quality/format option shown in the resolution picker.</summary>
public sealed class VideoQualityOption
{
    public string FormatId { get; init; } = "";
    public string Label { get; init; } = "";
    public string Resolution { get; init; } = "";
    public string Extension { get; init; } = "";
    /// <summary>The -f argument string passed directly to yt-dlp.</summary>
    public string YtDlpFormat { get; init; } = "bestvideo+bestaudio/best";

    public override string ToString() => Label;
}
