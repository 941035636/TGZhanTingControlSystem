using System;
using System.Collections;
using RenderHeads.Media.AVProVideo;
using UnityEngine;

namespace TG.Control.LedPlayer
{
    /// <summary>
    /// AVPro Video 1.x adapter used by the LED player. All public time values are seconds,
    /// while AVPro 1.x exposes playback positions in milliseconds.
    /// </summary>
    public sealed class AvProMediaPlaybackAdapter : MonoBehaviour, IMediaPlaybackAdapter
    {
        [SerializeField] private MediaPlayer mediaPlayer;
        [SerializeField] private float prepareTimeoutSeconds = 30f;

        public bool IsPlaying => mediaPlayer != null && mediaPlayer.Control != null && mediaPlayer.Control.IsPlaying();
        public bool IsFinished => mediaPlayer != null && mediaPlayer.Control != null && mediaPlayer.Control.IsFinished();
        public double CurrentTimeSeconds => mediaPlayer != null && mediaPlayer.Control != null
            ? mediaPlayer.Control.GetCurrentTimeMs() / 1000.0
            : 0.0;

        public void Prepare(string absolutePathOrUrl, Action<bool, string> completed)
        {
            StartCoroutine(PrepareRoutine(absolutePathOrUrl, completed));
        }

        private IEnumerator PrepareRoutine(string path, Action<bool, string> completed)
        {
            if (mediaPlayer == null)
            {
                completed(false, "未绑定 AVPro MediaPlayer 组件。");
                yield break;
            }

            mediaPlayer.CloseVideo();
            if (!mediaPlayer.OpenVideoFromFile(MediaPlayer.FileLocation.AbsolutePathOrURL, path, false))
            {
                completed(false, "AVPro 无法打开媒体：" + path);
                yield break;
            }

            var timeoutAt = Time.realtimeSinceStartup + prepareTimeoutSeconds;
            while (Time.realtimeSinceStartup < timeoutAt)
            {
                if (mediaPlayer.Control != null && mediaPlayer.Control.CanPlay())
                {
                    completed(true, null);
                    yield break;
                }

                yield return null;
            }

            mediaPlayer.CloseVideo();
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
            if (mediaPlayer != null)
            {
                mediaPlayer.CloseVideo();
            }
        }

        public void Seek(double positionSeconds)
        {
            if (mediaPlayer != null && mediaPlayer.Control != null)
            {
                mediaPlayer.Control.Seek((float)Math.Max(0.0, positionSeconds * 1000.0));
            }
        }

        public void SetVolume(double volume01)
        {
            if (mediaPlayer != null && mediaPlayer.Control != null)
            {
                mediaPlayer.Control.SetVolume(Mathf.Clamp01((float)volume01));
            }
        }
    }
}
