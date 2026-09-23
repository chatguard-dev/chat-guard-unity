#nullable enable
using System.Text.RegularExpressions;
using ChatGuard.Unity;
using NUnit.Framework;
using UnityEngine;

namespace ChatGuard.Tests
{
    /// <summary>
    /// Which author id ChatGuardUnityHook.Moderate(message) sends: PlayerId when set, otherwise the per-installation id
    /// kept in PlayerPrefs, and none in Dedicated Server builds. The editor's PlayerPrefs value is saved and put back.
    /// </summary>
    public class HookTests
    {
        private GameObject? _host;
        private string? _savedInstallId;

        [SetUp]
        public void SetUp()
        {
            _savedInstallId = PlayerPrefs.HasKey(ChatGuardUnityHook.InstallIdKey) ? PlayerPrefs.GetString(ChatGuardUnityHook.InstallIdKey) : null;
            PlayerPrefs.DeleteKey(ChatGuardUnityHook.InstallIdKey);
            ChatGuardUnityHook.ResetInstallIdCache();
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null)
            {
                UnityEngine.Object.DestroyImmediate(_host);
            }

            if (_savedInstallId != null)
            {
                PlayerPrefs.SetString(ChatGuardUnityHook.InstallIdKey, _savedInstallId);
            }
            else
            {
                PlayerPrefs.DeleteKey(ChatGuardUnityHook.InstallIdKey);
            }

            PlayerPrefs.Save();
            ChatGuardUnityHook.ResetInstallIdCache();
        }

        private ChatGuardUnityHook NewHook()
        {
            _host = _host != null ? _host : new GameObject("Chat Guard Hook test");
            return _host.AddComponent<ChatGuardUnityHook>();
        }

        [Test]
        public void PlayerId_IsSent_InEveryBuild()
        {
            ChatGuardUnityHook hook = NewHook();
            hook.SetPlayerId("player-42");

            Assert.That(hook.PlayerId, Is.EqualTo("player-42"));
            Assert.That(hook.DefaultAuthorId(dedicatedServer: false), Is.EqualTo("player-42"));
            Assert.That(hook.DefaultAuthorId(dedicatedServer: true), Is.EqualTo("player-42"));
            Assert.That(PlayerPrefs.HasKey(ChatGuardUnityHook.InstallIdKey), Is.False, "an explicit player id never creates the install id");
        }

        [Test]
        public void WithoutPlayerId_TheInstallId_IsCreatedOnce_AndPersisted()
        {
            ChatGuardUnityHook hook = NewHook();
            hook.PlayerId = string.Empty;

            string? id = hook.DefaultAuthorId(dedicatedServer: false);
            Assert.That(id, Does.Match("^[0-9a-f]{32}$"), "a GUID in \"N\" format");
            Assert.That(PlayerPrefs.GetString(ChatGuardUnityHook.InstallIdKey), Is.EqualTo(id), "kept in PlayerPrefs");
            Assert.That(hook.DefaultAuthorId(dedicatedServer: false), Is.SameAs(id), "read once, then cached");

            // A new session (the cache forgotten) and another Hook read the same id back.
            ChatGuardUnityHook.ResetInstallIdCache();
            ChatGuardUnityHook other = NewHook();
            Assert.That(other.DefaultAuthorId(dedicatedServer: false), Is.EqualTo(id));

            // Setting a player id later wins over the install id.
            other.SetPlayerId("player-7");
            Assert.That(other.DefaultAuthorId(dedicatedServer: false), Is.EqualTo("player-7"));
        }

        [Test]
        public void DedicatedServer_SendsNoAuthorId_AndLeavesPlayerPrefsAlone()
        {
            ChatGuardUnityHook hook = NewHook();

            Assert.That(hook.DefaultAuthorId(dedicatedServer: true), Is.Null);
            Assert.That(PlayerPrefs.HasKey(ChatGuardUnityHook.InstallIdKey), Is.False);
        }

        [Test]
        public void StoredValueOfAnotherShape_IsReplaced()
        {
            PlayerPrefs.SetString(ChatGuardUnityHook.InstallIdKey, "Not-An-Install-Id");
            ChatGuardUnityHook hook = NewHook();

            string? id = hook.DefaultAuthorId(dedicatedServer: false);
            Assert.That(id, Does.Match("^[0-9a-f]{32}$"));
            Assert.That(PlayerPrefs.GetString(ChatGuardUnityHook.InstallIdKey), Is.EqualTo(id));
        }

        [Test]
        public void Moderate_WithoutConfig_LogsInsteadOfThrowing()
        {
            ChatGuardUnityHook hook = NewHook();
            UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, new Regex("needs a ChatGuardConfig"));
            Assert.DoesNotThrow(() => hook.Moderate("hello"));
        }
    }
}
