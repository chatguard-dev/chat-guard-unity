#nullable enable
using System;

namespace ChatGuard.Core
{
    /// <summary>What Chat Guard recommends doing with a message, in rising order of severity.</summary>
    public enum ModerationAction
    {
        /// <summary>Nothing wrong: show the message to everyone.</summary>
        Allow = 0,
        /// <summary>Borderline: show it, and mark it for your team to review.</summary>
        Flag = 1,
        /// <summary>Abusive: show it only to its sender, so they do not learn it was hidden.</summary>
        Hide = 2,
        /// <summary>Severe: show it to nobody, and tell the sender.</summary>
        Block = 3,
    }

    public static class ModerationActions
    {
        public static string ToWireName(ModerationAction action)
        {
            switch (action)
            {
                case ModerationAction.Allow: return "allow";
                case ModerationAction.Flag: return "flag";
                case ModerationAction.Hide: return "hide";
                case ModerationAction.Block: return "block";
                default: throw new ArgumentOutOfRangeException(nameof(action), action, null);
            }
        }

        public static bool TryParse(string? name, out ModerationAction action)
        {
            switch ((name ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "allow": action = ModerationAction.Allow; return true;
                case "flag": action = ModerationAction.Flag; return true;
                case "hide": action = ModerationAction.Hide; return true;
                case "block": action = ModerationAction.Block; return true;
                default: action = ModerationAction.Allow; return false;
            }
        }
    }
}
