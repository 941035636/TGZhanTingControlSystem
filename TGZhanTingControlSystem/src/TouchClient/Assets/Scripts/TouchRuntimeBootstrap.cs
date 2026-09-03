using System;
using System.IO;
using System.Reflection;
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
            if (UnityEngine.Object.FindObjectOfType<TouchApiClient>() != null) return;
            var root = new GameObject("TG Touch Runtime");
            root.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(root);
            var api = root.AddComponent<TouchApiClient>();
            ApplySiteConfiguration(api);
            var facade = root.AddComponent<TouchControlFacade>();
            var ui = root.AddComponent<TouchOperatorUi>();
            SetReference(facade, "apiClient", api);
            SetReference(ui, "apiClient", api);
            SetReference(ui, "facade", facade);
            foreach (var camera in UnityEngine.Object.FindObjectsOfType<Camera>())
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
            }
            root.SetActive(true);
        }

        private static void SetReference(UnityEngine.Object target, string fieldName, UnityEngine.Object value) =>
            target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(target, value);

        private static void ApplySiteConfiguration(TouchApiClient api)
        {
            var path = ResolveConfigurationPath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            try
            {
                var config = JsonUtility.FromJson<TouchSiteConfiguration>(File.ReadAllText(path));
                if (config == null) return;
                if (!string.IsNullOrWhiteSpace(config.serverBaseUrl)) SetValue(api, "serverBaseUrl", config.serverBaseUrl);
                if (!string.IsNullOrWhiteSpace(config.clientId)) SetValue(api, "clientId", config.clientId);
                if (!string.IsNullOrWhiteSpace(config.terminalApiKey)) SetValue(api, "terminalApiKey", config.terminalApiKey);
                Debug.Log("TouchClient已加载现场配置：" + path);
            }
            catch (Exception exception)
            {
                Debug.LogError("TouchClient现场配置加载失败：" + exception.Message);
            }
        }

        private static string ResolveConfigurationPath()
        {
            var explicitPath = Environment.GetEnvironmentVariable("TG_TOUCH_CLIENT_CONFIG");
            if (!string.IsNullOrWhiteSpace(explicitPath)) return Path.GetFullPath(explicitPath);
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var sitePath = Path.Combine(programData, "TG Exhibition", "Config", "touch-client.json");
            if (File.Exists(sitePath)) return sitePath;
            return Path.Combine(Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath, "touch-client.json");
        }

        private static void SetValue(UnityEngine.Object target, string fieldName, string value) =>
            target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(target, value);

        [Serializable]
        private sealed class TouchSiteConfiguration
        {
            public string serverBaseUrl;
            public string clientId;
            public string terminalApiKey;
        }
    }
}
