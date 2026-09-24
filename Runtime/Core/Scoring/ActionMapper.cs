#nullable enable
using System.Collections.Generic;
using System.Globalization;

namespace ChatGuard.Core.Scoring
{
    /// <summary>An action and the threshold rules that produced it, for the dashboard's test panel.</summary>
    public sealed class ActionDecision
    {
        public ActionDecision(ModerationAction action, string[] reasons)
        {
            Action = action;
            Reasons = reasons;
        }

        public ModerationAction Action { get; }

        /// <summary>Readable reasons, such as <c>hide: insult 0.91 ≥ 0.80</c>.</summary>
        public string[] Reasons { get; }
    }

    /// <summary>
    /// Turns verdict probabilities, severity and the model's Score (<see cref="ModelScore"/>) into an action with the
    /// <see cref="Thresholds"/> you pass; the model never picks the action. Levels are checked Block, Hide, then Flag,
    /// and the first with a rule that fires wins. If none fires, the action is Allow.
    /// </summary>
    public static class ActionMapper
    {
        /// <summary>
        /// Returns the action. Pass a null <paramref name="score"/> for local-filter results, which skips the
        /// <see cref="Thresholds.FlagBelowScoreConfidence"/> rule.
        /// </summary>
        public static ModerationAction Map(VerdictSet verdicts, double severity, ModelScore? score, Thresholds thresholds)
        {
            if (severity >= thresholds.SeverityBlock)
            {
                return ModerationAction.Block;
            }

            foreach (VerdictCategory category in VerdictCategories.All)
            {
                double? block = thresholds.For(category).Block;
                if (block.HasValue && verdicts[category] >= block.Value)
                {
                    return ModerationAction.Block;
                }
            }

            foreach (VerdictCategory category in VerdictCategories.All)
            {
                double? hide = thresholds.For(category).Hide;
                if (hide.HasValue && verdicts[category] >= hide.Value)
                {
                    return ModerationAction.Hide;
                }
            }

            foreach (VerdictCategory category in VerdictCategories.All)
            {
                double? flag = thresholds.For(category).Flag;
                if (flag.HasValue && verdicts[category] >= flag.Value)
                {
                    return ModerationAction.Flag;
                }
            }

            if (score != null && score.Confidence < thresholds.FlagBelowScoreConfidence)
            {
                return ModerationAction.Flag;
            }

            return ModerationAction.Allow;
        }

        /// <summary>Same decision as <see cref="Map"/>, listing every rule that fired at the winning level.</summary>
        public static ActionDecision Explain(VerdictSet verdicts, double severity, ModelScore? score, Thresholds thresholds)
        {
            var reasons = new List<string>();
            if (severity >= thresholds.SeverityBlock)
            {
                reasons.Add("block: severity " + F(severity) + " ≥ " + F(thresholds.SeverityBlock));
            }

            foreach (VerdictCategory category in VerdictCategories.All)
            {
                double? block = thresholds.For(category).Block;
                if (block.HasValue && verdicts[category] >= block.Value)
                {
                    reasons.Add("block: " + VerdictCategories.ToWireName(category) + " " + F(verdicts[category]) + " ≥ " + F(block.Value));
                }
            }

            if (reasons.Count > 0)
            {
                return new ActionDecision(ModerationAction.Block, reasons.ToArray());
            }

            foreach (VerdictCategory category in VerdictCategories.All)
            {
                double? hide = thresholds.For(category).Hide;
                if (hide.HasValue && verdicts[category] >= hide.Value)
                {
                    reasons.Add("hide: " + VerdictCategories.ToWireName(category) + " " + F(verdicts[category]) + " ≥ " + F(hide.Value));
                }
            }

            if (reasons.Count > 0)
            {
                return new ActionDecision(ModerationAction.Hide, reasons.ToArray());
            }

            foreach (VerdictCategory category in VerdictCategories.All)
            {
                double? flag = thresholds.For(category).Flag;
                if (flag.HasValue && verdicts[category] >= flag.Value)
                {
                    reasons.Add("flag: " + VerdictCategories.ToWireName(category) + " " + F(verdicts[category]) + " ≥ " + F(flag.Value));
                }
            }

            if (score != null && score.Confidence < thresholds.FlagBelowScoreConfidence)
            {
                reasons.Add("flag: model score confidence " + F(score.Confidence) + " < " + F(thresholds.FlagBelowScoreConfidence));
            }

            if (reasons.Count > 0)
            {
                return new ActionDecision(ModerationAction.Flag, reasons.ToArray());
            }

            reasons.Add("allow: no threshold reached (severity " + F(severity) + ")");
            return new ActionDecision(ModerationAction.Allow, reasons.ToArray());
        }

        private static string F(double value)
        {
            return value.ToString("0.00", CultureInfo.InvariantCulture);
        }
    }
}
