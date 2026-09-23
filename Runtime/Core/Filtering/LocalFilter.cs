#nullable enable
using System;
using System.Collections.Generic;
using ChatGuard.Core.Text;

namespace ChatGuard.Core.Filtering
{
    public sealed class LocalFilterOptions
    {
        /// <summary>Apply the English list in addition to the requested language (English abuse is universal in game chat).</summary>
        public bool AlwaysApplyEnglish { get; set; } = true;

        /// <summary>Language used when the requested one has no list.</summary>
        public string FallbackLanguage { get; set; } = "en";
    }

    /// <summary>Result of the dictionary filter. Probabilities are 0 or 1 only.</summary>
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

        public VerdictSet Verdicts { get; }

        public bool BlockListHit { get { return BlockRule != null; } }

        public CustomRule? BlockRule { get; }

        /// <summary>Categories with p = 1 after allow rules were applied.</summary>
        public VerdictCategory[] MatchedCategories { get; }

        /// <summary>Categories an allow rule with a category matched; the API also zeroes the model verdict for these.</summary>
        public VerdictCategory[] SuppressedCategories { get; }

        public string[] AppliedLanguages { get; }
    }

    /// <summary>
    /// The dictionary-based fallback used when Jev is unavailable, over quota, or offline, and the
    /// pre-check that turns project block-list hits into an immediate block without a model call.
    /// </summary>
    public sealed class LocalFilter
    {
        /// <summary>
        /// Orders categories by their numeric value, which is what <c>List.Sort()</c> does by default;
        /// <c>Comparer&lt;enum&gt;.Default</c> boxes every comparison on Unity's Mono and IL2CPP.
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
