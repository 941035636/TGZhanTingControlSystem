namespace TG.Control.Server;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";
    public string DataDirectory { get; init; } = "Data";
}

public sealed class PlaybackOptions
{
    public const string SectionName = "Playback";
    public string TouchClientId { get; init; } = "touch-main";
    public string LedClientId { get; init; } = "led-main";
    public int PrepareLeadMilliseconds { get; init; } = 1500;
    public int SyncToleranceMilliseconds { get; init; } = 500;
    public int LongPollSeconds { get; init; } = 20;
}

public sealed class TtsOptions
{
    public const string SectionName = "Tts";
    public string Provider { get; init; } = "NotConfigured";
    public string Voice { get; init; } = "default";
}

public sealed class AdminOptions
{
    public const string SectionName = "Admin";
    public string Username { get; init; } = "admin";
    public string Password { get; init; } = "TG@2026";
    public int SessionHours { get; init; } = 12;
}
