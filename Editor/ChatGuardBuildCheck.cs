#nullable enable
using System;
using System.Collections.Generic;
using ChatGuard.Unity;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;

namespace ChatGuard.Editor
{
    /// <summary>
    /// Refuses to build a player that would ship a <c>cg_live_</c> server key, and any release (non-development) build
    /// that would ship a <c>cg_test_</c> test key. Before the build starts it inspects every <see cref="ChatGuardConfig"/>
    /// asset the build includes: those under a Resources folder, and those the build's scenes, any Resources asset or the
    /// Preloaded Assets in Player Settings reference, directly or through other assets (a Chat Guard Hook's config, for
    /// example). Dedicated Server builds may hold a server key, because that is where one belongs; test keys are for the
    /// Editor and development builds only, since one organization's test keys share a small daily allowance. Publishable
    /// (<c>cg_pub_</c>) keys always pass.
    /// Keys handed to <c>ChatGuardSdk.Configure</c> from code, and configs that only AssetBundles or Addressables load,
    /// cannot be inspected here; the runtime still logs a warning when a server key runs in a player build.
    /// </summary>
    public sealed class ChatGuardBuildCheck : IPreprocessBuildWithReport
    {
        // The scene list of the player build being prepared, recorded by ChatGuardBuildScenes (null when none was).
        private static string[]? s_buildScenes;

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            string[] scenes = s_buildScenes ?? EnabledBuildSettingsScenes();
            s_buildScenes = null;
            bool dedicatedServer = IsDedicatedServerBuild(report);
            bool developmentBuild = (report.summary.options & BuildOptions.Development) != 0;
            string? refusal = FindRefusal(scenes.Length > 0 ? scenes : OpenScenes(), dedicatedServer, developmentBuild);
            if (refusal != null)
            {
                throw new BuildFailedException(refusal);
            }
        }

        /// <summary>Records the scenes of the player build about to start; <see cref="OnPreprocessBuild"/> reads them.</summary>
        internal static void RecordBuildScenes(string[]? scenes)
        {
            s_buildScenes = scenes ?? Array.Empty<string>();
        }

        /// <summary>
        /// The first refusal (see <see cref="Refusal"/>) among the config assets a player build of
        /// <paramref name="scenePaths"/> would include, or null when it may ship all of them.
        /// </summary>
        internal static string? FindRefusal(IReadOnlyList<string> scenePaths, bool dedicatedServer, bool developmentBuild)
        {
            foreach (string path in ConfigsInBuild(scenePaths))
            {
                ChatGuardConfig? config = AssetDatabase.LoadAssetAtPath<ChatGuardConfig>(path);
                string? refusal = config == null ? null : Refusal(path, config.apiKey, dedicatedServer, developmentBuild);
                if (refusal != null)
                {
                    return refusal;
                }
            }

            return null;
        }

        /// <summary>
        /// Paths of the <see cref="ChatGuardConfig"/> assets a player build of <paramref name="scenePaths"/> includes:
        /// those under a Resources folder, and those the scenes, any asset under a Resources folder or the Preloaded Assets
        /// (Player Settings, which every player build loads) depend on, directly or through other assets.
        /// </summary>
        internal static List<string> ConfigsInBuild(IReadOnlyList<string> scenePaths)
        {
            var configs = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(ChatGuardConfig)))
            {
                configs.Add(AssetDatabase.GUIDToAssetPath(guid));
            }

            if (configs.Count == 0)
            {
                return configs;
            }

            var roots = new List<string>();
            foreach (string scene in scenePaths)
            {
                if (!string.IsNullOrEmpty(scene))
                {
                    roots.Add(scene);
                }
            }

            foreach (string asset in AssetDatabase.GetAllAssetPaths())
            {
                if (asset.IndexOf("Resources", StringComparison.Ordinal) >= 0 && IsUnderResources(asset) && !AssetDatabase.IsValidFolder(asset))
                {
                    roots.Add(asset);
                }
            }

            foreach (UnityEngine.Object? preloaded in PlayerSettings.GetPreloadedAssets())
            {
                string path = preloaded == null ? string.Empty : AssetDatabase.GetAssetPath(preloaded);
                if (!string.IsNullOrEmpty(path))
                {
                    roots.Add(path);
                }
            }

            var included = new HashSet<string>(roots.Count > 0 ? AssetDatabase.GetDependencies(roots.ToArray(), true) : Array.Empty<string>(), StringComparer.Ordinal);
            configs.RemoveAll(path => !IsUnderResources(path) && !included.Contains(path));
            return configs;
        }

        /// <summary>Why a build must not ship the config at <paramref name="path"/> with this key, or null when it may.</summary>
        internal static string? Refusal(string path, string? apiKey, bool dedicatedServer, bool developmentBuild)
        {
            string key = apiKey ?? string.Empty;
            if (!dedicatedServer && key.StartsWith("cg_live_", StringComparison.Ordinal))
            {
                return "Chat Guard: " + path + " holds a cg_live_ server key and would ship it inside this player build. " +
                    "Put a cg_pub_ publishable key in client builds, or moderate on your server or relay " +
                    "(package README, \"Where the key lives\"). Dedicated Server builds are exempt from this check.";
            }

            if (!developmentBuild && key.StartsWith("cg_test_", StringComparison.Ordinal))
            {
                return "Chat Guard: " + path + " holds a cg_test_ test key, and this is a release build. Test keys are for the " +
                    "Editor and development builds: all of an organization's test keys share 1,000 requests a day. Put a cg_pub_ " +
                    "publishable key in client builds or a cg_live_ key in a Dedicated Server build (package README, \"Where the " +
                    "key lives\"), or tick Development Build to keep testing.";
            }

            return null;
        }

        private static bool IsDedicatedServerBuild(BuildReport report)
        {
#if UNITY_2021_2_OR_NEWER
            return report.summary.platformGroup == BuildTargetGroup.Standalone
                && EditorUserBuildSettings.standaloneBuildSubtarget == StandaloneBuildSubtarget.Server;
#else
            return false;
#endif
        }

        /// <summary>The scenes ticked in Build Settings, which the Build Settings window builds.</summary>
        private static string[] EnabledBuildSettingsScenes()
        {
            var scenes = new List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled && !string.IsNullOrEmpty(scene.path))
                {
                    scenes.Add(scene.path);
                }
            }

            return scenes.ToArray();
        }

        /// <summary>The open scenes that are saved: what Unity builds when a build names no scene.</summary>
        private static string[] OpenScenes()
        {
            var scenes = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                string path = SceneManager.GetSceneAt(i).path;
                if (!string.IsNullOrEmpty(path))
                {
                    scenes.Add(path);
                }
            }

            return scenes.ToArray();
        }

        /// <summary>True when any folder on the path is called Resources (Unity includes those assets in builds).</summary>
        internal static bool IsUnderResources(string assetPath)
        {
            string[] parts = assetPath.Replace('\\', '/').Split('/');
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (string.Equals(parts[i], "Resources", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }

#if UNITY_2021_2_OR_NEWER
    /// <summary>
    /// Hands <see cref="ChatGuardBuildCheck"/> the scene list of each player build, which a build script may choose
    /// freely; Unity calls this before the build's preprocess step, for the Build Settings window and
    /// <c>BuildPipeline.BuildPlayer</c> alike. Without it the check reads the scenes ticked in Build Settings.
    /// </summary>
    internal sealed class ChatGuardBuildScenes : BuildPlayerProcessor
    {
        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            ChatGuardBuildCheck.RecordBuildScenes(buildPlayerContext.BuildPlayerOptions.scenes);
        }
    }
#endif
}
