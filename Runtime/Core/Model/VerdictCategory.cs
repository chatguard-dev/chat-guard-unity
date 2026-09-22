#nullable enable
using System;

namespace ChatGuard.Core
{
    /// <summary>The six verdict categories returned by <c>POST /v1/moderate</c>.</summary>
    public enum VerdictCategory
    {
        Insult = 0,
        Threat = 1,
        Hate = 2,
        Sexual = 3,
        Spam = 4,
        Trading = 5,
    }

    /// <summary>Helpers for <see cref="VerdictCategory"/>, including the wire names used in JSON.</summary>
    public static class VerdictCategories
    {
        public const int Count = 6;

        /// <summary>All categories in wire order.</summary>
        public static readonly VerdictCategory[] All =
        {
            VerdictCategory.Insult,
            VerdictCategory.Threat,
            VerdictCategory.Hate,
            VerdictCategory.Sexual,
            VerdictCategory.Spam,
            VerdictCategory.Trading,
        };

        /// <summary>The snake_case name used in API JSON and in word-list files.</summary>
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

        /// <summary>Parses a wire name (case-insensitive). Returns false for unknown names.</summary>
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
