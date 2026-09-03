using System;
using System.Collections;
using System.IO;
using RenderHeads.Media.AVProVideo;
using UnityEngine;

namespace TG.Control.LedPlayer
{
    /// <summary>
    /// AVPro Video 1.x adapter used by the LED player. All public time values are seconds,
    /// while AVPro 1.x exposes playback positions in milliseconds.
    /// </summary>
    public sealed class AvProMediaPlaybackAdapter : MonoBehaviour, IMediaPlaybackAdapter, IVideoPlaybackDiagnostics
    {
        [SerializeField] private MediaPlayer mediaPlayer;
        [SerializeField] private float prepareTimeoutSeconds = 30f;
        private ErrorCode prepareError = ErrorCode.None;

        public bool IsPlaying => mediaPlayer != null && mediaPlayer.Control != null && mediaPlayer.Control.IsPlaying();
        public bool IsFinished => mediaPlayer != null && mediaPlayer.Control != null && mediaPlayer.Control.IsFinished();
        public double CurrentTimeSeconds => mediaPlayer != null && mediaPlayer.Control != null
            ? mediaPlayer.Control.GetCurrentTimeMs() / 1000.0
            : 0.0;
        public bool HasRenderableVideoFrame => mediaPlayer != null &&
                                               mediaPlayer.TextureProducer != null &&
                                               mediaPlayer.TextureProducer.GetTextureFrameCount() > 0 &&
                                               mediaPlayer.TextureProducer.GetTexture() != null;
        public string PlaybackBackend => mediaPlayer != null && mediaPlayer.Info != null
            ? mediaPlayer.Info.GetPlayerDescription()
            : string.Empty;

        private void OnEnable()
        {
            if (mediaPlayer != null) mediaPlayer.Events.AddListener(OnMediaPlayerEvent);
        }

        private void OnDisable()
        {
            if (mediaPlayer != null) mediaPlayer.Events.RemoveListener(OnMediaPlayerEvent);
        }

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
            prepareError = ErrorCode.None;
            path = NormalizeMediaPath(path);
            if (!mediaPlayer.OpenVideoFromFile(MediaPlayer.FileLocation.AbsolutePathOrURL, path, false))
            {
                completed(false, "AVPro 无法打开媒体：" + path);
                yield break;
            }

            var timeoutAt = Time.realtimeSinceStartup + prepareTimeoutSeconds;
            while (Time.realtimeSinceStartup < timeoutAt)
            {
                if (prepareError != ErrorCode.None)
                {
                    var error = prepareError;
                    mediaPlayer.CloseVideo();
                    completed(false, "AVPro 媒体加载失败：" + error);
                    yield break;
                }

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

        private void OnMediaPlayerEvent(MediaPlayer source, MediaPlayerEvent.EventType eventType, ErrorCode errorCode)
        {
            if (source != mediaPlayer) return;
            if (eventType == MediaPlayerEvent.EventType.Error)
                prepareError = errorCode == ErrorCode.None ? ErrorCode.LoadFailed : errorCode;
            else if (eventType == MediaPlayerEvent.EventType.FirstFrameReady)
                Debug.Log($"[LED Player] AVPro first renderable frame ready via {PlaybackBackend}.");
        }

        private static string NormalizeMediaPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsFile)
                return Path.GetFullPath(uri.LocalPath);
            return value;
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
