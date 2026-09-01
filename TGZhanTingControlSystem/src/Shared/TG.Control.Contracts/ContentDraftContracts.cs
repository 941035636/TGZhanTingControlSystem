namespace TG.Control.Contracts;

public sealed record NarrationAudioDraftStatus(
    string ModuleId,
    string NodeId,
    NarrationAudioBindingStatus Status,
    string Message);

public enum ContentPublishIssueSeverity
{
    Warning,
    Error
}

public sealed record ContentPublishIssue(
    string ModuleId,
    string NodeId,
    string ModuleName,
    string NodeName,
    string Code,
    ContentPublishIssueSeverity Severity,
    string Message,
    NarrationAudioBindingStatus? NarrationAudioStatus = null);

public sealed record NarrationAudioPublishSummary(
    int Fresh,
    int Missing,
    int StaleText,
    int StaleSynthesisConfiguration,
    int LegacyUnverified,
    int InvalidAsset,
    int InvalidBinding,
    int BlockingIssues,
    int Warnings);

public sealed record ContentPublishReadiness(
    bool CanPublish,
    NarrationAudioPublishSummary NarrationAudio,
    IReadOnlyList<ContentPublishIssue> Issues);

public sealed record ContentDraftSnapshot(
    long BaseContentVersion,
    long Revision,
    DateTimeOffset UpdatedAtUtc,
    string UpdatedBy,
    IReadOnlyList<ExhibitionModule> Modules,
    IReadOnlyList<NarrationAudioDraftStatus> NarrationAudioStatuses,
    ContentPublishReadiness? PublishReadiness = null);

public sealed record SaveContentDraftRequest(
    long BaseContentVersion,
    long ExpectedRevision,
    IReadOnlyList<ExhibitionModule> Modules);

public sealed record AdoptNarrationAudioCandidateRequest(
    long BaseContentVersion,
    long ExpectedDraftRevision);

public sealed record RollbackContentRequest(
    long ExpectedContentVersion,
    long ExpectedDraftRevision);

public sealed record AdoptNarrationAudioCandidateResponse(
    ContentDraftSnapshot Draft,
    NarrationAudioBinding Binding);

public sealed record NarrationAudioCandidateEvaluation(
    string CandidateId,
    long BaseContentVersion,
    long DraftRevision,
    bool CandidateExists,
    bool LocationMatches,
    bool NarrationTextMatches,
    bool SynthesisConfigurationMatches,
    bool AssetValid,
    bool Adoptable,
    string Message);
