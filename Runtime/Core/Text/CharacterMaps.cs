#nullable enable
using System.Collections.Generic;

namespace ChatGuard.Core.Text
{
    /// <summary>
    /// Static character tables used by <see cref="TextNormalizer"/>. Mapping is script-aware: a
    /// homoglyph is only rewritten when it is the minority script inside a token (e.g. a Cyrillic
    /// "і" inside "shіt"), so pure Cyrillic text is never transliterated.
    /// </summary>
    internal static class CharacterMaps
    {
        /// <summary>Cyrillic letters that look like Latin letters (lowercase only; input is lowercased first).</summary>
        internal static readonly Dictionary<char, char> CyrillicToLatin = new Dictionary<char, char>
        {
            { 'а', 'a' }, { 'е', 'e' }, { 'о', 'o' }, { 'р', 'p' }, { 'с', 'c' }, { 'у', 'y' },
            { 'х', 'x' }, { 'к', 'k' }, { 'м', 'm' }, { 'т', 't' }, { 'н', 'h' }, { 'в', 'b' },
            { 'і', 'i' }, { 'ј', 'j' }, { 'ѕ', 's' }, { 'ԁ', 'd' }, { 'ԛ', 'q' }, { 'ԝ', 'w' },
            { 'ё', 'e' }, { 'ї', 'i' }, { 'ғ', 'f' }, { 'һ', 'h' }, { 'ӏ', 'l' },
        };

        /// <summary>Latin letters that look like Cyrillic letters, applied inside Cyrillic-majority tokens.</summary>
        internal static readonly Dictionary<char, char> LatinToCyrillic = new Dictionary<char, char>
        {
            { 'a', 'а' }, { 'e', 'е' }, { 'o', 'о' }, { 'p', 'р' }, { 'c', 'с' }, { 'y', 'у' },
            { 'x', 'х' }, { 'k', 'к' }, { 'm', 'м' }, { 't', 'т' }, { 'h', 'н' }, { 'b', 'в' },
            { 'i', 'и' }, { 'j', 'ј' }, { 'n', 'п' }, { 'u', 'и' }, { 'w', 'ш' }, { 'r', 'г' },
            { 'g', 'д' }, { 'd', 'д' }, { 's', 'с' }, { 'l', 'л' }, { 'v', 'в' }, { 'z', 'з' },
        };

        /// <summary>Greek letters that look like Latin letters, applied inside Latin-majority tokens.</summary>
        internal static readonly Dictionary<char, char> GreekToLatin = new Dictionary<char, char>
        {
            { 'α', 'a' }, { 'ο', 'o' }, { 'ε', 'e' }, { 'ρ', 'p' }, { 'τ', 't' }, { 'υ', 'u' },
            { 'ν', 'v' }, { 'κ', 'k' }, { 'χ', 'x' }, { 'ι', 'i' }, { 'η', 'n' }, { 'ϲ', 'c' },
            { 'ω', 'w' }, { 'ς', 's' }, { 'β', 'b' }, { 'γ', 'y' }, { 'μ', 'u' },
        };

        /// <summary>Leet digits and symbols → Latin letters, applied inside Latin-majority tokens.</summary>
        internal static readonly Dictionary<char, char> LatinLeet = new Dictionary<char, char>
        {
            { '0', 'o' }, { '1', 'i' }, { '3', 'e' }, { '4', 'a' }, { '5', 's' }, { '7', 't' },
            { '8', 'b' }, { '@', 'a' }, { '$', 's' }, { '!', 'i' }, { '|', 'l' }, { '+', 't' },
            { '€', 'e' }, { '¢', 'c' }, { '£', 'l' }, { '¥', 'y' },
        };

        /// <summary>Leet digits and symbols → Cyrillic letters, applied inside Cyrillic-majority tokens.</summary>
        internal static readonly Dictionary<char, char> CyrillicLeet = new Dictionary<char, char>
        {
            { '0', 'о' }, { '3', 'з' }, { '4', 'ч' }, { '6', 'б' }, { '@', 'а' }, { '$', 'с' },
        };

        /// <summary>Characters that carry no visible content and are used to break word matching.</summary>
        internal static bool IsInvisible(char c)
        {
            switch (c)
            {
                case '­': // soft hyphen
                case '͏': // combining grapheme joiner
                case '؜': // arabic letter mark
                case '᠎': // mongolian vowel separator
                case '​': // zero width space
                case '‌': // zero width non-joiner
                case '‍': // zero width joiner
                case '‎': // left-to-right mark
                case '‏': // right-to-left mark
                case '‪':
                case '‫':
                case '‬':
                case '‭':
                case '‮': // bidi embedding/override controls
                case '⁠': // word joiner
                case '⁡':
                case '⁢':
                case '⁣':
                case '⁤': // invisible operators
                case '⁦':
                case '⁧':
                case '⁨':
                case '⁩': // bidi isolates
                case '﻿': // zero width no-break space / BOM
                    return true;
                default:
                    return c >= '︀' && c <= '️'; // variation selectors
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
