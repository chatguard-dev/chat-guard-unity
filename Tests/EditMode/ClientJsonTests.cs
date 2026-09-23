#nullable enable
using System;
using System.Collections.Generic;
using ChatGuard.Core;
using ChatGuard.Unity;
using NUnit.Framework;

namespace ChatGuard.Tests
{
    public class ClientJsonTests
    {
        private const string SampleResponse = "{\"id\":\"019...\",\"action\":\"hide\",\"severity\":1.815,\"verdicts\":{\"insult\":{\"p\":0.91},\"threat\":{\"p\":0.03},\"hate\":{\"p\":0.05},\"sexual\":{\"p\":0.01},\"spam\":{\"p\":0.02},\"trading\":{\"p\":0.0}},\"target\":{\"choice\":\"other_user\",\"confidence\":0.84},\"degraded\":false,\"degraded_reason\":null,\"cached\":false,\"quota\":{\"used\":12345,\"limit\":50000,\"window_ends_at\":\"2026-09-23T00:00:00+00:00\"},\"model\":\"jev-1.13.0\",\"latency_ms\":212}";

        [Test]
        public void GameIdentity_SendsCleanBundleIds_AndNothingElse()
        {
            Assert.That(ChatGuardClient.GameIdentity("com.Studio.Game-2_beta"), Is.EqualTo("com.Studio.Game-2_beta"));
            Assert.That(ChatGuardClient.GameIdentity(null), Is.Empty);
            Assert.That(ChatGuardClient.GameIdentity(string.Empty), Is.Empty);
            Assert.That(ChatGuardClient.GameIdentity("com.studio game"), Is.Empty);
            Assert.That(ChatGuardClient.GameIdentity("com.stüdio.game"), Is.Empty);
            Assert.That(ChatGuardClient.GameIdentity(new string('a', 201)), Is.Empty);
            Assert.That(ChatGuardClient.GameIdentity(new string('a', 200)), Has.Length.EqualTo(200));
        }

        [Test]
        public void ParsesServerResponse()
        {
            ModerationResult? result = ChatGuardClient.TryParseResponse(SampleResponse, 250);
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Action, Is.EqualTo(ModerationAction.Hide));
            Assert.That(result.Verdicts.Insult, Is.EqualTo(0.91).Within(1e-9));
            Assert.That(result.Target!.Choice, Is.EqualTo(TargetChoice.OtherUser));
            Assert.That(result.Degraded, Is.False);
            Assert.That(result.DegradedReason, Is.EqualTo(DegradedReason.None));
            Assert.That(result.QuotaLimit, Is.EqualTo(50000));
            Assert.That(result.Model, Is.EqualTo("jev-1.13.0"));
            Assert.That(result.Source, Is.EqualTo(ResultSource.Server));
            Assert.That(result.ShouldDeliver, Is.False);
        }

        [Test]
        public void ParsesDegradedResponseWithNullTarget()
        {
            string json = SampleResponse.Replace("\"degraded\":false,\"degraded_reason\":null", "\"degraded\":true,\"degraded_reason\":\"quota\"").Replace("\"target\":{\"choice\":\"other_user\",\"confidence\":0.84}", "\"target\":null");
            ModerationResult? result = ChatGuardClient.TryParseResponse(json, 10);
            Assert.That(result!.Degraded, Is.True);
            Assert.That(result.DegradedReason, Is.EqualTo(DegradedReason.Quota));
            Assert.That(result.Target, Is.Null);
        }

        [Test]
        public void RejectsGarbage()
        {
            Assert.That(ChatGuardClient.TryParseResponse("not json", 0), Is.Null);
            Assert.That(ChatGuardClient.TryParseResponse("{\"action\":\"explode\"}", 0), Is.Null);
        }

        [Test]
        public void TryParseResponse_Null_ReturnsNull()
        {
            Assert.That(ChatGuardClient.TryParseResponse(null, 0), Is.Null);
        }

        /// <summary>A body cut off anywhere (a dropped connection, a proxy limit) is unparseable, never an exception.</summary>
        [Test]
        public void TryParseResponse_EveryTruncation_ReturnsNull()
        {
            Assert.That(ChatGuardClient.TryParseResponse("{\"action\":\"hide\",", 0), Is.Null, "used to throw IndexOutOfRangeException");
            for (int i = 0; i < SampleResponse.Length; i++)
            {
                string prefix = SampleResponse.Substring(0, i);
                ModerationResult? result = null;
                Assert.DoesNotThrow(() => result = ChatGuardClient.TryParseResponse(prefix, 0), prefix);
                Assert.That(result, Is.Null, prefix);
            }
        }

