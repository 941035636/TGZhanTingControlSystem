using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TG.Control.UnityContracts;
using UnityEngine;

namespace TG.Control.LedPlayer
{
    /// <summary>
    /// Minimal compatibility bridge for the previous Android controller.
    /// It intentionally supports one legacy film only and translates commands into the
    /// current LedPlaybackController. It does not create a second media player or protocol.
    /// </summary>
    public sealed class LegacyUdpPlaybackBridge : MonoBehaviour
    {
        [SerializeField] private LedPlaybackController playbackController;
        [SerializeField] private int listenPort = 7002;
        [SerializeField] private string legacyVideoPath = @"D:\video\yiluxiangxin.mp4";
        [SerializeField] private bool enabledByDefault = true;
        private UdpClient socket;
        private float legacyVideoVolume = 1f;
        private bool legacyPlaybackActive;
        private bool dispatchingLegacyCommand;
        private bool listening;

        public bool IsListening => listening;
        public string LegacyVideoPath => legacyVideoPath;

        private void OnEnable()
        {
            if (!enabledByDefault || playbackController == null) return;
            playbackController.PlaybackActiveChanged += HandlePlaybackActiveChanged;
            var configuredPath = Environment.GetEnvironmentVariable("TG_LEGACY_VIDEO_PATH");
            if (!string.IsNullOrWhiteSpace(configuredPath)) legacyVideoPath = configuredPath;
            var configuredPort = Environment.GetEnvironmentVariable("TG_LEGACY_UDP_PORT");
            if (int.TryParse(configuredPort, out var port) && port > 0 && port <= 65535) listenPort = port;
            var disabled = string.Equals(Environment.GetEnvironmentVariable("TG_LEGACY_UDP_ENABLED"), "0", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(Environment.GetEnvironmentVariable("TG_LEGACY_UDP_ENABLED"), "false", StringComparison.OrdinalIgnoreCase);
            if (disabled) return;

            try
            {
                socket = new UdpClient(new IPEndPoint(IPAddress.Any, listenPort));
                socket.Client.ReceiveTimeout = 1;
                listening = true;
                Debug.Log($"[LED Legacy UDP] 已监听 {listenPort}，单视频映射：{legacyVideoPath}");
                StartCoroutine(ReceiveLoop());
            }
            catch (Exception exception)
            {
                listening = false;
                Debug.LogError($"[LED Legacy UDP] 无法监听端口 {listenPort}：{exception.Message}");
            }
        }

        private void OnDisable()
        {
            if (playbackController != null)
                playbackController.PlaybackActiveChanged -= HandlePlaybackActiveChanged;
            legacyPlaybackActive = false;
            listening = false;
            socket?.Close();
            socket = null;
        }

        private IEnumerator ReceiveLoop()
        {
            while (listening && socket != null)
            {
                try
                {
                    while (socket.Available > 0)
                    {
                        var endpoint = new IPEndPoint(IPAddress.Any, 0);
                        var data = socket.Receive(ref endpoint);
                        HandlePayload(Encoding.ASCII.GetString(data));
                    }
                }
                catch (SocketException exception) when (exception.SocketErrorCode == SocketError.WouldBlock ||
                                                        exception.SocketErrorCode == SocketError.TimedOut)
                {
                    // No packet in this frame.
                }
                catch (ObjectDisposedException)
                {
                    yield break;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[LED Legacy UDP] 接收指令失败：" + exception.Message);
                }
                yield return null;
            }
        }

        private void HandlePayload(string payload)
        {
            var command = (payload ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(command)) return;
            Debug.Log("[LED Legacy UDP] 收到：" + command);
            if (IsLegacyStartCommand(command))
            {
                PlayLegacyVideo();
                return;
            }

            if (IsLegacyResumeCommand(command))
            {
                if (legacyPlaybackActive)
                {
                    Send(PlaybackAction.Resume);
                    Debug.Log("[LED Legacy UDP] VideoPlay继续当前视频。");
                }
                else
                {
                    Debug.Log("[LED Legacy UDP] VideoPlay首次启动旧现场视频。");
                    PlayLegacyVideo();
                }
                return;
            }

            switch (command)
            {
                case "VideoPause":
                    if (legacyPlaybackActive)
                    {
                        Send(PlaybackAction.Pause);
                        Debug.Log("[LED Legacy UDP] VideoPause暂停当前视频。");
                    }
                    else
                        Debug.LogWarning("[LED Legacy UDP] 尚未启动旧现场视频，忽略VideoPause。");
                    break;
                case "CloseVideo":
                    legacyPlaybackActive = false;
                    Send(PlaybackAction.Stop);
                    break;
                case "VoiceStart":
                    legacyVideoVolume = 1f;
                    Send(PlaybackAction.SetVideoVolume, legacyVideoVolume);
                    break;
                case "VoiceStop":
                    legacyVideoVolume = 0f;
                    Send(PlaybackAction.SetVideoVolume, legacyVideoVolume);
                    break;
                case "VoiceAdd":
                    legacyVideoVolume = Mathf.Clamp01(legacyVideoVolume + 0.1f);
                    Send(PlaybackAction.SetVideoVolume, legacyVideoVolume);
                    break;
                case "VoiceSub":
                    legacyVideoVolume = Mathf.Clamp01(legacyVideoVolume - 0.1f);
                    Send(PlaybackAction.SetVideoVolume, legacyVideoVolume);
                    break;
                default:
                    Debug.LogWarning("[LED Legacy UDP] 忽略不支持的旧指令：" + command);
                    break;
            }
        }

        private void PlayLegacyVideo()
        {
            var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(legacyVideoPath));
            if (!File.Exists(path))
            {
                Debug.LogError("[LED Legacy UDP] 找不到旧现场视频：" + path);
                return;
            }

            legacyPlaybackActive = true;
            Send(PlaybackAction.PlayVideo, 0, new Uri(path).AbsoluteUri);
        }

        private static bool IsLegacyStartCommand(string command)
        {
            return command.Equals("Play:yiluxiangxin", StringComparison.OrdinalIgnoreCase) ||
                   command.Equals("Play:yiluxiangxin.mp4", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLegacyResumeCommand(string command)
        {
            return command.Equals("VideoPlay|yiluxiangxin", StringComparison.OrdinalIgnoreCase) ||
                   command.Equals("VideoPlay|yiluxiangxin.mp4", StringComparison.OrdinalIgnoreCase);
        }

        private void Send(PlaybackAction action, double volume = 0, string mediaUrl = null)
        {
            if (playbackController == null) return;
            var now = DateTimeOffset.UtcNow;
            dispatchingLegacyCommand = true;
            try
            {
                playbackController.HandleLegacyCommand(new PlaybackCommand
                {
                    commandId = "legacy-udp-" + Guid.NewGuid().ToString("N"),
                    sessionId = "legacy-udp-yiluxiangxin",
                    moduleId = "legacy-yiluxiangxin",
                    nodeId = "legacy-yiluxiangxin",
                    action = action,
                    mediaUrl = mediaUrl,
                    executeAtUtc = now.ToString("O"),
                    contentVersion = 0,
                    videoVolume = volume,
                    audioMixPolicy = AudioMixPolicy.KeepOriginal
                });
            }
            finally
            {
                dispatchingLegacyCommand = false;
            }
        }

        private void HandlePlaybackActiveChanged(bool active)
        {
            if (dispatchingLegacyCommand) return;

            // A completed/failed legacy playback, or a formal Server session taking over the
            // single player, invalidates the legacy resume state. The next VideoPlay command
            // must then start the mapped film instead of resuming unrelated media.
            legacyPlaybackActive = false;
        }
    }
}
