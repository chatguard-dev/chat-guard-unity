#nullable enable
using System;
using System.Collections;
using System.Threading;
using ChatGuard.Core;
using ChatGuard.Unity;
using NUnit.Framework;

namespace ChatGuard.Tests
{
    public class LocalFallbackTests
    {
        // The offline path completes synchronously, so plain [Test]s can read op.Result right away.
        [Test]
        public void NoKey_UsesLocalFilter()
        {
            var client = new ChatGuardClient(string.Empty, "https://api.example.com");
            ModerationOperation op = client.Moderate(new ModerationRequest("you idiot"));
            Assert.That(op.IsDone, Is.True);
            Assert.That(op.IsCancelled, Is.False);
            Assert.That(op.keepWaiting, Is.False);
            ModerationResult result = op.Result!;
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Source, Is.EqualTo(ResultSource.Local));
            Assert.That(result.Degraded, Is.True);
            Assert.That(result.DegradedReason, Is.EqualTo(DegradedReason.Offline));
            Assert.That(result.Action, Is.EqualTo(ModerationAction.Hide));
            Assert.That(result.Verdicts.Insult, Is.EqualTo(1));
            Assert.That(result.Model, Does.StartWith("local-filter/"));
        }

        [Test]
        public void NoKey_AllowAll_And_BlockAll()
        {
            var allow = new ChatGuardClient(string.Empty, string.Empty, offline: OfflineBehavior.AllowAll);
            Assert.That(allow.Moderate(new ModerationRequest("kys")).Result!.Action, Is.EqualTo(ModerationAction.Allow));
            var block = new ChatGuardClient(string.Empty, string.Empty, offline: OfflineBehavior.BlockAll);
            Assert.That(block.Moderate(new ModerationRequest("gg")).Result!.Action, Is.EqualTo(ModerationAction.Block));
        }

        [Test]
        public void Completed_FiresImmediately_WhenSubscribedAfterCompletion()
        {
            var client = new ChatGuardClient(string.Empty, string.Empty);
            ModerationResult? viaArgument = null;
            ModerationOperation op = client.Moderate(new ModerationRequest("you idiot"), r => viaArgument = r);
            Assert.That(op.IsDone, Is.True);
            Assert.That(viaArgument, Is.SameAs(op.Result), "onCompleted argument runs for a synchronously completed operation");

            ModerationResult? viaEvent = null;
            op.Completed += r => viaEvent = r;
            Assert.That(viaEvent, Is.SameAs(op.Result), "subscribing after completion invokes the handler immediately");
        }

        [Test]
        public void ModerateCoroutine_DeliversResult_WhenIteratedManually()
        {
            var client = new ChatGuardClient(string.Empty, string.Empty);
            ModerationResult? got = null;
            IEnumerator e = client.ModerateCoroutine(new ModerationRequest("you idiot"), r => got = r);
            while (e.MoveNext())
            {
            }

            Assert.That(got, Is.Not.Null);
            Assert.That(got!.Source, Is.EqualTo(ResultSource.Local));
            Assert.That(got.Action, Is.EqualTo(ModerationAction.Hide));
        }

        [Test]
        public void StaticApi_DefaultsToLocalFilter_AndConfigureChangesTheAnswer()
        {
            ChatGuardSdk.Reset();
            try
            {
                Assert.That(ChatGuardSdk.IsConfigured, Is.False);
                ModerationOperation op = ChatGuardSdk.Moderate("you idiot", "p1");
                Assert.That(op.IsDone, Is.True);
                Assert.That(op.Result, Is.Not.Null);
                Assert.That(op.Result!.Source, Is.EqualTo(ResultSource.Local));
                Assert.That(op.Result.Action, Is.EqualTo(ModerationAction.Hide));
                Assert.That(ChatGuardSdk.IsConfigured, Is.True);

                ChatGuardSdk.Configure(new ChatGuardClient(string.Empty, string.Empty, offline: OfflineBehavior.AllowAll));
                Assert.That(ChatGuardSdk.Moderate("you idiot", "p1").Result!.Action, Is.EqualTo(ModerationAction.Allow));
            }
            finally
            {
                ChatGuardSdk.Reset();
            }

            Assert.That(ChatGuardSdk.IsConfigured, Is.False);
        }