        [Test]
        public void TryParseResponse_Nesting_UpToTheLimitIsRead_DeeperIsUnparseable()
        {
            // The root object is level 1, so MaxDepth - 1 arrays under it reach the limit.
            string deepest = SampleResponse.Replace("\"latency_ms\":212", "\"deep\":" + new string('[', MiniJson.MaxDepth - 1) + new string(']', MiniJson.MaxDepth - 1));
            Assert.That(ChatGuardClient.TryParseResponse(deepest, 0)!.Action, Is.EqualTo(ModerationAction.Hide));
            string tooDeep = SampleResponse.Replace("\"latency_ms\":212", "\"deep\":" + new string('[', MiniJson.MaxDepth) + new string(']', MiniJson.MaxDepth));
            Assert.That(ChatGuardClient.TryParseResponse(tooDeep, 0), Is.Null);
            string huge = SampleResponse.Replace("\"latency_ms\":212", "\"deep\":" + new string('[', 100000) + new string(']', 100000));
            Assert.That(ChatGuardClient.TryParseResponse(huge, 0), Is.Null);
        }

        [Test]
        public void RequestJson_OmitsUnsetOptionalFields()
        {
            var client = new ChatGuardClient("cg_test_x", "https://api.example.com", language: "de", channelType: "team", ageRating: "16+");
            var request = new ModerationRequest("hello \"quoted\" \n line", "p1") { thread = new List<ThreadEntry> { new ThreadEntry("p2", "yo") } };
            string json = client.BuildRequestJson(request);
            Dictionary<string, object?> root = MiniJson.AsObject(MiniJson.Parse(json))!;
            Assert.That(MiniJson.GetString(root, "message"), Is.EqualTo("hello \"quoted\" \n line"));
            Dictionary<string, object?> author = MiniJson.GetObject(root, "author")!;
            Assert.That(MiniJson.GetString(author, "id"), Is.EqualTo("p1"));
            Assert.That(author.ContainsKey("account_age_days"), Is.False, "unset counts must not be sent");
            Assert.That(MiniJson.GetString(MiniJson.GetObject(root, "channel"), "language"), Is.EqualTo("de"));
            Assert.That(MiniJson.GetString(MiniJson.GetObject(root, "channel"), "type"), Is.EqualTo("team"));
            Assert.That(MiniJson.AsArray(root["thread"])!.Count, Is.EqualTo(1));
            Assert.That(root.ContainsKey("request_id"), Is.False);
        }

        // The exact strings below were captured from the 0.2.1 serializer (a Dictionary tree written by MiniJson.Write),
        // so they pin the request bytes: key order, which fields are omitted, escaping, and the last-5 thread window.
        private static ChatGuardClient DefaultsClient()
        {
            return new ChatGuardClient("cg_test_x", "https://api.example.com");
        }

        [Test]
        public void RequestJson_Minimal_ExactString()
        {
            string json = DefaultsClient().BuildRequestJson(new ModerationRequest("hi"));
            Assert.That(json, Is.EqualTo("{\"message\":\"hi\",\"channel\":{\"type\":\"global\",\"language\":\"en\",\"age_rating\":\"16+\"}}"));
        }

        [Test]
        public void RequestJson_AuthorCounts_ExactString()
        {
            ChatGuardClient client = DefaultsClient();
            string both = client.BuildRequestJson(new ModerationRequest("gg", "p1") { accountAgeDays = 30, priorWarnings = 0 });
            Assert.That(both, Is.EqualTo("{\"message\":\"gg\",\"author\":{\"id\":\"p1\",\"account_age_days\":30,\"prior_warnings\":0},\"channel\":{\"type\":\"global\",\"language\":\"en\",\"age_rating\":\"16+\"}}"));
            string countOnly = client.BuildRequestJson(new ModerationRequest("gg") { accountAgeDays = -1, priorWarnings = 2 });
            Assert.That(countOnly, Is.EqualTo("{\"message\":\"gg\",\"author\":{\"prior_warnings\":2},\"channel\":{\"type\":\"global\",\"language\":\"en\",\"age_rating\":\"16+\"}}"));
        }

        [Test]
        public void RequestJson_NullMessageAndExtremeCounts_ExactString()
        {
            var request = new ModerationRequest { message = null!, authorId = string.Empty, accountAgeDays = int.MaxValue, priorWarnings = -5, thread = null!, requestId = string.Empty };
            string json = DefaultsClient().BuildRequestJson(request);
            Assert.That(json, Is.EqualTo("{\"message\":\"\",\"author\":{\"account_age_days\":2147483647},\"channel\":{\"type\":\"global\",\"language\":\"en\",\"age_rating\":\"16+\"}}"));
        }

        [Test]
        public void RequestJson_SendsLastFiveThreadEntries_ExactString()
        {
            var request = new ModerationRequest("reply", "p1")
            {
                thread = new List<ThreadEntry>
                {
                    new ThreadEntry("p0", "dropped: only the last 5 are sent"),
                    new ThreadEntry("p2", "one"),
                    new ThreadEntry(null!, "no author"),
                    new ThreadEntry("p3", null!),
                    new ThreadEntry("p4", string.Empty),
                    new ThreadEntry("p5", "five"),
                },
            };
            string json = DefaultsClient().BuildRequestJson(request);
            Assert.That(json, Is.EqualTo("{\"message\":\"reply\",\"author\":{\"id\":\"p1\"},\"thread\":[{\"author\":\"p2\",\"text\":\"one\"},{\"author\":\"unknown\",\"text\":\"no author\"},{\"author\":\"p3\",\"text\":\"\"},{\"author\":\"p4\",\"text\":\"\"},{\"author\":\"p5\",\"text\":\"five\"}],\"channel\":{\"type\":\"global\",\"language\":\"en\",\"age_rating\":\"16+\"}}"));
        }

