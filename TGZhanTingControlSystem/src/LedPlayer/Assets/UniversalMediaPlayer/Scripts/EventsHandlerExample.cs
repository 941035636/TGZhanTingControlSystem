using UnityEngine;

namespace UMP
{
    public class EventsHandlerExample : MonoBehaviour
    {
        public UniversalMediaPlayer _mediaPlayer;

        void Start()
        {
            if (_mediaPlayer != null)
            {
                _mediaPlayer.AddPlayingEvent(OnPlayerPlaying);
                _mediaPlayer.AddTimeChangedEvent(OnPlayerTimeChanged);
                _mediaPlayer.AddPositionChangedEvent(OnPlayerPositionChanged);
                _mediaPlayer.AddSnapshotTakenEvent(OnPlayerSnapshotTaken);
            }
        }
        public void Play()
        {
            _mediaPlayer.Play();
        }

        public void OnPlayerOpening()
        {
             Log.Debug("OnPlayerOpening");
        }

        public void OnPlayerBuffering()
        {
             Log.Debug("OnPlayerBuffering");
        }

        public void OnPlayerPlaying()
        {
             Log.Debug("OnPlayerPlaying");
        }

        public void OnPlayerPaused()
        {
             Log.Debug("OnPlayerPaused");
        }

        public void OnPlayerStopped()
        {
             Log.Debug("OnPlayerStopped");
        }

        public void OnPlayerEndReached()
        {
             Log.Debug("OnPlayerEndReached");
        }

        public void OnPlayerEncounteredError()
        {
             Log.Debug("OnPlayerEncounteredError");
        }

        public void OnPlayerTimeChanged(long time)
        {
             Log.Debug("OnPlayerTimeChanged: " + time);
        }

        public void OnPlayerPositionChanged(float position)
        {
             Log.Debug("OnPlayerPositionChanged: " + position);
        }

        public void OnPlayerSnapshotTaken(string path)
        {
             Log.Debug("OnPlayerSnapshotTaken: " + path);
        }
    }
}