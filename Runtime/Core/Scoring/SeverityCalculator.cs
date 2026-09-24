#nullable enable
namespace ChatGuard.Core.Scoring
{
    /// <summary>
    /// Computes a message's severity from 0 (fine) to 3 (severe), the scale of the model's policy levels. Code does
    /// this math, never the model.
    /// </summary>
    /// <remarks>
    /// Verdict part: V = min(3, 3·Σ(wᵢ·pᵢ) / max(wᵢ)), with category weights wᵢ from <see cref="SeverityWeights"/>.
    /// Dividing by the largest weight lets the most serious category alone (p = 1) reach 3. Lighter ones reach a
    /// fraction, and several add up to at most 3.
    /// With the model's Score (<see cref="ModelScore"/>): severity = w·score + (1 − w)·V, where w is
    /// <see cref="SeverityWeights.ScoreWeight"/> clamped to 0..1. Without one (local filter): severity = V.
    /// The caller, not this class, gives a project block-rule hit <see cref="MaxSeverity"/>.
    /// </remarks>
    public static class SeverityCalculator
    {
        public const double MaxSeverity = ModelScore.MaxLevel;

        public static double Compute(VerdictSet verdicts, ModelScore? score, SeverityWeights weights)
        {
            double fromVerdicts = FromVerdicts(verdicts, weights);
            if (score == null)
            {
                return Clamp(fromVerdicts);
            }

            double w = VerdictSet.Clamp01(weights.ScoreWeight);
            return Clamp((w * score.Score) + ((1 - w) * fromVerdicts));
        }

        /// <summary>The verdict-only part of the severity, 0..3.</summary>
        public static double FromVerdicts(VerdictSet verdicts, SeverityWeights weights)
        {
            double max = weights.CategoryMax();
            if (max <= 0)
            {
                return 0;
            }

            double weighted = 0;
            foreach (VerdictCategory category in VerdictCategories.All)
            {
                double w = weights.For(category);
                if (w > 0)
                {
                    weighted += w * verdicts[category];
                }
            }

            return Clamp(MaxSeverity * weighted / max);
        }

        private static double Clamp(double value)
        {
            if (double.IsNaN(value) || value < 0)
            {
                return 0;
            }

            return value > MaxSeverity ? MaxSeverity : value;
        }
    }
}
