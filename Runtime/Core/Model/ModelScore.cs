#nullable enable
namespace ChatGuard.Core
{
    /// <summary>
    /// The model's severity rating (its "Score") of a message on four policy levels: 0 = does not violate,
    /// 1 = rude but within policy, 2 = clear violation, 3 = severe.
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

        /// <summary>
        /// The average level from 0 to 3, each level weighted by the model's probability for it, so it can fall
        /// between levels.
        /// </summary>
        public double Score { get; }

        /// <summary>
        /// 0 to 1: how concentrated the model's probabilities are on one level. Below
        /// <see cref="Scoring.Thresholds.FlagBelowScoreConfidence"/>, the action is at least Flag.
        /// </summary>
        public double Confidence { get; }
    }
}
