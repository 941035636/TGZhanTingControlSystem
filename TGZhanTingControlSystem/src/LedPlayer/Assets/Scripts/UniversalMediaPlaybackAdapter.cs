using System;
using System.Collections;
using UMP;
using UnityEngine;

namespace TG.Control.LedPlayer
{
    /// <summary>
    /// LibVLC/Universal Media Player adapter. Public time values are seconds;
    /// UMP exposes its time values in milliseconds.
    /// </summary>
    public sealed class UniversalMediaPlaybackAdapter : MonoBehaviour, IMediaPlaybackAdapter
    {
        [SerializeField] private UniversalMediaPlayer mediaPlayer;
        [SerializeField] private float prepareTimeoutSeconds = 30f;

        private Action<bool, string> prepareCompleted;
        private Coroutine prepareTimeout;
        private bool isFinished;

        public bool IsPlaying => mediaPlayer != null && mediaPlayer.IsPlaying;
        public bool IsFinished => isFinished;
        public double CurrentTimeSeconds => mediaPlayer != null && mediaPlayer.Time > 0
            ? mediaPlayer.Time / 1000.0
            : 0.0;

        private void OnEnable()
        {
            if (mediaPlayer == null) return;
            mediaPlayer.AddPreparedEvent(HandlePrepared);
            mediaPlayer.AddEndReachedEvent(HandleEndReached);
            mediaPlayer.AddEncounteredErrorEvent(HandleError);
        }

        private void OnDisable()
        {
            if (mediaPlayer != null)
            {
                mediaPlayer.RemovePreparedEvent(HandlePrepared);
                mediaPlayer.RemoveEndReachedEvent(HandleEndReached);
                mediaPlayer.RemoveEncounteredErrorEvent(HandleError);
            }

            CancelPrepareTimeout();
            CompletePrepare(false, "LibVLC 播放组件已停用。");
        }

        public void Prepare(string absolutePathOrUrl, Action<bool, string> completed)
        {
            if (mediaPlayer == null)
            {
                completed?.Invoke(false, "未绑定 LibVLC UniversalMediaPlayer 组件。");
                return;
            }

            CancelPrepareTimeout();
            CompletePrepare(false, "新的媒体加载请求替换了上一请求。");
            isFinished = false;
            prepareCompleted = completed;

            mediaPlayer.Stop(false);
            mediaPlayer.Path = absolutePathOrUrl;
            mediaPlayer.Prepare();
            prepareTimeout = StartCoroutine(PrepareTimeoutRoutine());
        }

        public void Play(double positionSeconds)
        {
            if (mediaPlayer == null) return;
            isFinished = false;
            Seek(positionSeconds);
            mediaPlayer.Play();
        }

        public void Pause()
        {
            if (mediaPlayer != null) mediaPlayer.Pause();
        }

        public void Resume()
        {
            if (mediaPlayer == null) return;
            isFinished = false;
            mediaPlayer.Play();
        }

        public void Stop()
        {
            CancelPrepareTimeout();
            CompletePrepare(false, "媒体加载已停止。");
            isFinished = false;
            if (mediaPlayer != null) mediaPlayer.Stop();
        }

        public void Seek(double positionSeconds)
        {
            if (mediaPlayer == null) return;
            mediaPlayer.Time = (long)Math.Round(Math.Max(0.0, positionSeconds) * 1000.0);
        }

        public void SetVolume(double volume01)
        {
            if (mediaPlayer != null) mediaPlayer.Volume = Mathf.Clamp01((float)volume01) * 100f;
        }

        private IEnumerator PrepareTimeoutRoutine()
        {
            yield return new WaitForSecondsRealtime(prepareTimeoutSeconds);
            prepareTimeout = null;
            if (prepareCompleted == null) yield break;

            if (mediaPlayer != null) mediaPlayer.Stop(false);
            CompletePrepare(false, "LibVLC 媒体预加载超时。");
        }

        private void HandlePrepared(int width, int height)
        {
            CancelPrepareTimeout();
            Debug.Log($"[LED Player] LibVLC media prepared: {width}x{height}");
            CompletePrepare(true, null);
        }

        private void HandleEndReached()
        {
            isFinished = true;
        }

        private void HandleError()
        {
            CancelPrepareTimeout();
            CompletePrepare(false, "LibVLC 无法打开或解码该媒体文件。");
        }

        private void CancelPrepareTimeout()
        {
            if (prepareTimeout == null) return;
            StopCoroutine(prepareTimeout);
            prepareTimeout = null;
        }

        private void CompletePrepare(bool success, string error)
        {
            var callback = prepareCompleted;
            prepareCompleted = null;
            callback?.Invoke(success, error);
        }
    }
}
