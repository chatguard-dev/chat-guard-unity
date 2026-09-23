#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using ChatGuard.Unity;
using NUnit.Framework;

namespace ChatGuard.Tests
{
    /// <summary>
    /// MiniJson.Parse: strings with and without escapes (the fast path and the escape loop), keys, numbers, errors,
    /// truncated input and the nesting limit.
    /// </summary>
    public class MiniJsonTests
    {
        private const string SampleResponse = "{\"id\":\"019...\",\"action\":\"hide\",\"severity\":1.815,\"verdicts\":{\"insult\":{\"p\":0.91},\"threat\":{\"p\":0.03},\"hate\":{\"p\":0.05},\"sexual\":{\"p\":0.01},\"spam\":{\"p\":0.02},\"trading\":{\"p\":0.0}},\"target\":{\"choice\":\"other_user\",\"confidence\":0.84},\"degraded\":false,\"degraded_reason\":null,\"cached\":false,\"quota\":{\"used\":12345,\"limit\":50000,\"window_ends_at\":\"2026-09-23T00:00:00+00:00\"},\"model\":\"jev-1.13.0\",\"latency_ms\":212}";

        [Test]
        public void Parse_PlainStrings()
        {
            Assert.That(MiniJson.Parse("\"hello world\""), Is.EqualTo("hello world"));
            Assert.That(MiniJson.Parse("\"\""), Is.EqualTo(string.Empty));
            Assert.That(MiniJson.Parse(" \"caf\u00e9 \ud83c\udfae\" "), Is.EqualTo("caf\u00e9 \ud83c\udfae"));
            Dictionary<string, object?> obj = MiniJson.AsObject(MiniJson.Parse("{\"model\":\"jev-1.13.0\",\"id\":\"019...\",\"empty\":\"\"}"))!;
            Assert.That(obj.Keys, Is.EqualTo(new[] { "model", "id", "empty" }));
            Assert.That(obj["model"], Is.EqualTo("jev-1.13.0"));
            Assert.That(obj["id"], Is.EqualTo("019..."));
            Assert.That(obj["empty"], Is.EqualTo(string.Empty));
        }

        [Test]
        public void Parse_EscapeAfterPlainPrefix()
        {
            Assert.That(MiniJson.Parse("\"pre\\nfix\""), Is.EqualTo("pre\nfix"));
            Assert.That(MiniJson.Parse("\"say \\\"hi\\\" \\\\ bye\""), Is.EqualTo("say \"hi\" \\ bye"));
            Assert.That(MiniJson.Parse("\"\\tleading\""), Is.EqualTo("\tleading"));
            Assert.That(MiniJson.Parse("\"trailing\\/\""), Is.EqualTo("trailing/"));
            Assert.That(MiniJson.Parse("\"a\\b\\f\\n\\r\\tz\""), Is.EqualTo("a\b\f\n\r\tz"));
            Assert.That(MiniJson.Parse("[\"a\",\"b\\\"c\",\"\"]"), Is.EqualTo(new object?[] { "a", "b\"c", string.Empty }));
        }

        [Test]
        public void Parse_UnicodeEscapes()
        {
            Assert.That(MiniJson.Parse("\"caf\\u00e9 \\u0442\\u044B\""), Is.EqualTo("caf\u00e9 \u0442\u044b"));
            Assert.That(MiniJson.Parse("\"\\ud83c\\udfae\""), Is.EqualTo("\ud83c\udfae"));
            Assert.That(MiniJson.Parse("\"lone \\ud800 surrogate\""), Is.EqualTo("lone \ud800 surrogate"));
            Assert.That(MiniJson.Parse("\"nul\\u0000byte\""), Is.EqualTo("nul\u0000byte"));
        }

        [Test]
        public void Parse_KeysWithEscapes()
        {
            Dictionary<string, object?> obj = MiniJson.AsObject(MiniJson.Parse("{\"a\\u0041\":1,\"k\\\"ey\":\"v\",\"line\\nbreak\":true,\"plain\":null}"))!;
            Assert.That(obj.Keys, Is.EqualTo(new[] { "aA", "k\"ey", "line\nbreak", "plain" }));
            Assert.That(obj["aA"], Is.EqualTo(1.0));
            Assert.That(obj["k\"ey"], Is.EqualTo("v"));
            Assert.That(obj["line\nbreak"], Is.EqualTo(true));
            Assert.That(obj["plain"], Is.Null);
        }

