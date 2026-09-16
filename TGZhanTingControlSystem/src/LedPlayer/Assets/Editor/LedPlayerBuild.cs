using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;

namespace TG.Control.LedPlayer.Editor
{
    public static class LedPlayerBuild
    {
        private const string RuntimeScenePath = "Assets/Scenes/LedRuntime.unity";

        public static void BuildWindows64()
        {
            EnsureRuntimeScene();

            var configuredOutput = Environment.GetEnvironmentVariable("TG_LED_BUILD_OUTPUT");
            var outputDirectory = string.IsNullOrWhiteSpace(configuredOutput)
                ? Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..", "Builds", "LedPlayer"))
                : Path.GetFullPath(configuredOutput);
            Directory.CreateDirectory(outputDirectory);

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { RuntimeScenePath },
                locationPathName = Path.Combine(outputDirectory, "TG.Control.LedPlayer.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });

            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"LedPlayer Windows x64 build failed: {report.summary.result}, " +
                    $"errors={report.summary.totalErrors}, warnings={report.summary.totalWarnings}");
            }
        }

        private static void EnsureRuntimeScene()
        {
            if (!File.Exists(RuntimeScenePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RuntimeScenePath));
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, RuntimeScenePath);
            }

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(RuntimeScenePath, true)
            };
        }
    }
}
