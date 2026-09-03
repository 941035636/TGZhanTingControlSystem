using System.Text.Json;

namespace TG.Control.Launcher;

internal sealed class LauncherConfiguration
{
    public string ServerHealthUrl { get; init; } = "http://127.0.0.1:5080/api/health";
    public string AdminUrl { get; init; } = "http://127.0.0.1:5080/";
    public string TouchClientExecutable { get; init; } = @"C:\Program Files\TG Exhibition\TouchClient\TouchClient.exe";
    public string LedPlayerExecutable { get; init; } = @"C:\Program Files\TG Exhibition\LedPlayer\LedPlayer.exe";
    public string TouchClientConfiguration { get; init; } = @"C:\ProgramData\TG Exhibition\Config\touch-client.json";
    public string LedPlayerConfiguration { get; init; } = @"C:\ProgramData\TG Exhibition\Config\led-player.json";
    public string TouchClientLogFile { get; init; } = @"C:\ProgramData\TG Exhibition\Logs\TouchClient\Player.log";
    public string LedPlayerLogFile { get; init; } = @"C:\ProgramData\TG Exhibition\Logs\LedPlayer\Player.log";
    public string LogDirectory { get; init; } = @"C:\ProgramData\TG Exhibition\Logs\Launcher";
    public int HealthPollSeconds { get; init; } = 3;
    public int ClientRestartDelaySeconds { get; init; } = 5;
    public bool AutoStartTouchClient { get; init; } = true;
    public bool AutoStartLedPlayer { get; init; } = true;
    public bool AutoRestartClients { get; init; } = true;

    public static LauncherConfiguration Load()
    {
        var path = ResolveConfigurationPath();
        if (!File.Exists(path)) return new LauncherConfiguration();
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<LauncherConfiguration>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        }) ?? new LauncherConfiguration();
    }

    private static string ResolveConfigurationPath()
    {
        var explicitPath = Environment.GetEnvironmentVariable("TG_LAUNCHER_CONFIG");
        if (!string.IsNullOrWhiteSpace(explicitPath)) return Path.GetFullPath(explicitPath);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var sitePath = Path.Combine(programData, "TG Exhibition", "Config", "launcher.json");
        if (File.Exists(sitePath)) return sitePath;
        return Path.Combine(AppContext.BaseDirectory, "launcher.json");
    }
}

internal sealed class LauncherLog : IDisposable
{
    private readonly object gate = new();
    private readonly StreamWriter writer;

    public LauncherLog(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "launcher-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
        writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true
        };
    }

    public event Action<string>? Written;

    public void Write(string message)
    {
        var line = $"{DateTimeOffset.Now:O} {message}";
        lock (gate) writer.WriteLine(line);
        Written?.Invoke(line);
    }

    public void Dispose() => writer.Dispose();
}
