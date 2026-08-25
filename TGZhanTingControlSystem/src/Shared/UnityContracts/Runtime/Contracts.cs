using System;

namespace TG.Control.UnityContracts
{
    public enum ClientKind { Touch, LedPlayer }
    public enum PlaybackAction { Prepare, PlayVideo, PlayNarration, Pause, Resume, Stop, Seek, Skip }
    public enum PlaybackState { Received, Ready, Playing, Paused, Completed, Failed, Skipped }

    [Serializable]
    public sealed class ClientRegistration
    {
        public string clientId;
        public ClientKind kind;
        public string appVersion;
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
    }

    [Serializable]
    public sealed class PlaybackSessionLookup
    {
        public bool active;
        public PlaybackSessionStatus session;
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
}
