using System;

namespace TG.Control.LedPlayer
{
    public interface IMediaPlaybackAdapter
    {
        bool IsPlaying { get; }
        bool IsFinished { get; }
        double CurrentTimeSeconds { get; }
        void Prepare(string absolutePathOrUrl, Action<bool, string> completed);
        void Play(double positionSeconds);
        void Pause();
        void Resume();
        void Stop();
        void Seek(double positionSeconds);
        void SetVolume(double volume01);
    }

    /// <summary>
    /// Optional diagnostics implemented by video adapters that can prove a decoded frame is
    /// available to Unity. Playback state alone is insufficient because some native decoders
    /// can report Playing while failing to expose a renderable texture.
    /// </summary>
    public interface IVideoPlaybackDiagnostics
    {
        bool HasRenderableVideoFrame { get; }
        string PlaybackBackend { get; }
    }
}
