#nullable enable
namespace ChatGuard.Core.Scoring
{
    /// <summary>
    /// Computes severity on the 0..3 scale of the model's four policy levels.
    /// Verdict term: V = min(3, 3·Σ(wᵢ·pᵢ) / max(wᵢ)). Normalizing by the largest weight means the most
    /// serious category alone (p = 1) reaches the top of the scale, lighter categories reach a fraction
    /// of it, and several categories together add up (clamped).
    /// With a model Score: severity = w·score + (1 − w)·V. Without one (local filter): severity = V.
    /// A project block-list hit is reported as <see cref="MaxSeverity"/> by the caller.
    /// Everything here is arithmetic the model must never be asked to do.
    /// </summary>
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
