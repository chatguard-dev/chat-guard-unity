#nullable enable
using System;

namespace ChatGuard.Core.Scoring
{
    /// <summary>
    /// How much each category, and the model's Score, counts toward severity in <see cref="SeverityCalculator"/>.
    /// Server defaults come from appsettings (<c>Moderation:Severity</c>); projects override them on the dashboard's
    /// Thresholds page. The Unity client's own decisions use <see cref="Default"/>.
    /// </summary>
    public sealed class SeverityWeights
    {
        /// <summary>Share of the severity taken from the model Score (0..1); the rest comes from verdicts.</summary>
        public double ScoreWeight { get; set; } = 0.6;

        public double Insult { get; set; } = 0.15;

        public double Threat { get; set; } = 0.35;

        public double Hate { get; set; } = 0.25;

        public double Sexual { get; set; } = 0.15;

        public double Spam { get; set; } = 0.05;

        public double Trading { get; set; } = 0.05;

        public static SeverityWeights Default()
        {
            return new SeverityWeights();
        }

        public double For(VerdictCategory category)
        {
            switch (category)
            {
                case VerdictCategory.Insult: return Insult;
                case VerdictCategory.Threat: return Threat;
                case VerdictCategory.Hate: return Hate;
                case VerdictCategory.Sexual: return Sexual;
                case VerdictCategory.Spam: return Spam;
                case VerdictCategory.Trading: return Trading;
                default: throw new ArgumentOutOfRangeException(nameof(category), category, null);
            }
        }

        /// <summary>Sum of the category weights; the server refuses project weights unless it is positive.</summary>
        public double CategorySum()
        {
            return Insult + Threat + Hate + Sexual + Spam + Trading;
        }

        /// <summary>The largest category weight, which the verdict part of severity is divided by.</summary>
        public double CategoryMax()
        {
            double max = 0;
            foreach (VerdictCategory category in VerdictCategories.All)
            {
                double w = For(category);
                if (w > max)
                {
                    max = w;
                }
            }

            return max;
        }
    }
}
