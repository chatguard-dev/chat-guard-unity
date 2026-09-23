#nullable enable
using System;
using System.Globalization;
using System.Text;
using ChatGuard.Core;
using ChatGuard.Unity;
using NUnit.Framework;
using Unity.Collections;

namespace ChatGuard.Tests
{
    /// <summary>
    /// Moderate reads a 200 body with ModerationResponseReader and falls back to DownloadHandler.text +
    /// ChatGuardClient.TryParseResponse for whatever the reader does not handle. These tests check that every body the
    /// reader handles gives exactly TryParseResponse's result for its UTF-8 text, and that the unusual bodies are left
    /// to TryParseResponse.
    /// </summary>
    public class ResponseReaderTests
    {
        private const int Latency = 7;

        private const string Sample = "{\"id\":\"019...\",\"action\":\"hide\",\"severity\":1.815,\"verdicts\":{\"insult\":{\"p\":0.91},\"threat\":{\"p\":0.03},\"hate\":{\"p\":0.05},\"sexual\":{\"p\":0.01},\"spam\":{\"p\":0.02},\"trading\":{\"p\":0.0}},\"target\":{\"choice\":\"other_user\",\"confidence\":0.84},\"degraded\":false,\"degraded_reason\":null,\"cached\":false,\"quota\":{\"used\":12345,\"limit\":50000,\"window_ends_at\":\"2026-09-23T00:00:00+00:00\"},\"model\":\"jev-1.13.0\",\"latency_ms\":212}";

        private const string Pretty = "{\n  \"id\": \"0199b2c4-7d1e-7a3b-9c4d-5e6f7a8b9c0d\",\n  \"action\": \"block\",\r\n\t\"severity\" : 2.5 ,\n  \"verdicts\": {\n    \"insult\": { \"p\": 0.5 },\n    \"threat\": { \"p\": 1e-3 },\n    \"hate\": { \"p\": 0.99 }\n  },\n  \"target\": { \"confidence\": 0.7, \"choice\": \"group\" },\n  \"degraded\": false,\n  \"cached\": true,\n  \"quota\": { \"limit\": 100, \"used\": 7 },\n  \"model\": \"jev-1.13.0\"\n}\n";

        private static ModerationResult? Fast(string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            return ModerationResponseReader.TryRead(bytes, bytes.Length, Latency);
        }

        private static void AssertHandled(string json)
        {
            ModerationResult? expected = ChatGuardClient.TryParseResponse(json, Latency);
            Assert.That(expected, Is.Not.Null, "TryParseResponse must accept: " + json);
            ModerationResult? actual = Fast(json);
            Assert.That(actual, Is.Not.Null, "the byte reader must handle: " + json);
            AssertSameResult(expected!, actual!, json);
        }

        /// <summary>
        /// The byte reader must return null for this body. Moderate computes
        /// <c>TryReadPlainResponse(...) ?? TryParseResponse(downloadHandler.text, ...)</c>, so a null here means the body
        /// gets exactly TryParseResponse's result, as it did before the reader existed.
        /// </summary>
        private static void AssertNotHandled(string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            Assert.That(ModerationResponseReader.TryRead(bytes, bytes.Length, Latency), Is.Null, "the byte reader must leave this to TryParseResponse: " + json);
        }

