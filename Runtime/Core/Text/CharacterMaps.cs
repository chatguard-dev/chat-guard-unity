#nullable enable
using System.Collections.Generic;

namespace ChatGuard.Core.Text
{
    /// <summary>
    /// Character tables and tests for <see cref="TextNormalizer"/>. Look-alike letters are rewritten only when they are
    /// the minority script in a token, like the Cyrillic "і" in "shіt", so all-Cyrillic text is never transliterated.
    /// The tables hold lowercase letters only, because text is lowercased before mapping.
    /// </summary>
    internal static class CharacterMaps
    {
        /// <summary>Cyrillic look-alikes of Latin letters, rewritten inside Latin-majority tokens.</summary>
        internal static readonly Dictionary<char, char> CyrillicToLatin = new Dictionary<char, char>
        {
            { 'а', 'a' }, { 'е', 'e' }, { 'о', 'o' }, { 'р', 'p' }, { 'с', 'c' }, { 'у', 'y' },
            { 'х', 'x' }, { 'к', 'k' }, { 'м', 'm' }, { 'т', 't' }, { 'н', 'h' }, { 'в', 'b' },
            { 'і', 'i' }, { 'ј', 'j' }, { 'ѕ', 's' }, { 'ԁ', 'd' }, { 'ԛ', 'q' }, { 'ԝ', 'w' },
            { 'ё', 'e' }, { 'ї', 'i' }, { 'ғ', 'f' }, { 'һ', 'h' }, { 'ӏ', 'l' },
        };

        /// <summary>
        /// Latin stand-ins for Cyrillic letters, by shape or by sound, rewritten inside Cyrillic-majority tokens.
        /// </summary>
        internal static readonly Dictionary<char, char> LatinToCyrillic = new Dictionary<char, char>
        {
            { 'a', 'а' }, { 'e', 'е' }, { 'o', 'о' }, { 'p', 'р' }, { 'c', 'с' }, { 'y', 'у' },
            { 'x', 'х' }, { 'k', 'к' }, { 'm', 'м' }, { 't', 'т' }, { 'h', 'н' }, { 'b', 'в' },
            { 'i', 'и' }, { 'j', 'ј' }, { 'n', 'п' }, { 'u', 'и' }, { 'w', 'ш' }, { 'r', 'г' },
            { 'g', 'д' }, { 'd', 'д' }, { 's', 'с' }, { 'l', 'л' }, { 'v', 'в' }, { 'z', 'з' },
        };

        /// <summary>Greek look-alikes of Latin letters, rewritten inside Latin-majority tokens.</summary>
        internal static readonly Dictionary<char, char> GreekToLatin = new Dictionary<char, char>
        {
            { 'α', 'a' }, { 'ο', 'o' }, { 'ε', 'e' }, { 'ρ', 'p' }, { 'τ', 't' }, { 'υ', 'u' },
            { 'ν', 'v' }, { 'κ', 'k' }, { 'χ', 'x' }, { 'ι', 'i' }, { 'η', 'n' }, { 'ϲ', 'c' },
            { 'ω', 'w' }, { 'ς', 's' }, { 'β', 'b' }, { 'γ', 'y' }, { 'μ', 'u' },
        };

        /// <summary>Leet digits and symbols → Latin letters, rewritten inside Latin-majority tokens.</summary>
        internal static readonly Dictionary<char, char> LatinLeet = new Dictionary<char, char>
        {
            { '0', 'o' }, { '1', 'i' }, { '3', 'e' }, { '4', 'a' }, { '5', 's' }, { '7', 't' },
            { '8', 'b' }, { '@', 'a' }, { '$', 's' }, { '!', 'i' }, { '|', 'l' }, { '+', 't' },
            { '€', 'e' }, { '¢', 'c' }, { '£', 'l' }, { '¥', 'y' },
        };

        /// <summary>Leet digits and symbols → Cyrillic letters, rewritten inside Cyrillic-majority tokens.</summary>
        internal static readonly Dictionary<char, char> CyrillicLeet = new Dictionary<char, char>
        {
            { '0', 'о' }, { '3', 'з' }, { '4', 'ч' }, { '6', 'б' }, { '@', 'а' }, { '$', 'с' },
        };

        /// <summary>
        /// True for characters with no visible glyph, which players insert inside words to dodge the word lists.
        /// </summary>
        internal static bool IsInvisible(char c)
        {
            switch (c)
            {
                case '­': // U+00AD soft hyphen
                case '͏': // U+034F combining grapheme joiner
                case '؜': // U+061C Arabic letter mark
                case '᠎': // U+180E Mongolian vowel separator
                case '​': // U+200B zero-width space
                case '‌': // U+200C zero-width non-joiner
                case '‍': // U+200D zero-width joiner
                case '‎': // U+200E left-to-right mark
                case '‏': // U+200F right-to-left mark
                case '‪':
                case '‫':
                case '‬':
                case '‭':
                case '‮': // U+202A..U+202E bidi embedding and override controls
                case '⁠': // U+2060 word joiner
                case '⁡':
                case '⁢':
                case '⁣':
                case '⁤': // U+2061..U+2064 invisible operators
                case '⁦':
                case '⁧':
                case '⁨':
                case '⁩': // U+2066..U+2069 bidi isolates
                case '﻿': // U+FEFF zero-width no-break space (BOM)
                    return true;
                default:
                    return c >= '︀' && c <= '️'; // U+FE00..U+FE0F variation selectors
            }
        }

        internal static bool IsLatinLetter(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                || (c >= 'À' && c <= 'ɏ' && c != '×' && c != '÷')
                || (c >= 'Ḁ' && c <= 'ỿ');
        }

        internal static bool IsCyrillicLetter(char c)
        {
            return (c >= 'Ѐ' && c <= 'ӿ') || (c >= 'Ԁ' && c <= 'ԯ');
        }

        internal static bool IsGreekLetter(char c)
        {
            return c >= 'Ͱ' && c <= 'Ͽ';
        }
    }
}
