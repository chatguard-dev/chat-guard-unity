#nullable enable
using System;
using System.Collections.Generic;
using ChatGuard.Core.Text;

namespace ChatGuard.Core.Filtering
{
    public sealed class LocalFilterOptions
    {
        /// <summary>
        /// Also check the English list when another language is used (default true), since English abuse turns up in
        /// game chat everywhere.
        /// </summary>
        public bool AlwaysApplyEnglish { get; set; } = true;

        /// <summary>Language used when the requested one is empty or has no list (default <c>en</c>).</summary>
        public string FallbackLanguage { get; set; } = "en";
    }

    /// <summary>What the local filter found in one message. Its verdicts are always 0 or 1.</summary>
    public sealed class LocalFilterResult
    {
        internal LocalFilterResult(
            VerdictSet verdicts,
            CustomRule? blockRule,
            VerdictCategory[] matchedCategories,
            VerdictCategory[] suppressedCategories,
            string[] appliedLanguages)
        {
            Verdicts = verdicts;
            BlockRule = blockRule;
            MatchedCategories = matchedCategories;
            SuppressedCategories = suppressedCategories;
            AppliedLanguages = appliedLanguages;
        }

        /// <summary>1 for each category in <see cref="MatchedCategories"/>, 0 for the rest.</summary>
        public VerdictSet Verdicts { get; }

        /// <summary>
        /// True when a project block rule matched; word-list matches do not count. A hit always blocks the message.
        /// </summary>
        public bool BlockListHit { get { return BlockRule != null; } }

        /// <summary>The first block rule that matched, or null.</summary>
        public CustomRule? BlockRule { get; }

        /// <summary>Categories the word lists found after allow rules were applied, sorted by enum value.</summary>
        public VerdictCategory[] MatchedCategories { get; }

        /// <summary>
        /// Categories named by matching allow rules, sorted by enum value. See <see cref="CustomRule.Category"/>.
        /// </summary>
        public VerdictCategory[] SuppressedCategories { get; }

        /// <summary>
        /// Language codes checked: the resolved language, then <c>en</c> when
        /// <see cref="LocalFilterOptions.AlwaysApplyEnglish"/> adds it.
        /// </summary>
        public string[] AppliedLanguages { get; }
    }

    /// <summary>
    /// The local filter: checks a message against word lists (the built-in ones by default) and, optionally, a
    /// project's allow and block rules. It never calls the moderation model (Jev).
    /// </summary>
    /// <remarks>
    /// It answers when the model cannot, and those results are degraded (see <see cref="DegradedReason"/>). The server
    /// also runs it before the model on every message, so a block-rule hit skips the model. The Unity client runs it
    /// only with <c>OfflineBehavior.LocalFilter</c> (the default), to answer when the server gives no usable answer
    /// and, if <c>LocalFilterWhenDegraded</c> is on, to re-check degraded server answers. One instance can be shared
    /// between threads.
    /// </remarks>
    public sealed class LocalFilter
    {
        /// <summary>
        /// Sorts by numeric value, as <c>List.Sort()</c> would, without boxing: <c>Comparer&lt;enum&gt;.Default</c>
        /// boxes every comparison on Unity's Mono and IL2CPP.
        /// </summary>
        private static readonly Comparison<VerdictCategory> ByValue = (a, b) => ((int)a).CompareTo((int)b);

        private readonly WordListCatalog _catalog;
        private readonly LocalFilterOptions _options;

        public LocalFilter()
            : this(WordListCatalog.BuiltIn, null)
        {
        }

        public LocalFilter(WordListCatalog catalog, LocalFilterOptions? options)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _options = options ?? new LocalFilterOptions();
        }

        public WordListCatalog Catalog { get { return _catalog; } }

        /// <summary>Checks <paramref name="message"/> against the word lists and, when given, project rules.</summary>
        /// <param name="message">The message, from <see cref="NormalizedMessage.Create"/>.</param>
        /// <param name="language">
        /// A language code such as <c>en</c> or <c>pt-BR</c>, trimmed and lowercased; only the first two characters
        /// count. A null, empty or unknown code uses <see cref="LocalFilterOptions.FallbackLanguage"/>.
        /// </param>
        /// <param name="rules">A project's allow and block rules, or null to check the word lists only.</param>
        /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
        public LocalFilterResult Evaluate(NormalizedMessage message, string? language, CompiledRuleSet? rules = null)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            string[][] candidates = TokenForms.Candidates(message.Tokens);
            var verdicts = new VerdictSet();
            var suppressed = new List<VerdictCategory>();

            CustomRule? blockRule = rules == null ? null : rules.FirstBlockMatch(message, candidates);

            var hits = new List<TermHit<WordListEntry>>();
            string[] languages = ResolveLanguages(language);
            foreach (string lang in languages)
            {
                LanguageWordList? list = _catalog.Get(lang);
                if (list != null)
                {
                    list.Index.Match(candidates, hits);
                }
            }

            if (rules != null && rules.Count > 0)
            {
                List<CustomRule> allows = rules.AllowMatches(message, candidates);
                foreach (CustomRule allow in allows)
                {
                    if (allow.Category.HasValue)
                    {
                        if (!suppressed.Contains(allow.Category.Value))
                        {
                            suppressed.Add(allow.Category.Value);
                        }
                    }
                    else
                    {
                        // An allow rule without a category exempts exactly the term it names.
                        string exempt = allow.Key;
                        hits.RemoveAll(h => h.Key == exempt);
                    }
                }
            }

            var matched = new List<VerdictCategory>();
            foreach (TermHit<WordListEntry> hit in hits)
            {
                foreach (VerdictCategory category in hit.Payload.Categories)
                {
                    if (suppressed.Contains(category) || matched.Contains(category))
                    {
                        continue;
                    }

                    matched.Add(category);
                    verdicts[category] = 1;
                }
            }

            matched.Sort(ByValue);
            suppressed.Sort(ByValue);
            return new LocalFilterResult(verdicts, blockRule, matched.ToArray(), suppressed.ToArray(), languages);
        }

        private string[] ResolveLanguages(string? language)
        {
            string primary = (language ?? string.Empty).Trim().ToLowerInvariant();
            if (primary.Length >= 2)
            {
                primary = primary.Substring(0, 2); // "pt-BR" → "pt"
            }

            if (primary.Length == 0 || _catalog.Get(primary) == null)
            {
                primary = _options.FallbackLanguage;
            }

            if (_options.AlwaysApplyEnglish && primary != "en" && _catalog.Get("en") != null)
            {
                return new[] { primary, "en" };
            }

            return new[] { primary };
        }
    }
}
