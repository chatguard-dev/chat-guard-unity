#nullable enable
using System;
using System.Collections.Generic;
using ChatGuard.Unity;
using NUnit.Framework;

namespace ChatGuard.Tests
{
    /// <summary>MiniJson.Parse: strings with and without escapes (the fast path and the escape loop), keys, numbers, errors.</summary>
    public class MiniJsonTests
    {
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
    }
}
