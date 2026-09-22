#nullable enable
namespace ChatGuard.Core
{
    /// <summary>
    /// The Jev "severity" Score answer: a probability-weighted position on the four policy levels
    /// (0 = does not violate, 1 = rude but within policy, 2 = clear violation, 3 = severe) and the
    /// confidence derived from the level distribution. Null when the model was not consulted.
    /// </summary>
    public sealed class ModelScore
    {
        public const double MaxLevel = 3.0;

        public ModelScore(double score, double confidence)
        {
            if (double.IsNaN(score) || score < 0)
            {
                score = 0;
            }
            else if (score > MaxLevel)
            {
                score = MaxLevel;
            }

            Score = score;
            Confidence = VerdictSet.Clamp01(confidence);
        }

        /// <summary>Expected level, 0..3, may fall between levels.</summary>
        public double Score { get; }

        /// <summary>0..1, how concentrated the level distribution is.</summary>
        public double Confidence { get; }
    }
}
