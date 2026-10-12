using System;
using System.Collections;
using System.IO;
using TG.Control.UnityContracts;
using UnityEngine;
using UnityEngine.Networking;

namespace TG.Control.LedPlayer
{
    /// <summary>
    /// Owns both LED video and formal narration audio playback. Keeping both media streams on
    /// the LED host avoids Wi-Fi audio routing and lets one Unity frame start both streams.
    /// </summary>
    public sealed class LedPlaybackController : MonoBehaviour
    {
        [SerializeField] private LedApiClient apiClient;
        [SerializeField] private MonoBehaviour playbackAdapterComponent;
        [SerializeField] private AudioSource narrationAudioSource;
        [SerializeField] private float initialVideoStartTimeoutSeconds = 2.5f;
        [SerializeField] private float initialVideoRecoveryDelaySeconds = 0.65f;
        [SerializeField] private float recoveryPauseSeconds = 0.08f;
        [SerializeField] private float firstVideoFrameTimeoutSeconds = 8f;

        private readonly LedContentCache contentCache = LedContentCache.Shared;
        private int playbackGeneration;
        private string preparedVideoUrl;
        private string preparedNarrationUrl;
        private bool videoPrepared;
        private bool narrationPrepared;
        private bool playbackPaused;
        private IMediaPlaybackAdapter PlaybackAdapter => playbackAdapterComponent as IMediaPlaybackAdapter;
        public event Action<bool> PlaybackActiveChanged;

        /// <summary>
        /// Compatibility entry point for the legacy Android UDP controller. The adapter
        /// supplies a synthetic command envelope, while playback still goes through the
        /// same AVPro/cache/reporting path as a normal command.
        /// </summary>
        public void HandleLegacyCommand(PlaybackCommand command)
        {
            HandleCommand(command);
        }

        private void OnEnable()
        {
            if (apiClient != null) apiClient.CommandReceived += HandleCommand;
        }

        private void OnDisable()
        {
            if (apiClient != null) apiClient.CommandReceived -= HandleCommand;
            CancelPlayback(true);
        }

        private void HandleCommand(PlaybackCommand command)
        {
            if (apiClient == null || PlaybackAdapter == null || narrationAudioSource == null)
            {
                Debug.LogError("LED声画播放组件未完成初始化，无法执行播放指令。");
                return;
            }

            Debug.Log($"[LED Player] 收到指令：action={command.action}，commandId={command.commandId}，" +
                      $"sessionId={command.sessionId}，nodeId={command.nodeId}，sequence={command.sequence}。");

            switch (command.action)
            {
                case PlaybackAction.Prepare:
                    PlaybackActiveChanged?.Invoke(true);
                    apiClient.BeginPlaybackPriority();
                    CancelPlayback(true);
                    StartCoroutine(PrepareMedia(command, ++playbackGeneration, false));
                    break;
                case PlaybackAction.PlayVideo:
                case PlaybackAction.PlayNarration:
                    PlaybackActiveChanged?.Invoke(true);
                    apiClient.BeginPlaybackPriority();
                    if (MatchesPreparedMedia(command))
                        StartCoroutine(PlayPrepared(command, playbackGeneration));
                    else
                    {
                        CancelPlayback(true);
                        StartCoroutine(PrepareMedia(command, ++playbackGeneration, true));
                    }
                    break;
                case PlaybackAction.Pause:
                    playbackPaused = true;
                    if (videoPrepared) PlaybackAdapter.Pause();
                    if (narrationPrepared) narrationAudioSource.Pause();
                    apiClient.Report(command, PlaybackState.Paused, CurrentPositionSeconds());
                    break;
                case PlaybackAction.Resume:
                    playbackPaused = false;
                    if (videoPrepared) PlaybackAdapter.Resume();
                    if (narrationPrepared) narrationAudioSource.UnPause();
                    apiClient.Report(command, PlaybackState.Playing, CurrentPositionSeconds());
                    break;
                case PlaybackAction.Stop:
                    playbackGeneration++;
                    CancelPlayback(true);
                    apiClient.EndPlaybackPriority();
                    PlaybackActiveChanged?.Invoke(false);
                    break;
                case PlaybackAction.Seek:
                    SeekAll(command.positionSeconds);
                    break;
                case PlaybackAction.SetVideoVolume:
                    var videoVolume = Mathf.Clamp01((float)command.videoVolume);
                    PlaybackAdapter.SetVolume(videoVolume);
                    Debug.Log($"LED视频音量已实时调整为 {videoVolume * 100:0}% 。");
                    break;
                case PlaybackAction.Skip:
                    var skippedAt = CurrentPositionSeconds();
                    playbackGeneration++;
                    CancelPlayback(true);
                    apiClient.EndPlaybackPriority();
                    PlaybackActiveChanged?.Invoke(false);
                    apiClient.Report(command, PlaybackState.Skipped, skippedAt);
                    break;
            }
        }

