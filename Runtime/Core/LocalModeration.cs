#nullable enable
using System;
using ChatGuard.Core.Filtering;
using ChatGuard.Core.Scoring;
using ChatGuard.Core.Text;

namespace ChatGuard.Core
{
    /// <summary>
    /// Moderates a message without the model: runs the local filter (built-in word lists) and any project rules, then
    /// computes severity and action. The server uses it for block-rule hits and degraded results, and the Unity client
    /// for its local-filter fallback.
    /// </summary>
    public static class LocalModeration
    {
        /// <summary>
        /// The model name for local results: <c>local-filter/</c> plus the word-list version, such as
        /// <c>local-filter/3f2a9c1d0b77</c>.
        /// </summary>
        public static string ModelName(WordListCatalog catalog)
        {
            return "local-filter/" + catalog.Version;
        }

        /// <summary>
        /// Moderates <paramref name="message"/> with the local filter; null counts as empty. <paramref name="reason"/>
        /// goes into <see cref="ModerationOutcome.DegradedReason"/>, so pass <see cref="DegradedReason.None"/> when
        /// nothing failed. Null <paramref name="filter"/>, <paramref name="thresholds"/> or <paramref name="weights"/>
        /// means the built-in default; null <paramref name="rules"/> means no project rules.
        /// </summary>
        public static ModerationOutcome Evaluate(
            string? message,
            string? language,
            DegradedReason reason,
            LocalFilter? filter = null,
            CompiledRuleSet? rules = null,
            Thresholds? thresholds = null,
            SeverityWeights? weights = null)
        {
            NormalizedMessage normalized = NormalizedMessage.Create(message);
            return Evaluate(normalized, language, reason, filter, rules, thresholds, weights);
        }

        /// <summary>
        /// Same as the string overload, for a message already normalized with
        /// <see cref="NormalizedMessage.Create(string)"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
        public static ModerationOutcome Evaluate(
            NormalizedMessage message,
            string? language,
            DegradedReason reason,
            LocalFilter? filter = null,
            CompiledRuleSet? rules = null,
            Thresholds? thresholds = null,
            SeverityWeights? weights = null)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            LocalFilter effectiveFilter = filter ?? new LocalFilter();
            Thresholds effectiveThresholds = thresholds ?? Thresholds.Default();
            SeverityWeights effectiveWeights = weights ?? SeverityWeights.Default();

            LocalFilterResult result = effectiveFilter.Evaluate(message, language, rules);
            return FromFilterResult(result, reason, effectiveThresholds, effectiveWeights);
        }

        /// <summary>
        /// Turns a local filter result into an outcome. A project block-rule hit gives Block at maximum severity;
        /// otherwise <see cref="SeverityCalculator"/> and <see cref="ActionMapper"/> decide, with no model Score.
        /// </summary>
        public static ModerationOutcome FromFilterResult(LocalFilterResult result, DegradedReason reason, Thresholds thresholds, SeverityWeights weights)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            if (result.BlockListHit)
            {
                return new ModerationOutcome(
                    ModerationAction.Block,
                    SeverityCalculator.MaxSeverity,
                    result.Verdicts,
                    null,
                    null,
                    reason,
                    true,
                    result.BlockRule!.Id,
                    result.MatchedCategories,
                    result.SuppressedCategories);
            }

            double severity = SeverityCalculator.Compute(result.Verdicts, null, weights);
            ModerationAction action = ActionMapper.Map(result.Verdicts, severity, null, thresholds);
            return new ModerationOutcome(
                action,
                severity,
                result.Verdicts,
                null,
                null,
                reason,
                false,
                null,
                result.MatchedCategories,
                result.SuppressedCategories);
        }
    }
}
