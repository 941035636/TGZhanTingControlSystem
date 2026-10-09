namespace TG.Control.Contracts;

/// <summary>Independent attraction/welcome flow; it never becomes a formal narration session.</summary>
public enum WelcomePlaybackState
{
    Standby,
    Requested,
    Preparing,
    Playing,
    Completed,
    Failed
}

public enum WelcomePlaybackAction
{
    Prepare,
    Play,
    Stop
}

public sealed record WelcomeCaptionCue(double StartSeconds, double EndSeconds, string Text);

/// <summary>Optional settings are init-only so previously published UI JSON remains readable.</summary>
public sealed record WelcomeExperienceSettings(
    bool AudioEnabled = true,
    IReadOnlyList<WelcomeCaptionCue>? Captions = null);

public sealed record WelcomeAudioAsset(
    string AssetId,
    string Url,
    string Sha256,
    long SizeBytes,
    string MediaType,
    double DurationSeconds = 0);

public sealed record WelcomePlaybackCommand(
    long Sequence,
    string CommandId,
    string RequestId,
    WelcomePlaybackAction Action,
    WelcomeAudioAsset? Audio,
    DateTimeOffset ExecuteAtUtc);

public sealed record WelcomePlaybackStatusReport(
    string ClientId,
    string CommandId,
    string RequestId,
    WelcomePlaybackState State,
    double PositionSeconds,
    double Progress,
    string? Error,
    DateTimeOffset ReportedAtUtc);

public sealed record WelcomePlaybackStatus(
    string? RequestId,
    WelcomePlaybackState State,
    double PositionSeconds,
    double Progress,
    string? Message,
    IReadOnlyList<WelcomeCaptionCue> Captions,
    DateTimeOffset UpdatedAtUtc);

public sealed record WelcomeRequestResponse(WelcomePlaybackStatus Status, bool Accepted);
