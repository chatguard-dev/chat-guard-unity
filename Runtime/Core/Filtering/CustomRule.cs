#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using ChatGuard.Core.Text;

namespace ChatGuard.Core.Filtering
{
    public enum CustomRuleKind
    {
        /// <summary>
        /// Exempts its term from the word lists or, with a <see cref="CustomRule.Category"/>, suppresses that category.
        /// </summary>
        Allow = 0,

        /// <summary>Blocks any message it matches, before the model is called. Allow rules do not override it.</summary>
        Block = 1,
    }

    /// <summary>
    /// A project's allow or block rule, set in the Chat Guard dashboard and applied by the server. The Unity client
    /// never receives rules, so its local filter runs without them.
    /// </summary>
    public sealed class CustomRule
    {
        private string? _key;

        public CustomRule(string id, CustomRuleKind kind, string pattern, bool isRegex, VerdictCategory? category)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Kind = kind;
            Pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
            IsRegex = isRegex;
            Category = category;
        }

        public string Id { get; }

        public CustomRuleKind Kind { get; }

        /// <summary>
        /// A word or phrase of up to 6 words, matched as whole words with accents ignored, or a regular expression when
        /// <see cref="IsRegex"/> is true. Both match the normalized message: lowercase, with leetspeak and look-alike
        /// letters mapped (see <see cref="TextNormalizer.Normalize"/>). At most
        /// <see cref="CompiledRuleSet.MaxPatternLength"/> characters.
        /// </summary>
        public string Pattern { get; }

        public bool IsRegex { get; }

        /// <summary>
        /// Allow rules only. With a category, a match suppresses that category: the local filter ignores its word-list
        /// hits and the server sets the model's verdict for it to 0. Null exempts only the word-list term equal to the
        /// normalized <see cref="Pattern"/> and affects only the local filter. A regex rarely equals a term, so a regex
        /// allow rule needs a category to have an effect.
        /// </summary>
        public VerdictCategory? Category { get; }

