#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ChatGuard
{
    /// <summary>
    /// Small JSON reader and writer without third-party dependencies, for the optional fields and nulls JsonUtility
    /// cannot express. Objects map to <c>Dictionary&lt;string, object?&gt;</c>, arrays to <c>List&lt;object?&gt;</c>
    /// and numbers to <see cref="double"/>. The <c>Get*</c> helpers accept a null object and return null or the
    /// fallback for a missing key or another type, so lookups chain.
    /// </summary>
    public static class MiniJson
    {
        /// <summary>
        /// Deepest nesting of objects and arrays <see cref="Parse"/> accepts; the outermost container is level 1.
        /// Chat Guard responses nest at most 3 levels. The cap turns hostile input into a
        /// <see cref="FormatException"/> instead of an uncatchable stack overflow that would end the game.
        /// </summary>
        internal const int MaxDepth = 128;

        /// <summary>
        /// Parses one JSON value into the types listed on <see cref="MiniJson"/>. Lenient: it also accepts some invalid
        /// JSON, such as <c>+1</c>, <c>.5</c> or raw control characters in strings.
        /// </summary>
        /// <exception cref="FormatException">
        /// The text is unreadable or truncated, has non-whitespace after the value, or nests deeper than 128 levels.
        /// </exception>
        public static object? Parse(string json)
        {
            var reader = new Reader(json);
            object? value = reader.ReadValue(0);
            reader.SkipWhitespace();
            if (!reader.AtEnd)
            {
                throw new FormatException("Trailing characters after JSON value");
            }

            return value;
        }

        /// <summary>
        /// Writes <paramref name="value"/> as compact JSON. It handles the types <see cref="Parse"/> returns plus
        /// <see cref="int"/>, <see cref="long"/> and <see cref="float"/>. Other enumerables become arrays, even a
        /// <c>Dictionary&lt;string, int&gt;</c>. Anything else, such as a <see cref="decimal"/>, becomes a JSON string
        /// of its <c>ToString()</c>. NaN and infinities are written bare, as invalid JSON that <see cref="Parse"/>
        /// rejects.
        /// </summary>
        public static string Write(object? value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value);
            return sb.ToString();
        }

        public static Dictionary<string, object?>? AsObject(object? value)
        {
            return value as Dictionary<string, object?>;
        }

        public static List<object?>? AsArray(object? value)
        {
            return value as List<object?>;
        }

        public static string? GetString(Dictionary<string, object?>? obj, string key)
        {
            return obj != null && obj.TryGetValue(key, out object? v) ? v as string : null;
        }

        public static double GetNumber(Dictionary<string, object?>? obj, string key, double fallback = 0)
        {
            return obj != null && obj.TryGetValue(key, out object? v) && v is double d ? d : fallback;
        }

        public static bool GetBool(Dictionary<string, object?>? obj, string key, bool fallback = false)
        {
            return obj != null && obj.TryGetValue(key, out object? v) && v is bool b ? b : fallback;
        }

        public static Dictionary<string, object?>? GetObject(Dictionary<string, object?>? obj, string key)
        {
            return obj != null && obj.TryGetValue(key, out object? v) ? v as Dictionary<string, object?> : null;
        }

        private static void WriteValue(StringBuilder sb, object? value)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    break;
                case string s:
                    WriteString(sb, s);
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case int i:
                    sb.Append(i.ToString(CultureInfo.InvariantCulture));
                    break;
                case long l:
                    sb.Append(l.ToString(CultureInfo.InvariantCulture));
                    break;
                case float f:
                    sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case double d:
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case Dictionary<string, object?> obj:
                    sb.Append('{');
                    bool first = true;
                    foreach (KeyValuePair<string, object?> pair in obj)
                    {
                        if (!first)
                        {
                            sb.Append(',');
                        }

                        first = false;
                        WriteString(sb, pair.Key);
                        sb.Append(':');
                        WriteValue(sb, pair.Value);
                    }

                    sb.Append('}');
                    break;
                case System.Collections.IEnumerable list:
                    sb.Append('[');
                    bool firstItem = true;
                    foreach (object? item in list)
                    {
                        if (!firstItem)
                        {
                            sb.Append(',');
                        }

                        firstItem = false;
                        WriteValue(sb, item);
                    }

                    sb.Append(']');
                    break;
                default:
                    WriteString(sb, value.ToString() ?? string.Empty);
                    break;
            }
        }

        /// <summary>
        /// Appends <paramref name="s"/> as a quoted JSON string, escaping only <c>"</c>, <c>\</c> and characters below
        /// U+0020. <see cref="ChatGuardClient.BuildRequestJson"/> uses it, and
        /// <see cref="Utf8RequestJsonSink.WriteString"/> must match its escapes.
        /// </summary>
        internal static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < ' ')
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }

                        break;
                }
            }

            sb.Append('"');
        }

        private sealed class Reader
        {
            private readonly string _s;
            private int _i;

            public Reader(string s)
            {
                _s = s;
            }

            public bool AtEnd => _i >= _s.Length;

            public void SkipWhitespace()
            {
                while (_i < _s.Length && char.IsWhiteSpace(_s[_i]))
                {
                    _i++;
                }
            }

            /// <summary>Reads the value at the current position, which is inside <paramref name="depth"/> containers.</summary>
            public object? ReadValue(int depth)
            {
                SkipWhitespace();
                if (AtEnd)
                {
                    throw new FormatException("Unexpected end of JSON");
                }

                char c = _s[_i];
                switch (c)
                {
                    case '{': return ReadObject(depth + 1);
                    case '[': return ReadArray(depth + 1);
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default: return ReadNumber();
                }
            }

            /// <summary>Rejects a container deeper than <see cref="MaxDepth"/> before reading into it.</summary>
            private void CheckDepth(int depth)
            {
                if (depth > MaxDepth)
                {
                    throw new FormatException("Nesting deeper than " + MaxDepth + " levels at " + _i);
                }
            }

            private Dictionary<string, object?> ReadObject(int depth)
            {
                CheckDepth(depth);
                var obj = new Dictionary<string, object?>();
                _i++;
                SkipWhitespace();
                if (_i < _s.Length && _s[_i] == '}')
                {
                    _i++;
                    return obj;
                }

                while (true)
                {
                    SkipWhitespace();
                    string key = ReadString();
                    SkipWhitespace();
                    if (_i >= _s.Length || _s[_i] != ':')
                    {
                        throw new FormatException("Expected ':' at " + _i);
                    }

                    _i++;
                    obj[key] = ReadValue(depth);
                    SkipWhitespace();
                    if (_i < _s.Length && _s[_i] == ',')
                    {
                        _i++;
                        continue;
                    }

                    if (_i < _s.Length && _s[_i] == '}')
                    {
                        _i++;
                        return obj;
                    }

                    throw new FormatException("Expected ',' or '}' at " + _i);
                }
            }

            private List<object?> ReadArray(int depth)
            {
                CheckDepth(depth);
                var list = new List<object?>();
                _i++;
                SkipWhitespace();
                if (_i < _s.Length && _s[_i] == ']')
                {
                    _i++;
                    return list;
                }

                while (true)
                {
                    list.Add(ReadValue(depth));
                    SkipWhitespace();
                    if (_i < _s.Length && _s[_i] == ',')
                    {
                        _i++;
                        continue;
                    }

                    if (_i < _s.Length && _s[_i] == ']')
                    {
                        _i++;
                        return list;
                    }

                    throw new FormatException("Expected ',' or ']' at " + _i);
                }
            }

            private string ReadString()
            {
                if (_i >= _s.Length || _s[_i] != '"')
                {
                    throw new FormatException("Expected string at " + _i);
                }

                _i++;

                // Fast path: a string without escapes, the usual case, is one Substring. Otherwise the loop below takes
                // over with the plain prefix copied.
                int start = _i;
                while (_i < _s.Length && _s[_i] != '"' && _s[_i] != '\\')
                {
                    _i++;
                }

                if (_i < _s.Length && _s[_i] == '"')
                {
                    string plain = _s.Substring(start, _i - start);
                    _i++;
                    return plain;
                }

                var sb = new StringBuilder();
                sb.Append(_s, start, _i - start);
                while (_i < _s.Length)
                {
                    char c = _s[_i++];
                    if (c == '"')
                    {
                        return sb.ToString();
                    }

                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }

                    if (_i >= _s.Length)
                    {
                        break;
                    }

                    char e = _s[_i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_i + 4 > _s.Length)
                            {
                                throw new FormatException("Bad unicode escape");
                            }

                            sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _i += 4;
                            break;
                        default: throw new FormatException("Bad escape \\" + e);
                    }
                }

                throw new FormatException("Unterminated string");
            }

            private double ReadNumber()
            {
                int start = _i;
                while (_i < _s.Length && "+-0123456789.eE".IndexOf(_s[_i]) >= 0)
                {
                    _i++;
                }

                // On Mono and CoreCLR, double.TryParse(string) is this span overload plus a null check, so parsing the
                // slice in place gives the same result without a Substring.
                if (start == _i || !double.TryParse(_s.AsSpan(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                {
                    throw new FormatException("Invalid number at " + start);
                }

                return value;
            }

            private void Expect(string literal)
            {
                if (_s.Length - _i < literal.Length || string.CompareOrdinal(_s, _i, literal, 0, literal.Length) != 0)
                {
                    throw new FormatException("Expected " + literal + " at " + _i);
                }

                _i += literal.Length;
            }
        }
    }
}
