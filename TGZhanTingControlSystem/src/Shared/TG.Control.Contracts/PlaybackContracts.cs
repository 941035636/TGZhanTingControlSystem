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
    Skip
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

public sealed record ClientRegistration(string ClientId, ClientKind Kind, string AppVersion);

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
    long ContentVersion);

public sealed record PlaybackStatusReport(
    string ClientId,
    string CommandId,
    string SessionId,
    string NodeId,
    PlaybackState State,
    double PositionSeconds,
    string? Error,
    DateTimeOffset ReportedAtUtc);

public sealed record ClientRuntimeStatus(
    string ClientId,
    ClientKind Kind,
    string AppVersion,
    DateTimeOffset RegisteredAtUtc,
    DateTimeOffset LastSeenUtc,
    bool Online);

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
    IReadOnlyList<string> CompletedClients);

public sealed record TtsSynthesisRequest(string Text, string Voice, double Rate, double Volume, double Pitch);

public sealed record TtsSynthesisResult(string AudioUrl, double DurationSeconds, string ProviderRequestId);
