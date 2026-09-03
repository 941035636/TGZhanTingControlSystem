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
    public bool RequireLedReadyBeforeStart { get; init; } = true;
    public bool AllowDegradedPlayback { get; init; } = true;
}

public sealed class TtsOptions
{
    public const string SectionName = "Tts";
    public string Provider { get; init; } = "NotConfigured";
    public string Voice { get; init; } = "default";
}

public sealed class TtsProductionOptions
{
    public const string SectionName = "TtsProduction";
    public bool EnableDeterministicTestProvider { get; init; }
    public int MaxTextLength { get; init; } = 5000;
    public int MaxAttempts { get; init; } = 3;
    public int AttemptTimeoutMilliseconds { get; init; } = 30000;
    public int RetryDelayMilliseconds { get; init; } = 250;
    public long MinAudioSizeBytes { get; init; } = 45;
    public long MaxAudioSizeBytes { get; init; } = 100 * 1024 * 1024;
}

public sealed class MeloTtsLocalOptions
{
    public const string SectionName = "MeloTtsLocal";
    public bool Enabled { get; init; } = true;
    public bool AutoStartWorker { get; init; } = true;
    public string BaseAddress { get; init; } = "http://127.0.0.1:5091";
    public string PythonExecutablePath { get; init; } = "TtsWorker/MeloTtsLocal/runtime/python.exe";
    public string WorkerScriptPath { get; init; } = "TtsWorker/MeloTtsLocal/worker.py";
    public string MeloTtsSourcePath { get; init; } = "TtsWorker/MeloTtsLocal/vendor/MeloTTS";
    public string AcousticModelPath { get; init; } = "TtsWorker/MeloTtsLocal/models/MeloTTS-Chinese";
    public string BertModelPath { get; init; } = "TtsWorker/MeloTtsLocal/models/bert-base-multilingual-uncased";
    public string NltkDataPath { get; init; } = "TtsWorker/MeloTtsLocal/runtime/nltk_data";
    public int HealthTimeoutMilliseconds { get; init; } = 2500;
    public int RestartDelayMilliseconds { get; init; } = 5000;
}

public sealed class AdminOptions
{
    public const string SectionName = "Admin";
    public string Username { get; init; } = "admin";
    public string Password { get; init; } = "";
    public int SessionHours { get; init; } = 12;
}

public sealed class TerminalOptions
{
    public const string SectionName = "Terminal";
    public string ApiKey { get; init; } = "";
}
