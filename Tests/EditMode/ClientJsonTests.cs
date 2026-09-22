#nullable enable
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
