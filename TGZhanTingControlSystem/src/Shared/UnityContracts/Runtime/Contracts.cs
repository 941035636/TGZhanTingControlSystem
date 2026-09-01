using System;

namespace TG.Control.UnityContracts
{
    public enum ClientKind { Touch, LedPlayer }
    public enum PlaybackAction { Prepare, PlayVideo, PlayNarration, Pause, Resume, Stop, Seek, Skip, Retry }
    public enum PlaybackState { Received, Ready, Playing, Paused, Completed, Failed, Skipped }
    public enum AudioMixPolicy { Duck, KeepOriginal, MuteVideo }

    [Serializable]
    public sealed class ClientRegistration
    {
        public string clientId;
        public ClientKind kind;
        public string appVersion;
        public long contentVersion;
        public bool ready = true;
        public string status;
        public string instanceId;
    }

    [Serializable]
    public sealed class PlaybackCommand
    {
        public long sequence;
        public string commandId;
        public string sessionId;
        public string moduleId;
        public string nodeId;
        public PlaybackAction action;
        public string mediaUrl;
        public string executeAtUtc;
        public double positionSeconds;
        public long contentVersion;
        public string narrationAudioUrl;
        public AudioMixPolicy audioMixPolicy;
        public double videoVolume;
        public double narrationVolume;
    }

    [Serializable]
    public sealed class PlaybackStatusReport
    {
        public string clientId;
        public string commandId;
        public string sessionId;
        public string nodeId;
        public PlaybackState state;
        public double positionSeconds;
        public string error;
        public string reportedAtUtc;
        public double progress;
    }

    [Serializable]
    public sealed class StartNarrationRequest
    {
        public string[] moduleIds;
        public string requestedBy;
    }

    [Serializable]
    public sealed class StartNarrationResponse
    {
        public string sessionId;
        public string startAtUtc;
        public int nodeCount;
    }

    [Serializable]
    public sealed class ControlNarrationRequest
    {
        public string sessionId;
        public PlaybackAction action;
    }

    [Serializable]
    public sealed class ControlNarrationResponse
    {
        public string sessionId;
        public PlaybackAction action;
        public bool accepted;
        public string message;
    }

    [Serializable]
    public sealed class PlaybackSessionStatus
    {
        public string sessionId;
        public long contentVersion;
        public string moduleId;
        public string moduleName;
        public string nodeId;
        public string nodeName;
        public int currentNodeNumber;
        public int totalNodes;
        public bool paused;
        public bool playPublished;
        public string[] expectedClients;
        public string[] readyClients;
        public string[] completedClients;
        public double preparationProgress;
    }

    [Serializable]
    public sealed class PlaybackSessionLookup
    {
        public bool active;
        public PlaybackSessionStatus session;
    }

    [Serializable]
    public sealed class SystemReadiness
    {
        public bool canStart;
        public long contentVersion;
        public bool ledOnline;
        public bool ledReady;
        public long ledContentVersion;
        public string message;
        public string checkedAtUtc;
    }

    [Serializable]
    public sealed class ExhibitionModule
    {
        public string id;
        public string name;
        public int order;
        public string description;
        public string coverUrl;
        public bool enabled;
        public NarrationNode[] nodes;
    }

    [Serializable]
    public sealed class NarrationNode
    {
        public string id;
        public string name;
        public int order;
        public string narrationText;
        public string ttsAudioUrl;
        public ContentAsset[] assets;
        public int failurePolicy;
        public AudioMixPolicy audioMixPolicy;
        public double videoVolume;
        public double narrationVolume;
    }

    [Serializable]
    public sealed class ContentAsset
    {
        public string id;
        public string name;
        public int kind;
        public string url;
        public string sha256;
        public long sizeBytes;
        public double durationSeconds;
    }

    [Serializable]
    public sealed class PublishedContent
    {
        public long version;
        public string publishedAtUtc;
        public string publishedBy;
        public ExhibitionModule[] modules;
    }

    [Serializable]
    public sealed class ContentSyncAsset
    {
        public string url;
        public string sha256;
        public long sizeBytes;
        public string assetId;
        public string mediaType;
    }

    [Serializable]
    public sealed class ContentSyncManifest
    {
        public long version;
        public ContentSyncAsset[] assets;
    }

    [Serializable]
    public sealed class ContentSyncProgress
    {
        public long version;
        public int total;
        public int completed;
        public string currentUrl;
        public string error;
        public bool finished;
    }

    [Serializable]
    public sealed class NarrationRoute
    {
        public string id;
        public string name;
        public string[] moduleIds;
        public string updatedAtUtc;
    }

    [Serializable]
    public sealed class NarrationRouteCollection
    {
        public NarrationRoute[] routes;
    }

    [Serializable]
    public sealed class SaveNarrationRouteRequest
    {
        public string id;
        public string name;
        public string[] moduleIds;
    }

    [Serializable]
    public sealed class UiExperienceConfig
    {
        public long version;
        public string touchTitle;
        public string touchSubtitle;
        public string touchBackgroundUrl;
        public string touchBackgroundColor;
        public string touchAccentColor;
        public string ledTitle;
        public string ledSubtitle;
        public string ledIdleMediaUrl;
        public string ledIdleMediaKind;
        public string ledBackgroundColor;
        public bool ledShowBranding;
        public bool ledShowStatus;
        public string updatedAtUtc;
        public string updatedBy;
    }
}
