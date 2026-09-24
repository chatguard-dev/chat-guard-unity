#nullable enable
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;

namespace ChatGuard.Editor
{
    /// <summary>
    /// Fails a player build that would ship a <c>cg_live_</c> server key, unless it is a Dedicated Server build. Also
    /// fails a release (non-development) build that would ship a <c>cg_test_</c> key. Checks every
    /// <see cref="ChatGuardConfig"/> asset the build includes. Publishable (<c>cg_pub_</c>) and empty keys always pass.
    /// </summary>
    /// <remarks>
    /// Test keys stay out of release builds because players would soon use up the organization's daily test allowance
    /// (see <see cref="ChatGuardSettings.ApiKey"/>). Before the build starts, the check reads every config under a
    /// Resources folder, plus every config that the scenes, Resources assets or Preloaded Assets reference, directly
    /// or through other assets (a <see cref="ChatGuardUnityHook"/>'s config, for example).
    /// It cannot see a key set in code (such as with <c>ChatGuardSdk.Configure</c>), or a config that only
    /// AssetBundles or Addressables load. For those, the runtime logs a warning when a <c>cg_live_</c> key runs in a
    /// player build that is not a Dedicated Server build.
    /// </remarks>
    public sealed class ChatGuardBuildCheck : IPreprocessBuildWithReport
    {
        // Set by ChatGuardBuildScenes; null when it did not run. Cleared on use, so no later build reads a stale list.
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

        /// <summary>
        /// Records the scenes of the player build about to start, for <see cref="OnPreprocessBuild"/>. Null or empty
        /// means Unity builds the open scenes, so the check reads those.
        /// </summary>
        internal static void RecordBuildScenes(string[]? scenes)
        {
            s_buildScenes = scenes ?? Array.Empty<string>();
        }

        /// <summary>
        /// The first <see cref="Refusal"/> message among the config assets a player build of
        /// <paramref name="scenePaths"/> would ship, or null when there is none.
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
        /// Paths of the <see cref="ChatGuardConfig"/> assets a player build of <paramref name="scenePaths"/> ships:
        /// those under a Resources folder, plus those that the scenes, Resources assets or Preloaded Assets (which
        /// every player build loads) depend on, directly or through other assets.
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

        /// <summary>
        /// Why a build must not ship the config at <paramref name="path"/> with this key, or null when it may.
        /// </summary>
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

        /// <summary>
        /// True when any folder on the path is named Resources, whose assets Unity ships in player builds. It also
        /// matches Resources inside an Editor folder, which Unity leaves out, so a refused key there still fails the
        /// build.
        /// </summary>
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
    /// Passes each player build's scene list to <see cref="ChatGuardBuildCheck"/>, since a build script can build
    /// scenes other than those ticked in Build Settings. Unity calls it before the preprocess step, from the Build
    /// Settings window and <c>BuildPipeline.BuildPlayer</c> alike. Without it, the check reads the ticked scenes.
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