        [Test]
        public void ConfigThresholds_ConvertToCore()
        {
            var config = UnityEngine.ScriptableObject.CreateInstance<ChatGuardConfig>();
            config.overrideThresholds = true;
            config.thresholds.insult.hide = -1f;
            config.thresholds.insult.flag = 0.5f;
            Core.Scoring.Thresholds t = config.EffectiveThresholds;
            Assert.That(t.Insult.Hide, Is.Null);
            Assert.That(t.Insult.Flag, Is.EqualTo(0.5).Within(1e-6));
            Assert.That(t.Threat.Block, Is.EqualTo(0.85).Within(1e-6));
            UnityEngine.Object.DestroyImmediate(config);
        }

        [Test]
        public void StaticApi_ConfigureWithSettings_NeedsNoAsset()
        {
            ChatGuardSdk.Reset();
            try
            {
                ChatGuardSdk.Configure(new ChatGuardSettings { OfflineBehavior = OfflineBehavior.BlockAll });
                Assert.That(ChatGuardSdk.IsConfigured, Is.True);
                ModerationOperation op = ChatGuardSdk.Moderate("gg");
                Assert.That(op.IsDone, Is.True);
                Assert.That(op.Result, Is.Not.Null);
                Assert.That(op.Result!.Action, Is.EqualTo(ModerationAction.Block));
                Assert.That(op.Result.Source, Is.EqualTo(ResultSource.Local));
                Assert.That(op.Result.DegradedReason, Is.EqualTo(DegradedReason.Offline));

                ChatGuardSettings active = ChatGuardSdk.Client.Settings;
                Assert.That(active.OfflineBehavior, Is.EqualTo(OfflineBehavior.BlockAll));
                Assert.That(active.ApiKey, Is.Empty);
                Assert.That(ChatGuardSdk.Client.HasServer, Is.False);

                // Configure again swaps the shared client.
                ChatGuardSdk.Configure(new ChatGuardSettings { OfflineBehavior = OfflineBehavior.AllowAll });
                Assert.That(ChatGuardSdk.Moderate("gg").Result!.Action, Is.EqualTo(ModerationAction.Allow));
            }
            finally
            {
                ChatGuardSdk.Reset();
            }
        }

        [Test]
        public void Settings_InvalidValues_ThrowWhenClientIsBuilt()
        {
            var zeroTimeout = new ChatGuardSettings { TimeoutSeconds = 0f };
            Assert.Throws<ArgumentException>(() => new ChatGuardClient(zeroTimeout));
            Assert.Throws<ArgumentException>(() => ChatGuardSdk.Configure(new ChatGuardSettings { TimeoutSeconds = -1f }));
            Assert.Throws<ArgumentException>(() => new ChatGuardClient(new ChatGuardSettings { BaseUrl = "api.example.com" }));
            Assert.Throws<ArgumentException>(() => new ChatGuardClient(new ChatGuardSettings { BaseUrl = "ftp://api.example.com" }));

            // Empty key and URL are valid (local filter only); the client copies the settings and trims the URL.
            var settings = new ChatGuardSettings { BaseUrl = "https://api.example.com/", ChannelType = "team" };
            var client = new ChatGuardClient(settings);
            Assert.That(client.HasServer, Is.False);
            settings.ChannelType = "dm";
            Assert.That(client.Settings.ChannelType, Is.EqualTo("team"), "the client must keep its own copy");
            Assert.That(client.Settings.BaseUrl, Is.EqualTo("https://api.example.com"));
        }

        [Test]
        public void Settings_NonFiniteOrOversizedTimeout_Throws()
        {
            ArgumentException infinity = Assert.Throws<ArgumentException>(() => new ChatGuardSettings { TimeoutSeconds = float.PositiveInfinity }.Validate());
            Assert.That(infinity.ParamName, Is.EqualTo("TimeoutSeconds"));
            Assert.That(infinity.Message, Does.Contain("positive finite number of seconds"));
            Assert.Throws<ArgumentException>(() => new ChatGuardSettings { TimeoutSeconds = float.NegativeInfinity }.Validate());
            Assert.Throws<ArgumentException>(() => new ChatGuardSettings { TimeoutSeconds = float.NaN }.Validate());
            Assert.Throws<ArgumentException>(() => new ChatGuardClient(new ChatGuardSettings { TimeoutSeconds = float.PositiveInfinity }));
            Assert.Throws<ArgumentException>(() => new ChatGuardClient(new ChatGuardSettings { TimeoutSeconds = ChatGuardSettings.MaxTimeoutSeconds + 1f }));
            Assert.Throws<ArgumentException>(() => new ChatGuardClient(string.Empty, string.Empty, timeoutSeconds: float.PositiveInfinity));

            // The cap itself and small positive budgets are fine.
            Assert.DoesNotThrow(() => new ChatGuardSettings { TimeoutSeconds = ChatGuardSettings.MaxTimeoutSeconds }.Validate());
            Assert.DoesNotThrow(() => new ChatGuardSettings { TimeoutSeconds = 0.25f }.Validate());
        }

