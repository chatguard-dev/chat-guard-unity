#nullable enable
using System;

namespace ChatGuard.Core
{
    /// <summary>The recommended action, ordered by severity.</summary>
    public enum ModerationAction
    {
        Allow = 0,
        Flag = 1,
        Hide = 2,
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