        /// <summary>Every field, doubles compared bit for bit.</summary>
        internal static void AssertSameResult(ModerationResult expected, ModerationResult actual, string context)
        {
            Assert.That(actual.Action, Is.EqualTo(expected.Action), "Action: " + context);
            AssertSameDouble(expected.Severity, actual.Severity, "Severity: " + context);
            foreach (VerdictCategory category in VerdictCategories.All)
            {
                AssertSameDouble(expected.Verdicts[category], actual.Verdicts[category], category + ": " + context);
            }

            Assert.That(actual.Target == null, Is.EqualTo(expected.Target == null), "Target: " + context);
            if (expected.Target != null)
            {
                Assert.That(actual.Target!.Choice, Is.EqualTo(expected.Target.Choice), "Target.Choice: " + context);
                AssertSameDouble(expected.Target.Confidence, actual.Target.Confidence, "Target.Confidence: " + context);
            }

            Assert.That(actual.Degraded, Is.EqualTo(expected.Degraded), "Degraded: " + context);
            Assert.That(actual.DegradedReason, Is.EqualTo(expected.DegradedReason), "DegradedReason: " + context);
            Assert.That(actual.Cached, Is.EqualTo(expected.Cached), "Cached: " + context);
            Assert.That(actual.Model, Is.EqualTo(expected.Model), "Model: " + context);
            Assert.That(actual.LatencyMs, Is.EqualTo(expected.LatencyMs), "LatencyMs: " + context);
            Assert.That(actual.Id, Is.EqualTo(expected.Id), "Id: " + context);
            Assert.That(actual.QuotaUsed, Is.EqualTo(expected.QuotaUsed), "QuotaUsed: " + context);
            Assert.That(actual.QuotaLimit, Is.EqualTo(expected.QuotaLimit), "QuotaLimit: " + context);
            Assert.That(actual.Source, Is.EqualTo(expected.Source), "Source: " + context);
            Assert.That(actual.Error, Is.EqualTo(expected.Error), "Error: " + context);
        }

        private static void AssertSameDouble(double expected, double actual, string context)
        {
            Assert.That(BitConverter.DoubleToInt64Bits(actual), Is.EqualTo(BitConverter.DoubleToInt64Bits(expected)), context + " (expected " + expected.ToString("R", CultureInfo.InvariantCulture) + ", got " + actual.ToString("R", CultureInfo.InvariantCulture) + ")");
        }

        [Test]
        public void SampleAndPrettyPrinted_AreHandled()
        {
            AssertHandled(Sample);
            AssertHandled(Pretty);
            AssertHandled(" \t\r\n" + Sample + " \n");
        }

        [Test]
        public void DegradedVariants_AreHandled()
        {
            foreach (string reason in new[] { "quota", "upstream", "upstream_rate_limit", "timeout", "offline" })
            {
                AssertHandled(Sample.Replace("\"degraded\":false,\"degraded_reason\":null", "\"degraded\":true,\"degraded_reason\":\"" + reason + "\""));
                AssertHandled("{\"action\":\"allow\",\"degraded\":true,\"degraded_reason\":\"" + reason + "\",\"model\":\"local-filter/3f2a9c1d0b77\",\"target\":null}");
            }

            AssertHandled(Sample.Replace("\"degraded\":false", "\"degraded\":null"));
            AssertHandled(Sample.Replace("\"cached\":false", "\"cached\":true"));
        }

        [Test]
        public void TargetVariants_AreHandled()
        {
            AssertHandled(Sample.Replace("\"target\":{\"choice\":\"other_user\",\"confidence\":0.84}", "\"target\":null"));
            AssertHandled(Sample.Replace("\"target\":{\"choice\":\"other_user\",\"confidence\":0.84}", "\"target\":{}"));
            AssertHandled(Sample.Replace("\"choice\":\"other_user\"", "\"choice\":null"));
            AssertHandled(Sample.Replace("\"choice\":\"other_user\",\"confidence\":0.84", "\"confidence\":0.84"));
            AssertHandled(Sample.Replace(",\"confidence\":0.84", string.Empty));
            AssertHandled(Sample.Replace("\"confidence\":0.84", "\"confidence\":null"));
            AssertHandled(Sample.Replace("\"confidence\":0.84", "\"confidence\":1.7"));
            AssertHandled(Sample.Replace("\"confidence\":0.84", "\"confidence\":-3"));
            AssertHandled(Sample.Replace("\"confidence\":0.84", "\"confidence\":0.84,\"why\":[\"a\",{\"b\":null}]"));
            foreach (string choice in new[] { "other_user", "group", "self", "nobody", "other" })
            {
                AssertHandled(Sample.Replace("\"other_user\"", "\"" + choice + "\""));
            }
        }

