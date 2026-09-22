#nullable enable
using System;

namespace ChatGuard.Core.Scoring
{
    /// <summary>
    /// Per-category probability thresholds. A null level means that level is not used for the category.
    /// Plain settable properties so the API can persist the object as JSON (projects.thresholds).
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

        /// <summary>p at or above this value recommends at least Flag.</summary>
        public double? Flag { get; set; }

        /// <summary>p at or above this value recommends at least Hide.</summary>
        public double? Hide { get; set; }

        /// <summary>p at or above this value recommends Block.</summary>
        public double? Block { get; set; }

        public CategoryThresholds Clone()
        {
            return new CategoryThresholds(Flag, Hide, Block);
        }
    }

    /// <summary>
    /// Project-editable action thresholds. The defaults reproduce the spec's mapping exactly:
    /// block if threat.p ≥ 0.85 or severity ≥ 2.5; hide if insult/hate/sexual ≥ 0.80;
    /// flag if any category ≥ 0.55 or the Score confidence is below 0.5; otherwise allow.
    /// </summary>
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
