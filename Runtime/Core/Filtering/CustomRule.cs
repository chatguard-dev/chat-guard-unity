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
        /// <summary>Exempts a term from the built-in lists (and, with a category, zeroes that verdict).</summary>
        Allow = 0,

        /// <summary>An immediate block, evaluated before any model call.</summary>
        Block = 1,
    }

    /// <summary>A project-defined allow or block rule (custom_rules rows of kind allow/block).</summary>
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

        /// <summary>A term/phrase (matched on normalized tokens) or a regular expression (matched on normalized text).</summary>
        public string Pattern { get; }

        public bool IsRegex { get; }

        /// <summary>For allow rules: the verdict category this rule exempts. Null = only the local filter is affected.</summary>
        public VerdictCategory? Category { get; }

        /// <summary>
        /// The normalized, folded pattern a category-less allow rule exempts. Computed on first use and
        /// cached with Volatile.Read and Interlocked.CompareExchange, so a rule shared between threads is safe
        /// on ARM64 (IL2CPP or Mono) too. A race may compute it twice; every caller gets the first published value.
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

    /// <summary>Thrown by <see cref="CompiledRuleSet.Compile(System.Collections.Generic.IEnumerable{CustomRule})"/> when a rule cannot be used.</summary>
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
    /// Project rules compiled once and reused across requests. Regular expressions run with a match
    /// timeout (default 20 ms) and without RegexOptions.Compiled, which is unavailable on IL2CPP.
    /// A regex that times out is treated as a non-match.
    /// </summary>
    public sealed class CompiledRuleSet
    {
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

        public static CompiledRuleSet Compile(IEnumerable<CustomRule> rules)
        {
            return Compile(rules, DefaultRegexTimeout);
        }

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

        /// <summary>The first matching block rule, or null.</summary>
        internal CustomRule? FirstBlockMatch(NormalizedMessage message, string[][] candidates)
        {
            // Sets with only allow rules (and the empty set) cannot block; skip the lookups and the list.
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