        [Test]
        public void MissingAndNullFields_AreHandled()
        {
            AssertHandled("{\"action\":\"allow\"}");
            AssertHandled("{\"action\":\"flag\",\"verdicts\":null,\"target\":null,\"quota\":null,\"model\":null,\"id\":null,\"severity\":null,\"degraded\":null,\"cached\":null,\"degraded_reason\":null}");
            AssertHandled("{\"action\":\"block\",\"verdicts\":{},\"target\":{},\"quota\":{}}");
            AssertHandled("{\"action\":\"hide\",\"verdicts\":{\"insult\":null,\"hate\":{},\"spam\":{\"p\":null},\"threat\":{\"q\":1,\"p\":0.25}}}");
            AssertHandled("{\"action\":\"allow\",\"quota\":{\"used\":null,\"limit\":null}}");
            AssertHandled("{\"action\":\"allow\",\"model\":\"\",\"id\":\"\"}");
            AssertHandled("{\"action\":\"allow\",\"id\":\"has\ttab\"}");
        }

        [Test]
        public void UnknownFieldsAndNesting_AreSkipped()
        {
            AssertHandled("{\"extra\":[1,[2,{\"a\":[true,false,null,\"x\",-0.5e+3]}],{},[]],\"action\":\"hide\",\"meta\":{\"deep\":{\"x\":[[[[]]]]}},\"z\":\"\"}");
            AssertHandled(Sample.Replace("\"insult\":{\"p\":0.91}", "\"insult\":{\"p\":0.91,\"calibrated\":true,\"raw\":[0.1,0.2]},\"unknown_category\":{\"p\":1}"));
            AssertHandled(Sample.Replace("\"limit\":50000", "\"limit\":50000,\"plan\":{\"name\":\"pro\",\"tiers\":[1,2,3]}"));
            AssertHandled("{\"action\":\"allow\",\"Action\":\"block\",\"ACTION\":1,\"verdict\":{\"insult\":{\"p\":1}},\"verdicts \":2}");
            AssertHandled("{\"action\":\"allow\",\"a\":[[[[[[[[[[[[[1]]]]]]]]]]]]]}");
        }

        [Test]
        public void Numbers_MatchMiniJsonExactly()
        {
            string[] numbers =
            {
                "0", "-0", "1", "-1", "0.1", "1e-3", "1E+2", "2.5e0", "0.1234567890123456789012345", "123456789012345678901234567890",
                "1e308", "-1e308", "4.9e-324", "1.7976931348623157e308", "12.9", "-12.9", "9007199254740993", "1e20", "-1e20",
                "00012", "+5", ".5", "5.", "1e5", "0.30000000000000004",
            };
            foreach (string n in numbers)
            {
                AssertHandled("{\"action\":\"allow\",\"severity\":" + n + ",\"verdicts\":{\"insult\":{\"p\":" + n + "}},\"target\":{\"choice\":\"self\",\"confidence\":" + n + "},\"quota\":{\"used\":" + n + ",\"limit\":" + n + "},\"n\":" + n + "}");
            }
        }

        [Test]
        public void ModelString_IsReusedAndIdIsNew()
        {
            ModerationResult first = Fast(Sample)!;
            ModerationResult second = Fast(Sample)!;
            Assert.That(second.Model, Is.EqualTo("jev-1.13.0"));
            Assert.That(ReferenceEquals(first.Model, second.Model), Is.True, "an unchanged model name reuses the last string");
            Assert.That(second.Id, Is.EqualTo("019..."));
            Assert.That(ReferenceEquals(first.Id, second.Id), Is.False, "each result gets its own id string");

            ModerationResult other = Fast(Sample.Replace("jev-1.13.0", "jev-1.13.1"))!;
            Assert.That(other.Model, Is.EqualTo("jev-1.13.1"));
            Assert.That(Fast(Sample)!.Model, Is.EqualTo("jev-1.13.0"));
            Assert.That(Fast(Sample.Replace("jev-1.13.0", "jev-1.13"))!.Model, Is.EqualTo("jev-1.13"));
        }

