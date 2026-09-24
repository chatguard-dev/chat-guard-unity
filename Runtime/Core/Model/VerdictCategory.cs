#nullable enable
using System;

namespace ChatGuard.Core
{
    /// <summary>The six categories Chat Guard checks each message for, each scored with a probability.</summary>
    public enum VerdictCategory
    {
        /// <summary>Name-calling or a demeaning taunt aimed at a person.</summary>
        Insult = 0,
        /// <summary>A threat of real-world violence or harm, or a call for self-harm.</summary>
        Threat = 1,
        /// <summary>
        /// An attack or slur on a person or group because of race, religion, gender, sexual orientation, disability
        /// or a similar trait.
        /// </summary>
        Hate = 2,
        /// <summary>Sexual content that does not suit the chat's age rating.</summary>
        Sexual = 3,
        /// <summary>Advertising, invite links, scams, giveaways or flooding.</summary>
        Spam = 4,
        /// <summary>Buying or selling accounts, items, currency or boosting for real money.</summary>
        Trading = 5,
    }

    /// <summary>Helpers for <see cref="VerdictCategory"/>.</summary>
    public static class VerdictCategories
    {
        public const int Count = 6;

        /// <summary>All six categories in enum order, which is also the API JSON order.</summary>
        public static readonly VerdictCategory[] All =
        {
            VerdictCategory.Insult,
            VerdictCategory.Threat,
            VerdictCategory.Hate,
            VerdictCategory.Sexual,
            VerdictCategory.Spam,
            VerdictCategory.Trading,
        };

        /// <summary>The name used in API JSON and word-list files, such as <c>insult</c>.</summary>
        public static string ToWireName(VerdictCategory category)
        {
            switch (category)
            {
                case VerdictCategory.Insult: return "insult";
                case VerdictCategory.Threat: return "threat";
                case VerdictCategory.Hate: return "hate";
                case VerdictCategory.Sexual: return "sexual";
                case VerdictCategory.Spam: return "spam";
                case VerdictCategory.Trading: return "trading";
                default: throw new ArgumentOutOfRangeException(nameof(category), category, null);
            }
        }

        /// <summary>Parses a wire name, ignoring case and surrounding spaces; false for unknown names.</summary>
        public static bool TryParse(string? name, out VerdictCategory category)
        {
            switch ((name ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "insult": category = VerdictCategory.Insult; return true;
                case "threat": category = VerdictCategory.Threat; return true;
                case "hate": category = VerdictCategory.Hate; return true;
                case "sexual": category = VerdictCategory.Sexual; return true;
                case "spam": category = VerdictCategory.Spam; return true;
                case "trading": category = VerdictCategory.Trading; return true;
                default: category = VerdictCategory.Insult; return false;
            }
        }
    }
}
