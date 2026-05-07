using System.Text.Json;
using Silent.DownloadManager.App.Models;

namespace Silent.DownloadManager.App.Services;

public sealed class StorageService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public async Task<IReadOnlyList<DownloadItem>> LoadAsync()
    {
        AppPaths.Ensure();

        if (!File.Exists(AppPaths.HistoryFile))
        {
            return [];
        }

        await using var stream = File.OpenRead(AppPaths.HistoryFile);
        return await JsonSerializer.DeserializeAsync<List<DownloadItem>>(stream, Options) ?? [];
    }

    public async Task SaveAsync(IEnumerable<DownloadItem> downloads)
    {
        AppPaths.Ensure();
        await using var stream = File.Create(AppPaths.HistoryFile);
        await JsonSerializer.SerializeAsync(stream, downloads, Options);
    }
}

