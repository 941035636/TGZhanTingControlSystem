using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using TG.Control.Contracts;

namespace TG.Control.Server;

public sealed class PlaybackCoordinator(
    IContentRepository contentRepository,
    ICommandBroker broker,
    IOptions<PlaybackOptions> options,
    PlaybackSessionStore sessionStore,
    OperationalEventRepository eventLog,
    ILogger<PlaybackCoordinator> logger)
{
    private readonly PlaybackOptions settings = options.Value;
    private readonly ConcurrentDictionary<string, SessionState> sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim restoreGate = new(1, 1);
    private bool restored;

    public async Task<IReadOnlyList<PlaybackSessionStatus>> GetSessionsAsync(CancellationToken cancellationToken)
    {
        await EnsureRestoredAsync(cancellationToken);
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
        await EnsureRestoredAsync(cancellationToken);
        if (!sessions.TryGetValue(sessionId, out var session)) return null;
        await session.Gate.WaitAsync(cancellationToken);
        try { return CreateStatus(session); }
        finally { session.Gate.Release(); }
    }

    public async Task<StartNarrationResponse> StartAsync(StartNarrationRequest request, CancellationToken cancellationToken)
    {
        await EnsureRestoredAsync(cancellationToken);
        if (request.ModuleIds.Count == 0)
        {
            throw new ArgumentException("At least one module must be selected.", nameof(request));
        }

        if (!sessions.IsEmpty) throw new InvalidOperationException("当前已有正在执行的讲解，请先恢复或终止该任务。");
        var content = await contentRepository.GetAsync(cancellationToken);
        if (settings.RequireLedReadyBeforeStart)
        {
            var onlineThreshold = TimeSpan.FromSeconds(Math.Max(10, settings.LongPollSeconds * 2 + 5));
            var led = broker.GetClientStatuses(onlineThreshold)
                .FirstOrDefault(client => string.Equals(client.ClientId, settings.LedClientId, StringComparison.OrdinalIgnoreCase));
            if (led is null || !led.Online) throw new InvalidOperationException("LED播放端离线，暂时不能开始讲解。");
            if (led.ContentVersion != content.Version && !settings.AllowDegradedPlayback)
                throw new InvalidOperationException($"LED内容版本为 V{led.ContentVersion}，服务器为 V{content.Version}，请等待素材同步完成。");
            if (!led.Ready && !settings.AllowDegradedPlayback)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(led.Status) ? "LED播放端尚未准备完成。" : led.Status);
            if (led.ContentVersion != content.Version || !led.Ready)
            {
                await eventLog.AppendAsync("Warning", "Playback", "DegradedStart",
                    $"LED以受限模式启动讲解：服务器 V{content.Version}，LED V{led.ContentVersion}；缺失素材按需下载，失败节点按配置策略处理。",
                    cancellationToken: cancellationToken);
            }
        }
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
        await PersistAsync(session, cancellationToken);
        await eventLog.AppendAsync("Information", "Playback", "Start",
            $"讲解任务已启动，共 {nodes.Length} 个节点，发起人：{request.RequestedBy}。", sessionId, cancellationToken: cancellationToken);
        logger.LogInformation("Narration session {SessionId} started by {User} with {NodeCount} nodes", sessionId, request.RequestedBy, nodes.Length);
        return new StartNarrationResponse(sessionId, startAt, nodes.Length);
    }

    public async Task ReportAsync(PlaybackStatusReport report, CancellationToken cancellationToken)
    {
        await EnsureRestoredAsync(cancellationToken);
        if (!sessions.TryGetValue(report.SessionId, out var session)) return;
        await session.Gate.WaitAsync(cancellationToken);
        try
        {
            if (!string.Equals(session.Current.node.Id, report.NodeId, StringComparison.OrdinalIgnoreCase)) return;
            if (!session.ExpectedClients.Contains(report.ClientId)) return;
            if (report.State == PlaybackState.Ready)
            {
                session.PreparationProgress = 1;
                session.ReadyClients.Add(report.ClientId);
                if (!session.PlayPublished && session.ExpectedClients.IsSubsetOf(session.ReadyClients))
                {
                    await PublishPlayAsync(session, DateTimeOffset.UtcNow.AddMilliseconds(settings.PrepareLeadMilliseconds), cancellationToken);
                }
                await PersistAsync(session, cancellationToken);
                return;
            }
            if (report.State == PlaybackState.Received)
            {
                session.PreparationProgress = Math.Clamp(report.Progress, 0, 1);
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
                await PersistAsync(session, cancellationToken);
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
                    await sessionStore.ClearAsync(cancellationToken);
                    await eventLog.AppendAsync("Error", "Playback", "Failed",
                        "讲解因节点故障策略停止。", report.SessionId, report.Error, cancellationToken);
                    logger.LogError("Narration session {SessionId} stopped at node {NodeId}: {Error}", report.SessionId, report.NodeId, report.Error);
                    return;
                }
                await eventLog.AppendAsync("Warning", "Playback", "NodeSkippedAfterFailure",
                    $"节点“{session.Current.node.Name}”播放失败，已按策略跳过。", report.SessionId, report.Error, cancellationToken);
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
        await EnsureRestoredAsync(cancellationToken);
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
                    await PersistAsync(session, cancellationToken);
                    await eventLog.AppendAsync("Information", "Playback", "Pause", "讲解已暂停。", request.SessionId, cancellationToken: cancellationToken);
                    return Accepted(request, "讲解已暂停。");
                case PlaybackAction.Resume:
                    if (!session.Paused) return Accepted(request, "讲解正在播放。");
                    session.Paused = false;
                    await PublishControlAsync(session, PlaybackAction.Resume, cancellationToken);
                    await PersistAsync(session, cancellationToken);
                    await eventLog.AppendAsync("Information", "Playback", "Resume", "讲解已继续。", request.SessionId, cancellationToken: cancellationToken);
                    return Accepted(request, "讲解已继续。");
                case PlaybackAction.Skip:
                    session.Paused = false;
                    await PublishControlAsync(session, PlaybackAction.Skip, cancellationToken);
                    await eventLog.AppendAsync("Warning", "Playback", "Skip", "操作员跳过当前讲解节点。", request.SessionId, cancellationToken: cancellationToken);
                    return Accepted(request, "已跳过当前讲解节点。");
                case PlaybackAction.Retry:
                    session.Paused = false;
                    await StopCurrentNodeAsync(session, cancellationToken);
                    await PublishCurrentNodeAsync(session, cancellationToken);
                    await PersistAsync(session, cancellationToken);
                    await eventLog.AppendAsync("Warning", "Playback", "Retry", "操作员重新准备当前讲解节点。", request.SessionId, cancellationToken: cancellationToken);
                    return Accepted(request, "正在重新准备当前讲解节点。");
                case PlaybackAction.Stop:
                    await PublishControlAsync(session, PlaybackAction.Stop, cancellationToken);
                    sessions.TryRemove(session.SessionId, out _);
                    await sessionStore.ClearAsync(cancellationToken);
                    await eventLog.AppendAsync("Warning", "Playback", "Stop",
                        "讲解任务由操作员终止。", request.SessionId, cancellationToken: cancellationToken);
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
        session.PreparationProgress = 0;
        var (module, node) = session.Current;
        var video = node.Assets.FirstOrDefault(asset => asset.Kind is AssetKind.Video or AssetKind.Animation);
        var narrationUrl = string.IsNullOrWhiteSpace(node.TtsAudioUrl) ? null : node.TtsAudioUrl;
        if (video is not null || narrationUrl is not null)
        {
            session.ExpectedClients.Add(settings.LedClientId);
            await broker.PublishAsync(settings.LedClientId,
                NewCommand(session.SessionId, module.Id, node.Id, PlaybackAction.Prepare, video?.Url,
                    narrationUrl, DateTimeOffset.UtcNow, session.ContentVersion, node), cancellationToken);
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
        var narrationUrl = string.IsNullOrWhiteSpace(node.TtsAudioUrl) ? null : node.TtsAudioUrl;
        if (video is not null || narrationUrl is not null)
        {
            var action = video is null ? PlaybackAction.PlayNarration : PlaybackAction.PlayVideo;
            await broker.PublishAsync(settings.LedClientId,
                NewCommand(session.SessionId, module.Id, node.Id, action, video?.Url,
                    narrationUrl, executeAt, session.ContentVersion, node), cancellationToken);
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
            await sessionStore.ClearAsync(cancellationToken);
            await eventLog.AppendAsync("Information", "Playback", "Completed",
                "讲解任务已完成。", session.SessionId, cancellationToken: cancellationToken);
            logger.LogInformation("Narration session {SessionId} completed", session.SessionId);
            return;
        }
        await PublishCurrentNodeAsync(session, cancellationToken);
        await PersistAsync(session, cancellationToken);
    }

    private PlaybackCommand NewCommand(string sessionId, string moduleId, string nodeId, PlaybackAction action,
        string? mediaUrl, string? narrationAudioUrl, DateTimeOffset executeAt, long version, NarrationNode? node = null) =>
        new(broker.NextSequence(), Guid.NewGuid().ToString("N"), sessionId, moduleId, nodeId, action, mediaUrl,
            executeAt, 0, version, narrationAudioUrl, node?.AudioMixPolicy ?? AudioMixPolicy.Duck,
            NormalizeVideoVolume(node), NormalizeNarrationVolume(node));

    private static double NormalizeVideoVolume(NarrationNode? node) =>
        node is null ? 0.25 : Math.Clamp(node.VideoVolume > 0 ? node.VideoVolume : 0.25, 0, 1);

    private static double NormalizeNarrationVolume(NarrationNode? node) =>
        node is null ? 1 : Math.Clamp(node.NarrationVolume > 0 ? node.NarrationVolume : 1, 0, 1);

    private async Task PublishControlAsync(SessionState session, PlaybackAction action, CancellationToken cancellationToken)
    {
        var (module, node) = session.Current;
        foreach (var clientId in session.ExpectedClients)
        {
            await broker.PublishAsync(clientId,
                NewCommand(session.SessionId, module.Id, node.Id, action, null, null,
                    DateTimeOffset.UtcNow, session.ContentVersion, node),
                cancellationToken);
        }
    }

    private async Task EnsureRestoredAsync(CancellationToken cancellationToken)
    {
        if (restored) return;
        await restoreGate.WaitAsync(cancellationToken);
        try
        {
            if (restored) return;
            var snapshot = await sessionStore.LoadAsync(cancellationToken);
            if (snapshot is not null)
            {
                var content = await contentRepository.GetAsync(cancellationToken);
                var modules = content.Modules.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
                var nodes = new List<(ExhibitionModule module, NarrationNode node)>();
                foreach (var reference in snapshot.Nodes)
                {
                    if (!modules.TryGetValue(reference.ModuleId, out var module)) { nodes.Clear(); break; }
                    var node = module.Nodes.FirstOrDefault(item => string.Equals(item.Id, reference.NodeId, StringComparison.OrdinalIgnoreCase));
                    if (node is null) { nodes.Clear(); break; }
                    nodes.Add((module, node));
                }
                if (content.Version == snapshot.ContentVersion && nodes.Count > 0 && snapshot.Index >= 0 && snapshot.Index < nodes.Count)
                {
                    var session = new SessionState(snapshot.SessionId, snapshot.ContentVersion, nodes.ToArray())
                    {
                        Index = snapshot.Index,
                        Paused = snapshot.Paused,
                        PlayPublished = snapshot.PlayPublished
                    };
                    if (CurrentNeedsLed(session)) session.ExpectedClients.Add(settings.LedClientId);
                    sessions[session.SessionId] = session;
                    if (!session.PlayPublished) await PublishCurrentNodeAsync(session, cancellationToken);
                    await eventLog.AppendAsync("Warning", "Playback", "Recovered",
                        "服务启动后恢复了未结束的讲解任务。", session.SessionId, cancellationToken: cancellationToken);
                }
                else
                {
                    await sessionStore.ClearAsync(cancellationToken);
                    await eventLog.AppendAsync("Warning", "Playback", "RecoveryDiscarded",
                        "未结束讲解与当前内容版本不兼容，已停止恢复。", snapshot.SessionId, cancellationToken: cancellationToken);
                }
            }
            restored = true;
        }
        finally { restoreGate.Release(); }
    }

    private Task PersistAsync(SessionState session, CancellationToken cancellationToken)
    {
        if (!sessions.ContainsKey(session.SessionId)) return sessionStore.ClearAsync(cancellationToken);
        var snapshot = new PlaybackSessionSnapshot(session.SessionId, session.ContentVersion,
            session.Nodes.Select(item => new PlaybackNodeSnapshot(item.module.Id, item.node.Id)).ToArray(),
            session.Index, session.Paused, session.PlayPublished, DateTimeOffset.UtcNow);
        return sessionStore.SaveAsync(snapshot, cancellationToken);
    }

    private static bool CurrentNeedsLed(SessionState session)
    {
        var node = session.Current.node;
        return node.Assets.Any(asset => asset.Kind is AssetKind.Video or AssetKind.Animation) ||
               !string.IsNullOrWhiteSpace(node.TtsAudioUrl);
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
            session.CompletedClients.OrderBy(value => value).ToArray(), session.PreparationProgress);
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
        public double PreparationProgress { get; set; }
        public SemaphoreSlim Gate { get; } = new(1, 1);
    }
}