        /// <summary>
        /// The normalized, folded pattern that a category-less allow rule compares with <see cref="TermHit{T}.Key"/>.
        /// Cached on first use with the same thread-safe pattern as that property.
        /// </summary>
        internal string Key
        {
            get
            {
                string? key = Volatile.Read(ref _key);
                if (key == null)
                {
                    string computed = TextNormalizer.Fold(TextNormalizer.Normalize(Pattern));
                    key = Interlocked.CompareExchange(ref _key, computed, null) ?? computed;
                }

                return key;
            }
        }
    }

    /// <summary>
    /// Thrown by <see cref="CompiledRuleSet.Compile(System.Collections.Generic.IEnumerable{CustomRule})"/> and its
    /// overload for an invalid rule. The message names the rule and the problem.
    /// </summary>
    public sealed class RuleCompilationException : Exception
    {
        public RuleCompilationException(string ruleId, string message)
            : base("Rule '" + ruleId + "': " + message)
        {
            RuleId = ruleId;
        }

        public string RuleId { get; }
    }

    /// <summary>
    /// A project's allow and block rules, compiled once for reuse across requests and threads. Regular expressions
    /// ignore case, and one that exceeds its match timeout (default 20 ms) counts as no match.
    /// </summary>
    public sealed class CompiledRuleSet
    {
        /// <summary>Longest allowed <see cref="CustomRule.Pattern"/>, in UTF-16 code units.</summary>
        public const int MaxPatternLength = 200;

        public static readonly TimeSpan DefaultRegexTimeout = TimeSpan.FromMilliseconds(20);

        private readonly TermIndex<CustomRule> _blockTerms = new TermIndex<CustomRule>();
        private readonly TermIndex<CustomRule> _allowTerms = new TermIndex<CustomRule>();
        private readonly List<RegexRule> _blockRegexes = new List<RegexRule>();
        private readonly List<RegexRule> _allowRegexes = new List<RegexRule>();

        private CompiledRuleSet()
        {
        }

        public static CompiledRuleSet Empty { get; } = new CompiledRuleSet();

        public int Count { get; private set; }

        /// <summary>
        /// Compiles <paramref name="rules"/> with a regex match timeout of <see cref="DefaultRegexTimeout"/>.
        /// </summary>
        /// <exception cref="RuleCompilationException">Any rule is invalid; the whole set fails.</exception>
        public static CompiledRuleSet Compile(IEnumerable<CustomRule> rules)
        {
            return Compile(rules, DefaultRegexTimeout);
        }

        /// <summary>
        /// Compiles <paramref name="rules"/>, giving each regex match at most <paramref name="regexTimeout"/>. The
        /// timeout must be positive or <see cref="Regex.InfiniteMatchTimeout"/>; otherwise the first regex rule fails
        /// as if its pattern were invalid.
        /// </summary>
        /// <exception cref="RuleCompilationException">Any rule is invalid; the whole set fails.</exception>
        public static CompiledRuleSet Compile(IEnumerable<CustomRule> rules, TimeSpan regexTimeout)
        {
            if (rules == null)
            {
                throw new ArgumentNullException(nameof(rules));
            }

            var set = new CompiledRuleSet();
            foreach (CustomRule rule in rules)
            {
                if (rule.Pattern.Length > MaxPatternLength)
                {
                    throw new RuleCompilationException(rule.Id, "pattern exceeds " + MaxPatternLength + " characters");
                }

                if (rule.IsRegex)
                {
                    Regex regex;
                    try
                    {
                        // No RegexOptions.Compiled: it is unavailable on IL2CPP.
                        regex = new Regex(rule.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, regexTimeout);
                    }
                    catch (ArgumentException ex)
                    {
                        throw new RuleCompilationException(rule.Id, "invalid regular expression: " + ex.Message);
                    }

                    (rule.Kind == CustomRuleKind.Block ? set._blockRegexes : set._allowRegexes).Add(new RegexRule(regex, rule));
                }
                else
                {
                    TermIndex<CustomRule> index = rule.Kind == CustomRuleKind.Block ? set._blockTerms : set._allowTerms;
                    if (!index.TryAdd(rule.Pattern, false, rule, out string? error))
                    {
                        throw new RuleCompilationException(rule.Id, error ?? "invalid term");
                    }
                }

                set.Count++;
            }

            return set;
        }

        /// <summary>The first matching block rule, or null. Word and phrase rules are checked first.</summary>
        internal CustomRule? FirstBlockMatch(NormalizedMessage message, string[][] candidates)
        {
            // Allow-only and empty sets cannot block, so skip the lookups and the list allocation.
            if (_blockTerms.Count == 0 && _blockRegexes.Count == 0)
            {
                return null;
            }

            var hits = new List<TermHit<CustomRule>>();
            _blockTerms.Match(candidates, hits);
            if (hits.Count > 0)
            {
                return hits[0].Payload;
            }

            foreach (RegexRule rule in _blockRegexes)
            {
                if (rule.IsMatch(message.Normalized))
                {
                    return rule.Rule;
                }
            }

            return null;
        }

        /// <summary>All matching allow rules.</summary>
        internal List<CustomRule> AllowMatches(NormalizedMessage message, string[][] candidates)
        {
            var result = new List<CustomRule>();
            var hits = new List<TermHit<CustomRule>>();
            _allowTerms.Match(candidates, hits);
            foreach (TermHit<CustomRule> hit in hits)
            {
                result.Add(hit.Payload);
            }

            foreach (RegexRule rule in _allowRegexes)
            {
                if (rule.IsMatch(message.Normalized))
                {
                    result.Add(rule.Rule);
                }
            }

            return result;
        }

        private sealed class RegexRule
        {
            private readonly Regex _regex;

            public RegexRule(Regex regex, CustomRule rule)
            {
                _regex = regex;
                Rule = rule;
            }

            public CustomRule Rule { get; }

            public bool IsMatch(string text)
            {
                try
                {
                    return _regex.IsMatch(text);
                }
                catch (RegexMatchTimeoutException)
                {
                    return false;
                }
            }
        }
    }
}
