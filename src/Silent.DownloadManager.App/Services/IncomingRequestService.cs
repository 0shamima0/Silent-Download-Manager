using System.IO;
using System.Text.Json;

namespace Silent.DownloadManager.App.Services;

public sealed class IncomingRequestService : IDisposable
{
    private readonly FileSystemWatcher _watcher;

    public IncomingRequestService()
    {
        AppPaths.Ensure();
        _watcher = new FileSystemWatcher(AppPaths.IncomingFolder, "*.json")
        {
            IncludeSubdirectories = false
        };
    }

    public event EventHandler<IncomingDownloadRequest>? UrlReceived;

    public void Start()
    {
        _watcher.Created += OnCreated;
        _watcher.EnableRaisingEvents = true;
        ProcessExistingFiles();
    }

    public void Dispose()
    {
        _watcher.Created -= OnCreated;
        _watcher.Dispose();
    }

    private void OnCreated(object sender, FileSystemEventArgs e)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(150);
            ProcessFile(e.FullPath);
        });
    }

    private void ProcessExistingFiles()
    {
        foreach (var file in Directory.EnumerateFiles(AppPaths.IncomingFolder, "*.json"))
        {
            ProcessFile(file);
        }
    }

    private void ProcessFile(string file)
    {
        try
        {
            var json = File.ReadAllText(file);
            var request = JsonSerializer.Deserialize<IncomingDownloadRequest>(json);

            File.Delete(file);

            if (!string.IsNullOrWhiteSpace(request?.Url))
            {
                UrlReceived?.Invoke(this, request);
            }
        }
        catch
        {
            // Extension handoff should never crash the desktop app.
        }
    }

    public sealed record IncomingDownloadRequest(string Url, string? Referrer);
}

