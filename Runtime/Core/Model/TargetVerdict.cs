#nullable enable
using System;

namespace ChatGuard.Core
{
    /// <summary>Options of the Jev "target" Choice question.</summary>
    public enum TargetChoice
    {
        OtherUser = 0,
        Group = 1,
        Self = 2,
        Nobody = 3,
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

    /// <summary>Who the message is directed at, with the Choice confidence reported by Jev.</summary>
    public sealed class TargetVerdict
    {
        public TargetVerdict(TargetChoice choice, double confidence)
        {
            Choice = choice;
            Confidence = VerdictSet.Clamp01(confidence);
        }

        public TargetChoice Choice { get; }

        public double Confidence { get; }
    }
}