        private IEnumerator PrepareMedia(PlaybackCommand command, int generation, bool playAfterPrepare)
        {
            var prepareStartedAt = Time.realtimeSinceStartup;
            apiClient.Report(command, PlaybackState.Received);
            var hasVideo = !string.IsNullOrWhiteSpace(command.mediaUrl);
            var hasNarration = !string.IsNullOrWhiteSpace(command.narrationAudioUrl);
            if (!hasVideo && !hasNarration)
            {
                Fail(command, "播放指令未包含视频或讲解音频地址。");
                yield break;
            }

            string localVideoUrl = null;
            string localNarrationUrl = null;
            string cacheError = null;
            var progressScale = hasVideo && hasNarration ? 0.5 : 1.0;

            if (hasVideo)
            {
                yield return contentCache.Resolve(apiClient.NormalizeUrl(command.mediaUrl), value => localVideoUrl = value, value => cacheError = value, 0,
                    value => apiClient.Report(command, PlaybackState.Received, progress: value * progressScale));
                if (generation != playbackGeneration) yield break;
                if (cacheError != null)
                {
                    Fail(command, "视频缓存失败：" + cacheError);
                    yield break;
                }
            }

            if (hasNarration)
            {
                cacheError = null;
                yield return contentCache.Resolve(apiClient.NormalizeUrl(command.narrationAudioUrl), value => localNarrationUrl = value, value => cacheError = value, 0,
                    value => apiClient.Report(command, PlaybackState.Received, progress: (hasVideo ? 0.5 : 0) + value * progressScale));
                if (generation != playbackGeneration) yield break;
                if (cacheError != null)
                {
                    Fail(command, "讲解音频缓存失败：" + cacheError);
                    yield break;
                }
            }

            var cacheCompletedAt = Time.realtimeSinceStartup;

            if (hasVideo)
            {
                var prepared = false;
                string prepareError = null;
                PlaybackAdapter.Prepare(localVideoUrl, (ok, error) => { prepared = ok; prepareError = error; });
                while (!prepared && prepareError == null)
                {
                    if (generation != playbackGeneration) yield break;
                    yield return null;
                }
                if (!prepared)
                {
                    Fail(command, prepareError);
                    yield break;
                }
                videoPrepared = true;
            }

            if (hasNarration)
            {
                using (var request = UnityWebRequestMultimedia.GetAudioClip(localNarrationUrl, DetectAudioType(command.narrationAudioUrl)))
                {
                    yield return request.SendWebRequest();
                    if (generation != playbackGeneration) yield break;
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        Fail(command, "讲解音频解码失败：" + request.error);
                        yield break;
                    }

                    ReleaseNarrationClip();
                    narrationAudioSource.clip = DownloadHandlerAudioClip.GetContent(request);
                    narrationPrepared = narrationAudioSource.clip != null;
                }

                if (!narrationPrepared)
                {
                    Fail(command, "讲解音频解码后未生成可播放音轨。");
                    yield break;
                }
            }

            preparedVideoUrl = command.mediaUrl;
            preparedNarrationUrl = command.narrationAudioUrl;
            var preparedAt = Time.realtimeSinceStartup;
            Debug.Log($"[LED Player] 素材准备完成：缓存 {cacheCompletedAt - prepareStartedAt:0.000} 秒，" +
                      $"媒体预加载 {preparedAt - cacheCompletedAt:0.000} 秒，总计 {preparedAt - prepareStartedAt:0.000} 秒。");
            apiClient.Report(command, PlaybackState.Ready);
            if (playAfterPrepare) yield return PlayPrepared(command, generation);
        }