        [Test]
        public void RequestJson_NullThreadEntry_OutsideWindowIgnored_InsideWindowThrows()
        {
            ChatGuardClient client = DefaultsClient();
            var outside = new ModerationRequest("m") { thread = new List<ThreadEntry> { null!, new ThreadEntry("a", "1"), new ThreadEntry("b", "2"), new ThreadEntry("c", "3"), new ThreadEntry("d", "4"), new ThreadEntry("e", "5") } };
            Assert.That(client.BuildRequestJson(outside), Is.EqualTo("{\"message\":\"m\",\"thread\":[{\"author\":\"a\",\"text\":\"1\"},{\"author\":\"b\",\"text\":\"2\"},{\"author\":\"c\",\"text\":\"3\"},{\"author\":\"d\",\"text\":\"4\"},{\"author\":\"e\",\"text\":\"5\"}],\"channel\":{\"type\":\"global\",\"language\":\"en\",\"age_rating\":\"16+\"}}"));

            var inside = new ModerationRequest("m") { thread = new List<ThreadEntry> { new ThreadEntry("a", "1"), null!, new ThreadEntry("c", "3") } };
            Assert.Throws<NullReferenceException>(() => client.BuildRequestJson(inside));
        }

        [Test]
        public void RequestJson_ChannelOmittedWhenEverythingEmpty_ExactString()
        {
            var client = new ChatGuardClient("cg_test_x", "https://api.example.com", language: string.Empty, channelType: string.Empty, ageRating: null);
            Assert.That(client.BuildRequestJson(new ModerationRequest("hi")), Is.EqualTo("{\"message\":\"hi\"}"));
            Assert.That(client.BuildRequestJson(new ModerationRequest("hi") { language = "de", ageRating = "18+" }), Is.EqualTo("{\"message\":\"hi\",\"channel\":{\"language\":\"de\",\"age_rating\":\"18+\"}}"));
        }

        [Test]
        public void RequestJson_EscapesQuotesControlCharsSurrogatesAndNonAscii_ExactString()
        {
            var request = new ModerationRequest("q\"b\\s/ \b\f\n\r\t\u0000\u0001\u001f\u007f \ud800 x\udc00 \u2028\u2029 \u0442\u044b \u4f60\u597d \ud83c\udfae", "id\"\\\n")
            {
                thread = new List<ThreadEntry> { new ThreadEntry("\ud83d", "caf\u00e9\u0007") },
                channelType = "te\tam",
                language = "\u00fc",
                ageRating = "16+\"",
            };
            string json = DefaultsClient().BuildRequestJson(request);
            Assert.That(json, Is.EqualTo("{\"message\":\"q\\\"b\\\\s/ \\b\\f\\n\\r\\t\\u0000\\u0001\\u001f\u007f \ud800 x\udc00 \u2028\u2029 \u0442\u044b \u4f60\u597d \ud83c\udfae\",\"author\":{\"id\":\"id\\\"\\\\\\n\"},\"thread\":[{\"author\":\"\ud83d\",\"text\":\"caf\u00e9\\u0007\"}],\"channel\":{\"type\":\"te\\tam\",\"language\":\"\u00fc\",\"age_rating\":\"16+\\\"\"}}"));
            Assert.That(MiniJson.GetString(MiniJson.AsObject(MiniJson.Parse(json)), "message"), Is.EqualTo(request.message));
        }

        [Test]
        public void RequestJson_RequestId_ExactString()
        {
            string json = DefaultsClient().BuildRequestJson(new ModerationRequest("hi") { requestId = "req-42", channelType = "team", language = "ru" });
            Assert.That(json, Is.EqualTo("{\"message\":\"hi\",\"channel\":{\"type\":\"team\",\"language\":\"ru\",\"age_rating\":\"16+\"},\"request_id\":\"req-42\"}"));
        }

        [Test]
        public void MiniJson_RoundTripsUnicodeAndNumbers()
        {
            var value = new Dictionary<string, object?> { ["s"] = "ты дебил 🎮", ["n"] = 1.5, ["b"] = true, ["z"] = null, ["a"] = new List<object?> { 1.0, "x" } };
            string json = MiniJson.Write(value);
            Dictionary<string, object?> back = MiniJson.AsObject(MiniJson.Parse(json))!;
            Assert.That(MiniJson.GetString(back, "s"), Is.EqualTo("ты дебил 🎮"));
            Assert.That(MiniJson.GetNumber(back, "n"), Is.EqualTo(1.5));
            Assert.That(MiniJson.GetBool(back, "b"), Is.True);
            Assert.That(back["z"], Is.Null);
            Assert.That(MiniJson.AsArray(back["a"])!.Count, Is.EqualTo(2));
        }
    }
}
