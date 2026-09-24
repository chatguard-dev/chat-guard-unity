#nullable enable
using System.Collections.Generic;
using System.IO;
using ChatGuard.Core;
using ChatGuard.Core.Filtering;
using ChatGuard.Core.Text;
using NUnit.Framework;

namespace ChatGuard.Tests
{
    /// <summary>
    /// Runs tests/vectors/local-filter.json, copied to <c>Vectors/</c>, against this package's copy of Core. The .NET
    /// Core tests run the same cases, so both copies of Core must agree.
    /// </summary>
    public class LocalFilterVectorTests
    {
        private static Dictionary<string, object?> LoadVectors()
        {
            string path = Path.Combine(Path.GetDirectoryName(GetThisFilePath())!, "Vectors", "local-filter.json");
            return MiniJson.AsObject(MiniJson.Parse(File.ReadAllText(path)))!;
        }

        private static string GetThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "")
        {
            return path;
        }

        [Test]
        public void NormalizerVectors()
        {
            List<object?> cases = MiniJson.AsArray(LoadVectors()["normalizer"])!;
            Assert.That(cases.Count, Is.GreaterThan(20));
            foreach (object? item in cases)
            {
                Dictionary<string, object?> c = MiniJson.AsObject(item)!;
                string input = MiniJson.GetString(c, "input") ?? string.Empty;
                string expected = MiniJson.GetString(c, "expected") ?? string.Empty;
                Assert.That(TextNormalizer.Normalize(input), Is.EqualTo(expected), MiniJson.GetString(c, "id"));
            }
        }

        [Test]
        public void FilterVectors()
        {
            var filter = new LocalFilter();
            List<object?> cases = MiniJson.AsArray(LoadVectors()["filter"])!;
            Assert.That(cases.Count, Is.GreaterThan(50));
            foreach (object? item in cases)
            {
                Dictionary<string, object?> c = MiniJson.AsObject(item)!;
                string id = MiniJson.GetString(c, "id") ?? "?";
                string message = MiniJson.GetString(c, "message") ?? string.Empty;
                string? language = MiniJson.GetString(c, "language");
                var expected = new List<string>();
                foreach (object? cat in MiniJson.AsArray(c["categories"])!)
                {
                    expected.Add((string)cat!);
                }

                expected.Sort(string.CompareOrdinal);
                LocalFilterResult result = filter.Evaluate(NormalizedMessage.Create(message), language);
                var actual = new List<string>();
                foreach (VerdictCategory category in result.MatchedCategories)
                {
                    actual.Add(VerdictCategories.ToWireName(category));
                }

                actual.Sort(string.CompareOrdinal);
                Assert.That(actual, Is.EqualTo(expected), id);
            }
        }

        [Test]
        public void BuiltInCatalog_HasAllLanguages()
        {
            var languages = new List<string>(WordListCatalog.BuiltIn.Languages);
            foreach (string lang in new[] { "en", "ru", "sr", "pl", "tr", "de", "es", "pt" })
            {
                Assert.That(languages, Does.Contain(lang));
            }
        }
    }
}
