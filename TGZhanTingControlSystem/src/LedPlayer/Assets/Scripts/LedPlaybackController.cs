using System;
using System.Collections;
using TG.Control.UnityContracts;
using UnityEngine;

namespace TG.Control.LedPlayer
{
    public sealed class LedPlaybackController : MonoBehaviour
    {
        [SerializeField] private LedApiClient apiClient;
        [SerializeField] private UniversalMediaPlaybackAdapter playbackAdapter;
        private readonly LedContentCache contentCache = new LedContentCache();
        private int playbackGeneration;
        private string preparedMediaUrl;
        private bool mediaPrepared;

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
            if (apiClient == null || playbackAdapter == null)
            {
                Debug.LogError("LED播放组件未完成初始化，无法执行播放指令。");
                return;
            }

            switch (command.action)
            {
                case PlaybackAction.Prepare:
                    mediaPrepared = false;
                    StartCoroutine(PrepareMedia(command, ++playbackGeneration, false));
                    break;
                case PlaybackAction.PlayVideo:
                    if (mediaPrepared && string.Equals(preparedMediaUrl, command.mediaUrl, StringComparison.Ordinal))
                        StartCoroutine(PlayPrepared(command, playbackGeneration));
                    else
                        StartCoroutine(PrepareMedia(command, ++playbackGeneration, true));
                    break;
                case PlaybackAction.Pause:
                    playbackAdapter.Pause();
                    apiClient.Report(command, PlaybackState.Paused, playbackAdapter.CurrentTimeSeconds);
                    break;
                case PlaybackAction.Resume:
                    playbackAdapter.Resume();
                    apiClient.Report(command, PlaybackState.Playing, playbackAdapter.CurrentTimeSeconds);
                    break;
                case PlaybackAction.Stop:
                    playbackGeneration++;
                    mediaPrepared = false;
                    preparedMediaUrl = null;
                    playbackAdapter.Stop();
                    break;
                case PlaybackAction.Seek:
                    playbackAdapter.Seek(command.positionSeconds);
                    break;
                case PlaybackAction.Skip:
                    playbackGeneration++;
                    var skippedAt = playbackAdapter.CurrentTimeSeconds;
                    mediaPrepared = false;
                    preparedMediaUrl = null;
                    playbackAdapter.Stop();
                    apiClient.Report(command, PlaybackState.Skipped, skippedAt);
                    break;
            }
        }

        private IEnumerator PrepareMedia(PlaybackCommand command, int generation, bool playAfterPrepare)
        {
            apiClient.Report(command, PlaybackState.Received);
            string localUrl = null;
            string cacheError = null;
            yield return contentCache.Resolve(command.mediaUrl, value => localUrl = value, value => cacheError = value);
            if (generation != playbackGeneration) yield break;
            if (cacheError != null)
            {
                apiClient.Report(command, PlaybackState.Failed, error: cacheError);
                yield break;
            }

            var prepared = false;
            string prepareError = null;
            playbackAdapter.Prepare(localUrl, (ok, error) => { prepared = ok; prepareError = error; });
            while (!prepared && prepareError == null)
            {
                if (generation != playbackGeneration) yield break;
                yield return null;
            }
            if (!prepared)
            {
                apiClient.Report(command, PlaybackState.Failed, error: prepareError);
                yield break;
            }

            preparedMediaUrl = command.mediaUrl;
            mediaPrepared = true;
            apiClient.Report(command, PlaybackState.Ready);
            if (playAfterPrepare)
            {
                yield return PlayPrepared(command, generation);
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
            var startPosition = command.positionSeconds + lateBySeconds;
            playbackAdapter.Play(startPosition);
            apiClient.Report(command, PlaybackState.Playing, startPosition);
            while (generation == playbackGeneration && !playbackAdapter.IsFinished) yield return null;
            if (generation != playbackGeneration) yield break;
            mediaPrepared = false;
            preparedMediaUrl = null;
            apiClient.Report(command, PlaybackState.Completed, playbackAdapter.CurrentTimeSeconds);
        }
    }
}
