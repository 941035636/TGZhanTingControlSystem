using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;

namespace TG.Control.Editor
{
    public static class WindowsPlayerBuilder
    {
        private const string BootstrapScenePath = "Assets/Scenes/RuntimeBootstrap.unity";

        public static void Build()
        {
            EnsureBootstrapScene();
            var outputPath = Environment.GetEnvironmentVariable("TG_WINDOWS_BUILD_OUTPUT");
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new InvalidOperationException("TG_WINDOWS_BUILD_OUTPUT is required.");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { BootstrapScenePath },
                locationPathName = outputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"Windows player build failed: {report.summary.result}");
        }

        private static void EnsureBootstrapScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            if (!File.Exists(BootstrapScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, BootstrapScenePath);
            }

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(BootstrapScenePath, true) };
        }
    }
}
