#nullable enable
using System;

namespace ChatGuard.Core
{
    /// <summary>
    /// The result of moderating one message: verdict probabilities, severity, action and where they came from. The
    /// server builds the <c>POST /v1/moderate</c> response from it; the Unity client builds one for its local-filter
    /// fallback.
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

        /// <summary>0 (fine) to 3 (severe), computed by <see cref="Scoring.SeverityCalculator"/>.</summary>
        public double Severity { get; }

        public VerdictSet Verdicts { get; }

        /// <summary>Null when the model was not asked (local filter).</summary>
        public TargetVerdict? Target { get; }

        /// <summary>Null when the model was not asked (local filter).</summary>
        public ModelScore? Score { get; }

        /// <summary>True when a fallback decided instead of the model; <see cref="DegradedReason"/> says why.</summary>
        public bool Degraded { get { return DegradedReason != DegradedReason.None; } }

        public DegradedReason DegradedReason { get; }

        /// <summary>True when a project block rule matched; the action is then Block at severity 3.</summary>
        public bool BlockListHit { get; }

        /// <summary>Id of the project block rule that matched, or null.</summary>
        public string? BlockRuleId { get; }

        /// <summary>
        /// Categories the local filter's word lists matched, after allow rules. Filled even when the model answered.
        /// </summary>
        public VerdictCategory[] MatchedCategories { get; }

        /// <summary>Categories forced to 0 by a project allow rule with a category.</summary>
        public VerdictCategory[] SuppressedCategories { get; }
    }
}
