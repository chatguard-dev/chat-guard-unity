#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using ChatGuard.Unity;
using NUnit.Framework;

namespace ChatGuard.Tests
{
    /// <summary>
    /// Moderate uploads the request as UTF-8 written without the JSON string. These tests pin those bytes to
    /// Encoding.UTF8.GetBytes(BuildRequestJson(request)), which is what earlier versions uploaded.
    /// </summary>
    public class RequestBytesTests
    {
        private static byte[] Utf8Body(ChatGuardClient client, ModerationRequest request)
        {
            Utf8RequestJsonSink body = client.WriteRequestUtf8(request);
            var bytes = new byte[body.Length];
            Buffer.BlockCopy(body.Buffer, 0, bytes, 0, body.Length);
            body.Release();
            return bytes;
        }

        private static void AssertSameBytes(ChatGuardClient client, ModerationRequest request, string context)
        {
            byte[] expected = Encoding.UTF8.GetBytes(client.BuildRequestJson(request));
            byte[] actual = Utf8Body(client, request);
            if (!AreEqual(expected, actual))
            {
                Assert.Fail(context + ": UTF-8 body differs from Encoding.UTF8.GetBytes(BuildRequestJson)\nexpected " + BitConverter.ToString(expected) + "\nactual   " + BitConverter.ToString(actual));
            }
        }

        private static bool AreEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static ChatGuardClient DefaultsClient()
        {
            return new ChatGuardClient("cg_test_x", "https://api.example.com");
        }

        [Test]
        public void GoldenRequests_MatchUtf8OfBuildRequestJson()
        {
            ChatGuardClient defaults = DefaultsClient();
            AssertSameBytes(defaults, new ModerationRequest("hi"), "minimal");
            AssertSameBytes(defaults, new ModerationRequest("gg", "p1") { accountAgeDays = 30, priorWarnings = 0 }, "author and counts");
            AssertSameBytes(defaults, new ModerationRequest("gg") { accountAgeDays = -1, priorWarnings = 2 }, "count only");
            AssertSameBytes(defaults, new ModerationRequest { message = null!, authorId = string.Empty, accountAgeDays = int.MaxValue, priorWarnings = -5, thread = null!, requestId = string.Empty }, "null message, extreme counts");
            AssertSameBytes(defaults, new ModerationRequest("hi") { requestId = "req-42", channelType = "team", language = "ru" }, "request id");
            var lastFive = new ModerationRequest("reply", "p1")
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
            AssertSameBytes(defaults, lastFive, "last five thread entries");
            var nullOutsideWindow = new ModerationRequest("m") { thread = new List<ThreadEntry> { null!, new ThreadEntry("a", "1"), new ThreadEntry("b", "2"), new ThreadEntry("c", "3"), new ThreadEntry("d", "4"), new ThreadEntry("e", "5") } };
            AssertSameBytes(defaults, nullOutsideWindow, "null entry outside the window");
            var escapes = new ModerationRequest("q\"b\\s/ \b\f\n\r\t\u0000\u0001\u001f\u007f \ud800 x\udc00 \u2028\u2029 \u0442\u044b \u4f60\u597d \ud83c\udfae", "id\"\\\n")
            {
                thread = new List<ThreadEntry> { new ThreadEntry("\ud83d", "caf\u00e9\u0007") },
                channelType = "te\tam",
                language = "\u00fc",
                ageRating = "16+\"",
            };
            AssertSameBytes(defaults, escapes, "escapes, surrogates and non-ASCII");

            var empty = new ChatGuardClient("cg_test_x", "https://api.example.com", language: string.Empty, channelType: string.Empty, ageRating: null);
            AssertSameBytes(empty, new ModerationRequest("hi"), "channel omitted");
            AssertSameBytes(empty, new ModerationRequest("hi") { language = "de", ageRating = "18+" }, "channel without type");

            var custom = new ChatGuardClient("cg_test_x", "https://api.example.com", language: "de", channelType: "team", ageRating: "16+");
            AssertSameBytes(custom, new ModerationRequest("hello \"quoted\" \n line", "p1") { thread = new List<ThreadEntry> { new ThreadEntry("p2", "yo") } }, "custom defaults");
        }

        [Test]
        public void SurrogateEdges_MatchEncodingUtf8Replacement()
        {
            ChatGuardClient client = DefaultsClient();
            string[] messages =
            {
                "\ud800", "\udc00", "\ud800\ud800", "\udc00\ud800", "\ud800a", "a\udc00", "\ud800\udc00", "\udbff\udfff",
                "\ud83c\udfae\ud83c", "\ud83c\ud83c\udfae", "\udfae\ud83c\udfae", "\ud800\"", "\ud800\\", "\ud800\n", "\ud800\u00e9",
                "\uffff\ufffe\ufffd\ufeff", "\u007f\u0080\u07ff\u0800", string.Empty,
            };
            foreach (string message in messages)
            {
                AssertSameBytes(client, new ModerationRequest(message, message) { requestId = message }, "message " + BitConverter.ToString(Encoding.Unicode.GetBytes(message)));
            }
        }

