using System;
using System.Collections;
using TG.Control.UnityContracts;
using UnityEngine;
using UnityEngine.Networking;

namespace TG.Control.Touch
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class NarrationAudioPlayer : MonoBehaviour
    {
        [SerializeField] private TouchApiClient apiClient;
        private AudioSource audioSource;
        private int playbackGeneration;
        private bool paused;
        private string preparedMediaUrl;
        private bool audioPrepared;

        private void Awake() => audioSource = GetComponent<AudioSource>();

        private void OnEnable()
        {
            if (apiClient != null) apiClient.CommandReceived += HandleCommand;
        }

        private void OnDisable()
        {
            if (apiClient != null) apiClient.CommandReceived -= HandleCommand;
        }

        private void HandleCommand(PlaybackCommand command)
        {
            switch (command.action)
            {
                case PlaybackAction.Prepare:
                    paused = false;
                    audioPrepared = false;
                    StartCoroutine(PrepareAudio(command, ++playbackGeneration, false));
                    break;
                case PlaybackAction.PlayNarration:
                    paused = false;
                    if (audioPrepared && string.Equals(preparedMediaUrl, command.mediaUrl, StringComparison.Ordinal))
                        StartCoroutine(PlayPrepared(command, playbackGeneration));
                    else
                        StartCoroutine(PrepareAudio(command, ++playbackGeneration, true));
                    break;
                case PlaybackAction.Pause:
                    paused = true;
                    audioSource.Pause();
                    apiClient.Report(command, PlaybackState.Paused, audioSource.time);
                    break;
                case PlaybackAction.Resume:
                    paused = false;
                    audioSource.UnPause();
                    apiClient.Report(command, PlaybackState.Playing, audioSource.time);
                    break;
                case PlaybackAction.Stop:
                    paused = false;
                    playbackGeneration++;
                    audioPrepared = false;
                    preparedMediaUrl = null;
                    audioSource.Stop();
                    break;
                case PlaybackAction.Skip:
                    paused = false;
                    playbackGeneration++;
                    var skippedAt = audioSource.time;
                    audioSource.Stop();
                    audioPrepared = false;
                    preparedMediaUrl = null;
                    apiClient.Report(command, PlaybackState.Skipped, skippedAt);
                    break;
            }
        }

        private IEnumerator PrepareAudio(PlaybackCommand command, int generation, bool playAfterPrepare)
        {
            apiClient.Report(command, PlaybackState.Received);
            using (var request = UnityWebRequestMultimedia.GetAudioClip(command.mediaUrl, AudioType.UNKNOWN))
            {
                yield return request.SendWebRequest();
                if (generation != playbackGeneration) yield break;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    apiClient.Report(command, PlaybackState.Failed, error: request.error);
                    yield break;
                }

                audioSource.clip = DownloadHandlerAudioClip.GetContent(request);
                preparedMediaUrl = command.mediaUrl;
                audioPrepared = true;
                apiClient.Report(command, PlaybackState.Ready);
                if (playAfterPrepare)
                {
                    yield return PlayPrepared(command, generation);
                }
            }
        }

        private IEnumerator PlayPrepared(PlaybackCommand command, int generation)
        {
            if (!DateTimeOffset.TryParse(command.executeAtUtc, out var executeAt))
            {
                apiClient.Report(command, PlaybackState.Failed, error: "播放计划时间无效。");
                yield break;
            }
            while (DateTimeOffset.UtcNow < executeAt)
            {
                if (generation != playbackGeneration) yield break;
                yield return null;
            }
            var lateBySeconds = Math.Max(0, (DateTimeOffset.UtcNow - executeAt).TotalSeconds);
            audioSource.time = Mathf.Clamp((float)(command.positionSeconds + lateBySeconds), 0, Mathf.Max(0, audioSource.clip.length - 0.01f));
            audioSource.Play();
            if (paused) audioSource.Pause();
            apiClient.Report(command, PlaybackState.Playing, audioSource.time);
            while (generation == playbackGeneration && (audioSource.isPlaying || paused)) yield return null;
            if (generation != playbackGeneration) yield break;
            var duration = audioSource.clip.length;
            audioPrepared = false;
            preparedMediaUrl = null;
            apiClient.Report(command, PlaybackState.Completed, duration);
        }
    }
}
