#nullable enable
using System;

namespace ChatGuard.Core.Scoring
{
    /// <summary>
    /// The Flag, Hide and Block thresholds of one category, as probabilities from 0 to 1. Null turns that level off.
    /// </summary>
    public sealed class CategoryThresholds
    {
        public CategoryThresholds()
        {
        }

        public CategoryThresholds(double? flag, double? hide, double? block)
        {
            Flag = flag;
            Hide = hide;
            Block = block;
        }

        /// <summary>A probability at or above this recommends at least Flag.</summary>
        public double? Flag { get; set; }

        /// <summary>A probability at or above this recommends at least Hide.</summary>
        public double? Hide { get; set; }

        /// <summary>A probability at or above this recommends Block.</summary>
        public double? Block { get; set; }

        public CategoryThresholds Clone()
        {
            return new CategoryThresholds(Flag, Hide, Block);
        }
    }

    /// <summary>
    /// The thresholds <see cref="ActionMapper"/> uses to turn verdicts and severity into an action. Each project edits
    /// its own on the dashboard's Thresholds page.
    /// </summary>
    /// <remarks>
    /// Defaults: Block when threat ≥ 0.85 or severity ≥ 2.5. Hide when insult, hate or sexual ≥ 0.80. Flag when
    /// any category ≥ 0.55, or when the model's <see cref="ModelScore.Confidence"/> is below 0.5. Otherwise Allow.
    /// In the Unity package, <c>ChatGuardSettings.Thresholds</c> sets them for decisions made on the device.
    /// </remarks>
    public sealed class Thresholds
    {
        public const double DefaultFlag = 0.55;
        public const double DefaultHide = 0.80;
        public const double DefaultThreatBlock = 0.85;
        public const double DefaultSeverityBlock = 2.5;
        public const double DefaultFlagBelowScoreConfidence = 0.5;

        /// <summary>Severity (0..3) at or above which the action is Block.</summary>
        public double SeverityBlock { get; set; } = DefaultSeverityBlock;

        /// <summary>When the model's Score confidence is below this, the action is at least Flag.</summary>
        public double FlagBelowScoreConfidence { get; set; } = DefaultFlagBelowScoreConfidence;

        public CategoryThresholds Insult { get; set; } = new CategoryThresholds(DefaultFlag, DefaultHide, null);

        public CategoryThresholds Threat { get; set; } = new CategoryThresholds(DefaultFlag, null, DefaultThreatBlock);

        public CategoryThresholds Hate { get; set; } = new CategoryThresholds(DefaultFlag, DefaultHide, null);

        public CategoryThresholds Sexual { get; set; } = new CategoryThresholds(DefaultFlag, DefaultHide, null);

        public CategoryThresholds Spam { get; set; } = new CategoryThresholds(DefaultFlag, null, null);

        public CategoryThresholds Trading { get; set; } = new CategoryThresholds(DefaultFlag, null, null);

        public static Thresholds Default()
        {
            return new Thresholds();
        }

        public CategoryThresholds For(VerdictCategory category)
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

        public Thresholds Clone()
        {
            return new Thresholds
            {
                SeverityBlock = SeverityBlock,
                FlagBelowScoreConfidence = FlagBelowScoreConfidence,
                Insult = Insult.Clone(),
                Threat = Threat.Clone(),
                Hate = Hate.Clone(),
                Sexual = Sexual.Clone(),
                Spam = Spam.Clone(),
                Trading = Trading.Clone(),
            };
        }
    }
}
