#nullable enable
using System;

namespace ChatGuard.Core
{
    /// <summary>
    /// The computed result of moderating one message: verdict probabilities, severity, and the action,
    /// plus provenance flags. The API maps this onto the <c>/v1/moderate</c> response; the Unity client
    /// produces it locally when offline.
    /// </summary>
    public sealed class ModerationOutcome
    {
        public ModerationOutcome(
            ModerationAction action,
            double severity,
            VerdictSet verdicts,
            TargetVerdict? target,
            ModelScore? score,
            DegradedReason degradedReason,
            bool blockListHit,
            string? blockRuleId,
            VerdictCategory[] matchedCategories,
            VerdictCategory[] suppressedCategories)
        {
            Action = action;
            Severity = severity;
            Verdicts = verdicts ?? throw new ArgumentNullException(nameof(verdicts));
            Target = target;
            Score = score;
            DegradedReason = degradedReason;
            BlockListHit = blockListHit;
            BlockRuleId = blockRuleId;
            MatchedCategories = matchedCategories ?? Array.Empty<VerdictCategory>();
            SuppressedCategories = suppressedCategories ?? Array.Empty<VerdictCategory>();
        }

        public ModerationAction Action { get; }

        /// <summary>0..3, see <see cref="Scoring.SeverityCalculator"/>.</summary>
        public double Severity { get; }

        public VerdictSet Verdicts { get; }

        /// <summary>Null when the model was not consulted (local filter).</summary>
        public TargetVerdict? Target { get; }

        /// <summary>Null when the model was not consulted (local filter).</summary>
        public ModelScore? Score { get; }

        public bool Degraded { get { return DegradedReason != DegradedReason.None; } }

        public DegradedReason DegradedReason { get; }

        /// <summary>True when a project block-list rule matched; the action is then always Block.</summary>
        public bool BlockListHit { get; }

        /// <summary>Id of the custom block rule that matched, if any.</summary>
        public string? BlockRuleId { get; }

        /// <summary>Categories the local filter matched (built-in word lists).</summary>
        public VerdictCategory[] MatchedCategories { get; }

        /// <summary>Categories forced to 0 by a project allow rule with a category.</summary>
        public VerdictCategory[] SuppressedCategories { get; }
    }
}
