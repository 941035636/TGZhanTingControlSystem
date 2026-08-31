namespace TG.Control.Contracts;

public sealed record NarrationAudioDraftStatus(
    string ModuleId,
    string NodeId,
    NarrationAudioBindingStatus Status,
    string Message);

public sealed record ContentDraftSnapshot(
    long BaseContentVersion,
    long Revision,
    DateTimeOffset UpdatedAtUtc,
    string UpdatedBy,
    IReadOnlyList<ExhibitionModule> Modules,
    IReadOnlyList<NarrationAudioDraftStatus> NarrationAudioStatuses);

public sealed record SaveContentDraftRequest(
    long BaseContentVersion,
    long ExpectedRevision,
    IReadOnlyList<ExhibitionModule> Modules);

public sealed record AdoptNarrationAudioCandidateRequest(
    long BaseContentVersion,
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
