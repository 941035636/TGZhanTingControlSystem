using System;
using System.IO;
using System.Reflection;
using RenderHeads.Media.AVProVideo;
using UnityEngine;
using UnityEngine.UI;

namespace TG.Control.LedPlayer
{
    public static class LedRuntimeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateRuntime()
        {
            Application.targetFrameRate = 60;
            Screen.SetResolution(1920, 1080, FullScreenMode.FullScreenWindow);
            if (UnityEngine.Object.FindObjectOfType<LedApiClient>() != null) return;
            var root = new GameObject("TG LED Runtime");
            root.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(root);
            var api = root.AddComponent<LedApiClient>();
            ApplySiteConfiguration(api);
            var mediaPlayer = root.AddComponent<MediaPlayer>();
            mediaPlayer.m_AutoOpen = false;
            mediaPlayer.m_AutoStart = false;
            mediaPlayer.m_Loop = false;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            // AVPro 1.8's Media Foundation hardware path can return a DXGI surface that
            // Unity 2020 cannot import on older professional NVIDIA cards. The native player
            // then reports Playing while DisplayUGUI remains black (Unsupported D3D format
            // 0x58). The software path still streams the local cached file and exposes a
            // normal RGBA texture, which is the reliable 1080p delivery path for this client.
            mediaPlayer.PlatformOptionsWindows.videoApi = Windows.VideoApi.MediaFoundation;
            mediaPlayer.PlatformOptionsWindows.useHardwareDecoding = false;
#endif
            var adapter = root.AddComponent<AvProMediaPlaybackAdapter>();
            var narrationAudio = root.AddComponent<AudioSource>();
            narrationAudio.playOnAwake = false;
            narrationAudio.loop = false;
            narrationAudio.spatialBlend = 0f;
            var controller = root.AddComponent<LedPlaybackController>();
            var overlay = root.AddComponent<LedStatusOverlay>();
            CreateVideoCanvas(root.transform, mediaPlayer);
            SetReference(adapter, "mediaPlayer", mediaPlayer);
            SetReference(controller, "apiClient", api);
            SetReference(controller, "playbackAdapterComponent", adapter);
            SetReference(controller, "narrationAudioSource", narrationAudio);
            SetReference(overlay, "apiClient", api);
            SetReference(overlay, "playbackController", controller);
            foreach (var camera in UnityEngine.Object.FindObjectsOfType<Camera>())
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
            }
            root.SetActive(true);
        }

        private static void CreateVideoCanvas(Transform parent, MediaPlayer mediaPlayer)
        {
            var canvasObject = new GameObject("LED Video Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(parent, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 0;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            var displayObject = new GameObject("AVPro Fullscreen Display", typeof(RectTransform), typeof(DisplayUGUI));
            displayObject.transform.SetParent(canvasObject.transform, false);
            var rect = displayObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var display = displayObject.GetComponent<DisplayUGUI>();
            display._mediaPlayer = mediaPlayer;
            display.color = Color.white;
            display.raycastTarget = false;
        }

        private static void SetReference(UnityEngine.Object target, string fieldName, UnityEngine.Object value) =>
            target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(target, value);

        private static void ApplySiteConfiguration(LedApiClient api)
        {
            var path = ResolveConfigurationPath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            try
            {
                var config = JsonUtility.FromJson<LedSiteConfiguration>(File.ReadAllText(path));
                if (config == null) return;
                if (!string.IsNullOrWhiteSpace(config.serverBaseUrl)) SetValue(api, "serverBaseUrl", config.serverBaseUrl);
                if (!string.IsNullOrWhiteSpace(config.clientId)) SetValue(api, "clientId", config.clientId);
                if (!string.IsNullOrWhiteSpace(config.terminalApiKey)) SetValue(api, "terminalApiKey", config.terminalApiKey);
                if (!string.IsNullOrWhiteSpace(config.cacheDirectory)) LedContentCache.Shared.ConfigureDirectory(config.cacheDirectory);
                Debug.Log("LedPlayer已加载现场配置：" + path);
            }
            catch (Exception exception)
            {
                Debug.LogError("LedPlayer现场配置加载失败：" + exception.Message);
            }
        }

        private static string ResolveConfigurationPath()
        {
            var explicitPath = Environment.GetEnvironmentVariable("TG_LED_PLAYER_CONFIG");
            if (!string.IsNullOrWhiteSpace(explicitPath)) return Path.GetFullPath(explicitPath);
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var sitePath = Path.Combine(programData, "TG Exhibition", "Config", "led-player.json");
            if (File.Exists(sitePath)) return sitePath;
            return Path.Combine(Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath, "led-player.json");
        }

        private static void SetValue(UnityEngine.Object target, string fieldName, string value) =>
            target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(target, value);

        [Serializable]
        private sealed class LedSiteConfiguration
        {
            public string serverBaseUrl;
            public string clientId;
            public string terminalApiKey;
            public string cacheDirectory;
        }
    }
}
