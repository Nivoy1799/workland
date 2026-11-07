using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Worktown.Game.Editor.Build
{
    /// <summary>
    /// Builds the Windows client and the headless Linux dedicated server (spec 003).
    /// Menu: Worktown/Build. Command line:
    /// Unity -batchmode -quit -projectPath game -executeMethod Worktown.Game.Editor.Build.WorktownBuild.BuildLinuxServer
    /// Output goes to game/Builds (gitignored).
    /// </summary>
    public static class WorktownBuild
    {
        private const string SteamAppIdFile = "steam_appid.txt";

        [MenuItem("Worktown/Build/Windows Client")]
        public static void BuildWindowsClient()
        {
            Build(new BuildPlayerOptions
            {
                scenes = Scenes(),
                locationPathName = "Builds/Windows/Worktown.exe",
                target = BuildTarget.StandaloneWindows64,
                subtarget = (int)StandaloneBuildSubtarget.Player,
                options = BuildOptions.Development
            });
        }

        [MenuItem("Worktown/Build/Linux Dedicated Server")]
        public static void BuildLinuxServer()
        {
            Build(new BuildPlayerOptions
            {
                scenes = Scenes(),
                locationPathName = "Builds/LinuxServer/WorktownServer.x86_64",
                target = BuildTarget.StandaloneLinux64,
                subtarget = (int)StandaloneBuildSubtarget.Server,
                options = BuildOptions.Development
            });
        }

        [MenuItem("Worktown/Build/All")]
        public static void BuildAll()
        {
            BuildLinuxServer();
            BuildWindowsClient();
        }

        private static string[] Scenes()
        {
            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
                throw new InvalidOperationException("No scenes in build settings. Run Worktown/Build Graybox Scene first.");
            return scenes;
        }

        private static void Build(BuildPlayerOptions options)
        {
            BuildTarget previousTarget = EditorUserBuildSettings.activeBuildTarget;
            BuildTargetGroup previousGroup = BuildPipeline.GetBuildTargetGroup(previousTarget);
            int previousSubtarget = (int)EditorUserBuildSettings.standaloneBuildSubtarget;

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            // Building can switch the active target; switch back so the Editor keeps its setup.
            EditorUserBuildSettings.standaloneBuildSubtarget = (StandaloneBuildSubtarget)previousSubtarget;
            if (EditorUserBuildSettings.activeBuildTarget != previousTarget)
                EditorUserBuildSettings.SwitchActiveBuildTarget(previousGroup, previousTarget);

            if (summary.result != BuildResult.Succeeded)
            {
                string message = "[Build] " + options.target + " failed: " + summary.result + ", " + summary.totalErrors + " errors";
                Debug.LogError(message);
                if (Application.isBatchMode)
                    EditorApplication.Exit(1);
                return;
            }

            // Spec 003: the 480 test App ID only ships with development builds; release builds
            // get the real App ID from build configuration.
            if ((options.options & BuildOptions.Development) != 0)
                CopySteamAppId(options.locationPathName);
            Debug.Log("[Build] " + options.target + " succeeded: " + options.locationPathName
                + " (" + summary.totalSize / (1024 * 1024) + " MB, " + summary.totalTime.TotalSeconds.ToString("0") + " s)");
        }

        /// <summary>Development builds read the App ID (480 for now) from a file next to the binary.</summary>
        private static void CopySteamAppId(string locationPathName)
        {
            if (!File.Exists(SteamAppIdFile)) return;
            string directory = Path.GetDirectoryName(Path.GetFullPath(locationPathName));
            File.Copy(SteamAppIdFile, Path.Combine(directory, SteamAppIdFile), overwrite: true);
        }
    }
}
