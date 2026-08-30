namespace TG.Control.Contracts;

public enum ClientKind
{
    Touch,
    LedPlayer
}

public enum PlaybackAction
{
    Prepare,
    PlayVideo,
    PlayNarration,
    Pause,
    Resume,
    Stop,
    Seek,
    Skip,
    Retry
}

public enum PlaybackState
{
    Received,
    Ready,
    Playing,
    Paused,
    Completed,
    Failed,
    Skipped
}

public sealed record ClientRegistration(
    string ClientId,
    ClientKind Kind,
    string AppVersion,
    long ContentVersion = 0,
    bool Ready = true,
    string? Status = null,
    string? InstanceId = null);

public sealed record StartNarrationRequest(IReadOnlyList<string> ModuleIds, string RequestedBy);

public sealed record StartNarrationResponse(string SessionId, DateTimeOffset StartAtUtc, int NodeCount);

public sealed record ControlNarrationRequest(string SessionId, PlaybackAction Action);

public sealed record ControlNarrationResponse(string SessionId, PlaybackAction Action, bool Accepted, string Message);

public sealed record PlaybackCommand(
    long Sequence,
    string CommandId,
    string SessionId,
    string ModuleId,
    string NodeId,
    PlaybackAction Action,
    string? MediaUrl,
    DateTimeOffset ExecuteAtUtc,
    double PositionSeconds,
    long ContentVersion,
    string? NarrationAudioUrl = null,
    AudioMixPolicy AudioMixPolicy = AudioMixPolicy.Duck,
    double VideoVolume = 0.25,
    double NarrationVolume = 1.0);

public sealed record PlaybackStatusReport(
    string ClientId,
    string CommandId,
    string SessionId,
    string NodeId,
    PlaybackState State,
    double PositionSeconds,
    string? Error,
    DateTimeOffset ReportedAtUtc,
    double Progress = 0);

public sealed record ClientRuntimeStatus(
    string ClientId,
    ClientKind Kind,
    string AppVersion,
    DateTimeOffset RegisteredAtUtc,
    DateTimeOffset LastSeenUtc,
    bool Online,
    long ContentVersion = 0,
    bool Ready = true,
    string? Status = null);

public sealed record PlaybackSessionStatus(
    string SessionId,
    long ContentVersion,
    string ModuleId,
    string ModuleName,
    string NodeId,
    string NodeName,
    int CurrentNodeNumber,
    int TotalNodes,
    bool Paused,
    bool PlayPublished,
    IReadOnlyList<string> ExpectedClients,
    IReadOnlyList<string> ReadyClients,
    IReadOnlyList<string> CompletedClients,
    double PreparationProgress = 0);

public sealed record TtsSynthesisRequest(string Text, string Voice, double Rate, double Volume, double Pitch);

public sealed record TtsSynthesisResult(string AudioUrl, double DurationSeconds, string ProviderRequestId);

public sealed record ContentSyncAsset(string Url, string Sha256, long SizeBytes);

public sealed record ContentSyncManifest(long Version, IReadOnlyList<ContentSyncAsset> Assets);

public sealed record NarrationRoute(
    string Id,
    string Name,
    IReadOnlyList<string> ModuleIds,
    DateTimeOffset UpdatedAtUtc);

public sealed record SaveNarrationRouteRequest(string? Id, string Name, IReadOnlyList<string> ModuleIds);

public sealed record SystemReadiness(
    bool CanStart,
    long ContentVersion,
    bool LedOnline,
    bool LedReady,
    long LedContentVersion,
    string Message,
    DateTimeOffset CheckedAtUtc);

public sealed record ContentVersionSummary(
    long Version,
    DateTimeOffset PublishedAtUtc,
    string PublishedBy,
    int ModuleCount,
    int NodeCount,
    bool Current);

public sealed record OperationalEvent(
    string Id,
    DateTimeOffset OccurredAtUtc,
    string Level,
    string Category,
    string Action,
    string Message,
    string? SessionId = null,
    string? Detail = null,
    string? ClientId = null,
    string? NodeId = null);