        [Test]
        public void NullThreadEntryInsideWindow_ThrowsLikeBuildRequestJson()
        {
            ChatGuardClient client = DefaultsClient();
            var inside = new ModerationRequest("m") { thread = new List<ThreadEntry> { new ThreadEntry("a", "1"), null!, new ThreadEntry("c", "3") } };
            Assert.Throws<NullReferenceException>(() => client.BuildRequestJson(inside));
            Assert.Throws<NullReferenceException>(() => client.WriteRequestUtf8(inside));

            // The thread's buffer is still usable afterwards.
            AssertSameBytes(client, new ModerationRequest("after"), "after an exception");
        }

        [Test]
        public void Moderate_NullThreadEntryInsideWindow_CompletesWithOfflineFallback()
        {
            ChatGuardClient client = DefaultsClient();
            var inside = new ModerationRequest("you idiot") { thread = new List<ThreadEntry> { new ThreadEntry("a", "1"), null! } };
            string expectedError = Assert.Throws<NullReferenceException>(() => client.BuildRequestJson(inside)).Message;

            // Moderate catches what the body writer throws and completes synchronously with the offline fallback,
            // carrying the message BuildRequestJson throws, as before.
            ModerationOperation op = client.Moderate(inside);
            Assert.That(op.IsDone, Is.True);
            Assert.That(op.Result!.Source, Is.EqualTo(ResultSource.Local));
            Assert.That(op.Result.DegradedReason, Is.EqualTo(ChatGuard.Core.DegradedReason.Offline));
            Assert.That(op.Result.Error, Is.EqualTo(expectedError));
        }

        [Test]
        public void LargeMessage_MatchesAndIsNotRetained()
        {
            ChatGuardClient client = DefaultsClient();
            var sb = new StringBuilder();
            for (int i = 0; i < 60000; i++)
            {
                sb.Append(i % 7 == 0 ? "\u4f60" : i % 11 == 0 ? "\"" : i % 13 == 0 ? "\u0001" : "a");
            }

            AssertSameBytes(client, new ModerationRequest(sb.ToString(), "p1"), "large message");
            Assert.That(Utf8RequestJsonSink.Rent().Buffer.Length, Is.LessThanOrEqualTo(64 * 1024), "a buffer grown past 64 KB must not be kept for the next request");
            AssertSameBytes(client, new ModerationRequest("small again"), "small message after a large one");
        }

        [Test]
        public void RandomRequests_MatchUtf8OfBuildRequestJson()
        {
            ChatGuardClient[] clients =
            {
                DefaultsClient(),
                new ChatGuardClient("cg_test_x", "https://api.example.com", language: "de", channelType: "team", ageRating: null),
                new ChatGuardClient("cg_test_x", "https://api.example.com", language: string.Empty, channelType: string.Empty, ageRating: string.Empty),
                new ChatGuardClient("cg_test_x", "https://api.example.com", language: "\u00fc\ud800", channelType: "g\"\\", ageRating: "18+\u0000"),
            };
            var random = new Random(20260923);
            for (int n = 0; n < 4000; n++)
            {
                ChatGuardClient client = clients[n % clients.Length];
                var request = new ModerationRequest
                {
                    message = RandomString(random, allowNull: true)!,
                    authorId = RandomString(random, allowNull: true),
                    accountAgeDays = RandomCount(random),
                    priorWarnings = RandomCount(random),
                    thread = random.Next(8) == 0 ? null! : RandomThread(random),
                    channelType = RandomString(random, allowNull: true),
                    language = RandomString(random, allowNull: true),
                    ageRating = RandomString(random, allowNull: true),
                    requestId = RandomString(random, allowNull: true),
                };
                AssertSameBytes(client, request, "random request #" + n);
            }
        }

        private static List<ThreadEntry> RandomThread(Random random)
        {
            int count = random.Next(9);
            var thread = new List<ThreadEntry>(count);
            for (int i = 0; i < count; i++)
            {
                thread.Add(new ThreadEntry(RandomString(random, allowNull: true)!, RandomString(random, allowNull: true)!));
            }

            return thread;
        }

        private static int RandomCount(Random random)
        {
            switch (random.Next(7))
            {
                case 0: return -1;
                case 1: return -5;
                case 2: return 0;
                case 3: return int.MaxValue;
                case 4: return int.MinValue;
                case 5: return random.Next(10);
                default: return random.Next();
            }
        }

        private static readonly string[] Pieces =
        {
            "a", "Z", "0", " ", "/", "\"", "\\", "\b", "\f", "\n", "\r", "\t", "\u0000", "\u0001", "\u001f", "\u007f", "\u0080",
            "\u00e9", "\u07ff", "\u0800", "\u0442", "\u4f60", "\u2028", "\u2029", "\ufeff", "\ufffd", "\uffff", "\ud83c\udfae",
            "\ud800\udc00", "\udbff\udfff", "\ud800", "\udbff", "\udc00", "\udfff", "hello", "\u0442\u044b \u0434\u0435\u0431\u0438\u043b",
        };

        private static string? RandomString(Random random, bool allowNull)
        {
            int kind = random.Next(10);
            if (allowNull && kind == 0)
            {
                return null;
            }

            if (kind == 1)
            {
                return string.Empty;
            }

            int pieces = random.Next(1, 24);
            var sb = new StringBuilder();
            for (int i = 0; i < pieces; i++)
            {
                sb.Append(Pieces[random.Next(Pieces.Length)]);
            }

            return sb.ToString();
        }
    }
}