        private IEnumerator PlayPrepared(PlaybackCommand command, int generation)
        {
            if (!DateTimeOffset.TryParse(command.executeAtUtc, out var executeAt))
            {
                Fail(command, "播放计划时间无效。");
                yield break;
            }

            while (DateTimeOffset.UtcNow < executeAt)
            {
                if (generation != playbackGeneration) yield break;
                yield return null;
            }

            var lateBySeconds = Math.Max(0, (DateTimeOffset.UtcNow - executeAt).TotalSeconds);
            var startPosition = command.positionSeconds + lateBySeconds;
            var startRequestedAt = Time.realtimeSinceStartup;
            var initialStartRecoveryDeadline = Time.realtimeSinceStartup +
                                               Mathf.Max(0.25f, initialVideoStartTimeoutSeconds);
            var diagnostics = PlaybackAdapter as IVideoPlaybackDiagnostics;
            var frameBaseline = diagnostics?.RenderedVideoFrameCount ?? 0L;
            ApplyMix(command);
            if (videoPrepared) PlaybackAdapter.Play(startPosition);
            if (narrationPrepared)
            {
                narrationAudioSource.time = ClampAudioPosition(startPosition);
                narrationAudioSource.Play();
            }

            if (playbackPaused)
            {
                if (videoPrepared) PlaybackAdapter.Pause();
                if (narrationPrepared) narrationAudioSource.Pause();
            }

            // A native decoder can report Playing even when it failed to provide Unity with a
            // renderable texture. Confirm the first frame so a black screen is never accepted
            // as a successful LED start.
            if (videoPrepared && diagnostics != null)
            {
                // Cached formal content must either expose a new frame or fail clearly before the
                // three-second product limit. Do not silently wait for the legacy eight-second
                // decoder timeout after the controlled startup recovery window has elapsed.
                var frameDeadline = startRequestedAt + Mathf.Min(
                    Mathf.Max(0.25f, initialVideoStartTimeoutSeconds),
                    Mathf.Max(0.25f, firstVideoFrameTimeoutSeconds));
                var nextRecoveryAt = startRequestedAt + Mathf.Max(0.25f, initialVideoRecoveryDelaySeconds);
                var recoveryCount = 0;
                while (generation == playbackGeneration &&
                       !HasNewVideoFrame(diagnostics, frameBaseline) &&
                       Time.realtimeSinceStartup < frameDeadline)
                {
                    if (playbackPaused) frameDeadline += Time.unscaledDeltaTime;
                    else if (Time.realtimeSinceStartup >= nextRecoveryAt &&
                             Time.realtimeSinceStartup < initialStartRecoveryDeadline)
                    {
                        // Some Windows hardware decoders accept Seek+Play but never release a
                        // frame until their native state is toggled. Reproduce the operator's
                        // formerly required Pause/Resume sequence automatically, and re-seek to
                        // the current planned position so audio/video timing remains correct.
                        PlaybackAdapter.Pause();
                        yield return new WaitForSecondsRealtime(Mathf.Max(0.02f, recoveryPauseSeconds));
                        if (generation != playbackGeneration || playbackPaused) yield break;
                        var recoveredPosition = startPosition +
                                                Math.Max(0, Time.realtimeSinceStartup - startRequestedAt);
                        PlaybackAdapter.Seek(recoveredPosition);
                        yield return null;
                        if (generation != playbackGeneration || playbackPaused) yield break;
                        PlaybackAdapter.Resume();
                        recoveryCount++;
                        nextRecoveryAt = Time.realtimeSinceStartup +
                                         Mathf.Max(0.35f, initialVideoRecoveryDelaySeconds);
                        Debug.LogWarning($"[LED Player] 首帧尚未生成，执行第 {recoveryCount} 次受控启动恢复，" +
                                         $"目标位置={recoveredPosition:0.000}s，帧基线={frameBaseline}，" +
                                         $"当前帧={diagnostics.RenderedVideoFrameCount}。");
                    }
                    yield return null;
                }

                if (generation != playbackGeneration) yield break;
                if (!HasNewVideoFrame(diagnostics, frameBaseline))
                {
                    var backend = string.IsNullOrWhiteSpace(diagnostics.PlaybackBackend)
                        ? "unknown backend"
                        : diagnostics.PlaybackBackend;
                    CancelPlayback(true);
                    Fail(command, "视频已启动但未生成可显示画面，请检查视频编码或播放组件。播放路径：" + backend);
                    yield break;
                }

                var firstFrameElapsed = Time.realtimeSinceStartup - startRequestedAt;
                Debug.Log($"[LED Player] 当前媒体新画面已确认：耗时 {firstFrameElapsed:0.000} 秒，" +
                          $"帧 {frameBaseline}->{diagnostics.RenderedVideoFrameCount}，后端={diagnostics.PlaybackBackend}。");
                if (recoveryCount > 0)
                    Debug.LogWarning($"[LED Player] 当前媒体首帧通过 {recoveryCount} 次受控恢复后生成。");
            }
            else
            {
                yield return null;
            }

            apiClient.Report(command, playbackPaused ? PlaybackState.Paused : PlaybackState.Playing, startPosition);

            while (generation == playbackGeneration)
            {
                if (playbackPaused)
                {
                    yield return null;
                    continue;
                }

                var videoFinished = !videoPrepared || PlaybackAdapter.IsFinished;
                var narrationFinished = !narrationPrepared || !narrationAudioSource.isPlaying;
                if (videoFinished && narrationFinished) break;
                yield return null;
            }
            if (generation != playbackGeneration) yield break;

            var completedAt = CurrentPositionSeconds();
            CancelPlayback(false);
            apiClient.Report(command, PlaybackState.Completed, completedAt);
            apiClient.EndPlaybackPriority();
            PlaybackActiveChanged?.Invoke(false);
        }

