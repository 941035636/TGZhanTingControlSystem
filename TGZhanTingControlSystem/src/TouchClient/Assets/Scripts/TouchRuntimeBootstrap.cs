using UnityEngine;

namespace TG.Control.Touch
{
    public static class TouchRuntimeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateRuntime()
        {
            if (Object.FindObjectOfType<TouchApiClient>() != null) return;
            var root = new GameObject("TG Touch Runtime");
            root.SetActive(false);
            Object.DontDestroyOnLoad(root);
            var api = root.AddComponent<TouchApiClient>();
            var audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            var player = root.AddComponent<NarrationAudioPlayer>();
            var facade = root.AddComponent<TouchControlFacade>();
            var ui = root.AddComponent<TouchOperatorUi>();
            SetReference(player, "apiClient", api);
            SetReference(facade, "apiClient", api);
            SetReference(ui, "apiClient", api);
            SetReference(ui, "facade", facade);
            root.SetActive(true);
        }

        private static void SetReference(Object target, string fieldName, Object value) =>
            target.GetType().GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(target, value);
    }
}
