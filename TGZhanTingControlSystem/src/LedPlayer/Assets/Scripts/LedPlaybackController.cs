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
        [SerializeField] private UniversalMediaPlaybackAdapter playbackAdapter;
        [SerializeField] private AudioSource narrationAudioSource;

        private readonly LedContentCache contentCache = LedContentCache.Shared;
        private int playbackGeneration;
        private string preparedVideoUrl;
        private string preparedNarrationUrl;
        private bool videoPrepared;
        private bool narrationPrepared;
        private bool playbackPaused;
        public event Action<bool> PlaybackActiveChanged;

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
            if (apiClient == null || playbackAdapter == null || narrationAudioSource == null)
            {
                Debug.LogError("LED声画播放组件未完成初始化，无法执行播放指令。");
                return;
            }

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
                    if (videoPrepared) playbackAdapter.Pause();
                    if (narrationPrepared) narrationAudioSource.Pause();
                    apiClient.Report(command, PlaybackState.Paused, CurrentPositionSeconds());
                    break;
                case PlaybackAction.Resume:
                    playbackPaused = false;
                    if (videoPrepared) playbackAdapter.Resume();
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

            if (hasVideo)
            {
                var prepared = false;
                string prepareError = null;
                playbackAdapter.Prepare(localVideoUrl, (ok, error) => { prepared = ok; prepareError = error; });
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
            ApplyMix(command);
            if (videoPrepared) playbackAdapter.Play(startPosition);
            if (narrationPrepared)
            {
                narrationAudioSource.time = ClampAudioPosition(startPosition);
                narrationAudioSource.Play();
            }
            if (playbackPaused)
            {
                if (videoPrepared) playbackAdapter.Pause();
                if (narrationPrepared) narrationAudioSource.Pause();
            }
            apiClient.Report(command, playbackPaused ? PlaybackState.Paused : PlaybackState.Playing, startPosition);

            // Allow Unity and the native video backend to enter playing state before testing completion.
            yield return null;
            while (generation == playbackGeneration)
            {
                if (playbackPaused)
                {
                    yield return null;
                    continue;
                }

                var videoFinished = !videoPrepared || playbackAdapter.IsFinished;
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

        private void ApplyMix(PlaybackCommand command)
        {
            var narrationVolume = command.narrationVolume > 0 ? command.narrationVolume : 1.0;
            narrationAudioSource.volume = Mathf.Clamp01((float)narrationVolume);
            if (!narrationPrepared)
            {
                playbackAdapter.SetVolume(1);
                return;
            }

            switch (command.audioMixPolicy)
            {
                case AudioMixPolicy.KeepOriginal:
                    playbackAdapter.SetVolume(1);
                    break;
                case AudioMixPolicy.MuteVideo:
                    playbackAdapter.SetVolume(0);
                    break;
                default:
                    playbackAdapter.SetVolume(command.videoVolume > 0 ? command.videoVolume : 0.25);
                    break;
            }
        }

        private void SeekAll(double positionSeconds)
        {
            if (videoPrepared) playbackAdapter.Seek(positionSeconds);
            if (narrationPrepared) narrationAudioSource.time = ClampAudioPosition(positionSeconds);
        }

        private double CurrentPositionSeconds()
        {
            var videoPosition = videoPrepared ? playbackAdapter.CurrentTimeSeconds : 0;
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
            if (playbackAdapter != null) playbackAdapter.Stop();
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