        [Test]
        public void UnusualBodies_AreLeftToTryParseResponse()
        {
            // Raw non-ASCII, a BOM, control bytes, backslashes (escapes), DEL.
            AssertNotHandled(Sample.Replace("jev-1.13.0", "j\u00e9v"));
            AssertNotHandled("\ufeff" + Sample);
            AssertNotHandled(Sample.Replace("\"cached\"", "\u000b\"cached\""));
            AssertNotHandled(Sample.Replace("\"cached\"", "\f\"cached\""));
            AssertNotHandled(Sample.Replace("019...", "019\u0000"));
            AssertNotHandled(Sample.Replace("019...", "019\u001f"));
            AssertNotHandled(Sample.Replace("019...", "019\u007f"));
            AssertNotHandled(Sample.Replace("jev-1.13.0", "jev\\/1.13.0"));
            AssertNotHandled(Sample.Replace("019...", "\\u0030"));
            AssertNotHandled(Sample.Replace("\"action\"", "\"act\\u0069on\""));
            AssertNotHandled(Sample.Replace("2026-09-23T00:00:00+00:00", "a\\\"b"));
            AssertNotHandled(Sample.Replace("\"hide\"", "\"h\\u0069de\""));

            // Not an object, invalid JSON, trailing content.
            foreach (string json in new[] { "", " ", "[]", "\"hide\"", "1", "null", "true", "{}", "{\"action\":\"hide\",}", "{\"action\":\"hide\"", "{\"action\" \"hide\"}", "{action:\"hide\"}", "{'action':'hide'}", "{\"action\":\"hide\"}}", "{\"action\":\"hide\"} x", "{\"action\":\"hide\"}{}", "{\"action\":\"hide\",\"a\":[1,]}", "{\"action\":\"hide\",\"a\":[,1]}", "{\"action\":\"hide\",\"a\":tru}", "{\"action\":\"hide\",\"a\":nul}", "{\"action\":\"hide\",\"a\":undefined}", "{\"action\":\"hide\",\"a\":NaN}", "{\"action\":\"hide\",\"a\":Infinity}", "{\"action\":\"hide\",,\"a\":1}", "{,\"action\":\"hide\"}", "{\"action\":\"hide\" \"a\":1}" })
            {
                AssertNotHandled(json);
            }

            // Every truncation of the sample.
            for (int i = 0; i < Sample.Length; i++)
            {
                AssertNotHandled(Sample.Substring(0, i));
            }

            // Duplicate known keys at every level.
            AssertNotHandled(Sample.Replace("\"action\":\"hide\"", "\"action\":\"hide\",\"action\":\"allow\""));
            AssertNotHandled(Sample.Replace("\"model\":\"jev-1.13.0\"", "\"model\":\"a\",\"model\":\"b\""));
            AssertNotHandled(Sample.Replace("\"id\":\"019...\"", "\"id\":\"a\",\"id\":null"));
            AssertNotHandled(Sample.Replace("\"severity\":1.815", "\"severity\":1.815,\"severity\":1"));
            AssertNotHandled(Sample.Replace("\"cached\":false", "\"cached\":false,\"cached\":true"));
            AssertNotHandled(Sample.Replace("\"degraded\":false", "\"degraded\":false,\"degraded\":true"));
            AssertNotHandled(Sample.Replace("\"degraded_reason\":null", "\"degraded_reason\":null,\"degraded_reason\":\"quota\""));
            AssertNotHandled(Sample.Replace("\"quota\":{", "\"quota\":null,\"quota\":{"));
            AssertNotHandled(Sample.Replace("\"target\":{", "\"target\":null,\"target\":{"));
            AssertNotHandled(Sample.Replace("\"verdicts\":{", "\"verdicts\":null,\"verdicts\":{"));
            AssertNotHandled(Sample.Replace("\"insult\":{\"p\":0.91}", "\"insult\":{\"p\":0.91},\"insult\":{\"p\":0.1}"));
            AssertNotHandled(Sample.Replace("\"insult\":{\"p\":0.91}", "\"insult\":{\"p\":0.91,\"p\":0.1}"));
            AssertNotHandled(Sample.Replace("\"choice\":\"other_user\"", "\"choice\":\"other_user\",\"choice\":\"self\""));
            AssertNotHandled(Sample.Replace("\"confidence\":0.84", "\"confidence\":0.84,\"confidence\":0.1"));
            AssertNotHandled(Sample.Replace("\"used\":12345", "\"used\":12345,\"used\":1"));
            AssertNotHandled(Sample.Replace("\"limit\":50000", "\"limit\":50000,\"limit\":1"));

            // Known keys with an unexpected JSON type.
            foreach (string replacement in new[]
            {
                "\"action\":1", "\"action\":null", "\"action\":true", "\"action\":[\"hide\"]", "\"action\":{\"v\":\"hide\"}",
            })
            {
                AssertNotHandled(Sample.Replace("\"action\":\"hide\"", replacement));
            }

            AssertNotHandled(Sample.Replace("\"action\":\"hide\",", string.Empty));
            AssertNotHandled(Sample.Replace("\"severity\":1.815", "\"severity\":\"1.815\""));
            AssertNotHandled(Sample.Replace("\"severity\":1.815", "\"severity\":true"));
            AssertNotHandled(Sample.Replace("\"verdicts\":{", "\"verdicts\":[],\"v\":{"));
            AssertNotHandled(Sample.Replace("\"verdicts\":{", "\"verdicts\":\"x\",\"v\":{"));
            AssertNotHandled(Sample.Replace("\"insult\":{\"p\":0.91}", "\"insult\":0.91"));
            AssertNotHandled(Sample.Replace("\"insult\":{\"p\":0.91}", "\"insult\":[0.91]"));
            AssertNotHandled(Sample.Replace("\"insult\":{\"p\":0.91}", "\"insult\":{\"p\":\"0.91\"}"));
            AssertNotHandled(Sample.Replace("\"insult\":{\"p\":0.91}", "\"insult\":{\"p\":false}"));
            AssertNotHandled(Sample.Replace("\"insult\":{\"p\":0.91}", "\"insult\":{\"p\":{}}"));
            AssertNotHandled(Sample.Replace("\"target\":{\"choice\":\"other_user\",\"confidence\":0.84}", "\"target\":[]"));
            AssertNotHandled(Sample.Replace("\"target\":{\"choice\":\"other_user\",\"confidence\":0.84}", "\"target\":\"other_user\""));
            AssertNotHandled(Sample.Replace("\"choice\":\"other_user\"", "\"choice\":1"));
            AssertNotHandled(Sample.Replace("\"choice\":\"other_user\"", "\"choice\":true"));
            AssertNotHandled(Sample.Replace("\"confidence\":0.84", "\"confidence\":\"0.84\""));
            AssertNotHandled(Sample.Replace("\"degraded\":false", "\"degraded\":\"false\""));
            AssertNotHandled(Sample.Replace("\"degraded\":false", "\"degraded\":0"));
            AssertNotHandled(Sample.Replace("\"cached\":false", "\"cached\":\"true\""));
            AssertNotHandled(Sample.Replace("\"degraded_reason\":null", "\"degraded_reason\":1"));
            AssertNotHandled(Sample.Replace("\"degraded_reason\":null", "\"degraded_reason\":false"));
            AssertNotHandled(Sample.Replace("\"model\":\"jev-1.13.0\"", "\"model\":1"));
            AssertNotHandled(Sample.Replace("\"model\":\"jev-1.13.0\"", "\"model\":{\"name\":\"jev\"}"));
            AssertNotHandled(Sample.Replace("\"id\":\"019...\"", "\"id\":19"));
            AssertNotHandled(Sample.Replace("\"id\":\"019...\"", "\"id\":false"));
            AssertNotHandled(Sample.Replace("\"quota\":{", "\"quota\":[],\"q\":{"));
            AssertNotHandled(Sample.Replace("\"used\":12345", "\"used\":\"12345\""));
            AssertNotHandled(Sample.Replace("\"limit\":50000", "\"limit\":true"));

            // Enum values that are not exactly a lowercase wire name.
            foreach (string action in new[] { "Hide", "HIDE", " hide", "hide ", "explode", string.Empty, "allow\t" })
            {
                AssertNotHandled(Sample.Replace("\"hide\"", "\"" + action + "\""));
            }

            foreach (string choice in new[] { "Other_User", "OTHER", "someone", string.Empty, " self" })
            {
                AssertNotHandled(Sample.Replace("\"other_user\"", "\"" + choice + "\""));
            }

            foreach (string reason in new[] { "QUOTA", "Quota", "none", string.Empty, "timeout ", "rate_limit" })
            {
                AssertNotHandled(Sample.Replace("\"degraded_reason\":null", "\"degraded_reason\":\"" + reason + "\""));
            }

            // Numbers MiniJson's parser rejects, and tokens longer than the reader handles.
            foreach (string n in new[] { "1.2.3", "--1", "+-1", "1e", "-", ".", "e5", "1e+", "0x10" })
            {
                AssertNotHandled(Sample.Replace("\"severity\":1.815", "\"severity\":" + n));
            }

            AssertNotHandled(Sample.Replace("\"severity\":1.815", "\"severity\":0." + new string('1', ModerationResponseReader.MaxNumberLength)));
            AssertNotHandled(Sample.Replace("\"latency_ms\":212", "\"latency_ms\":" + new string('2', ModerationResponseReader.MaxNumberLength + 1)));

            // Nesting deeper than the reader handles.
            AssertNotHandled("{\"action\":\"allow\",\"deep\":" + new string('[', ModerationResponseReader.MaxDepth) + new string(']', ModerationResponseReader.MaxDepth) + "}");
        }

