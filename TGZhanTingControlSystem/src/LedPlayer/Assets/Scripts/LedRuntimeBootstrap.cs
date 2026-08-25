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
            if (Object.FindObjectOfType<LedApiClient>() != null) return;
            var root = new GameObject("TG LED Runtime");
            root.SetActive(false);
            Object.DontDestroyOnLoad(root);
            var api = root.AddComponent<LedApiClient>();
            var mediaPlayer = root.AddComponent<UniversalMediaPlayer>();
            mediaPlayer.AutoPlay = false;
            mediaPlayer.Loop = false;
            var adapter = root.AddComponent<UniversalMediaPlaybackAdapter>();
            var controller = root.AddComponent<LedPlaybackController>();
            var overlay = root.AddComponent<LedStatusOverlay>();
            var videoOutput = CreateVideoCanvas(root.transform);
            mediaPlayer.RenderingObjects = new[] { videoOutput };
            SetReference(adapter, "mediaPlayer", mediaPlayer);
            SetReference(controller, "apiClient", api);
            SetReference(controller, "playbackAdapter", adapter);
            SetReference(overlay, "apiClient", api);
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
            displayObject.GetComponent<RawImage>().color = Color.white;
            return displayObject;
        }

        private static void SetReference(Object target, string fieldName, Object value) =>
            target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(target, value);
    }
}
