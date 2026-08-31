namespace TG.Control.Contracts;

public enum AssetKind
{
    Video,
    Image,
    Animation,
    NarrationAudio
}

public enum FailurePolicy
{
    Skip,
    Stop
}

public enum AudioMixPolicy
{
    Duck,
    KeepOriginal,
    MuteVideo
}

public enum NarrationAudioOrigin
{
    Generated,
    ManualUpload,
    Legacy
}

public enum NarrationAudioBindingStatus
{
    Missing,
    Fresh,
    StaleText,
    StaleSynthesisConfiguration,
    InvalidAsset,
    InvalidBinding,
    LegacyUnverified
}

public sealed record ContentAsset(
    string Id,
    string Name,
    AssetKind Kind,
    string Url,
    string Sha256,
    long SizeBytes,
    double DurationSeconds,
    string? MediaType = null);

public sealed record TtsSynthesisConfiguration(
    string ProviderKey,
    string Voice,
    string Language,
    double Rate = 1,
    double Pitch = 0,
    double Volume = 1,
    string OutputMediaType = "audio/mpeg",
    int SampleRateHz = 24000,
    int Channels = 1);

public sealed record NarrationAudioBinding(
    ContentAsset Asset,
    string NarrationTextFingerprint,
    string SynthesisConfigurationFingerprint,
    TtsSynthesisConfiguration SynthesisConfiguration,
    NarrationAudioOrigin Origin,
    DateTimeOffset BoundAtUtc,
    string FingerprintVersion,
    string? ProviderRequestId = null);

public sealed record NarrationNode(
    string Id,
    string Name,
    int Order,
    string NarrationText,
    string? TtsAudioUrl,
    IReadOnlyList<ContentAsset> Assets,
    FailurePolicy FailurePolicy = FailurePolicy.Skip,
    AudioMixPolicy AudioMixPolicy = AudioMixPolicy.Duck,
    double VideoVolume = 0.25,
    double NarrationVolume = 1.0,
    TtsSynthesisConfiguration? TtsConfiguration = null,
    NarrationAudioBinding? NarrationAudio = null);

public sealed record ExhibitionModule(
    string Id,
    string Name,
    int Order,
    string Description,
    string? CoverUrl,
    bool Enabled,
    IReadOnlyList<NarrationNode> Nodes);

public sealed record PublishedContent(
    long Version,
    DateTimeOffset PublishedAtUtc,
    string PublishedBy,
    IReadOnlyList<ExhibitionModule> Modules);

public sealed record UiExperienceConfig(
    long Version,
    string TouchTitle,
    string TouchSubtitle,
    string? TouchBackgroundUrl,
    string TouchBackgroundColor,
    string TouchAccentColor,
    string LedTitle,
    string LedSubtitle,
    string? LedIdleMediaUrl,
    string LedIdleMediaKind,
    string LedBackgroundColor,
    bool LedShowBranding,
    bool LedShowStatus,
    DateTimeOffset UpdatedAtUtc,
    string UpdatedBy);