        /// <summary>
        /// A truncated body is left to TryParseResponse, which returns null for it, so Moderate reports every truncation
        /// as an unparseable response rather than an exception message.
        /// </summary>
        [Test]
        public void EveryTruncation_IsUnparseableOnBothPaths()
        {
            // Pretty ends with a newline; without it, every shorter prefix is cut before the closing brace.
            foreach (string body in new[] { Sample, Pretty.TrimEnd() })
            {
                for (int i = 0; i < body.Length; i++)
                {
                    string prefix = body.Substring(0, i);
                    AssertNotHandled(prefix);
                    Assert.That(ChatGuardClient.TryParseResponse(prefix, Latency), Is.Null, prefix);
                }
            }
        }

        [Test]
        public void NumberTokenAtTheLengthLimit_IsHandled()
        {
            AssertHandled(Sample.Replace("\"severity\":1.815", "\"severity\":0." + new string('1', ModerationResponseReader.MaxNumberLength - 2)));
        }

        [Test]
        public void NestingAtMaxDepth_IsHandled()
        {
            // The root object is depth 1, so MaxDepth - 1 arrays under it reach exactly MaxDepth.
            AssertHandled("{\"action\":\"allow\",\"deep\":" + new string('[', ModerationResponseReader.MaxDepth - 1) + new string(']', ModerationResponseReader.MaxDepth - 1) + "}");
        }

