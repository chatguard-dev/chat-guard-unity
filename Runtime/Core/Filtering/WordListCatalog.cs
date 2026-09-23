#nullable enable
using System;
using System.Collections.Generic;

namespace ChatGuard.Core.Filtering
{
    /// <summary>The categories attached to a built-in word-list term.</summary>
    internal sealed class WordListEntry
    {
        public WordListEntry(VerdictCategory[] categories)
        {
            Categories = categories;
        }

        public VerdictCategory[] Categories { get; }
    }

    /// <summary>One language's built-in terms, indexed for matching.</summary>
    public sealed class LanguageWordList
    {
        internal LanguageWordList(string language, TermIndex<WordListEntry> index, int skipped)
        {
            Language = language;
            Index = index;
            SkippedEntries = skipped;
        }

        public string Language { get; }

        public int Count { get { return Index.Count; } }

        /// <summary>Entries rejected at load time (malformed lines); should be 0 for the built-in lists.</summary>
        public int SkippedEntries { get; }

        internal TermIndex<WordListEntry> Index { get; }
    }

    /// <summary>
    /// Built-in word lists for the local filter. Sources are the *.txt files next to this file
    /// (one <c>categories|term</c> per line); <c>WordLists.Generated.cs</c> is produced from them by
    /// <c>tools/WordListGen</c> so that the data travels with the C# sources into the Unity package.
    /// Each language's list is parsed on its first use, with the same normalizer the filter uses, so a
    /// client that only ever filters one or two languages never pays for the others.
    /// </summary>
    public sealed class WordListCatalog
    {
        private static readonly Lazy<WordListCatalog> BuiltInLazy = new Lazy<WordListCatalog>(LoadBuiltIn);

        // Built once and never mutated afterwards. Built-in slots parse their language on first access;
        // FromEntries slots are parsed up front.
        private readonly Dictionary<string, Lazy<LanguageWordList>> _lists;

        private WordListCatalog(string version, Dictionary<string, Lazy<LanguageWordList>> lists)
        {
            Version = version;
            _lists = lists;
        }

        /// <summary>The lists compiled into this assembly.</summary>
        public static WordListCatalog BuiltIn { get { return BuiltInLazy.Value; } }

        /// <summary>Content hash of the source lists, reported as the local model version.</summary>
        public string Version { get; }

        public IEnumerable<string> Languages { get { return _lists.Keys; } }

        public LanguageWordList? Get(string? language)
        {
            if (string.IsNullOrEmpty(language))
            {
                return null;
            }

            return _lists.TryGetValue(language!.Trim().ToLowerInvariant(), out Lazy<LanguageWordList> slot) ? slot.Value : null;
        }

        /// <summary>Builds a catalog from raw entries; used by tests and by the generator's self-check.</summary>
        public static WordListCatalog FromEntries(string version, IDictionary<string, string[]> entriesByLanguage)
        {
            var lists = new Dictionary<string, Lazy<LanguageWordList>>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string[]> pair in entriesByLanguage)
            {
                // Parsed eagerly: SkippedEntries/Count are known right away and the caller's arrays are not kept.
                string key = pair.Key.ToLowerInvariant();
                lists[key] = new Lazy<LanguageWordList>(Parse(key, pair.Value));
            }

            return new WordListCatalog(version, lists);
        }

        /// <summary>
        /// Parses one <c>categories|term</c> line. Returns false for blank lines, comments and malformed input.
        /// A leading <c>~</c> on the term marks a prefix match.
        /// </summary>
        public static bool TryParseLine(string? line, out VerdictCategory[] categories, out string term, out bool isPrefix)
        {
            categories = Array.Empty<VerdictCategory>();
            term = string.Empty;
            isPrefix = false;
            if (line == null)
            {
                return false;
            }

            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#')
            {
                return false;
            }

            int bar = trimmed.IndexOf('|');
            if (bar <= 0 || bar == trimmed.Length - 1)
            {
                return false;
            }

            string[] names = trimmed.Substring(0, bar).Split(',');
            var parsed = new List<VerdictCategory>(names.Length);
            foreach (string name in names)
            {
                if (!VerdictCategories.TryParse(name, out VerdictCategory category))
                {
                    return false;
                }

                if (!parsed.Contains(category))
                {
                    parsed.Add(category);
                }
            }

            string rest = trimmed.Substring(bar + 1).Trim();
            if (rest.StartsWith("~", StringComparison.Ordinal))
            {
                isPrefix = true;
                rest = rest.Substring(1).Trim();
            }

            if (rest.Length == 0)
            {
                return false;
            }

            categories = parsed.ToArray();
            term = rest;
            return true;
        }

        private static WordListCatalog LoadBuiltIn()
        {
            // Generated language codes are already lower case. The generated data is only materialized
            // (and parsed) when a language is first used.
            var lists = new Dictionary<string, Lazy<LanguageWordList>>(StringComparer.Ordinal);
            foreach (string language in GeneratedWordLists.Languages)
            {
                lists[language] = new Lazy<LanguageWordList>(() => Parse(language, GeneratedWordLists.Entries(language)));
            }

            return new WordListCatalog(GeneratedWordLists.Version, lists);
        }

        private static LanguageWordList Parse(string language, string[] lines)
        {
            var index = new TermIndex<WordListEntry>();
            int skipped = 0;
            foreach (string line in lines)
            {
                if (!TryParseLine(line, out VerdictCategory[] categories, out string term, out bool isPrefix))
                {
                    skipped++;
                    continue;
                }

                if (!index.TryAdd(term, isPrefix, new WordListEntry(categories), out _))
                {
                    skipped++;
                }
            }

            return new LanguageWordList(language, index, skipped);
        }
    }
}
