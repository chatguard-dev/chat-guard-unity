#nullable enable
using System;
using System.Collections.Generic;

namespace ChatGuard.Core.Filtering
{
    /// <summary>The categories of one word-list term.</summary>
    internal sealed class WordListEntry
    {
        public WordListEntry(VerdictCategory[] categories)
        {
            Categories = categories;
        }

        public VerdictCategory[] Categories { get; }
    }

    /// <summary>One language's word list, parsed and indexed for matching.</summary>
    public sealed class LanguageWordList
    {
        internal LanguageWordList(string language, TermIndex<WordListEntry> index, int skipped)
        {
            Language = language;
            Index = index;
            SkippedEntries = skipped;
        }

        /// <summary>Lowercase language code, such as <c>en</c>.</summary>
        public string Language { get; }

        public int Count { get { return Index.Count; } }

        /// <summary>
        /// Lines not indexed: blank, comment and malformed lines, and terms the index rejects, such as a too-short
        /// prefix. Always 0 for the built-in lists.
        /// </summary>
        public int SkippedEntries { get; }

        internal TermIndex<WordListEntry> Index { get; }
    }

    /// <summary>
    /// The word lists of the local filter, one per language. <see cref="BuiltIn"/> holds the lists that ship with
    /// Chat Guard.
    /// </summary>
    /// <remarks>
    /// The built-in lists are generated into <c>WordLists.Generated.cs</c> so they travel with the C# sources into the
    /// Unity package. Each is parsed on first use, so languages a client never filters cost nothing. Safe to use from
    /// any thread.
    /// </remarks>
    public sealed class WordListCatalog
    {
        private static readonly Lazy<WordListCatalog> BuiltInLazy = new Lazy<WordListCatalog>(LoadBuiltIn);

        // Never mutated after construction, so concurrent reads are safe.
        private readonly Dictionary<string, Lazy<LanguageWordList>> _lists;

        private WordListCatalog(string version, Dictionary<string, Lazy<LanguageWordList>> lists)
        {
            Version = version;
            _lists = lists;
        }

        /// <summary>The lists compiled into this assembly.</summary>
        public static WordListCatalog BuiltIn { get { return BuiltInLazy.Value; } }

        /// <summary>
        /// Identifies the list contents; for <see cref="BuiltIn"/>, the first 12 hex characters of a SHA-256 hash of
        /// the source lists. Local results report it in their model name (see <see cref="LocalModeration.ModelName"/>).
        /// </summary>
        public string Version { get; }

        public IEnumerable<string> Languages { get { return _lists.Keys; } }

        /// <summary>
        /// Returns the list for a language code such as <c>en</c>, ignoring case and surrounding spaces, or null when
        /// there is none. Region codes are not stripped, so <c>pt-BR</c> returns null.
        /// </summary>
        public LanguageWordList? Get(string? language)
        {
            if (string.IsNullOrEmpty(language))
            {
                return null;
            }

            return _lists.TryGetValue(language!.Trim().ToLowerInvariant(), out Lazy<LanguageWordList> slot) ? slot.Value : null;
        }

        /// <summary>
        /// Builds a catalog from raw lines in the <see cref="TryParseLine"/> format, keyed by language code. Keys are
        /// lowercased, and every list is parsed at once.
        /// </summary>
        public static WordListCatalog FromEntries(string version, IDictionary<string, string[]> entriesByLanguage)
        {
            var lists = new Dictionary<string, Lazy<LanguageWordList>>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string[]> pair in entriesByLanguage)
            {
                // Eager, so SkippedEntries and Count are known at once and the caller's arrays are not kept.
                string key = pair.Key.ToLowerInvariant();
                lists[key] = new Lazy<LanguageWordList>(Parse(key, pair.Value));
            }

            return new WordListCatalog(version, lists);
        }

        /// <summary>
        /// Parses one word-list line: comma-separated <see cref="VerdictCategory"/> names, <c>|</c>, then the term, as in
        /// <c>insult|idiot</c>. A leading <c>~</c> makes the term a prefix. Returns false for blank, <c>#</c> comment
        /// and malformed lines. Term limits, such as the minimum prefix length, are checked later, at indexing.
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
            // Generated codes are already lowercase. Each language's array is built and parsed only on first use.
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