        /// <summary>
        /// Moderate reads the body from DownloadHandler.nativeData. The sample is padded with trailing whitespace, which
        /// the byte reader handles, so only the length decides: up to MaxBodyLength it is handled from native memory,
        /// one byte more is left to TryParseResponse without being copied.
        /// </summary>
        [Test]
        public void NativeBody_IsHandledUpToMaxBodyLength()
        {
            AssertNativeBody(Encoding.UTF8.GetBytes(Sample), handled: true);
            AssertNativeBody(Encoding.UTF8.GetBytes(Sample.PadRight(ModerationResponseReader.MaxBodyLength)), handled: true);
            AssertNativeBody(Encoding.UTF8.GetBytes(Sample.PadRight(ModerationResponseReader.MaxBodyLength + 1)), handled: false);
            Assert.That(ModerationResponseReader.TryRead(default(NativeArray<byte>.ReadOnly), Latency), Is.Null, "no native data");
        }

        private static void AssertNativeBody(byte[] bytes, bool handled)
        {
            string context = bytes.Length + " bytes";
            Assert.That(ModerationResponseReader.TryRead(bytes, bytes.Length, Latency), Is.Not.Null, "byte[] overload: " + context);
            var native = new NativeArray<byte>(bytes, Allocator.Persistent);
            try
            {
                ModerationResult? actual = ModerationResponseReader.TryRead(native.AsReadOnly(), Latency);
                if (handled)
                {
                    Assert.That(actual, Is.Not.Null, "native: " + context);
                    AssertSameResult(ChatGuardClient.TryParseResponse(Encoding.UTF8.GetString(bytes), Latency)!, actual!, context);
                }
                else
                {
                    Assert.That(actual, Is.Null, "native: " + context);
                }
            }
            finally
            {
                native.Dispose();
            }
        }

