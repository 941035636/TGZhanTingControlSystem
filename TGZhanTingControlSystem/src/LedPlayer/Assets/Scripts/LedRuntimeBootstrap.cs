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
            Application.runInBackground = true;
            Screen.SetResolution(1920, 1080, FullScreenMode.FullScreenWindow);
            if (Object.FindObjectOfType<LedApiClient>() != null) return;
            var root = new GameObject("TG LED Runtime");
            root.SetActive(false);
            Object.DontDestroyOnLoad(root);
            var api = root.AddComponent<LedApiClient>();
            var mediaPlayer = root.AddComponent<MediaPlayer>();
            mediaPlayer.AutoStart = false;
            mediaPlayer.Loop = false;
            var adapter = root.AddComponent<AvProMediaPlaybackAdapter>();
            var controller = root.AddComponent<LedPlaybackController>();
            var overlay = root.AddComponent<LedStatusOverlay>();
            CreateVideoCanvas(root.transform, mediaPlayer);
            SetReference(adapter, "mediaPlayer", mediaPlayer);
            SetReference(controller, "apiClient", api);
            SetReference(controller, "playbackAdapterSource", adapter);
            SetReference(overlay, "apiClient", api);
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

            var backgroundObject = new GameObject("Video Background", typeof(RectTransform), typeof(RawImage));
            backgroundObject.transform.SetParent(canvasObject.transform, false);
            var backgroundRect = backgroundObject.GetComponent<RectTransform>();
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            backgroundObject.GetComponent<RawImage>().color = Color.black;

            var displayObject = new GameObject("AVPro Fullscreen Display", typeof(RectTransform), typeof(DisplayUGUI));
            displayObject.transform.SetParent(canvasObject.transform, false);
            var rect = displayObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var display = displayObject.GetComponent<DisplayUGUI>();
            display.Player = mediaPlayer;
            display.ScaleMode = ScaleMode.ScaleToFit;
            display.color = Color.white;
        }

        private static void SetReference(Object target, string fieldName, Object value) =>
            target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(target, value);
    }
}
