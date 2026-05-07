namespace Silent.DownloadManager.App.Services;

public static class AppPaths
{
    public static string AppDataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SiS SDM");

    public static string IncomingFolder { get; } = Path.Combine(AppDataRoot, "Incoming");

    public static string HistoryFile { get; } = Path.Combine(AppDataRoot, "downloads.json");

    public static string DefaultDownloadFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Downloads",
        "SiS SDM");

    public static void Ensure()
    {
        Directory.CreateDirectory(AppDataRoot);
        Directory.CreateDirectory(IncomingFolder);
        Directory.CreateDirectory(DefaultDownloadFolder);
    }
}