        [Test]
        public void ServerShapedBodies_AreAllHandled()
        {
            string[] actions = { "allow", "flag", "hide", "block" };
            string[] choices = { "other_user", "group", "self", "nobody", "other" };
            string[] reasons = { "quota", "upstream", "upstream_rate_limit", "timeout", "offline" };
            var random = new Random(7331);
            for (int n = 0; n < 1000; n++)
            {
                var sb = new StringBuilder("{");
                sb.Append("\"id\":").Append(random.Next(10) == 0 ? "null" : "\"0199b2c4-7d1e-7a3b-9c4d-" + random.Next().ToString("x12", CultureInfo.InvariantCulture) + "\"");
                sb.Append(",\"action\":\"").Append(actions[random.Next(actions.Length)]).Append('"');
                sb.Append(",\"severity\":").Append(Number(random, 3));
                sb.Append(",\"verdicts\":{");
                for (int c = 0; c < VerdictCategories.Count; c++)
                {
                    sb.Append(c == 0 ? string.Empty : ",").Append('"').Append(VerdictCategories.ToWireName((VerdictCategory)c)).Append("\":{\"p\":").Append(Number(random, 1)).Append('}');
                }

                sb.Append('}');
                sb.Append(",\"target\":").Append(random.Next(4) == 0 ? "null" : "{\"choice\":\"" + choices[random.Next(choices.Length)] + "\",\"confidence\":" + Number(random, 1) + "}");
                bool degraded = random.Next(3) == 0;
                sb.Append(",\"degraded\":").Append(degraded ? "true" : "false");
                sb.Append(",\"degraded_reason\":").Append(degraded ? "\"" + reasons[random.Next(reasons.Length)] + "\"" : "null");
                sb.Append(",\"cached\":").Append(random.Next(2) == 0 ? "true" : "false");
                sb.Append(",\"quota\":{\"used\":").Append(random.Next()).Append(",\"limit\":").Append(random.Next()).Append(",\"window_ends_at\":\"2026-09-23T00:00:00+00:00\"}");
                sb.Append(",\"model\":\"").Append(random.Next(5) == 0 ? "local-filter/3f2a9c1d0b77" : "jev-1.13.0").Append('"');
                sb.Append(",\"latency_ms\":").Append(random.Next(2000));
                sb.Append('}');
                AssertHandled(sb.ToString());
            }
        }

        private static string Number(Random random, double scale)
        {
            switch (random.Next(6))
            {
                case 0: return "0";
                case 1: return "1";
                case 2: return (random.NextDouble() * scale).ToString("R", CultureInfo.InvariantCulture);
                case 3: return (random.NextDouble() * 1e-6).ToString("R", CultureInfo.InvariantCulture);
                case 4: return (random.NextDouble() * scale).ToString("0.###", CultureInfo.InvariantCulture);
                default: return (random.NextDouble() * scale * 1.5 - 0.25).ToString("R", CultureInfo.InvariantCulture);
            }
        }

