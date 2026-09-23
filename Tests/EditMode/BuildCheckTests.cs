#nullable enable
using ChatGuard.Editor;
using NUnit.Framework;

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
    }
}