        [Test]
        public void Parse_UnterminatedString_ThrowsFormatException()
        {
            foreach (string json in new[] { "\"abc", "\"", "\"pre\\nfix", "\"ends with backslash\\", "{\"a\":\"b", "[\"x" })
            {
                FormatException ex = Assert.Throws<FormatException>(() => MiniJson.Parse(json));
                Assert.That(ex.Message, Is.EqualTo("Unterminated string"), json);
            }
        }

        [Test]
        public void Parse_BadEscapesAndMissingKey_ThrowFormatException()
        {
            Assert.That(Assert.Throws<FormatException>(() => MiniJson.Parse("\"ab\\xcd\"")).Message, Is.EqualTo("Bad escape \\x"));
            Assert.That(Assert.Throws<FormatException>(() => MiniJson.Parse("\"ab\\u12\"")).Message, Is.EqualTo("Bad unicode escape"));
            Assert.That(Assert.Throws<FormatException>(() => MiniJson.Parse("{\"k\":1,x}")).Message, Is.EqualTo("Expected string at 7"));
        }

        [Test]
        public void Parse_Numbers()
        {
            List<object?> list = MiniJson.AsArray(MiniJson.Parse("[0, 42, -7, 1.5, -0.25, 1e3, 2E-2, -2.5e+3, 12345678901234567890, 0.91]"))!;
            Assert.That(list, Is.EqualTo(new object?[] { 0.0, 42.0, -7.0, 1.5, -0.25, 1000.0, 0.02, -2500.0, 12345678901234567890.0, 0.91 }));
            Assert.That(MiniJson.GetNumber(MiniJson.AsObject(MiniJson.Parse("{\"severity\":1.815}")), "severity"), Is.EqualTo(1.815));
        }

        [Test]
        public void Parse_InvalidNumbers_ThrowFormatException()
        {
            Assert.That(Assert.Throws<FormatException>(() => MiniJson.Parse("1e")).Message, Is.EqualTo("Invalid number at 0"));
            Assert.That(Assert.Throws<FormatException>(() => MiniJson.Parse("[1,--2]")).Message, Is.EqualTo("Invalid number at 3"));
            Assert.That(Assert.Throws<FormatException>(() => MiniJson.Parse("{\"a\":x}")).Message, Is.EqualTo("Invalid number at 5"));
            Assert.That(Assert.Throws<FormatException>(() => MiniJson.Parse("12abc")).Message, Is.EqualTo("Trailing characters after JSON value"));
        }

        [Test]
        public void Parse_EveryTruncationOfAResponse_ThrowsFormatException()
        {
            Assert.That(MiniJson.Parse(SampleResponse), Is.Not.Null);
            for (int i = 0; i < SampleResponse.Length; i++)
            {
                string prefix = SampleResponse.Substring(0, i);
                Assert.Throws<FormatException>(() => MiniJson.Parse(prefix), prefix);
            }
        }

        [Test]
        public void Parse_EndOfInputWhereAValueOrKeyShouldFollow_ThrowsFormatException()
        {
            // A key expected at the end of the input used to read past it (IndexOutOfRangeException).
            Assert.That(Assert.Throws<FormatException>(() => MiniJson.Parse("{\"action\":\"hide\",")).Message, Is.EqualTo("Expected string at 17"));
            Assert.That(Assert.Throws<FormatException>(() => MiniJson.Parse("{")).Message, Is.EqualTo("Expected string at 1"));
            Assert.That(Assert.Throws<FormatException>(() => MiniJson.Parse("{ \n")).Message, Is.EqualTo("Expected string at 3"));
            Assert.That(Assert.Throws<FormatException>(() => MiniJson.Parse("{\"a\"")).Message, Is.EqualTo("Expected ':' at 4"));
            Assert.That(Assert.Throws<FormatException>(() => MiniJson.Parse("{\"a\":")).Message, Is.EqualTo("Unexpected end of JSON"));
            Assert.That(Assert.Throws<FormatException>(() => MiniJson.Parse("{\"a\":1")).Message, Is.EqualTo("Expected ',' or '}' at 6"));
            Assert.That(Assert.Throws<FormatException>(() => MiniJson.Parse("[")).Message, Is.EqualTo("Unexpected end of JSON"));
            Assert.That(Assert.Throws<FormatException>(() => MiniJson.Parse("[1,")).Message, Is.EqualTo("Unexpected end of JSON"));
            Assert.That(Assert.Throws<FormatException>(() => MiniJson.Parse("[1")).Message, Is.EqualTo("Expected ',' or ']' at 2"));
            Assert.That(Assert.Throws<FormatException>(() => MiniJson.Parse(string.Empty)).Message, Is.EqualTo("Unexpected end of JSON"));
            Assert.That(Assert.Throws<FormatException>(() => MiniJson.Parse("  ")).Message, Is.EqualTo("Unexpected end of JSON"));

            // Literals cut short.
            Assert.That(Assert.Throws<FormatException>(() => MiniJson.Parse("t")).Message, Is.EqualTo("Expected true at 0"));
            Assert.That(Assert.Throws<FormatException>(() => MiniJson.Parse("[fals")).Message, Is.EqualTo("Expected false at 1"));
            Assert.That(Assert.Throws<FormatException>(() => MiniJson.Parse("{\"a\":nul")).Message, Is.EqualTo("Expected null at 5"));
            Assert.That(MiniJson.Parse("[true,false,null]"), Is.EqualTo(new object?[] { true, false, null }));
        }

