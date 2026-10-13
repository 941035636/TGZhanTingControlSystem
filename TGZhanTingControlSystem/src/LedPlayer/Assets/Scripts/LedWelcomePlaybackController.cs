using System;
using System.Collections;
using System.IO;
using TG.Control.UnityContracts;
using UnityEngine;
using UnityEngine.Networking;

namespace TG.Control.LedPlayer
{
    /// <summary>Plays only the independent welcome asset through the same LED-host audio output.</summary>
    public sealed class LedWelcomePlaybackController : MonoBehaviour
    {
        [SerializeField] private LedApiClient apiClient;
        [SerializeField] private AudioSource audioSource;
        private int generation;
        private WelcomePlaybackCommand prepared;
        private AudioClip clip;
        private string playStartingRequestId;

        private void OnEnable()
        {
            if (apiClient != null)
            {
                apiClient.WelcomeCommandReceived += HandleWelcome;
                apiClient.CommandReceived += HandleFormal;
            }
        }
        private void OnDisable()
        {
            if (apiClient != null) { apiClient.WelcomeCommandReceived -= HandleWelcome; apiClient.CommandReceived -= HandleFormal; }
            Cancel(true);
        }
        private void HandleFormal(PlaybackCommand command)
        {
            if (command.action == PlaybackAction.Prepare || command.action == PlaybackAction.PlayVideo ||
                command.action == PlaybackAction.PlayNarration) Cancel(true);
        }
        private void HandleWelcome(WelcomePlaybackCommand command)
        {
            if (apiClient == null) return;
            if (audioSource == null)
            {
                apiClient.ReportWelcome(command, WelcomePlaybackState.Failed, error: "LED音频输出组件未初始化");
                return;
            }
            if (command.action == WelcomePlaybackAction.Stop) { Cancel(true); return; }
            if (command.action == WelcomePlaybackAction.Prepare)
            {
                // Play delivery is retried by the server. Do not throw away a decoded
                // clip when a duplicate Prepare arrives during that retry window.
                if (prepared != null && prepared.requestId == command.requestId && clip != null)
                {
                    Debug.Log($"Welcome duplicate Prepare acknowledged request={command.requestId}");
                    apiClient.ReportWelcome(command, WelcomePlaybackState.Preparing, progress: 1);
                    return;
                }
                Cancel(true);
                StartCoroutine(Prepare(command, ++generation, false));
            }
            else if (command.action == WelcomePlaybackAction.Play && prepared != null && prepared.requestId == command.requestId)
            {
                if (audioSource.isPlaying)
                {
                    var duration = Math.Max(.01f, audioSource.clip == null ? 0 : audioSource.clip.length);
                    apiClient.ReportWelcome(command, WelcomePlaybackState.Playing, audioSource.time,
                        progress: audioSource.time / duration);
                }
                else if (!string.Equals(playStartingRequestId, command.requestId, StringComparison.Ordinal))
                {
                    playStartingRequestId = command.requestId;
                    StartCoroutine(Play(command, generation));
                }
            }
            else if (command.action == WelcomePlaybackAction.Play)
            {
                // Play includes the complete asset descriptor. Recover when this process did
                // not receive Prepare (restart, duplicate client ID, or interrupted polling).
                Debug.LogWarning("Welcome Play arrived without matching prepared state; recovering from command asset.");
                Cancel(true);
                StartCoroutine(Prepare(command, ++generation, true));
            }
        }
        private IEnumerator Prepare(WelcomePlaybackCommand command, int activeGeneration, bool playAfterPrepare)
        {
            var asset = command.audio;
            if (asset == null || string.IsNullOrWhiteSpace(asset.url) || string.IsNullOrWhiteSpace(asset.sha256) || asset.sizeBytes <= 0)
            { apiClient.ReportWelcome(command, WelcomePlaybackState.Failed, error: "欢迎音频资产完整性信息缺失"); yield break; }
            Debug.Log($"Welcome prepare request={command.requestId}, url={asset.url}, bytes={asset.sizeBytes}, sha256={asset.sha256.Substring(0, Math.Min(12, asset.sha256.Length))}...");
            string local = null, error = null;
            yield return LedContentCache.Shared.Resolve(apiClient.NormalizeUrl(asset.url), value => local = value, value => error = value,
                asset.sizeBytes, expectedSha256: asset.sha256);
            if (activeGeneration != generation) yield break;
            if (!string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(local))
            {
                Debug.LogError($"Welcome cache validation failed request={command.requestId}: {error}");
                apiClient.ReportWelcome(command, WelcomePlaybackState.Failed, error: "欢迎音频缓存校验失败：" + error); yield break;
            }
            Debug.Log($"Welcome cache ready request={command.requestId}, local={local}");
            using (var request = UnityWebRequestMultimedia.GetAudioClip(local, DetectAudioType(asset.url)))
            {
                yield return request.SendWebRequest();
                if (activeGeneration != generation) yield break;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"Welcome decode failed request={command.requestId}: {request.error}");
                    apiClient.ReportWelcome(command, WelcomePlaybackState.Failed, error: "欢迎音频解码失败：" + request.error); yield break;
                }
                clip = DownloadHandlerAudioClip.GetContent(request);
            }
            if (clip == null) { apiClient.ReportWelcome(command, WelcomePlaybackState.Failed, error: "欢迎音频不可播放"); yield break; }
            Debug.Log($"Welcome decoded request={command.requestId}, duration={clip.length:F3}s, channels={clip.channels}, frequency={clip.frequency}");
            audioSource.clip = clip; prepared = command;
            if (playAfterPrepare)
            {
                playStartingRequestId = command.requestId;
                yield return Play(command, activeGeneration);
            }
            else StartCoroutine(ReportPreparedUntilPlay(command, activeGeneration));
        }

        private IEnumerator ReportPreparedUntilPlay(WelcomePlaybackCommand command, int activeGeneration)
        {
            // The status POST and the following Play long-poll are separate network
            // requests. Repeat the prepared acknowledgement until Play arrives so a
            // transient deployment-network failure cannot leave Touch stuck forever.
            while (activeGeneration == generation && prepared != null &&
                   prepared.requestId == command.requestId && !audioSource.isPlaying)
            {
                apiClient.ReportWelcome(command, WelcomePlaybackState.Preparing, progress: 1);
                Debug.Log($"Welcome prepared acknowledgement request={command.requestId}");
                yield return new WaitForSecondsRealtime(.75f);
            }
        }
        private IEnumerator Play(WelcomePlaybackCommand command, int activeGeneration)
        {
            if (!DateTimeOffset.TryParse(command.executeAtUtc, out var executeAt)) { apiClient.ReportWelcome(command, WelcomePlaybackState.Failed, error: "欢迎播放时间无效"); yield break; }
            while (DateTimeOffset.UtcNow < executeAt) { if (activeGeneration != generation) yield break; yield return null; }
            audioSource.time = 0; audioSource.Play();
            yield return null;
            if (activeGeneration != generation) yield break;
            if (!audioSource.isPlaying)
            {
                playStartingRequestId = null;
                Debug.LogError($"Welcome AudioSource failed to start request={command.requestId}");
                apiClient.ReportWelcome(command, WelcomePlaybackState.Failed, error: "欢迎音频输出启动失败，请检查播放端音频设备");
                yield break;
            }
            apiClient.ReportWelcome(command, WelcomePlaybackState.Playing, 0, progress: 0);
            Debug.Log($"Welcome playback started request={command.requestId}, duration={audioSource.clip.length:F3}s");
            while (activeGeneration == generation && audioSource.isPlaying)
            {
                var duration = Math.Max(.01f, audioSource.clip == null ? 0 : audioSource.clip.length);
                apiClient.ReportWelcome(command, WelcomePlaybackState.Playing, audioSource.time, progress: audioSource.time / duration);
                yield return new WaitForSecondsRealtime(.25f);
            }
            if (activeGeneration == generation) { apiClient.ReportWelcome(command, WelcomePlaybackState.Completed, audioSource.time, progress: 1); Cancel(false); }
        }
        private void Cancel(bool release) { generation++; playStartingRequestId = null; if (audioSource != null) audioSource.Stop(); prepared = null; if (release && clip != null) { if (audioSource != null && audioSource.clip == clip) audioSource.clip = null; Destroy(clip); clip = null; } }
        private static AudioType DetectAudioType(string url)
        {
            var clean = (url ?? string.Empty).Split('?', '#')[0];
            switch (Path.GetExtension(clean).ToLowerInvariant())
            {
                case ".wav": return AudioType.WAV;
                case ".mp3": return AudioType.MPEG;
                default: return AudioType.UNKNOWN;
            }
        }
    }
}
