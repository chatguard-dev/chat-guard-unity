#nullable enable
using System;

namespace ChatGuard.Core
{
    /// <summary>Who a message is aimed at, as judged by the model.</summary>
    public enum TargetChoice
    {
        /// <summary>
        /// Another specific player: the one addressed as "you", a named player, or an author in the request's
        /// <c>thread</c>.
        /// </summary>
        OtherUser = 0,
        /// <summary>A group: a team, everyone in the chat, or a demographic group.</summary>
        Group = 1,
        /// <summary>The author, talking about themself.</summary>
        Self = 2,
        /// <summary>Nobody in particular: a general statement, a question, or talk about the match.</summary>
        Nobody = 3,
        /// <summary>Something else, such as the game, its developers, an item or the situation.</summary>
        Other = 4,
    }

    public static class TargetChoices
    {
        public static string ToWireName(TargetChoice choice)
        {
            switch (choice)
            {
                case TargetChoice.OtherUser: return "other_user";
                case TargetChoice.Group: return "group";
                case TargetChoice.Self: return "self";
                case TargetChoice.Nobody: return "nobody";
                case TargetChoice.Other: return "other";
                default: throw new ArgumentOutOfRangeException(nameof(choice), choice, null);
            }
        }

        public static bool TryParse(string? name, out TargetChoice choice)
        {
            switch ((name ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "other_user": choice = TargetChoice.OtherUser; return true;
                case "group": choice = TargetChoice.Group; return true;
                case "self": choice = TargetChoice.Self; return true;
                case "nobody": choice = TargetChoice.Nobody; return true;
                case "other": choice = TargetChoice.Other; return true;
                default: choice = TargetChoice.Other; return false;
            }
        }
    }

    /// <summary>The model's answer to who a message is aimed at, and how sure it is.</summary>
    public sealed class TargetVerdict
    {
        public TargetVerdict(TargetChoice choice, double confidence)
        {
            Choice = choice;
            Confidence = VerdictSet.Clamp01(confidence);
        }

        public TargetChoice Choice { get; }

        /// <summary>The model's confidence in <see cref="Choice"/>, from 0 to 1.</summary>
        public double Confidence { get; }
    }
}