        [Test]
        public void Parse_NestingUpToMaxDepth_IsRead()
        {
            // The outermost container is level 1, so MaxDepth brackets reach exactly the limit.
            object? arrays = MiniJson.Parse(new string('[', MiniJson.MaxDepth) + "1" + new string(']', MiniJson.MaxDepth));
            for (int level = 1; level < MiniJson.MaxDepth; level++)
            {
                arrays = MiniJson.AsArray(arrays)![0];
            }

            Assert.That(MiniJson.AsArray(arrays), Is.EqualTo(new object?[] { 1.0 }));

            object? objects = MiniJson.Parse(Nested("{\"a\":", "1", "}", MiniJson.MaxDepth));
            for (int level = 1; level < MiniJson.MaxDepth; level++)
            {
                objects = MiniJson.GetObject(MiniJson.AsObject(objects), "a");
            }

            Assert.That(MiniJson.GetNumber(MiniJson.AsObject(objects), "a"), Is.EqualTo(1.0));
            Assert.That(MiniJson.Parse(Nested("[{\"k\":", "1", "}]", MiniJson.MaxDepth / 2)), Is.Not.Null);
        }

        [Test]
        public void Parse_NestingDeeperThanMaxDepth_ThrowsFormatException()
        {
            int tooDeep = MiniJson.MaxDepth + 1;
            FormatException arrays = Assert.Throws<FormatException>(() => MiniJson.Parse(new string('[', tooDeep) + new string(']', tooDeep)));
            Assert.That(arrays.Message, Is.EqualTo("Nesting deeper than " + MiniJson.MaxDepth + " levels at " + MiniJson.MaxDepth));
            Assert.Throws<FormatException>(() => MiniJson.Parse(Nested("{\"a\":", "1", "}", tooDeep)));
            Assert.Throws<FormatException>(() => MiniJson.Parse(Nested("[{\"k\":", "[]", "}]", MiniJson.MaxDepth / 2)));
        }

        /// <summary>Nesting deep enough to overflow the stack of an unlimited recursive reader fails like other malformed input.</summary>
        [Test]
        public void Parse_HugeNesting_ThrowsFormatExceptionWithoutOverflowingTheStack()
        {
            const int Levels = 100000;
            Assert.Throws<FormatException>(() => MiniJson.Parse(new string('[', Levels) + new string(']', Levels)));
            Assert.Throws<FormatException>(() => MiniJson.Parse(new string('[', Levels)));
            Assert.Throws<FormatException>(() => MiniJson.Parse(Nested("{\"a\":", "1", "}", Levels)));
        }

        /// <summary><paramref name="open"/> <paramref name="levels"/> times, then <paramref name="inner"/>, then as many <paramref name="close"/>.</summary>
        private static string Nested(string open, string inner, string close, int levels)
        {
            var sb = new StringBuilder((open.Length + close.Length) * levels + inner.Length);
            for (int i = 0; i < levels; i++)
            {
                sb.Append(open);
            }

            sb.Append(inner);
            for (int i = 0; i < levels; i++)
            {
                sb.Append(close);
            }

            return sb.ToString();
        }
    }
}