        [Test]
        public void FreshConfigAsset_HasTheSameDefaultsAsSettings()
        {
            var config = UnityEngine.ScriptableObject.CreateInstance<ChatGuardConfig>();
            try
            {
                var defaults = new ChatGuardSettings();
                Assert.That(config.baseUrl, Is.Empty, "a fresh asset must not point at a placeholder URL");
                Assert.That(config.apiKey, Is.Empty);

                ChatGuardSettings s = config.ToSettings();
                Assert.That(s.BaseUrl, Is.Empty, "ToSettings must keep the empty URL (local filter only)");
                Assert.That(s.BaseUrl, Is.EqualTo(defaults.BaseUrl));
                Assert.That(s.ApiKey, Is.EqualTo(defaults.ApiKey));
                Assert.That(s.TimeoutSeconds, Is.EqualTo(defaults.TimeoutSeconds));
                Assert.That(s.OfflineBehavior, Is.EqualTo(defaults.OfflineBehavior));
                Assert.That(s.LocalFilterWhenDegraded, Is.EqualTo(defaults.LocalFilterWhenDegraded));
                Assert.That(s.DefaultLanguage, Is.EqualTo(defaults.DefaultLanguage));
                Assert.That(s.ChannelType, Is.EqualTo(defaults.ChannelType));
                Assert.That(s.AgeRating, Is.EqualTo(defaults.AgeRating));
                Assert.That(s.Thresholds, Is.Null);

                var client = new ChatGuardClient(config);
                Assert.That(client.HasServer, Is.False);
                Assert.That(client.Settings.BaseUrl, Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void PositionalConstructor_SendsTheSameDefaultAgeRatingAsSettings()
        {
            var request = new ModerationRequest("gg", "p1");
            var positional = new ChatGuardClient("cg_test_x", "https://api.example.com");
            var fromSettings = new ChatGuardClient(new ChatGuardSettings { ApiKey = "cg_test_x", BaseUrl = "https://api.example.com" });

            string json = positional.BuildRequestJson(request);
            Assert.That(json, Does.Contain("16+"));
            Assert.That(MiniJson.GetString(MiniJson.GetObject(MiniJson.AsObject(MiniJson.Parse(json)), "channel"), "age_rating"), Is.EqualTo("16+"));
            Assert.That(positional.Settings.AgeRating, Is.EqualTo(new ChatGuardSettings().AgeRating));
            Assert.That(json, Is.EqualTo(fromSettings.BuildRequestJson(request)), "both constructors must build the same request");

            // Null or empty sends no age rating at all.
            Assert.That(new ChatGuardClient("cg_test_x", "https://api.example.com", ageRating: null).BuildRequestJson(request), Does.Not.Contain("age_rating"));
            Assert.That(new ChatGuardClient("cg_test_x", "https://api.example.com", ageRating: string.Empty).BuildRequestJson(request), Does.Not.Contain("age_rating"));
        }

        [Test]
        public void ConfigToSettings_RoundTripsAssetValues()
        {
            var config = UnityEngine.ScriptableObject.CreateInstance<ChatGuardConfig>();
            try
            {
                config.apiKey = "cg_test_" + new string('x', 32);
                config.baseUrl = "https://api.example.com";
                config.timeoutSeconds = 3.5f;
                config.offlineBehavior = OfflineBehavior.AllowAll;
                config.localFilterWhenDegraded = false;
                config.defaultLanguage = "de";
                config.channelType = "team";
                config.ageRating = "18+";
                config.overrideThresholds = true;
                config.thresholds.insult.hide = -1f;
                config.thresholds.insult.flag = 0.5f;
                config.thresholds.severityBlock = 2f;

                ChatGuardSettings s = config.ToSettings();
                Assert.That(s.ApiKey, Is.EqualTo(config.apiKey));
                Assert.That(s.BaseUrl, Is.EqualTo("https://api.example.com"));
                Assert.That(s.TimeoutSeconds, Is.EqualTo(3.5f));
                Assert.That(s.OfflineBehavior, Is.EqualTo(OfflineBehavior.AllowAll));
                Assert.That(s.LocalFilterWhenDegraded, Is.False);
                Assert.That(s.DefaultLanguage, Is.EqualTo("de"));
                Assert.That(s.ChannelType, Is.EqualTo("team"));
                Assert.That(s.AgeRating, Is.EqualTo("18+"));
                Assert.That(s.Thresholds, Is.Not.Null);
                Assert.That(s.Thresholds!.Insult.Hide, Is.Null);
                Assert.That(s.Thresholds.Insult.Flag, Is.EqualTo(0.5).Within(1e-6));
                Assert.That(s.Thresholds.Threat.Block, Is.EqualTo(0.85).Within(1e-6));
                Assert.That(s.Thresholds.SeverityBlock, Is.EqualTo(2.0).Within(1e-6));

                config.overrideThresholds = false;
                Assert.That(config.ToSettings().Thresholds, Is.Null, "no override means the built-in defaults");

                var client = new ChatGuardClient(config);
                Assert.That(client.HasServer, Is.True);
                Assert.That(client.Settings.DefaultLanguage, Is.EqualTo("de"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        // The offline path completes synchronously and the awaiter runs its continuation inline, so the async void
        // helper has already stored the result (or the exception) by the time it returns.
        [Test]
        public void Await_AlreadyCompletedOfflineOperation_DeliversResultInline()
        {
            var client = new ChatGuardClient(string.Empty, string.Empty);
            ModerationOperation op = client.Moderate(new ModerationRequest("you idiot"));
            Assert.That(op.IsDone, Is.True);

            ModerationResult? awaited = null;
            Exception? error = null;
            AwaitInto(op, r => awaited = r, ex => error = ex);

            Assert.That(error, Is.Null);
            Assert.That(awaited, Is.Not.Null, "await did not resume inline for an already-completed operation");
            Assert.That(awaited, Is.SameAs(op.Result));
            Assert.That(awaited!.Source, Is.EqualTo(ResultSource.Local));
            Assert.That(awaited.Action, Is.EqualTo(ModerationAction.Hide));
        }

        [Test]
        public void Await_CancelledOperation_ThrowsOperationCanceledException()
        {
            var client = new ChatGuardClient("cg_test_" + new string('x', 32), "http://127.0.0.1:9", timeoutSeconds: 1f);
            ModerationOperation op = client.Moderate(new ModerationRequest("you idiot", "p1"));
            Assert.That(op.IsDone, Is.False, "a network request must not complete synchronously");

            op.Cancel();
            Assert.That(op.IsDone, Is.True);
            Assert.That(op.IsCancelled, Is.True);

            ModerationResult? awaited = null;
            Exception? error = null;
            AwaitInto(op, r => awaited = r, ex => error = ex);

            Assert.That(awaited, Is.Null);
            Assert.That(error, Is.TypeOf<OperationCanceledException>(), "awaiting a cancelled operation must throw");
            Assert.That(((OperationCanceledException)error!).CancellationToken, Is.EqualTo(CancellationToken.None), "Cancel() is not a token");
        }

        [Test]
        public void Token_AlreadyCancelled_ReturnsCancelledOperationWithoutSending()
        {
            var client = new ChatGuardClient("cg_test_" + new string('x', 32), "http://127.0.0.1:9", timeoutSeconds: 1f);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            int completedCalls = 0;
            ModerationOperation op = client.Moderate(new ModerationRequest("you idiot", "p1"), _ => completedCalls++, cts.Token);
            Assert.That(op.IsDone, Is.True);
            Assert.That(op.IsCancelled, Is.True);
            Assert.That(op.Result, Is.Null);
            Assert.That(completedCalls, Is.EqualTo(0));

            // Without a server too: a cancelled token wins over the synchronous offline fallback.
            ModerationOperation offline = new ChatGuardClient(string.Empty, string.Empty).Moderate(new ModerationRequest("you idiot"), cts.Token);
            Assert.That(offline.IsCancelled, Is.True);
            Assert.That(offline.Result, Is.Null);

            Exception? error = null;
            AwaitInto(op, _ => { }, ex => error = ex);
            Assert.That(error, Is.TypeOf<OperationCanceledException>());
            Assert.That(((OperationCanceledException)error!).CancellationToken, Is.EqualTo(cts.Token));
        }

        [Test]
        public void Token_CancelledWhileInFlight_EndsTheOperationLikeCancel()
        {
            var client = new ChatGuardClient("cg_test_" + new string('x', 32), "http://127.0.0.1:9", timeoutSeconds: 1f);
            using var cts = new CancellationTokenSource();
            int completedCalls = 0;
            ModerationOperation op = client.Moderate(new ModerationRequest("you idiot", "p1"), _ => completedCalls++, cts.Token);
            Assert.That(op.IsDone, Is.False, "a network request must not complete synchronously");

            ModerationResult? awaited = null;
            Exception? error = null;
            AwaitInto(op, r => awaited = r, ex => error = ex);
            cts.Cancel();

            Assert.That(op.IsDone, Is.True, "a token cancelled on the main thread ends the operation right away");
            Assert.That(op.IsCancelled, Is.True);
            Assert.That(op.Result, Is.Null);
            Assert.That(completedCalls, Is.EqualTo(0));
            Assert.That(awaited, Is.Null);
            Assert.That(error, Is.TypeOf<OperationCanceledException>());
            Assert.That(((OperationCanceledException)error!).CancellationToken, Is.EqualTo(cts.Token));
        }

        [Test]
        public void Token_CancelledAfterCompletion_ChangesNothing()
        {
            var client = new ChatGuardClient(string.Empty, string.Empty);
            using var cts = new CancellationTokenSource();
            ModerationOperation op = client.Moderate(new ModerationRequest("you idiot"), cts.Token);
            Assert.That(op.IsDone, Is.True);
            ModerationResult? result = op.Result;
            Assert.That(result, Is.Not.Null);

            cts.Cancel();
            Assert.That(op.IsCancelled, Is.False);
            Assert.That(op.Result, Is.SameAs(result));
        }

        [Test]
        public void StaticApi_TokenOverloads()
        {
            ChatGuardSdk.Reset();
            try
            {
                ChatGuardSdk.Configure(new ChatGuardSettings { OfflineBehavior = OfflineBehavior.BlockAll });
                using var live = new CancellationTokenSource();
                ModerationResult? viaCallback = null;
                Assert.That(ChatGuardSdk.Moderate("gg", "p1", live.Token).Result!.Action, Is.EqualTo(ModerationAction.Block));
                ChatGuardSdk.Moderate("gg", "p1", r => viaCallback = r, live.Token);
                Assert.That(viaCallback, Is.Not.Null);
                Assert.That(ChatGuardSdk.Moderate(new ModerationRequest("gg", "p1"), live.Token).Result, Is.Not.Null);
                Assert.That(ChatGuardSdk.Moderate(new ModerationRequest("gg", "p1"), _ => { }, live.Token).Result, Is.Not.Null);

                using var cancelled = new CancellationTokenSource();
                cancelled.Cancel();
                Assert.That(ChatGuardSdk.Moderate("gg", "p1", cancelled.Token).IsCancelled, Is.True);
                Assert.That(ChatGuardSdk.Moderate("gg", "p1", _ => Assert.Fail("callback after cancel"), cancelled.Token).IsCancelled, Is.True);
                Assert.That(ChatGuardSdk.Moderate(new ModerationRequest("gg", "p1"), cancelled.Token).IsCancelled, Is.True);
            }
            finally
            {
                ChatGuardSdk.Reset();
            }
        }

        /// <summary>Awaits the operation and hands the outcome to a callback; catches everything so nothing escapes the async void.</summary>
        private static async void AwaitInto(ModerationOperation op, Action<ModerationResult> onResult, Action<Exception> onError)
        {
            try
            {
                ModerationResult result = await op;
                onResult(result);
            }
            catch (Exception ex)
            {
                onError(ex);
            }
        }
    }
}
