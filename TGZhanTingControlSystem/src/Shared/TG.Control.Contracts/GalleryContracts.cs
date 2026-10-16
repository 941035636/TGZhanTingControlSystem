namespace TG.Control.Contracts;

/// <summary>Operator intent for an active manual image-gallery session.</summary>
public enum GalleryControlAction
{
    Show = 0,
    Previous = 1,
    Next = 2,
    StartSlideshow = 3,
    PauseSlideshow = 4,
    Stop = 5
}

/// <summary>Commands delivered on the gallery-only LED channel.</summary>
public enum GalleryDisplayAction
{
    Prepare = 0,
    Show = 1,
    Stop = 2
}

public enum GalleryDisplayState
{
    Received = 0,
    Ready = 1,
    Showing = 2,
    Failed = 3,
    Stopped = 4
}

public enum GallerySessionState
{
    Preparing = 0,
    Showing = 1,
    Failed = 2
}

public enum GalleryFitMode
{
    Contain = 0
}

public sealed record StartGalleryRequest(
    string ModuleId,
    int ImageIndex = 0,
    string RequestedBy = "touch-terminal");

public sealed record StartGalleryResponse(
    string SessionId,
    DateTimeOffset StartedAtUtc,
    int ImageCount);

public sealed record ControlGalleryRequest(
    string SessionId,
    GalleryControlAction Action,
    int? ImageIndex = null,
    int? SlideshowIntervalSeconds = null);

public sealed record ControlGalleryResponse(
    string SessionId,
    GalleryControlAction Action,
    bool Accepted,
    string Message,
    GallerySessionStatus? Session = null);

public sealed record GalleryDisplayCommand(
    long Sequence,
    string CommandId,
    string SessionId,
    string ModuleId,
    string ImageId,
    GalleryDisplayAction Action,
    long ContentVersion,
    string AssetId,
    string Url,
    string Sha256,
    long SizeBytes,
    string MediaType,
    DateTimeOffset ExecuteAtUtc,
    GalleryFitMode FitMode = GalleryFitMode.Contain);

public sealed record GalleryDisplayStatusReport(
    string ClientId,
    string CommandId,
    string SessionId,
    string ImageId,
    GalleryDisplayState State,
    string? Error,
    DateTimeOffset ReportedAtUtc);

public sealed record GallerySessionStatus(
    string SessionId,
    long ContentVersion,
    string ModuleId,
    string ModuleName,
    string ImageId,
    string ImageName,
    int CurrentImageNumber,
    int ImageCount,
    GallerySessionState State,
    bool SlideshowActive,
    int SlideshowIntervalSeconds,
    string? Error,
    DateTimeOffset UpdatedAtUtc);

public sealed record GallerySessionLookup(bool Active, GallerySessionStatus? Session);
