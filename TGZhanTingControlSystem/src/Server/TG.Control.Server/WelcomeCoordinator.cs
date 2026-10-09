using Microsoft.Extensions.Options;
using TG.Control.Contracts;

namespace TG.Control.Server;

/// <summary>
/// Coordinates the non-session welcome audio. It intentionally has no access to
/// the formal PlaybackSession store and therefore cannot mutate formal narration.
/// </summary>
public sealed class WelcomeCoordinator(
    ICommandBroker broker,
    PlaybackCoordinator playback,
    UiExperienceRepository uiExperience,
    IOptions<PlaybackOptions> playbackOptions,
    OperationalEventRepository eventLog,
    ILogger<WelcomeCoordinator> logger)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly PlaybackOptions options = playbackOptions.Value;
    private WelcomePlaybackStatus current = NewStatus(null, WelcomePlaybackState.Standby, "等待触摸唤醒");
    private CancellationTokenSource? timeoutSource;

    public bool IsProtectingFormalPlayback => current.State is WelcomePlaybackState.Requested or WelcomePlaybackState.Preparing or WelcomePlaybackState.Playing;

    public WelcomePlaybackStatus GetStatus() => current;

    public async Task<WelcomeRequestResponse> RequestAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (IsProtectingFormalPlayback) return new WelcomeRequestResponse(current, true);
            if ((await playback.GetSessionsAsync(cancellationToken)).Count > 0)
                return new WelcomeRequestResponse(Set(null, WelcomePlaybackState.Failed, "当前正在进行正式讲解，暂不播放欢迎词"), false);

            var config = await uiExperience.GetAsync(cancellationToken);
            var settings = config.Welcome ?? new WelcomeExperienceSettings();
            if (!settings.AudioEnabled)
                return new WelcomeRequestResponse(Set(Guid.NewGuid().ToString("N"), WelcomePlaybackState.Completed, "欢迎音频已由现场配置关闭", settings.Captions), true);
            var element = config.TouchElements?.LastOrDefault(item => string.Equals(item.Key, "welcome.audio", StringComparison.OrdinalIgnoreCase));
            if (element is null || string.IsNullOrWhiteSpace(element.AssetId) || string.IsNullOrWhiteSpace(element.AssetUrl) ||
                string.IsNullOrWhiteSpace(element.AssetSha256) || element.AssetSha256.Length != 64 || element.AssetSizeBytes <= 0 ||
                string.IsNullOrWhiteSpace(element.AssetMediaType))
                return new WelcomeRequestResponse(Set(null, WelcomePlaybackState.Failed, "未配置已校验的欢迎语音素材", settings.Captions), false);

            var onlineThreshold = TimeSpan.FromSeconds(Math.Max(10, options.LongPollSeconds * 2 + 5));
            var led = broker.GetClientStatuses(onlineThreshold).FirstOrDefault(client =>
                string.Equals(client.ClientId, options.LedClientId, StringComparison.OrdinalIgnoreCase));
            if (led?.Online != true)
                return new WelcomeRequestResponse(Set(null, WelcomePlaybackState.Failed, "LED播放端离线，无法播放欢迎语音", settings.Captions), false);

            var requestId = Guid.NewGuid().ToString("N");
            var audio = new WelcomeAudioAsset(element.AssetId, element.AssetUrl, element.AssetSha256,
                element.AssetSizeBytes, element.AssetMediaType);
            current = NewStatus(requestId, WelcomePlaybackState.Preparing, "正在由LED播放端准备欢迎语音", settings.Captions);
            ArmTimeout(requestId);
            logger.LogInformation("Welcome {RequestId} requested for LED client {LedClientId}", requestId, options.LedClientId);
            var command = new WelcomePlaybackCommand(broker.NextSequence(), Guid.NewGuid().ToString("N"), requestId,
                WelcomePlaybackAction.Prepare, audio, DateTimeOffset.UtcNow);
            await broker.PublishWelcomeAsync(options.LedClientId, command, cancellationToken);
            await eventLog.AppendAsync("Information", "Welcome", "Requested", "欢迎语音已请求LED准备", cancellationToken: cancellationToken);
            return new WelcomeRequestResponse(current, true);
        }
        finally { gate.Release(); }
    }

    public async Task ReportAsync(WelcomePlaybackStatusReport report, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (string.IsNullOrWhiteSpace(current.RequestId) || !string.Equals(current.RequestId, report.RequestId, StringComparison.OrdinalIgnoreCase)) return;
            var captions = current.Captions;
            if (report.State == WelcomePlaybackState.Preparing) current = Set(current.RequestId, WelcomePlaybackState.Preparing, "LED正在校验欢迎语音素材", captions, report.PositionSeconds, report.Progress);
            else if (report.State == WelcomePlaybackState.Playing) current = Set(current.RequestId, WelcomePlaybackState.Playing, "欢迎语音播放中", captions, report.PositionSeconds, report.Progress);
            else if (report.State == WelcomePlaybackState.Completed) current = Set(current.RequestId, WelcomePlaybackState.Completed, "欢迎语音播放完成", captions, report.PositionSeconds, 1);
            else if (report.State == WelcomePlaybackState.Failed)
            {
                current = Set(current.RequestId, WelcomePlaybackState.Failed,
                    string.IsNullOrWhiteSpace(report.Error) ? "欢迎语音播放失败" : report.Error, captions, report.PositionSeconds, report.Progress);
                logger.LogWarning("Welcome {RequestId} failed on LED: {Error}", report.RequestId, current.Message);
                CancelTimeout();
                await eventLog.AppendAsync("Warning", "Welcome", "Failed", current.Message ?? "欢迎语音播放失败", cancellationToken: cancellationToken);
                return;
            }
            if (report.State == WelcomePlaybackState.Completed) CancelTimeout();
            if (report.State == WelcomePlaybackState.Preparing)
            {
                var config = await uiExperience.GetAsync(cancellationToken);
                var element = config.TouchElements?.LastOrDefault(item => string.Equals(item.Key, "welcome.audio", StringComparison.OrdinalIgnoreCase));
                if (element is null) return;
                var play = new WelcomePlaybackCommand(broker.NextSequence(), Guid.NewGuid().ToString("N"), current.RequestId!,
                    WelcomePlaybackAction.Play, new WelcomeAudioAsset(element.AssetId!, element.AssetUrl!, element.AssetSha256!, element.AssetSizeBytes, element.AssetMediaType!),
                    DateTimeOffset.UtcNow.AddMilliseconds(Math.Max(250, options.PrepareLeadMilliseconds)));
                await broker.PublishWelcomeAsync(options.LedClientId, play, cancellationToken);
            }
        }
        finally { gate.Release(); }
    }

    public void Acknowledge(string? requestId)
    {
        if (current.State == WelcomePlaybackState.Completed && (string.IsNullOrWhiteSpace(requestId) || string.Equals(requestId, current.RequestId, StringComparison.OrdinalIgnoreCase)))
            current = NewStatus(null, WelcomePlaybackState.Standby, "等待触摸唤醒");
    }

    private void ArmTimeout(string requestId)
    {
        CancelTimeout();
        timeoutSource = new CancellationTokenSource();
        var token = timeoutSource.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(2), token);
                await gate.WaitAsync(token);
                try
                {
                    if (string.Equals(current.RequestId, requestId, StringComparison.OrdinalIgnoreCase) && IsProtectingFormalPlayback)
                    {
                        current = Set(requestId, WelcomePlaybackState.Failed, "欢迎语音播放超时，请检查LED播放端后重试", current.Captions);
                        await broker.PublishWelcomeAsync(options.LedClientId, new WelcomePlaybackCommand(broker.NextSequence(),
                            Guid.NewGuid().ToString("N"), requestId, WelcomePlaybackAction.Stop, null, DateTimeOffset.UtcNow), CancellationToken.None);
                        await eventLog.AppendAsync("Warning", "Welcome", "Timeout", current.Message!, cancellationToken: CancellationToken.None);
                    }
                }
                finally { gate.Release(); }
            }
            catch (OperationCanceledException) { }
        });
    }

    private void CancelTimeout()
    {
        if (timeoutSource is null) return;
        timeoutSource.Cancel();
        timeoutSource.Dispose();
        timeoutSource = null;
    }

    private WelcomePlaybackStatus Set(string? requestId, WelcomePlaybackState state, string message,
        IReadOnlyList<WelcomeCaptionCue>? captions = null, double position = 0, double progress = 0) =>
        current = NewStatus(requestId, state, message, captions, position, progress);
    private static WelcomePlaybackStatus NewStatus(string? requestId, WelcomePlaybackState state, string message,
        IReadOnlyList<WelcomeCaptionCue>? captions = null, double position = 0, double progress = 0) =>
        new(requestId, state, position, Math.Clamp(progress, 0, 1), message, captions ?? Array.Empty<WelcomeCaptionCue>(), DateTimeOffset.UtcNow);
}
