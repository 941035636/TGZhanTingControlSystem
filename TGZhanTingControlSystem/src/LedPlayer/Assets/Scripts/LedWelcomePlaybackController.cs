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
            if (apiClient == null || audioSource == null) return;
            if (command.action == WelcomePlaybackAction.Stop) { Cancel(true); return; }
            if (command.action == WelcomePlaybackAction.Prepare) { Cancel(true); StartCoroutine(Prepare(command, ++generation)); }
            else if (command.action == WelcomePlaybackAction.Play && prepared != null && prepared.requestId == command.requestId)
                StartCoroutine(Play(command, generation));
            else apiClient.ReportWelcome(command, WelcomePlaybackState.Failed, error: "欢迎音频尚未准备完成");
        }
        private IEnumerator Prepare(WelcomePlaybackCommand command, int activeGeneration)
        {
            var asset = command.audio;
            if (asset == null || string.IsNullOrWhiteSpace(asset.url) || string.IsNullOrWhiteSpace(asset.sha256) || asset.sizeBytes <= 0)
            { apiClient.ReportWelcome(command, WelcomePlaybackState.Failed, error: "欢迎音频资产完整性信息缺失"); yield break; }
            string local = null, error = null;
            yield return LedContentCache.Shared.Resolve(apiClient.NormalizeUrl(asset.url), value => local = value, value => error = value,
                asset.sizeBytes, expectedSha256: asset.sha256);
            if (activeGeneration != generation) yield break;
            if (!string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(local))
            { apiClient.ReportWelcome(command, WelcomePlaybackState.Failed, error: "欢迎音频缓存校验失败：" + error); yield break; }
            using (var request = UnityWebRequestMultimedia.GetAudioClip(local, DetectAudioType(asset.url)))
            {
                yield return request.SendWebRequest();
                if (activeGeneration != generation) yield break;
                if (request.result != UnityWebRequest.Result.Success)
                { apiClient.ReportWelcome(command, WelcomePlaybackState.Failed, error: "欢迎音频解码失败：" + request.error); yield break; }
                clip = DownloadHandlerAudioClip.GetContent(request);
            }
            if (clip == null) { apiClient.ReportWelcome(command, WelcomePlaybackState.Failed, error: "欢迎音频不可播放"); yield break; }
            audioSource.clip = clip; prepared = command;
            apiClient.ReportWelcome(command, WelcomePlaybackState.Preparing, progress: 1);
        }
        private IEnumerator Play(WelcomePlaybackCommand command, int activeGeneration)
        {
            if (!DateTimeOffset.TryParse(command.executeAtUtc, out var executeAt)) { apiClient.ReportWelcome(command, WelcomePlaybackState.Failed, error: "欢迎播放时间无效"); yield break; }
            while (DateTimeOffset.UtcNow < executeAt) { if (activeGeneration != generation) yield break; yield return null; }
            audioSource.time = 0; audioSource.Play();
            apiClient.ReportWelcome(command, WelcomePlaybackState.Playing, 0, progress: 0);
            while (activeGeneration == generation && audioSource.isPlaying)
            {
                var duration = Math.Max(.01f, audioSource.clip == null ? 0 : audioSource.clip.length);
                apiClient.ReportWelcome(command, WelcomePlaybackState.Playing, audioSource.time, progress: audioSource.time / duration);
                yield return new WaitForSecondsRealtime(.25f);
            }
            if (activeGeneration == generation) { apiClient.ReportWelcome(command, WelcomePlaybackState.Completed, audioSource.time, progress: 1); Cancel(false); }
        }
        private void Cancel(bool release) { generation++; if (audioSource != null) audioSource.Stop(); prepared = null; if (release && clip != null) { if (audioSource != null && audioSource.clip == clip) audioSource.clip = null; Destroy(clip); clip = null; } }
        private static AudioType DetectAudioType(string url) { switch (Path.GetExtension(url ?? string.Empty).ToLowerInvariant()) { case ".wav": return AudioType.WAV; case ".mp3": return AudioType.MPEG; case ".ogg": return AudioType.OGGVORBIS; default: return AudioType.UNKNOWN; } }
    }
}
