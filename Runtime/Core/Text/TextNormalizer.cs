#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ChatGuard.Core.Text
{
    /// <summary>
    /// Turns chat text into one standard form so disguised words still match the word lists. The server and the Unity
    /// client share this code, so both produce the same string for a message.
    /// </summary>
    /// <remarks>Uses only BCL APIs available in Unity 2021.3 (netstandard2.1).</remarks>
    public static class TextNormalizer
    {
        /// <summary>Longest run of one character that <see cref="Normalize"/> keeps: "shiiiit" → "shiit".</summary>
        public const int MaxRun = 2;

        /// <summary>
        /// Returns the standard form of <paramref name="input"/>, which the word lists match and the server hashes.
        /// Null or empty input returns an empty string.
        /// </summary>
        /// <remarks>
        /// Steps: 1. Drop invisible characters and non-whitespace control characters. 2. Apply Unicode NFKC, which
        /// makes full-width and styled letters plain. 3. Lowercase. 4. In each word, rewrite look-alike letters from
        /// another script and leet ("sh1t" → "shit"). 5. Join words with single spaces, trimmed. 6. Cut runs of a
        /// repeated character to <see cref="MaxRun"/>.
        /// </remarks>
        public static string Normalize(string? input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            string s = StripInvisible(input!);
            s = TryNormalize(s, NormalizationForm.FormKC);
            s = s.Replace('İ', 'i'); // before lowering: runtimes disagree on the lowercase form of İ
            s = IsLowercaseAscii(s) ? s : s.ToLowerInvariant(); // skip the no-op: Mono's ToLowerInvariant always copies
            s = s.Replace("i̇", "i"); // i + combining dot above (U+0307), which some runtimes leave behind
            s = MapTokens(s);
            s = CollapseRuns(s, MaxRun);
            return s;
        }

        /// <summary>
        /// Splits <see cref="Normalize"/> output at whitespace into the tokens the word lists match. Removes every
        /// character that is not a letter or digit ("s.h.i.t" → "shit", "discord.gg/x" → "discordggx") and skips tokens
        /// left empty.
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
        /// Accent-insensitive form of a token or phrase, used to match word lists and project rules. Removes combining
        /// marks after Unicode NFD ("señor" → "senor") and folds letters such as ß → ss, ł → l and æ → ae. Case is
        /// kept, so pass <see cref="Normalize"/> output or its tokens. Cyrillic й and ё fold to и and е, on purpose:
        /// chat uses ё and е interchangeably.
        /// </summary>
        public static string Fold(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return string.Empty;
            }

            // ASCII is NFD-stable, has no combining marks and hits none of the cases below.
            if (IsAscii(token))
            {
                return token;
            }

            string d = TryNormalize(token, NormalizationForm.FormD);
            StringBuilder? sb = null; // created at the first change; until then the output is d[0..i)
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

        /// <summary>
        /// Cuts every run of a repeated character to <paramref name="maxRun"/> ("shiiiit" → "shiit" with 2, "shit"
        /// with 1). Returns <paramref name="text"/> unchanged when <paramref name="maxRun"/> is below 1, and an empty
        /// string for null.
        /// </summary>
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
        /// Returns <paramref name="sb"/>, first creating it with <c>text[0..end)</c> when it is null. Lets a loop skip
        /// copying until the first character it drops or replaces.
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

        /// <summary>True when <paramref name="s"/> is ASCII without A..Z, so lowercasing would not change it.</summary>
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
        /// Drops invisible characters, non-whitespace control characters and the supplementary variation selectors.
        /// Folds styled Latin letters and digits from the Mathematical Alphanumeric Symbols block (𝐢𝐝𝐢𝐨𝐭) to ASCII
        /// here: Unity's Mono skips NFKC for characters outside the BMP, and both hosts must produce the same text.
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
                        // Keep emoji and other supplementary characters; drop the supplementary variation selectors.
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
        /// Pre-scan for <see cref="StripInvisible"/>: false only when the text has no surrogates, invisible characters
        /// or non-whitespace controls, so stripping would change nothing.
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
            // ASCII is stable under every normalization form; checking it first skips Mono's slow IsNormalized.
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
                // Invalid surrogate pairs: keep the text as it is rather than throw.
                return s;
            }
        }

        /// <summary>
        /// Runs <see cref="MapToken"/> on every whitespace-separated token and joins the results with single spaces.
        /// Copies nothing while the output is still a prefix of <paramref name="s"/>.
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
                    // Still a prefix of s if this token is unmapped and starts s or follows the previous one after
                    // exactly one ' '.
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
        /// Rewrites look-alike letters from a minority script, and leet characters, toward the majority script of the
        /// token <c>s[start..end)</c>. Latin wins ties, and Greek-majority tokens are kept as they are. Leet applies
        /// only when the token has at least as many letters as leet characters: "sh1t" is mapped, "1v1" is not.
        /// </summary>
        /// <remarks>
        /// Returns null when there is nothing to rewrite. A non-null result can still equal the input, as for "αβa".
        /// Indices stay absolute, which is safe because <see cref="IsLeetCandidate"/> only compares them.
        /// </remarks>
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
        /// Whether <paramref name="c"/> counts as leet. Leet digits count anywhere in the token; leet symbols count
        /// only when a letter comes later in it ("sh!t", "@ss"), so "idiot!!!" keeps its punctuation.
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
