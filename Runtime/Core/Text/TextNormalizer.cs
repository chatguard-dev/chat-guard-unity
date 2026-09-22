#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ChatGuard.Core.Text
{
    /// <summary>
    /// Deterministic text normalization shared by the API and the Unity client, so that the same
    /// message hashes to the same value everywhere and the local filter behaves identically.
    /// Pipeline: strip invisible characters → Unicode NFKC → lowercase → per-token script-aware
    /// homoglyph and leet mapping → collapse runs of 3+ identical characters to 2 → collapse whitespace.
    /// Only BCL APIs available in Unity 2021.3 (netstandard2.1) are used.
    /// </summary>
    public static class TextNormalizer
    {
        /// <summary>Maximum run length kept for repeated characters ("shiiiit" → "shiit").</summary>
        public const int MaxRun = 2;

        public static string Normalize(string? input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            string s = StripInvisible(input!);
            s = TryNormalize(s, NormalizationForm.FormKC);
            s = s.Replace('İ', 'i'); // İ → i before lowering; runtimes disagree on its lowercase form
            s = s.ToLowerInvariant();
            s = s.Replace("i̇", "i"); // leftover combining dot above from some runtimes
            s = MapTokens(s);
            s = CollapseRuns(s, MaxRun);
            return s;
        }

        /// <summary>
        /// Splits normalized text into matching tokens: whitespace-separated, with every character that
        /// is not a letter or digit removed ("s.h.i.t" → "shit", "discord.gg/x" → "discordggx").
        /// </summary>
        public static string[] Tokenize(string normalized)
        {
            if (string.IsNullOrEmpty(normalized))
            {
                return Array.Empty<string>();
            }

            var tokens = new List<string>();
            var sb = new StringBuilder();
            foreach (char c in normalized)
            {
                if (char.IsWhiteSpace(c))
                {
                    Flush(sb, tokens);
                }
                else if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                }
            }

            Flush(sb, tokens);
            return tokens.ToArray();
        }

        /// <summary>
        /// Accent/case-insensitive comparison form used for word-list lookups: NFD with combining marks
        /// removed, plus ß→ss, ı→i, ł→l, ø→o, đ→d, æ→ae, œ→oe. Cyrillic й/ё fold to и/е as a side effect,
        /// which is intended (ё/е are used interchangeably in chat).
        /// </summary>
        public static string Fold(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return string.Empty;
            }

            string d = TryNormalize(token, NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            foreach (char c in d)
            {
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category == UnicodeCategory.NonSpacingMark
                    || category == UnicodeCategory.SpacingCombiningMark
                    || category == UnicodeCategory.EnclosingMark)
                {
                    continue;
                }

                switch (c)
                {
                    case 'ß': sb.Append("ss"); break;
                    case 'ı': sb.Append('i'); break;
                    case 'ł': sb.Append('l'); break;
                    case 'ø': sb.Append('o'); break;
                    case 'đ': sb.Append('d'); break;
                    case 'ð': sb.Append('d'); break;
                    case 'æ': sb.Append("ae"); break;
                    case 'œ': sb.Append("oe"); break;
                    case 'þ': sb.Append("th"); break;
                    case 'ħ': sb.Append('h'); break;
                    default: sb.Append(c); break;
                }
            }

            return sb.ToString();
        }

        /// <summary>Collapses every run of identical characters to a single character ("shiit" → "shit").</summary>
        public static string CollapseRuns(string text, int maxRun)
        {
            if (string.IsNullOrEmpty(text) || maxRun < 1)
            {
                return text ?? string.Empty;
            }

            var sb = new StringBuilder(text.Length);
            char previous = '\0';
            int run = 0;
            foreach (char c in text)
            {
                if (c == previous)
                {
                    run++;
                }
                else
                {
                    previous = c;
                    run = 1;
                }

                if (run <= maxRun)
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        private static void Flush(StringBuilder sb, List<string> tokens)
        {
            if (sb.Length > 0)
            {
                tokens.Add(sb.ToString());
                sb.Length = 0;
            }
        }

        /// <summary>
        /// Drops invisible characters and folds the Mathematical Alphanumeric Symbols block (𝐢𝐝𝐢𝐨𝐭, 𝒾𝒹𝒾𝑜𝓉, …)
        /// to ASCII explicitly: NFKC does the same on .NET, but Unity's Mono runtime does not normalize
        /// supplementary-plane characters, and the hash must be identical on both hosts.
        /// </summary>
        private static string StripInvisible(string input)
        {
            var sb = new StringBuilder(input.Length);
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (char.IsHighSurrogate(c) && i + 1 < input.Length && char.IsLowSurrogate(input[i + 1]))
                {
                    int codePoint = char.ConvertToUtf32(c, input[i + 1]);
                    i++;
                    if (codePoint >= 0x1D400 && codePoint <= 0x1D6A3)
                    {
                        int letter = (codePoint - 0x1D400) % 52;
                        sb.Append(letter < 26 ? (char)('A' + letter) : (char)('a' + letter - 26));
                    }
                    else if (codePoint >= 0x1D7CE && codePoint <= 0x1D7FF)
                    {
                        sb.Append((char)('0' + ((codePoint - 0x1D7CE) % 10)));
                    }
                    else if (codePoint < 0xE0100 || codePoint > 0xE01EF)
                    {
                        // Keep emoji and other supplementary characters; drop variation selectors supplement.
                        sb.Append(c).Append(input[i]);
                    }

                    continue;
                }

                if (CharacterMaps.IsInvisible(c))
                {
                    continue;
                }

                if (char.IsControl(c) && !char.IsWhiteSpace(c))
                {
                    continue;
                }

                sb.Append(c);
            }

            return sb.ToString();
        }

        private static string TryNormalize(string s, NormalizationForm form)
        {
            try
            {
                return s.IsNormalized(form) ? s : s.Normalize(form);
            }
            catch (ArgumentException)
            {
                // Invalid surrogate pairs: keep the raw text rather than fail the request.
                return s;
            }
        }

        private static string MapTokens(string s)
        {
            var sb = new StringBuilder(s.Length);
            var token = new StringBuilder();
            bool pendingSpace = false;
            foreach (char c in s)
            {
                if (char.IsWhiteSpace(c))
                {
                    if (token.Length > 0)
                    {
                        AppendToken(sb, token, ref pendingSpace);
                    }

                    continue;
                }

                token.Append(c);
            }

            if (token.Length > 0)
            {
                AppendToken(sb, token, ref pendingSpace);
            }

            return sb.ToString();
        }

        private static void AppendToken(StringBuilder output, StringBuilder token, ref bool pendingSpace)
        {
            if (pendingSpace)
            {
                output.Append(' ');
            }

            output.Append(MapToken(token.ToString()));
            token.Length = 0;
            pendingSpace = true;
        }

        private enum Script
        {
            None,
            Latin,
            Cyrillic,
            Greek,
        }

        /// <summary>
        /// Rewrites minority-script homoglyphs and leet characters toward the token's majority script.
        /// Leet is only applied when the token has at least as many letters as digits/symbols, so
        /// "1v1" and "2024" are untouched while "sh1t" and "b00bs" are mapped.
        /// </summary>
        private static string MapToken(string token)
        {
            int latin = 0, cyrillic = 0, greek = 0, leetish = 0;
            int lastLetter = -1;
            for (int i = 0; i < token.Length; i++)
            {
                char c = token[i];
                if (CharacterMaps.IsLatinLetter(c) || CharacterMaps.IsCyrillicLetter(c) || CharacterMaps.IsGreekLetter(c) || char.IsLetter(c))
                {
                    lastLetter = i;
                }
            }

            for (int i = 0; i < token.Length; i++)
            {
                char c = token[i];
                if (CharacterMaps.IsLatinLetter(c))
                {
                    latin++;
                }
                else if (CharacterMaps.IsCyrillicLetter(c))
                {
                    cyrillic++;
                }
                else if (CharacterMaps.IsGreekLetter(c))
                {
                    greek++;
                }
                else if (IsLeetCandidate(c, i, lastLetter))
                {
                    leetish++;
                }
            }

            int letters = latin + cyrillic + greek;
            if (letters == 0)
            {
                return token;
            }

            Script majority = Script.Latin;
            if (cyrillic > latin && cyrillic >= greek)
            {
                majority = Script.Cyrillic;
            }
            else if (greek > latin && greek > cyrillic)
            {
                majority = Script.Greek;
            }

            bool allowLeet = leetish > 0 && letters >= leetish;
            bool mixed = (latin > 0 ? 1 : 0) + (cyrillic > 0 ? 1 : 0) + (greek > 0 ? 1 : 0) > 1;
            if (!mixed && !allowLeet)
            {
                return token;
            }

            var sb = new StringBuilder(token.Length);
            for (int i = 0; i < token.Length; i++)
            {
                char c = token[i];
                bool leetHere = allowLeet && IsLeetCandidate(c, i, lastLetter);
                char mapped;
                if (majority == Script.Latin)
                {
                    if (mixed && (CharacterMaps.CyrillicToLatin.TryGetValue(c, out mapped) || CharacterMaps.GreekToLatin.TryGetValue(c, out mapped)))
                    {
                        sb.Append(mapped);
                        continue;
                    }

                    if (leetHere && CharacterMaps.LatinLeet.TryGetValue(c, out mapped))
                    {
                        sb.Append(mapped);
                        continue;
                    }
                }
                else if (majority == Script.Cyrillic)
                {
                    if (mixed && CharacterMaps.LatinToCyrillic.TryGetValue(c, out mapped))
                    {
                        sb.Append(mapped);
                        continue;
                    }

                    if (leetHere && CharacterMaps.CyrillicLeet.TryGetValue(c, out mapped))
                    {
                        sb.Append(mapped);
                        continue;
                    }
                }

                sb.Append(c);
            }

            return sb.ToString();
        }
        /// <summary>
        /// Digits are leet candidates anywhere in a token ("a55"); symbols only when a letter follows them
        /// ("sh!t", "@ss"), so trailing punctuation such as "idiot!!!" is never rewritten.
        /// </summary>
        private static bool IsLeetCandidate(char c, int index, int lastLetterIndex)
        {
            if (char.IsDigit(c))
            {
                return CharacterMaps.LatinLeet.ContainsKey(c) || CharacterMaps.CyrillicLeet.ContainsKey(c);
            }

            if (index >= lastLetterIndex)
            {
                return false;
            }

            return CharacterMaps.LatinLeet.ContainsKey(c) || CharacterMaps.CyrillicLeet.ContainsKey(c);
        }
    }
}
