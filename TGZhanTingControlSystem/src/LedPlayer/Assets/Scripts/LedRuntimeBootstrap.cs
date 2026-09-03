using System;
using System.IO;
using System.Reflection;
using UMP;
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
            // AVPro 1.8.9 exposes an unsupported D3D 0x58 texture on Unity 2020,
            // including with hardware decoding disabled. Use the bundled LibVLC
            // backend, which uploads frames into a Unity-owned BGRA32 texture.
            var mediaPlayer = root.AddComponent<UniversalMediaPlayer>();
            mediaPlayer.AutoPlay = false;
            mediaPlayer.Loop = false;
            var adapter = root.AddComponent<UniversalMediaPlaybackAdapter>();
            var narrationAudio = root.AddComponent<AudioSource>();
            narrationAudio.playOnAwake = false;
            narrationAudio.loop = false;
            narrationAudio.spatialBlend = 0f;
            var controller = root.AddComponent<LedPlaybackController>();
            var overlay = root.AddComponent<LedStatusOverlay>();
            mediaPlayer.RenderingObjects = new[] { CreateVideoCanvas(root.transform) };
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

        private static GameObject CreateVideoCanvas(Transform parent)
        {
            var canvasObject = new GameObject("LED Video Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(parent, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 0;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            var displayObject = new GameObject("LibVLC Fullscreen Display", typeof(RectTransform), typeof(RawImage));
            displayObject.transform.SetParent(canvasObject.transform, false);
            var rect = displayObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var display = displayObject.GetComponent<RawImage>();
            display.color = Color.white;
            display.raycastTarget = false;
            return displayObject;
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
