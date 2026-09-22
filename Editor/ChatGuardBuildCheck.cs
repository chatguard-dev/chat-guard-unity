#nullable enable
using System;
using ChatGuard.Unity;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace ChatGuard.Editor
{
    /// <summary>
    /// Refuses to build a player that would ship a <c>cg_live_</c> server key. Every <see cref="ChatGuardConfig"/>
    /// asset that sits under a Resources folder (and is therefore included in the build) is inspected before the
    /// build starts. Dedicated Server builds are exempt, because that is where a server key belongs; publishable
    /// (<c>cg_pub_</c>) and test (<c>cg_test_</c>) keys pass. Keys handed to <c>ChatGuardSdk.Configure</c> from code
    /// cannot be inspected here; the runtime still logs a warning when a server key runs in a player build.
    /// </summary>
    public sealed class ChatGuardBuildCheck : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (IsDedicatedServerBuild(report))
            {
                return;
            }

            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(ChatGuardConfig)))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!IsUnderResources(path))
                {
                    continue;
                }

                ChatGuardConfig? config = AssetDatabase.LoadAssetAtPath<ChatGuardConfig>(path);
                if (config != null && (config.apiKey ?? string.Empty).StartsWith("cg_live_", StringComparison.Ordinal))
                {
                    throw new BuildFailedException(
                        "Chat Guard: " + path + " holds a cg_live_ server key and would ship it inside this player build. " +
                        "Put a cg_pub_ publishable key in client builds, or moderate on your server or relay " +
                        "(package README, \"Where the key lives\"). Dedicated Server builds are exempt from this check.");
                }
            }
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
