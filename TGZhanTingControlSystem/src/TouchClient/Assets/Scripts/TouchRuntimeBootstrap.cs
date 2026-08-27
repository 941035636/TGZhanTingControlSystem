using UnityEngine;

namespace TG.Control.Touch
{
    public static class TouchRuntimeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateRuntime()
        {
            Application.targetFrameRate = 60;
            Screen.SetResolution(1920, 1080, FullScreenMode.FullScreenWindow);
            if (Object.FindObjectOfType<TouchApiClient>() != null) return;
            var root = new GameObject("TG Touch Runtime");
            root.SetActive(false);
            Object.DontDestroyOnLoad(root);
            var api = root.AddComponent<TouchApiClient>();
            var facade = root.AddComponent<TouchControlFacade>();
            var ui = root.AddComponent<TouchOperatorUi>();
            SetReference(facade, "apiClient", api);
            SetReference(ui, "apiClient", api);
            SetReference(ui, "facade", facade);
            foreach (var camera in Object.FindObjectsOfType<Camera>())
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
            }
            root.SetActive(true);
        }

        private static void SetReference(Object target, string fieldName, Object value) =>
            target.GetType().GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(target, value);
    }
}
