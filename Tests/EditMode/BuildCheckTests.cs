#nullable enable
using System;
using System.Collections.Generic;
using ChatGuard.Editor;
using ChatGuard.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ChatGuard.Tests
{
    public class BuildCheckTests
    {
        private const string Path = "Assets/Resources/ChatGuardConfig.asset";
        private static readonly string Live = "cg_live_" + new string('x', 32);
        private static readonly string Test = "cg_test_" + new string('x', 32);
        private static readonly string Pub = "cg_pub_" + new string('x', 32);

        [Test]
        public void ServerKey_RefusedInPlayers_AllowedInDedicatedServers()
        {
            Assert.That(ChatGuardBuildCheck.Refusal(Path, Live, dedicatedServer: false, developmentBuild: false), Does.Contain("cg_live_ server key"));
            Assert.That(ChatGuardBuildCheck.Refusal(Path, Live, dedicatedServer: false, developmentBuild: true), Does.Contain("cg_live_ server key"));
            Assert.That(ChatGuardBuildCheck.Refusal(Path, Live, dedicatedServer: true, developmentBuild: false), Is.Null);
        }

        [Test]
        public void TestKey_RefusedInReleaseBuilds_AllowedInDevelopmentBuilds()
        {
            Assert.That(ChatGuardBuildCheck.Refusal(Path, Test, dedicatedServer: false, developmentBuild: false), Does.Contain("release build"));
            Assert.That(ChatGuardBuildCheck.Refusal(Path, Test, dedicatedServer: true, developmentBuild: false), Does.Contain("release build"));
            Assert.That(ChatGuardBuildCheck.Refusal(Path, Test, dedicatedServer: false, developmentBuild: true), Is.Null);
            Assert.That(ChatGuardBuildCheck.Refusal(Path, Test, dedicatedServer: true, developmentBuild: true), Is.Null);
        }

        [Test]
        public void PublishableAndEmptyKeys_AlwaysPass()
        {
            Assert.That(ChatGuardBuildCheck.Refusal(Path, Pub, dedicatedServer: false, developmentBuild: false), Is.Null);
            Assert.That(ChatGuardBuildCheck.Refusal(Path, string.Empty, dedicatedServer: false, developmentBuild: false), Is.Null);
            Assert.That(ChatGuardBuildCheck.Refusal(Path, null, dedicatedServer: false, developmentBuild: false), Is.Null);
        }

        /// <summary>
        /// A config outside Resources ships when a scene in the build (here a Chat Guard Hook in it) or an asset under
        /// Resources (here a prefab) references it, so the check inspects it then, and only then. The assets live in a
        /// temporary folder that is deleted afterwards.
        /// </summary>
        [Test]
        public void ConfigsUsedByBuildScenesOrResourcesAssets_AreInspected()
        {
            string folder = "Assets/ChatGuardBuildCheckTest" + Guid.NewGuid().ToString("N").Substring(0, 8);
            try
            {
                AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
                string configPath = folder + "/SceneConfig.asset";
                CreateConfig(configPath, Live);
                string unusedPath = folder + "/UnusedConfig.asset";
                CreateConfig(unusedPath, Live);

                // Unity cannot open a scene next to an untitled one (the usual state of a batch-mode test run), so then
                // the new scene replaces it and an empty untitled scene is put back afterwards.
                Scene active = SceneManager.GetActiveScene();
                bool untitled = SceneManager.sceneCount == 1 && string.IsNullOrEmpty(active.path);
                if (untitled && active.isDirty)
                {
                    Assert.Ignore("Save or discard the untitled scene first: this test opens a scene of its own.");
                }

                string scenePath = folder + "/Chat.unity";
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, untitled ? NewSceneMode.Single : NewSceneMode.Additive);
                var host = new GameObject("Chat");
                SceneManager.MoveGameObjectToScene(host, scene);
                host.AddComponent<ChatGuardUnityHook>().config = AssetDatabase.LoadAssetAtPath<ChatGuardConfig>(configPath);
                Assert.That(EditorSceneManager.SaveScene(scene, scenePath), Is.True);
                if (untitled)
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                }
                else
                {
                    EditorSceneManager.CloseScene(scene, true);
                }

                List<string> withScene = ChatGuardBuildCheck.ConfigsInBuild(new[] { scenePath });
                Assert.That(withScene, Does.Contain(configPath));
                Assert.That(withScene, Does.Not.Contain(unusedPath), "a config nothing in the build uses does not ship");
                Assert.That(ChatGuardBuildCheck.ConfigsInBuild(Array.Empty<string>()), Does.Not.Contain(configPath), "the scene is not in this build");

                string? refusal = ChatGuardBuildCheck.FindRefusal(new[] { scenePath }, dedicatedServer: false, developmentBuild: false);
                Assert.That(refusal, Does.Contain(configPath).And.Contain("cg_live_ server key"));
                Assert.That(ChatGuardBuildCheck.FindRefusal(new[] { scenePath }, dedicatedServer: true, developmentBuild: false), Is.Null);

                // A prefab under Resources that references a config elsewhere ships that config with every build.
                AssetDatabase.CreateFolder(folder, "Resources");
                string prefabConfigPath = folder + "/PrefabConfig.asset";
                CreateConfig(prefabConfigPath, Test);
                var prefabSource = new GameObject("ChatPrefab");
                prefabSource.AddComponent<ChatGuardUnityHook>().config = AssetDatabase.LoadAssetAtPath<ChatGuardConfig>(prefabConfigPath);
                PrefabUtility.SaveAsPrefabAsset(prefabSource, folder + "/Resources/ChatPrefab.prefab");
                UnityEngine.Object.DestroyImmediate(prefabSource);

                Assert.That(ChatGuardBuildCheck.ConfigsInBuild(Array.Empty<string>()), Does.Contain(prefabConfigPath));
                Assert.That(ChatGuardBuildCheck.FindRefusal(Array.Empty<string>(), dedicatedServer: false, developmentBuild: true), Is.Null, "a test key passes in a development build");
                Assert.That(ChatGuardBuildCheck.FindRefusal(Array.Empty<string>(), dedicatedServer: false, developmentBuild: false), Does.Contain(prefabConfigPath).And.Contain("cg_test_ test key"));
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        /// <summary>
        /// Every player build loads the Preloaded Assets of Player Settings, so a config in that list ships and the check
        /// inspects it; an empty slot in the list is skipped. The list is put back and the temporary folder deleted afterwards.
        /// </summary>
        [Test]
        public void ConfigsInPreloadedAssets_AreInspected()
        {
            string folder = "Assets/ChatGuardBuildCheckTest" + Guid.NewGuid().ToString("N").Substring(0, 8);
            UnityEngine.Object[] preloaded = PlayerSettings.GetPreloadedAssets();
            try
            {
                AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
                string configPath = folder + "/PreloadedConfig.asset";
                CreateConfig(configPath, Live);
                Assert.That(ChatGuardBuildCheck.ConfigsInBuild(Array.Empty<string>()), Does.Not.Contain(configPath), "not preloaded yet");

                var withConfig = new List<UnityEngine.Object?>(preloaded) { null, AssetDatabase.LoadAssetAtPath<ChatGuardConfig>(configPath) };
                PlayerSettings.SetPreloadedAssets(withConfig.ToArray());

                Assert.That(ChatGuardBuildCheck.ConfigsInBuild(Array.Empty<string>()), Does.Contain(configPath));
                Assert.That(ChatGuardBuildCheck.FindRefusal(Array.Empty<string>(), dedicatedServer: false, developmentBuild: true), Does.Contain(configPath).And.Contain("cg_live_ server key"));
                Assert.That(ChatGuardBuildCheck.FindRefusal(Array.Empty<string>(), dedicatedServer: true, developmentBuild: false), Is.Null);
            }
            finally
            {
                PlayerSettings.SetPreloadedAssets(preloaded);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        private static void CreateConfig(string path, string apiKey)
        {
            var config = ScriptableObject.CreateInstance<ChatGuardConfig>();
            config.apiKey = apiKey;
            AssetDatabase.CreateAsset(config, path);
            AssetDatabase.SaveAssets();
        }
    }
}
