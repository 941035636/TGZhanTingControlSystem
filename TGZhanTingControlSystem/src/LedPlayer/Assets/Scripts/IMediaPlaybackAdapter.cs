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
    }
}
