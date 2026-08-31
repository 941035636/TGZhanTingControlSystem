namespace TG.Control.Contracts;

public enum TtsProductionJobStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled
}

public enum TtsProductionErrorCategory
{
    TransientProvider,
    PermanentInput,
    InvalidMedia,
    Cancelled,
    Interrupted,
    Internal
}

public sealed record TtsVoiceDescriptor(
    string VoiceId,
    string DisplayName,
    string Language);

public sealed record TtsProviderCapabilities(
    int MaxTextLength,
    double MinRate,
    double MaxRate,
    double MinPitch,
    double MaxPitch,
    IReadOnlyList<string> SupportedMediaTypes);

public sealed record TtsProviderDescriptor(
    string ProviderId,
    string DisplayName,
    bool Available,
    bool DevelopmentOnly,
    string? UnavailableReason,
    IReadOnlyList<TtsVoiceDescriptor> Voices,
    TtsProviderCapabilities Capabilities);

public sealed record CreateTtsProductionJobRequest(
    string ModuleId,
    string NodeId,
    string NarrationText,
    TtsSynthesisConfiguration SynthesisConfiguration,
    bool RetryFailed = false);

public sealed record CreateTtsProductionJobResponse(
    TtsProductionJob Job,
    bool Created);

public sealed record TtsProductionJobAttempt(
    int AttemptNumber,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    bool Succeeded,
    TtsProductionErrorCategory? ErrorCategory = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record TtsProductionJob(
    string JobId,
    string ModuleId,
    string NodeId,
    string RequestedBy,
    string NarrationText,
    string NarrationTextFingerprint,
    TtsSynthesisConfiguration SynthesisConfiguration,
    string SynthesisConfigurationFingerprint,
    string IdempotencyKey,
    string ProviderId,
    string Voice,
    TtsProductionJobStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int RetryCount,
    IReadOnlyList<TtsProductionJobAttempt> Attempts,
    TtsProductionErrorCategory? ErrorCategory = null,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    string? CandidateId = null);

public sealed record NarrationAudioCandidateValidation(
    bool Valid,
    string Validator,
    string MediaType,
    double DurationSeconds,
    DateTimeOffset ValidatedAtUtc);

public sealed record NarrationAudioCandidate(
    string CandidateId,
    string JobId,
    ContentAsset Asset,
    string NarrationTextFingerprint,
    string SynthesisConfigurationFingerprint,
    TtsSynthesisConfiguration SynthesisConfiguration,
    string ProviderId,
    string Voice,
    DateTimeOffset CreatedAtUtc,
    NarrationAudioCandidateValidation Validation,
    string? ProviderRequestId = null);
