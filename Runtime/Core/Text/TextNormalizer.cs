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
            s = IsLowercaseAscii(s) ? s : s.ToLowerInvariant(); // skip the no-op: Mono's ToLowerInvariant always copies
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

            // Exact: ASCII is NFD-stable, has no combining marks and none of the special cases below is ASCII.
            if (IsAscii(token))
            {
                return token;
            }

            string d = TryNormalize(token, NormalizationForm.FormD);
            StringBuilder? sb = null; // created at the first dropped or replaced character; until then the output is d[0..i)
            for (int i = 0; i < d.Length; i++)
            {
                char c = d[i];
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category == UnicodeCategory.NonSpacingMark
                    || category == UnicodeCategory.SpacingCombiningMark
                    || category == UnicodeCategory.EnclosingMark)
                {
                    StartBuilder(ref sb, d, i);
                    continue;
                }

                switch (c)
                {
                    case 'ß': StartBuilder(ref sb, d, i).Append("ss"); break;
                    case 'ı': StartBuilder(ref sb, d, i).Append('i'); break;
                    case 'ł': StartBuilder(ref sb, d, i).Append('l'); break;
                    case 'ø': StartBuilder(ref sb, d, i).Append('o'); break;
                    case 'đ': StartBuilder(ref sb, d, i).Append('d'); break;
                    case 'ð': StartBuilder(ref sb, d, i).Append('d'); break;
                    case 'æ': StartBuilder(ref sb, d, i).Append("ae"); break;
                    case 'œ': StartBuilder(ref sb, d, i).Append("oe"); break;
                    case 'þ': StartBuilder(ref sb, d, i).Append("th"); break;
                    case 'ħ': StartBuilder(ref sb, d, i).Append('h'); break;
                    default: sb?.Append(c); break;
                }
            }

            return sb == null ? d : sb.ToString();
        }

        /// <summary>Collapses every run of identical characters to a single character ("shiit" → "shit").</summary>
        public static string CollapseRuns(string text, int maxRun)
        {
            if (string.IsNullOrEmpty(text) || maxRun < 1)
            {
                return text ?? string.Empty;
            }

            StringBuilder? sb = null; // created at the first dropped character; until then the output is text[0..i)
            char previous = '\0';
            int run = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
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
                    sb?.Append(c);
                }
                else
                {
                    StartBuilder(ref sb, text, i);
                }
            }

            return sb == null ? text : sb.ToString();
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
        /// Returns <paramref name="sb"/>, creating it first (seeded with <c>text[0..end)</c>) when it is still null.
        /// Used by loops whose output equals their input until the first character they drop or replace.
        /// </summary>
        private static StringBuilder StartBuilder(ref StringBuilder? sb, string text, int end)
        {
            if (sb == null)
            {
                sb = new StringBuilder(text.Length);
                sb.Append(text, 0, end);
            }

            return sb;
        }

        /// <summary>True when every character is U+0000..U+007F (netstandard2.1 has no char.IsAscii).</summary>
        private static bool IsAscii(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] > '\u007F')
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>True when <paramref name="s"/> is ASCII without 'A'..'Z', so ToLowerInvariant would return it unchanged.</summary>
        private static bool IsLowercaseAscii(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c > '\u007F' || (c >= 'A' && c <= 'Z'))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Drops invisible characters and folds the Mathematical Alphanumeric Symbols block (𝐢𝐝𝐢𝐨𝐭, 𝒾𝒹𝒾𝑜𝓉, …)
        /// to ASCII explicitly: NFKC does the same on .NET, but Unity's Mono runtime does not normalize
        /// supplementary-plane characters, and the hash must be identical on both hosts.
        /// </summary>
        private static string StripInvisible(string input)
        {
            if (!NeedsStripping(input))
            {
                return input;
            }

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

        /// <summary>
        /// Conservative pre-scan for <see cref="StripInvisible"/>: false only when its loop would copy every character
        /// unchanged (no surrogates at all, even lone ones, no invisible characters, no non-whitespace controls).
        /// </summary>
        private static bool NeedsStripping(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsSurrogate(c) || CharacterMaps.IsInvisible(c) || (char.IsControl(c) && !char.IsWhiteSpace(c)))
                {
                    return true;
                }
            }

            return false;
        }

        private static string TryNormalize(string s, NormalizationForm form)
        {
            // ASCII is stable under NFC, NFD, NFKC and NFKD, so IsNormalized/Normalize would return it unchanged.
            // Checking first skips Mono's slow per-character IsNormalized (Normalize uses FormKC, Fold uses FormD).
            if (IsAscii(s))
            {
                return s;
            }

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

        /// <summary>
        /// Maps every whitespace-separated token of <paramref name="s"/> and joins them with single spaces. Nothing is
        /// copied while the output is still a prefix of <paramref name="s"/> (first token at index 0, single U+0020
        /// separators, <see cref="MapToken"/> returned null for every token), so such a message comes back as
        /// <paramref name="s"/> itself, or, when <paramref name="s"/> ends in whitespace, as one Substring of
        /// <paramref name="s"/> without that whitespace.
        /// </summary>
        private static string MapTokens(string s)
        {
            StringBuilder? sb = null;
            int consumed = 0; // while sb is null, the output so far is s[0..consumed)
            bool pendingSpace = false;
            int i = 0;
            while (i < s.Length)
            {
                if (char.IsWhiteSpace(s[i]))
                {
                    i++;
                    continue;
                }

                int start = i;
                while (i < s.Length && !char.IsWhiteSpace(s[i]))
                {
                    i++;
                }

                string? mapped = MapToken(s, start, i);
                if (sb == null)
                {
                    // Appending the token keeps the output equal to s[0..i) when it is unmapped and starts the text,
                    // or follows the previous token after exactly one ' '.
                    bool extendsPrefix = pendingSpace ? (start == consumed + 1 && s[consumed] == ' ') : start == 0;
                    if (mapped == null && extendsPrefix)
                    {
                        consumed = i;
                        pendingSpace = true;
                        continue;
                    }

                    sb = new StringBuilder(s.Length);
                    sb.Append(s, 0, consumed);
                }

                if (pendingSpace)
                {
                    sb.Append(' ');
                }

                if (mapped == null)
                {
                    sb.Append(s, start, i - start);
                }
                else
                {
                    sb.Append(mapped);
                }

                pendingSpace = true;
            }

            if (sb != null)
            {
                return sb.ToString();
            }

            // Unchanged prefix: s itself, or s without its trailing whitespace.
            return consumed == s.Length ? s : s.Substring(0, consumed);
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
        /// The token is <c>s[start..end)</c>. Returns null when no rewrite is needed (no letters, or neither mixed
        /// scripts nor leet); otherwise returns the rebuilt token, which can still equal the input when none of its
        /// characters has a mapping (for example the Greek-majority token "αβa").
        /// Indices are absolute: <see cref="IsLeetCandidate"/> only compares them, so that equals token-relative indices.
        /// </summary>
        private static string? MapToken(string s, int start, int end)
        {
            int latin = 0, cyrillic = 0, greek = 0, leetish = 0;
            int lastLetter = -1;
            for (int i = start; i < end; i++)
            {
                char c = s[i];
                if (CharacterMaps.IsLatinLetter(c) || CharacterMaps.IsCyrillicLetter(c) || CharacterMaps.IsGreekLetter(c) || char.IsLetter(c))
                {
                    lastLetter = i;
                }
            }

            for (int i = start; i < end; i++)
            {
                char c = s[i];
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
                return null;
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
                return null;
            }

            var sb = new StringBuilder(end - start);
            for (int i = start; i < end; i++)
            {
                char c = s[i];
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