        [Test]
        public void MutatedBodies_AreHandledIdenticallyOrLeftToTryParseResponse()
        {
            string[] seeds =
            {
                Sample,
                Pretty,
                Sample.Replace("\"degraded\":false,\"degraded_reason\":null", "\"degraded\":true,\"degraded_reason\":\"timeout\"").Replace("\"target\":{\"choice\":\"other_user\",\"confidence\":0.84}", "\"target\":null"),
                "{\"action\":\"flag\",\"extra\":[1,{\"a\":[true,null]}],\"verdicts\":{\"spam\":{\"p\":0.5}},\"quota\":{\"used\":1e3,\"limit\":-2.5}}",
            };
            byte[] alphabet = Encoding.ASCII.GetBytes("{}[]\",:0123456789.-+eEtrufalsn \t\n\r\\/xpqa");
            var random = new Random(4242);
            int handled = 0;
            const int Runs = 6000;
            for (int n = 0; n < Runs; n++)
            {
                byte[] bytes = Mutate(Encoding.UTF8.GetBytes(seeds[n % seeds.Length]), random, alphabet);
                ModerationResult? fast = ModerationResponseReader.TryRead(bytes, bytes.Length, Latency);
                if (fast == null)
                {
                    continue;
                }

                handled++;
                string text = Encoding.UTF8.GetString(bytes);
                ModerationResult? expected = ChatGuardClient.TryParseResponse(text, Latency);
                Assert.That(expected, Is.Not.Null, "handled a body TryParseResponse rejects: " + text);
                AssertSameResult(expected!, fast, text);
            }

            // The mutations must exercise the reader's accepting paths, not only its bail-outs.
            TestContext.Out.WriteLine("The byte reader handled " + handled + " of " + Runs + " mutated bodies.");
            Assert.That(handled, Is.GreaterThan(Runs / 20), "handled " + handled + " of " + Runs);
        }

        private static byte[] Mutate(byte[] body, Random random, byte[] alphabet)
        {
            var bytes = new System.Collections.Generic.List<byte>(body);
            int edits = 1 + random.Next(3);
            for (int e = 0; e < edits; e++)
            {
                int at = random.Next(bytes.Count + 1);
                switch (random.Next(5))
                {
                    case 0:
                        if (at < bytes.Count)
                        {
                            bytes.RemoveAt(at);
                        }

                        break;
                    case 1:
                        bytes.Insert(at, alphabet[random.Next(alphabet.Length)]);
                        break;
                    case 2:
                        if (at < bytes.Count)
                        {
                            bytes[at] = alphabet[random.Next(alphabet.Length)];
                        }

                        break;
                    case 3:
                        if (at < bytes.Count)
                        {
                            int length = Math.Min(bytes.Count - at, 1 + random.Next(12));
                            bytes.InsertRange(random.Next(bytes.Count + 1), bytes.GetRange(at, length));
                        }

                        break;
                    default:
                        // Digits are where most accepted mutations come from: change one inside a number.
                        int digit = bytes.FindIndex(at < bytes.Count ? at : 0, b => b >= (byte)'0' && b <= (byte)'9');
                        if (digit >= 0)
                        {
                            bytes[digit] = (byte)('0' + random.Next(10));
                        }

                        break;
                }
            }

            return bytes.ToArray();
        }

        [Test]
        public void ContentTypeCharset_MatchesDownloadHandlerTextDecoding()
        {
            foreach (string? handled in new[]
            {
                null, string.Empty, "application/json", "application/json; charset=utf-8", "application/json; charset=UTF-8",
                "application/json;charset=\"utf-8\"", "application/json; charset=utf-8; x=1", "application/json; charset = utf-8",
                "application/json; CHARSET=Utf-8", "application/json; charset", "text/plain; x=1; charset=utf-8",
                "application/json; charset=\"'utf-8'\"",
            })
            {
                Assert.That(ModerationResponseReader.IsUtf8ContentType(handled), Is.True, handled ?? "null");
            }

            // Another charset, a value DownloadHandler.text would pass to Encoding.GetEncoding as something other than
            // "utf-8" (it may log a warning and fall back to UTF-8), or a header with a character outside printable
            // ASCII (the tab and NBSP cases: DownloadHandler.text trims those away and decodes UTF-8, but
            // IsUtf8ContentType declines any such header): all are left to the string path, which decodes them as it
            // always did.
            foreach (string notHandled in new[]
            {
                "application/json; charset=utf-16", "application/json; charset=utf8", "application/json; charset=iso-8859-1",
                "application/json; charset=utf-8 ;x", "application/json; charset='utf-8' ; x=1", "application/json; charset=\tutf-8", "application/json; charset=utf-8\u00a0",
                "application/json; charset=", "application/json; charset=utf-7", "application/json; charset=unicode-1-1-utf-8",
            })
            {
                Assert.That(ModerationResponseReader.IsUtf8ContentType(notHandled), Is.False, notHandled);
            }
        }
    }
}
