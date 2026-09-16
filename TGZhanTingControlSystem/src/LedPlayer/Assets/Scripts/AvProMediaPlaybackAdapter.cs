using System;
using System.Collections;
using RenderHeads.Media.AVProVideo;
using UnityEngine;

namespace TG.Control.LedPlayer
{
    /// <summary>
    /// AVPro Video 3.x adapter used by the LED player. All public time values are seconds.
    /// </summary>
    public sealed class AvProMediaPlaybackAdapter : MonoBehaviour, IMediaPlaybackAdapter
    {
        [SerializeField] private MediaPlayer mediaPlayer;
        [SerializeField] private float prepareTimeoutSeconds = 30f;
        private int operationGeneration;
        private string lastPlaybackError;

        public bool IsPlaying => mediaPlayer != null && mediaPlayer.Control != null && mediaPlayer.Control.IsPlaying();
        public bool IsFinished => mediaPlayer != null && mediaPlayer.Control != null && mediaPlayer.Control.IsFinished();
        public double CurrentTimeSeconds => mediaPlayer != null && mediaPlayer.Control != null
            ? mediaPlayer.Control.GetCurrentTime()
            : 0.0;

        private void OnEnable()
        {
            if (mediaPlayer != null)
            {
                mediaPlayer.Events.AddListener(HandleMediaPlayerEvent);
            }
        }

        private void OnDisable()
        {
            operationGeneration++;
            if (mediaPlayer != null)
            {
                mediaPlayer.Events.RemoveListener(HandleMediaPlayerEvent);
            }
        }

        public void Prepare(string absolutePathOrUrl, Action<bool, string> completed)
        {
            var generation = ++operationGeneration;
            StartCoroutine(PrepareRoutine(absolutePathOrUrl, generation, completed));
        }

        private IEnumerator PrepareRoutine(string path, int generation, Action<bool, string> completed)
        {
            if (mediaPlayer == null)
            {
                completed(false, "未绑定 AVPro MediaPlayer 组件。");
                yield break;
            }

            lastPlaybackError = null;
            mediaPlayer.AutoStart = false;
            mediaPlayer.Loop = false;
            mediaPlayer.CloseMedia();
            var normalizedPath = NormalizeMediaPath(path);
            if (!mediaPlayer.OpenMedia(MediaPathType.AbsolutePathOrURL, normalizedPath, false))
            {
                completed(false, "AVPro 无法打开媒体：" + normalizedPath);
                yield break;
            }

            var timeoutAt = Time.realtimeSinceStartup + prepareTimeoutSeconds;
            while (Time.realtimeSinceStartup < timeoutAt)
            {
                if (generation != operationGeneration)
                {
                    yield break;
                }

                if (!string.IsNullOrWhiteSpace(lastPlaybackError))
                {
                    mediaPlayer.CloseMedia();
                    completed(false, lastPlaybackError);
                    yield break;
                }

                if (mediaPlayer.Control != null && mediaPlayer.Control.CanPlay())
                {
                    completed(true, null);
                    yield break;
                }

                yield return null;
            }

            mediaPlayer.CloseMedia();
            completed(false, "AVPro 媒体预加载超时。");
        }

        public void Play(double positionSeconds)
        {
            if (mediaPlayer == null || mediaPlayer.Control == null)
            {
                return;
            }

            Seek(positionSeconds);
            mediaPlayer.Control.Play();
        }

        public void Pause()
        {
            if (mediaPlayer != null && mediaPlayer.Control != null)
            {
                mediaPlayer.Control.Pause();
            }
        }

        public void Resume()
        {
            if (mediaPlayer != null && mediaPlayer.Control != null)
            {
                mediaPlayer.Control.Play();
            }
        }

        public void Stop()
        {
            operationGeneration++;
            if (mediaPlayer != null)
            {
                mediaPlayer.CloseMedia();
            }
        }

        public void Seek(double positionSeconds)
        {
            if (mediaPlayer != null && mediaPlayer.Control != null)
            {
                mediaPlayer.Control.Seek(Math.Max(0.0, positionSeconds));
            }
        }

        private void HandleMediaPlayerEvent(MediaPlayer player, MediaPlayerEvent.EventType eventType, ErrorCode errorCode)
        {
            if (eventType == MediaPlayerEvent.EventType.Error)
            {
                lastPlaybackError = "AVPro 播放失败：" + errorCode;
            }
        }

        private static string NormalizeMediaPath(string path)
        {
            if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile)
            {
                return uri.LocalPath;
            }

            return path;
        }
    }
}
