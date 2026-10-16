using Microsoft.Extensions.Options;
using TG.Control.Contracts;

namespace TG.Control.Server;

/// <summary>
/// Server-authoritative coordinator for manual module galleries. Gallery traffic is
/// intentionally isolated from formal playback and welcome command channels.
/// </summary>
public sealed class ImageGalleryCoordinator(
    IContentRepository contentRepository,
    ICommandBroker broker,
    IPlaybackActivityState playback,
    IWelcomeActivityState welcome,
    IOptions<PlaybackOptions> playbackOptions,
    GallerySessionStore sessionStore,
    OperationalEventRepository eventLog,
    ILogger<ImageGalleryCoordinator> logger)
{
    private const int DefaultSlideshowIntervalSeconds = 8;
    private const int MinimumSlideshowIntervalSeconds = 3;
    private const int MaximumSlideshowIntervalSeconds = 3600;

    private readonly PlaybackOptions options = playbackOptions.Value;
    private readonly SemaphoreSlim transitionGate = new(1, 1);
    private readonly SemaphoreSlim stateGate = new(1, 1);
    private readonly SemaphoreSlim restoreGate = new(1, 1);
    private GallerySession? active;
    private bool restored;

    public async Task<StartGalleryResponse> StartAsync(StartGalleryRequest request, CancellationToken cancellationToken)
    {
        await EnsureRestoredAsync(cancellationToken);
        await transitionGate.WaitAsync(cancellationToken);
        try
        {
            if (await playback.HasActiveSessionAsync(cancellationToken))
                throw new InvalidOperationException("正式讲解正在进行，暂时不能启动图片图集。");
            if (welcome.IsProtectingFormalPlayback)
                throw new InvalidOperationException("欢迎流程正在准备或播放，暂时不能启动图片图集。");

            await stateGate.WaitAsync(cancellationToken);
            try
            {
                if (active is not null)
                    throw new InvalidOperationException("当前已有图片图集正在展示，请先停止当前图集。");

                var content = await contentRepository.GetAsync(cancellationToken);
                EnsureLedCanDisplay(content.Version);
                var module = content.Modules.FirstOrDefault(item =>
                    string.Equals(item.Id, request.ModuleId, StringComparison.OrdinalIgnoreCase));
                if (module is null) throw new KeyNotFoundException($"模块 '{request.ModuleId}' 不存在。");
                var images = NormalizeAndValidateImages(module);
                if (request.ImageIndex < 0 || request.ImageIndex >= images.Length)
                    throw new ArgumentOutOfRangeException(nameof(request), "图片序号超出模块图集范围。");

                var now = DateTimeOffset.UtcNow;
                active = new GallerySession(Guid.NewGuid().ToString("N"), content.Version, module, images,
                    request.ImageIndex, now);
                await PublishPrepareAsync(active, cancellationToken);
                await PersistAsync(active, cancellationToken);
                await eventLog.AppendAsync("Information", "Gallery", "Start",
                    $"图片图集已启动：{module.Name}，共 {images.Length} 张，发起人：{request.RequestedBy}。",
                    active.SessionId, cancellationToken: cancellationToken);
                logger.LogInformation("Gallery session {SessionId} started for module {ModuleId} by {RequestedBy}",
                    active.SessionId, module.Id, request.RequestedBy);
                return new StartGalleryResponse(active.SessionId, now, images.Length);
            }
            finally
            {
                stateGate.Release();
            }
        }
        finally
        {
            transitionGate.Release();
        }
    }

    public async Task<ControlGalleryResponse> ControlAsync(ControlGalleryRequest request, CancellationToken cancellationToken)
    {
        await EnsureRestoredAsync(cancellationToken);
        await transitionGate.WaitAsync(cancellationToken);
        try
        {
            await stateGate.WaitAsync(cancellationToken);
            try
            {
                if (active is null || !string.Equals(active.SessionId, request.SessionId, StringComparison.OrdinalIgnoreCase))
                    return Rejected(request, "图集会话不存在或已经结束。");

                switch (request.Action)
                {
                    case GalleryControlAction.Show:
                        if (!request.ImageIndex.HasValue || request.ImageIndex.Value < 0 || request.ImageIndex.Value >= active.Images.Length)
                            return Rejected(request, "请选择有效的目标图片序号。");
                        active.Index = request.ImageIndex.Value;
                        await PublishPrepareAsync(active, cancellationToken);
                        break;
                    case GalleryControlAction.Previous:
                        active.Index = (active.Index - 1 + active.Images.Length) % active.Images.Length;
                        await PublishPrepareAsync(active, cancellationToken);
                        break;
                    case GalleryControlAction.Next:
                        active.Index = (active.Index + 1) % active.Images.Length;
                        await PublishPrepareAsync(active, cancellationToken);
                        break;
                    case GalleryControlAction.StartSlideshow:
                        var interval = request.SlideshowIntervalSeconds ?? active.SlideshowIntervalSeconds;
                        if (interval is < MinimumSlideshowIntervalSeconds or > MaximumSlideshowIntervalSeconds)
                            return Rejected(request, $"轮播间隔必须在 {MinimumSlideshowIntervalSeconds} 到 {MaximumSlideshowIntervalSeconds} 秒之间。");
                        active.SlideshowIntervalSeconds = interval;
                        active.SlideshowActive = true;
                        active.UpdatedAtUtc = DateTimeOffset.UtcNow;
                        RestartSlideshowLoop(active);
                        break;
                    case GalleryControlAction.PauseSlideshow:
                        active.SlideshowActive = false;
                        active.UpdatedAtUtc = DateTimeOffset.UtcNow;
                        CancelSlideshowLoop(active);
                        break;
                    case GalleryControlAction.Stop:
                        var stoppedSessionId = active.SessionId;
                        await StopCoreAsync("operator", cancellationToken);
                        await eventLog.AppendAsync("Information", "Gallery", "Stop", "图片图集已由操作员停止。",
                            stoppedSessionId, cancellationToken: cancellationToken);
                        return new ControlGalleryResponse(request.SessionId, request.Action, true, "图片图集已停止。");
                    default:
                        return Rejected(request, "不支持该图集控制操作。");
                }

                await PersistAsync(active, cancellationToken);
                await eventLog.AppendAsync("Information", "Gallery", request.Action.ToString(),
                    $"图集操作已接受：{request.Action}。", active.SessionId,
                    nodeId: active.Current.Id, cancellationToken: cancellationToken);
                return Accepted(request, "图集操作已接受。", active);
            }
            finally
            {
                stateGate.Release();
            }
        }
        finally
        {
            transitionGate.Release();
        }
    }

    public async Task ReportAsync(GalleryDisplayStatusReport report, CancellationToken cancellationToken)
    {
        await EnsureRestoredAsync(cancellationToken);
        await stateGate.WaitAsync(cancellationToken);
        try
        {
            if (active is null ||
                !string.Equals(active.SessionId, report.SessionId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(active.Current.Id, report.ImageId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(report.ClientId, options.LedClientId, StringComparison.OrdinalIgnoreCase)) return;

            if (report.State == GalleryDisplayState.Ready)
            {
                if (!string.Equals(active.PrepareCommandId, report.CommandId, StringComparison.OrdinalIgnoreCase)) return;
                await PublishShowAsync(active, cancellationToken);
                await PersistAsync(active, cancellationToken);
                return;
            }

            if (report.State == GalleryDisplayState.Showing)
            {
                if (!string.Equals(active.ShowCommandId, report.CommandId, StringComparison.OrdinalIgnoreCase)) return;
                active.State = GallerySessionState.Showing;
                active.Error = null;
                active.UpdatedAtUtc = DateTimeOffset.UtcNow;
                await PersistAsync(active, cancellationToken);
                return;
            }

            if (report.State == GalleryDisplayState.Failed &&
                (string.Equals(active.PrepareCommandId, report.CommandId, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(active.ShowCommandId, report.CommandId, StringComparison.OrdinalIgnoreCase)))
            {
                active.State = GallerySessionState.Failed;
                active.Error = string.IsNullOrWhiteSpace(report.Error) ? "LED播放端无法显示当前图片。" : report.Error;
                active.SlideshowActive = false;
                active.UpdatedAtUtc = DateTimeOffset.UtcNow;
                CancelSlideshowLoop(active);
                await PersistAsync(active, cancellationToken);
                await eventLog.AppendAsync("Error", "Gallery", "DisplayFailed", active.Error,
                    active.SessionId, clientId: report.ClientId, nodeId: active.Current.Id,
                    cancellationToken: cancellationToken);
            }
        }
        finally
        {
            stateGate.Release();
        }
    }

    public async Task<GallerySessionStatus?> GetActiveAsync(CancellationToken cancellationToken)
    {
        await EnsureRestoredAsync(cancellationToken);
        await stateGate.WaitAsync(cancellationToken);
        try
        {
            return active is null ? null : CreateStatus(active);
        }
        finally
        {
            stateGate.Release();
        }
    }

    public async Task<GallerySessionStatus?> GetSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        var current = await GetActiveAsync(cancellationToken);
        return current is not null && string.Equals(current.SessionId, sessionId, StringComparison.OrdinalIgnoreCase)
            ? current
            : null;
    }

    public async Task RecoverClientAsync(string clientId, CancellationToken cancellationToken)
    {
        if (!string.Equals(clientId, options.LedClientId, StringComparison.OrdinalIgnoreCase)) return;
        await EnsureRestoredAsync(cancellationToken);
        await transitionGate.WaitAsync(cancellationToken);
        try
        {
            await stateGate.WaitAsync(cancellationToken);
            try
            {
                if (active is null) return;
                EnsureLedCanDisplay(active.ContentVersion);
                await PublishPrepareAsync(active, cancellationToken);
                await PersistAsync(active, cancellationToken);
                await eventLog.AppendAsync("Warning", "Gallery", "ClientRecovered",
                    "LED播放端重新连接，Server已重新下发当前图集图片。", active.SessionId,
                    clientId: clientId, nodeId: active.Current.Id, cancellationToken: cancellationToken);
            }
            catch (InvalidOperationException exception)
            {
                logger.LogWarning(exception, "Gallery session {SessionId} could not be restored to LED", active?.SessionId);
            }
            finally
            {
                stateGate.Release();
            }
        }
        finally
        {
            transitionGate.Release();
        }
    }

    /// <summary>
    /// Serializes a higher-priority transition with gallery start. If a gallery is active,
    /// Stop is queued and the persisted session is cleared before the supplied operation runs.
    /// </summary>
    public async Task<T> RunHigherPriorityAsync<T>(string reason,
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        await EnsureRestoredAsync(cancellationToken);
        await transitionGate.WaitAsync(cancellationToken);
        try
        {
            await stateGate.WaitAsync(cancellationToken);
            try
            {
                if (active is not null)
                {
                    var sessionId = active.SessionId;
                    await StopCoreAsync(reason, cancellationToken);
                    await eventLog.AppendAsync("Warning", "Gallery", "Preempted",
                        $"图片图集已被更高优先级流程停止：{reason}。", sessionId,
                        cancellationToken: cancellationToken);
                }
            }
            finally
            {
                stateGate.Release();
            }
            return await operation(cancellationToken);
        }
        finally
        {
            transitionGate.Release();
        }
    }

    private async Task AdvanceSlideshowAsync(string sessionId, CancellationToken cancellationToken)
    {
        await transitionGate.WaitAsync(cancellationToken);
        try
        {
            await stateGate.WaitAsync(cancellationToken);
            try
            {
                if (active is null || !active.SlideshowActive ||
                    !string.Equals(active.SessionId, sessionId, StringComparison.OrdinalIgnoreCase) ||
                    active.State != GallerySessionState.Showing) return;
                active.Index = (active.Index + 1) % active.Images.Length;
                await PublishPrepareAsync(active, cancellationToken);
                await PersistAsync(active, cancellationToken);
            }
            finally
            {
                stateGate.Release();
            }
        }
        finally
        {
            transitionGate.Release();
        }
    }

    private void RestartSlideshowLoop(GallerySession session)
    {
        CancelSlideshowLoop(session);
        if (!session.SlideshowActive) return;
        session.SlideshowSource = new CancellationTokenSource();
        var source = session.SlideshowSource;
        _ = Task.Run(async () =>
        {
            try
            {
                while (!source.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(session.SlideshowIntervalSeconds), source.Token);
                    await AdvanceSlideshowAsync(session.SessionId, source.Token);
                }
            }
            catch (OperationCanceledException) when (source.IsCancellationRequested)
            {
            }
        }, source.Token);
    }

    private static void CancelSlideshowLoop(GallerySession session)
    {
        if (session.SlideshowSource is null) return;
        session.SlideshowSource.Cancel();
        session.SlideshowSource.Dispose();
        session.SlideshowSource = null;
    }

    private async Task PublishPrepareAsync(GallerySession session, CancellationToken cancellationToken)
    {
        session.State = GallerySessionState.Preparing;
        session.Error = null;
        session.ShowCommandId = null;
        session.UpdatedAtUtc = DateTimeOffset.UtcNow;
        var command = NewCommand(session, GalleryDisplayAction.Prepare, DateTimeOffset.UtcNow);
        session.PrepareCommandId = command.CommandId;
        await broker.PublishGalleryAsync(options.LedClientId, command, cancellationToken);
    }

    private async Task PublishShowAsync(GallerySession session, CancellationToken cancellationToken)
    {
        var executeAt = DateTimeOffset.UtcNow.AddMilliseconds(Math.Max(250, options.PrepareLeadMilliseconds));
        var command = NewCommand(session, GalleryDisplayAction.Show, executeAt);
        session.ShowCommandId = command.CommandId;
        session.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await broker.PublishGalleryAsync(options.LedClientId, command, cancellationToken);
    }

    private async Task StopCoreAsync(string reason, CancellationToken cancellationToken)
    {
        if (active is null) return;
        CancelSlideshowLoop(active);
        await broker.PublishGalleryAsync(options.LedClientId,
            NewCommand(active, GalleryDisplayAction.Stop, DateTimeOffset.UtcNow), cancellationToken);
        logger.LogInformation("Gallery session {SessionId} stopped: {Reason}", active.SessionId, reason);
        active = null;
        await sessionStore.ClearAsync(cancellationToken);
    }

    private GalleryDisplayCommand NewCommand(GallerySession session, GalleryDisplayAction action,
        DateTimeOffset executeAtUtc)
    {
        var image = session.Current;
        return new GalleryDisplayCommand(broker.NextSequence(), Guid.NewGuid().ToString("N"), session.SessionId,
            session.Module.Id, image.Id, action, session.ContentVersion, image.Id, image.Url, image.Sha256,
            image.SizeBytes, image.MediaType!, executeAtUtc, GalleryFitMode.Contain);
    }

    private void EnsureLedCanDisplay(long contentVersion)
    {
        if (!options.RequireLedReadyBeforeStart) return;
        var threshold = TimeSpan.FromSeconds(Math.Max(10, options.LongPollSeconds * 2 + 5));
        var led = broker.GetClientStatuses(threshold).FirstOrDefault(client =>
            string.Equals(client.ClientId, options.LedClientId, StringComparison.OrdinalIgnoreCase));
        if (led?.Online != true) throw new InvalidOperationException("LED播放端离线，暂时不能展示图片图集。");
        if (led.ContentVersion != contentVersion && !options.AllowDegradedPlayback)
            throw new InvalidOperationException($"LED内容版本为 V{led.ContentVersion}，服务器为 V{contentVersion}，请等待素材同步完成。");
        if (!led.Ready && !options.AllowDegradedPlayback)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(led.Status) ? "LED播放端尚未准备完成。" : led.Status);
    }

    private static ContentAsset[] NormalizeAndValidateImages(ExhibitionModule module)
    {
        var images = module.GalleryImages?.ToArray() ?? [];
        if (images.Length == 0) throw new InvalidOperationException($"模块“{module.Name}”尚未配置图片图集。");
        foreach (var image in images)
        {
            if (image.Kind != AssetKind.Image || string.IsNullOrWhiteSpace(image.Id) ||
                string.IsNullOrWhiteSpace(image.Url) || image.SizeBytes <= 0 ||
                string.IsNullOrWhiteSpace(image.Sha256) || image.Sha256.Length != 64 ||
                !image.Sha256.All(Uri.IsHexDigit) ||
                image.MediaType?.ToLowerInvariant() is not ("image/jpeg" or "image/png"))
                throw new InvalidOperationException($"模块“{module.Name}”中的图片“{image.Name}”缺少有效的不可变资产信息。");
        }
        return images;
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
                var module = content.Modules.FirstOrDefault(item =>
                    string.Equals(item.Id, snapshot.ModuleId, StringComparison.OrdinalIgnoreCase));
                var images = module is null ? [] : module.GalleryImages?.ToArray() ?? [];
                var compatible = content.Version == snapshot.ContentVersion && module is not null &&
                                 snapshot.ImageIndex >= 0 && snapshot.ImageIndex < images.Length &&
                                 string.Equals(images[snapshot.ImageIndex].Id, snapshot.ImageId, StringComparison.OrdinalIgnoreCase);
                if (compatible)
                {
                    try
                    {
                        var validImages = NormalizeAndValidateImages(module!);
                        active = new GallerySession(snapshot.SessionId, snapshot.ContentVersion, module!, validImages,
                            snapshot.ImageIndex, snapshot.UpdatedAtUtc)
                        {
                            // Recovery always requires a fresh LED Prepare/Ready/Show handshake.
                            // Keeping a persisted Showing state would let the slideshow advance
                            // before the reconnected LED has restored the current image.
                            State = GallerySessionState.Preparing,
                            SlideshowActive = snapshot.SlideshowActive,
                            SlideshowIntervalSeconds = Math.Clamp(snapshot.SlideshowIntervalSeconds,
                                MinimumSlideshowIntervalSeconds, MaximumSlideshowIntervalSeconds),
                            Error = snapshot.Error
                        };
                        if (active.SlideshowActive) RestartSlideshowLoop(active);
                        await eventLog.AppendAsync("Warning", "Gallery", "Recovered",
                            "Server启动后已恢复未结束的图片图集会话，等待LED重连后重新下发当前图片。",
                            active.SessionId, cancellationToken: cancellationToken);
                    }
                    catch (InvalidOperationException)
                    {
                        compatible = false;
                    }
                }

                if (!compatible)
                {
                    await sessionStore.ClearAsync(cancellationToken);
                    await eventLog.AppendAsync("Warning", "Gallery", "RecoveryDiscarded",
                        "未结束的图集会话与当前正式内容不兼容，已停止恢复。", snapshot.SessionId,
                        cancellationToken: cancellationToken);
                }
            }
            restored = true;
        }
        finally
        {
            restoreGate.Release();
        }
    }

    private Task PersistAsync(GallerySession session, CancellationToken cancellationToken) =>
        sessionStore.SaveAsync(new GallerySessionSnapshot(session.SessionId, session.ContentVersion,
            session.Module.Id, session.Current.Id, session.Index, session.State, session.SlideshowActive,
            session.SlideshowIntervalSeconds, session.Error, session.UpdatedAtUtc), cancellationToken);

    private static GallerySessionStatus CreateStatus(GallerySession session) =>
        new(session.SessionId, session.ContentVersion, session.Module.Id, session.Module.Name,
            session.Current.Id, session.Current.Name, session.Index + 1, session.Images.Length,
            session.State, session.SlideshowActive, session.SlideshowIntervalSeconds,
            session.Error, session.UpdatedAtUtc);

    private static ControlGalleryResponse Accepted(ControlGalleryRequest request, string message, GallerySession session) =>
        new(request.SessionId, request.Action, true, message, CreateStatus(session));

    private static ControlGalleryResponse Rejected(ControlGalleryRequest request, string message) =>
        new(request.SessionId, request.Action, false, message);

    private sealed class GallerySession(string sessionId, long contentVersion, ExhibitionModule module,
        ContentAsset[] images, int index, DateTimeOffset updatedAtUtc)
    {
        public string SessionId { get; } = sessionId;
        public long ContentVersion { get; } = contentVersion;
        public ExhibitionModule Module { get; } = module;
        public ContentAsset[] Images { get; } = images;
        public int Index { get; set; } = index;
        public ContentAsset Current => Images[Index];
        public GallerySessionState State { get; set; } = GallerySessionState.Preparing;
        public bool SlideshowActive { get; set; }
        public int SlideshowIntervalSeconds { get; set; } = DefaultSlideshowIntervalSeconds;
        public string? Error { get; set; }
        public string? PrepareCommandId { get; set; }
        public string? ShowCommandId { get; set; }
        public DateTimeOffset UpdatedAtUtc { get; set; } = updatedAtUtc;
        public CancellationTokenSource? SlideshowSource { get; set; }
    }
}
