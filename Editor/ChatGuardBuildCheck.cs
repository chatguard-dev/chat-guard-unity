#nullable enable
using System;
using ChatGuard.Unity;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace ChatGuard.Editor
{
    /// <summary>
    /// Refuses to build a player that would ship a <c>cg_live_</c> server key, and any release (non-development) build
    /// that would ship a <c>cg_test_</c> test key. Every <see cref="ChatGuardConfig"/> asset that sits under a Resources
    /// folder (and is therefore included in the build) is inspected before the build starts. Dedicated Server builds may
    /// hold a server key, because that is where one belongs; test keys are for the Editor and development builds only,
    /// since one organization's test keys share a small daily allowance. Publishable (<c>cg_pub_</c>) keys always pass.
    /// Keys handed to <c>ChatGuardSdk.Configure</c> from code cannot be inspected here; the runtime still logs a warning
    /// when a server key runs in a player build.
    /// </summary>
    public sealed class ChatGuardBuildCheck : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            bool dedicatedServer = IsDedicatedServerBuild(report);
            bool developmentBuild = (report.summary.options & BuildOptions.Development) != 0;
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(ChatGuardConfig)))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!IsUnderResources(path))
                {
                    continue;
                }

                ChatGuardConfig? config = AssetDatabase.LoadAssetAtPath<ChatGuardConfig>(path);
                string? refusal = config == null ? null : Refusal(path, config.apiKey, dedicatedServer, developmentBuild);
                if (refusal != null)
                {
                    throw new BuildFailedException(refusal);
                }
            }
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
}
