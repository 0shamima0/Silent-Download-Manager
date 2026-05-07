using System.Diagnostics;
using System.Text;
using System.Text.Json;

const string AppFolderName = "SiS SDM";

try
{
    var message = ReadNativeMessage();

    if (message is null || string.IsNullOrWhiteSpace(message.Url))
    {
        WriteNativeMessage(new NativeResponse(false, "No URL received."));
        return;
    }

    var incomingFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppFolderName,
        "Incoming");

    Directory.CreateDirectory(incomingFolder);

    var requestPath = Path.Combine(incomingFolder, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json");
    await File.WriteAllTextAsync(
        requestPath,
        JsonSerializer.Serialize(new IncomingRequest(message.Url, message.Referrer)),
        Encoding.UTF8);

    TryStartDesktopApp();
    WriteNativeMessage(new NativeResponse(true, "Sent to Silent Download Manager."));
}
catch (Exception ex)
{
    WriteNativeMessage(new NativeResponse(false, ex.Message));
}

static NativeRequest? ReadNativeMessage()
{
    using var stdin = Console.OpenStandardInput();
    var lengthBytes = new byte[4];
    var read = stdin.Read(lengthBytes, 0, 4);

    if (read != 4)
    {
        return null;
    }

    var length = BitConverter.ToInt32(lengthBytes, 0);
    if (length <= 0)
    {
        return null;
    }

    var buffer = new byte[length];
    var offset = 0;

    while (offset < length)
    {
        var chunk = stdin.Read(buffer, offset, length - offset);
        if (chunk == 0)
        {
            break;
        }

        offset += chunk;
    }

    var json = Encoding.UTF8.GetString(buffer, 0, offset);
    return JsonSerializer.Deserialize<NativeRequest>(json, new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    });
}

static void WriteNativeMessage(NativeResponse response)
{
    var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });

    var bytes = Encoding.UTF8.GetBytes(json);
    var lengthBytes = BitConverter.GetBytes(bytes.Length);

    using var stdout = Console.OpenStandardOutput();
    stdout.Write(lengthBytes, 0, lengthBytes.Length);
    stdout.Write(bytes, 0, bytes.Length);
}

static void TryStartDesktopApp()
{
    var baseDirectory = AppContext.BaseDirectory;
    var candidates = new[]
    {
        Path.Combine(baseDirectory, "SDM.App.exe"),
        Path.Combine(baseDirectory, "SiS.SDM.App.exe"),
        Path.Combine(baseDirectory, "Silent.DownloadManager.App.exe"),
        Path.Combine(baseDirectory, "..", "Silent.DownloadManager.App", "Silent.DownloadManager.App.exe")
    };

    var appPath = candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);

    if (appPath is null)
    {
        return;
    }

    Process.Start(new ProcessStartInfo
    {
        FileName = appPath,
        UseShellExecute = true
    });
}

internal sealed record NativeRequest(string Url, string? Referrer);

internal sealed record IncomingRequest(string Url, string? Referrer);

internal sealed record NativeResponse(bool Ok, string Message);