        private bool MatchesPreparedMedia(PlaybackCommand command) =>
            string.Equals(preparedVideoUrl, command.mediaUrl, StringComparison.Ordinal) &&
            string.Equals(preparedNarrationUrl, command.narrationAudioUrl, StringComparison.Ordinal) &&
            (!string.IsNullOrWhiteSpace(command.mediaUrl) ? videoPrepared : true) &&
            (!string.IsNullOrWhiteSpace(command.narrationAudioUrl) ? narrationPrepared : true);

        private static bool HasNewVideoFrame(IVideoPlaybackDiagnostics diagnostics, long frameBaseline) =>
            diagnostics.HasRenderableVideoFrame && diagnostics.RenderedVideoFrameCount > frameBaseline;

        private void ApplyMix(PlaybackCommand command)
        {
            var narrationVolume = command.narrationVolume > 0 ? command.narrationVolume : 1.0;
            narrationAudioSource.volume = Mathf.Clamp01((float)narrationVolume);
            if (!narrationPrepared)
            {
                PlaybackAdapter.SetVolume(1);
                return;
            }

            switch (command.audioMixPolicy)
            {
                case AudioMixPolicy.KeepOriginal:
                    PlaybackAdapter.SetVolume(1);
                    break;
                case AudioMixPolicy.MuteVideo:
                    PlaybackAdapter.SetVolume(0);
                    break;
                default:
                    PlaybackAdapter.SetVolume(Math.Max(0, Math.Min(1, command.videoVolume)));
                    break;
            }
        }

        private void SeekAll(double positionSeconds)
        {
            if (videoPrepared) PlaybackAdapter.Seek(positionSeconds);
            if (narrationPrepared) narrationAudioSource.time = ClampAudioPosition(positionSeconds);
        }

        private double CurrentPositionSeconds()
        {
            var videoPosition = videoPrepared ? PlaybackAdapter.CurrentTimeSeconds : 0;
            var narrationPosition = narrationPrepared ? narrationAudioSource.time : 0;
            return Math.Max(videoPosition, narrationPosition);
        }

        private float ClampAudioPosition(double positionSeconds)
        {
            if (narrationAudioSource.clip == null) return 0;
            return Mathf.Clamp((float)Math.Max(0, positionSeconds), 0, Math.Max(0, narrationAudioSource.clip.length - 0.01f));
        }

        private void CancelPlayback(bool releaseAudio)
        {
            playbackPaused = false;
            videoPrepared = false;
            narrationPrepared = false;
            preparedVideoUrl = null;
            preparedNarrationUrl = null;
            if (PlaybackAdapter != null) PlaybackAdapter.Stop();
            if (narrationAudioSource != null) narrationAudioSource.Stop();
            if (releaseAudio) ReleaseNarrationClip();
        }

        private void ReleaseNarrationClip()
        {
            if (narrationAudioSource == null || narrationAudioSource.clip == null) return;
            var oldClip = narrationAudioSource.clip;
            narrationAudioSource.clip = null;
            Destroy(oldClip);
        }

        private static AudioType DetectAudioType(string url)
        {
            var extension = Path.GetExtension(url ?? string.Empty).ToLowerInvariant();
            switch (extension)
            {
                case ".mp3": return AudioType.MPEG;
                case ".wav": return AudioType.WAV;
                case ".ogg": return AudioType.OGGVORBIS;
                case ".aif":
                case ".aiff": return AudioType.AIFF;
                default: return AudioType.UNKNOWN;
            }
        }

        private void Fail(PlaybackCommand command, string error)
        {
            apiClient.Report(command, PlaybackState.Failed, error: error);
            apiClient.EndPlaybackPriority();
            PlaybackActiveChanged?.Invoke(false);
        }
    }
}
