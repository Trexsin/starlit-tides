// GameBuilder: builds the finished game from the Starlit Tides menu.
//   Starlit Tides > 9. Build Windows (.exe)   -> Builds/Windows/StarlitTides.exe
//   Starlit Tides > Build macOS (.app)        -> Builds/macOS/StarlitTides.app
// The Windows build needs the "Windows Build Support (Mono)" module
// (Unity Hub > Installs > 6000.5.10f1 > Manage > Add modules).

#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace StarlitTides.EditorTools
{
    public static class GameBuilder
    {
        [MenuItem("Starlit Tides/9. Build Windows (.exe)", priority = 25)]
        public static void BuildWindows()
        {
            Build(BuildTarget.StandaloneWindows64, "Builds/Windows/StarlitTides.exe");
        }

        [MenuItem("Starlit Tides/Build macOS (.app)", priority = 26)]
        public static void BuildMac()
        {
            Build(BuildTarget.StandaloneOSX, "Builds/macOS/StarlitTides.app");
        }

        static void Build(BuildTarget target, string path)
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target))
            {
                string message = "The build module for " + target + " isn't installed.\n\n" +
                                 "Unity Hub > Installs > 6000.5.10f1 > Manage > Add modules > " +
                                 (target == BuildTarget.StandaloneWindows64 ? "Windows Build Support (Mono)" : "Mac Build Support");
                if (Application.isBatchMode) Debug.LogError("[Starlit Tides] " + message);
                else EditorUtility.DisplayDialog("Starlit Tides", message, "OK");
                return;
            }

            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("[Starlit Tides] No scenes in File > Build Profiles. Add StarlitBay first.");
                return;
            }

            // Player settings for the final game.
            PlayerSettings.companyName = "Starlit Tides Team";
            PlayerSettings.productName = "Starlit Tides";
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.runInBackground = false;

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            BuildPlayerOptions options = new BuildPlayerOptions();
            options.scenes = scenes;
            options.locationPathName = path;
            options.target = target;
            options.targetGroup = BuildTargetGroup.Standalone;
            options.options = BuildOptions.None;

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log("[Starlit Tides] Build succeeded: " + path + " (" + (summary.totalSize / (1024 * 1024)) + " MB, " + summary.totalTime.TotalSeconds.ToString("0") + " s)");
                if (!Application.isBatchMode) EditorUtility.RevealInFinder(path);
            }
            else
            {
                Debug.LogError("[Starlit Tides] Build " + summary.result + " with " + summary.totalErrors + " errors. See the Console.");
            }
        }
    }
}
#endif
