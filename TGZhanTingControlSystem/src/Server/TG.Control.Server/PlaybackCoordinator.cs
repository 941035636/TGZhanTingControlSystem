using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using TG.Control.Contracts;

namespace TG.Control.Server;

public sealed class PlaybackCoordinator(
    IContentRepository contentRepository,
    ICommandBroker broker,
    IOptions<PlaybackOptions> options,
    ILogger<PlaybackCoordinator> logger)
{
    private readonly PlaybackOptions settings = options.Value;
    private readonly ConcurrentDictionary<string, SessionState> sessions = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<PlaybackSessionStatus>> GetSessionsAsync(CancellationToken cancellationToken)
    {
        var result = new List<PlaybackSessionStatus>();
        foreach (var session in sessions.Values)
        {
            await session.Gate.WaitAsync(cancellationToken);
            try { result.Add(CreateStatus(session)); }
            finally { session.Gate.Release(); }
        }
        return result;
    }

    public async Task<PlaybackSessionStatus?> GetSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        if (!sessions.TryGetValue(sessionId, out var session)) return null;
        await session.Gate.WaitAsync(cancellationToken);
        try { return CreateStatus(session); }
        finally { session.Gate.Release(); }
    }

    public async Task<StartNarrationResponse> StartAsync(StartNarrationRequest request, CancellationToken cancellationToken)
    {
        if (request.ModuleIds.Count == 0)
        {
            throw new ArgumentException("At least one module must be selected.", nameof(request));
        }

        var content = await contentRepository.GetAsync(cancellationToken);
        var lookup = content.Modules.ToDictionary(module => module.Id, StringComparer.OrdinalIgnoreCase);
        var modules = request.ModuleIds.Select(id => lookup.TryGetValue(id, out var module)
            ? module
            : throw new KeyNotFoundException($"Module '{id}' was not found.")).ToArray();
        var nodes = modules.SelectMany(module => module.Nodes.OrderBy(node => node.Order).Select(node => (module, node))).ToArray();
        if (nodes.Length == 0)
        {
            throw new InvalidOperationException("The selected modules do not contain any narration nodes.");
        }

        var sessionId = Guid.NewGuid().ToString("N");
        var startAt = DateTimeOffset.UtcNow;
        var session = new SessionState(sessionId, content.Version, nodes);
        sessions[sessionId] = session;
        await PublishCurrentNodeAsync(session, cancellationToken);
        logger.LogInformation("Narration session {SessionId} started by {User} with {NodeCount} nodes", sessionId, request.RequestedBy, nodes.Length);
        return new StartNarrationResponse(sessionId, startAt, nodes.Length);
    }

    public async Task ReportAsync(PlaybackStatusReport report, CancellationToken cancellationToken)
    {
        if (!sessions.TryGetValue(report.SessionId, out var session)) return;
        await session.Gate.WaitAsync(cancellationToken);
        try
        {
            if (!string.Equals(session.Current.node.Id, report.NodeId, StringComparison.OrdinalIgnoreCase)) return;
            if (!session.ExpectedClients.Contains(report.ClientId)) return;
            if (report.State == PlaybackState.Ready)
            {
                session.ReadyClients.Add(report.ClientId);
                if (!session.PlayPublished && session.ExpectedClients.IsSubsetOf(session.ReadyClients))
                {
                    await PublishPlayAsync(session, DateTimeOffset.UtcNow.AddMilliseconds(settings.PrepareLeadMilliseconds), cancellationToken);
                }
                return;
            }
            if (report.State == PlaybackState.Playing)
            {
                session.PlayingAtUtc[report.ClientId] = report.ReportedAtUtc;
                if (session.ExpectedClients.IsSubsetOf(session.PlayingAtUtc.Keys))
                {
                    var timestamps = session.PlayingAtUtc.Values.ToArray();
                    var driftMilliseconds = (timestamps.Max() - timestamps.Min()).TotalMilliseconds;
                    if (driftMilliseconds > settings.SyncToleranceMilliseconds)
                        logger.LogWarning("Narration session {SessionId} node {NodeId} start drift {DriftMilliseconds:F0}ms exceeds tolerance {ToleranceMilliseconds}ms", session.SessionId, report.NodeId, driftMilliseconds, settings.SyncToleranceMilliseconds);
                    else
                        logger.LogInformation("Narration session {SessionId} node {NodeId} start drift {DriftMilliseconds:F0}ms", session.SessionId, report.NodeId, driftMilliseconds);
                }
                return;
            }

            if (report.State is PlaybackState.Completed or PlaybackState.Skipped)
            {
                session.CompletedClients.Add(report.ClientId);
            }
            else if (report.State == PlaybackState.Failed)
            {
                if (session.Current.node.FailurePolicy == FailurePolicy.Stop)
                {
                    sessions.TryRemove(session.SessionId, out _);
                    logger.LogError("Narration session {SessionId} stopped at node {NodeId}: {Error}", report.SessionId, report.NodeId, report.Error);
                    return;
                }
                await StopCurrentNodeAsync(session, cancellationToken);
                await AdvanceAsync(session, cancellationToken);
                return;
            }

            if (!session.ExpectedClients.IsSubsetOf(session.CompletedClients)) return;
            await AdvanceAsync(session, cancellationToken);
        }
        finally
        {
            session.Gate.Release();
        }
    }

    public async Task<ControlNarrationResponse> ControlAsync(ControlNarrationRequest request, CancellationToken cancellationToken)
    {
        if (!sessions.TryGetValue(request.SessionId, out var session))
        {
            return new ControlNarrationResponse(request.SessionId, request.Action, false, "讲解任务不存在或已经结束。");
        }

        await session.Gate.WaitAsync(cancellationToken);
        try
        {
            switch (request.Action)
            {
                case PlaybackAction.Pause:
                    if (session.Paused) return Accepted(request, "讲解已经暂停。");
                    session.Paused = true;
                    await PublishControlAsync(session, PlaybackAction.Pause, cancellationToken);
                    return Accepted(request, "讲解已暂停。");
                case PlaybackAction.Resume:
                    if (!session.Paused) return Accepted(request, "讲解正在播放。");
                    session.Paused = false;
                    await PublishControlAsync(session, PlaybackAction.Resume, cancellationToken);
                    return Accepted(request, "讲解已继续。");
                case PlaybackAction.Skip:
                    session.Paused = false;
                    await PublishControlAsync(session, PlaybackAction.Skip, cancellationToken);
                    return Accepted(request, "已跳过当前讲解节点。");
                case PlaybackAction.Stop:
                    await PublishControlAsync(session, PlaybackAction.Stop, cancellationToken);
                    sessions.TryRemove(session.SessionId, out _);
                    return Accepted(request, "讲解任务已终止。");
                default:
                    return new ControlNarrationResponse(request.SessionId, request.Action, false, "不支持该控制操作。");
            }
        }
        finally
        {
            session.Gate.Release();
        }
    }

    private async Task PublishCurrentNodeAsync(SessionState session, CancellationToken cancellationToken)
    {
        session.ExpectedClients.Clear();
        session.CompletedClients.Clear();
        session.ReadyClients.Clear();
        session.PlayingAtUtc.Clear();
        session.PlayPublished = false;
        var (module, node) = session.Current;
        var video = node.Assets.FirstOrDefault(asset => asset.Kind is AssetKind.Video or AssetKind.Animation);
        if (video is not null)
        {
            session.ExpectedClients.Add(settings.LedClientId);
            await broker.PublishAsync(settings.LedClientId, NewCommand(session.SessionId, module.Id, node.Id, PlaybackAction.Prepare, video.Url, DateTimeOffset.UtcNow, session.ContentVersion), cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(node.TtsAudioUrl))
        {
            session.ExpectedClients.Add(settings.TouchClientId);
            await broker.PublishAsync(settings.TouchClientId, NewCommand(session.SessionId, module.Id, node.Id, PlaybackAction.Prepare, node.TtsAudioUrl, DateTimeOffset.UtcNow, session.ContentVersion), cancellationToken);
        }

        if (session.ExpectedClients.Count == 0)
        {
            await AdvanceAsync(session, cancellationToken);
        }
    }

    private async Task PublishPlayAsync(SessionState session, DateTimeOffset executeAt, CancellationToken cancellationToken)
    {
        session.PlayPublished = true;
        session.CompletedClients.Clear();
        var (module, node) = session.Current;
        var video = node.Assets.FirstOrDefault(asset => asset.Kind is AssetKind.Video or AssetKind.Animation);
        if (video is not null)
        {
            await broker.PublishAsync(settings.LedClientId, NewCommand(session.SessionId, module.Id, node.Id, PlaybackAction.PlayVideo, video.Url, executeAt, session.ContentVersion), cancellationToken);
        }
        if (!string.IsNullOrWhiteSpace(node.TtsAudioUrl))
        {
            await broker.PublishAsync(settings.TouchClientId, NewCommand(session.SessionId, module.Id, node.Id, PlaybackAction.PlayNarration, node.TtsAudioUrl, executeAt, session.ContentVersion), cancellationToken);
        }
        logger.LogInformation("Narration session {SessionId} node {NodeId} ready; scheduled for {ExecuteAtUtc}", session.SessionId, node.Id, executeAt);
    }

    private async Task StopCurrentNodeAsync(SessionState session, CancellationToken cancellationToken)
    {
        await PublishControlAsync(session, PlaybackAction.Stop, cancellationToken);
    }

    private async Task AdvanceAsync(SessionState session, CancellationToken cancellationToken)
    {
        session.Index++;
        if (session.Index >= session.Nodes.Length)
        {
            sessions.TryRemove(session.SessionId, out _);
            logger.LogInformation("Narration session {SessionId} completed", session.SessionId);
            return;
        }
        await PublishCurrentNodeAsync(session, cancellationToken);
    }

    private PlaybackCommand NewCommand(string sessionId, string moduleId, string nodeId, PlaybackAction action, string? mediaUrl, DateTimeOffset executeAt, long version) =>
        new(broker.NextSequence(), Guid.NewGuid().ToString("N"), sessionId, moduleId, nodeId, action, mediaUrl, executeAt, 0, version);

    private async Task PublishControlAsync(SessionState session, PlaybackAction action, CancellationToken cancellationToken)
    {
        var (module, node) = session.Current;
        foreach (var clientId in session.ExpectedClients)
        {
            await broker.PublishAsync(clientId,
                NewCommand(session.SessionId, module.Id, node.Id, action, null, DateTimeOffset.UtcNow, session.ContentVersion),
                cancellationToken);
        }
    }

    private static ControlNarrationResponse Accepted(ControlNarrationRequest request, string message) =>
        new(request.SessionId, request.Action, true, message);

    private static PlaybackSessionStatus CreateStatus(SessionState session)
    {
        var (module, node) = session.Current;
        return new PlaybackSessionStatus(session.SessionId, session.ContentVersion, module.Id, module.Name,
            node.Id, node.Name, session.Index + 1, session.Nodes.Length, session.Paused, session.PlayPublished,
            session.ExpectedClients.OrderBy(value => value).ToArray(),
            session.ReadyClients.OrderBy(value => value).ToArray(),
            session.CompletedClients.OrderBy(value => value).ToArray());
    }

    private sealed class SessionState(string sessionId, long contentVersion, (ExhibitionModule module, NarrationNode node)[] nodes)
    {
        public string SessionId { get; } = sessionId;
        public long ContentVersion { get; } = contentVersion;
        public (ExhibitionModule module, NarrationNode node)[] Nodes { get; } = nodes;
        public int Index { get; set; }
        public bool Paused { get; set; }
        public (ExhibitionModule module, NarrationNode node) Current => Nodes[Index];
        public HashSet<string> ExpectedClients { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ReadyClients { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> CompletedClients { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, DateTimeOffset> PlayingAtUtc { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool PlayPublished { get; set; }
        public SemaphoreSlim Gate { get; } = new(1, 1);
    }
}
