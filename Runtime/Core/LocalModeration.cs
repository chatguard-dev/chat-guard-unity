#nullable enable
using System;
using ChatGuard.Core.Filtering;
using ChatGuard.Core.Scoring;
using ChatGuard.Core.Text;

namespace ChatGuard.Core
{
    /// <summary>
    /// End-to-end local moderation without the model: normalize, run the dictionary filter and project
    /// rules, compute severity and action. Used by the API on every degrade path and by the Unity client
    /// when offline or when the server reports <c>degraded</c>.
    /// </summary>
    public static class LocalModeration
    {
        /// <summary>Model name reported for local outcomes, e.g. <c>local-filter/3f2a9c1d0b77</c>.</summary>
        public static string ModelName(WordListCatalog catalog)
        {
            return "local-filter/" + catalog.Version;
        }

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

        /// <summary>Turns a filter result into an outcome; severity and action are computed here, never by a model.</summary>
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
