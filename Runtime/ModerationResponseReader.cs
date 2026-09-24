#nullable enable
using System;
using System.Globalization;
using System.Text;
using System.Threading;
using ChatGuard.Core;
using Unity.Collections;

namespace ChatGuard
{
    /// <summary>
    /// Reads a 200 body from <c>POST /v1/moderate</c> straight from its UTF-8 bytes in one pass, with no body string
    /// or Dictionary tree. It handles only the plain JSON the server writes and returns null ("not handled") for
    /// anything else. The caller then takes the string path (<c>DownloadHandler.text</c> +
    /// <see cref="ChatGuardClient.TryParseResponse"/>), so an unusual body gets exactly the string path's result.
    /// </summary>
    /// <remarks>
    /// Unknown keys are skipped, but their values must still pass the byte, syntax, depth and number rules below.
    /// Not handled:
    /// <list type="bullet">
    /// <item>any byte of 0x80 or above, and any control byte other than tab, LF and CR (DEL included);</item>
    /// <item>any backslash;</item>
    /// <item>a root that is not an object, or JSON that <see cref="MiniJson.Parse"/> rejects (malformed, truncated or
    /// followed by more content);</item>
    /// <item>nesting deeper than <see cref="MaxDepth"/>;</item>
    /// <item>a number token longer than <see cref="MaxNumberLength"/> characters, or one <c>double.TryParse</c>
    /// rejects;</item>
    /// <item>a known key twice in the same object;</item>
    /// <item>a known key with a value of an unexpected JSON type. Null is accepted and means missing, as in MiniJson's
    /// getters;</item>
    /// <item>a missing or null action, and an action, target choice or degraded reason that is not exactly a
    /// lowercase wire name;</item>
    /// <item>a body in native memory longer than <see cref="MaxBodyLength"/> bytes.</item>
    /// </list>
    /// A handled body gives the same <see cref="ModerationResult"/> as <see cref="ChatGuardClient.TryParseResponse"/>
    /// on its UTF-8 text. Every byte read is ASCII, so each character equals its byte. Numbers go through the same
    /// <c>double.TryParse(ReadOnlySpan&lt;char&gt;, NumberStyles.Float, CultureInfo.InvariantCulture)</c> call as
    /// MiniJson.
    /// </remarks>
    internal static class ModerationResponseReader
    {
        /// <summary>Deepest container nesting handled; the root object is level 1.</summary>
        internal const int MaxDepth = 16;

        /// <summary>Longest number token handled, in characters; also the size of ReadNumber's stack buffer.</summary>
        internal const int MaxNumberLength = 64;

        /// <summary>
        /// Longest body read from native memory, in bytes. The server's body is under 1 KB, so a 200 body over this
        /// limit is something else, such as an HTML page from a proxy or a wrong base URL. It goes to the string path
        /// uncopied, so the reusable buffer never grows past this size.
        /// </summary>
        internal const int MaxBodyLength = 64 * 1024;

        private const int InitialCapacity = 2048;

        private const int KeyAction = 1;
        private const int KeySeverity = 2;
        private const int KeyVerdicts = 4;
        private const int KeyTarget = 8;
        private const int KeyDegraded = 16;
        private const int KeyDegradedReason = 32;
        private const int KeyCached = 64;
        private const int KeyModel = 128;
        private const int KeyId = 256;
        private const int KeyQuota = 512;

        // One buffer per thread, like Utf8RequestJsonSink's. In the SDK only ChatGuardClient.Finish reaches it, on the
        // main thread, so in practice there is one buffer; being per thread keeps a call from another thread safe.
        [ThreadStatic]
        private static byte[]? t_body;

        // The model name repeats between responses, so reusing the last instance saves a string per message. Accessed
        // through Volatile: on ARM64 a plain store does not guarantee another thread sees the string's characters.
        private static string? s_lastModel;

        /// <summary>
        /// Reads a body held in native memory (<c>DownloadHandler.nativeData</c>) after copying it into this thread's
        /// reusable buffer. Returns null when the body is not handled (see the class remarks), including when it is
        /// empty or longer than <see cref="MaxBodyLength"/> bytes.
        /// </summary>
        internal static ModerationResult? TryRead(NativeArray<byte>.ReadOnly body, int latencyMs)
        {
            // Length only: NativeArray<T>.ReadOnly.IsCreated does not exist before Unity 2021.3.3, and a ReadOnly with
            // no memory behind it (DownloadHandler's default nativeData) has Length 0 on every version.
            if (body.Length == 0 || body.Length > MaxBodyLength)
            {
                return null;
            }

            int length = body.Length;
            byte[]? buffer = t_body;
            if (buffer == null || buffer.Length < length)
            {
                buffer = new byte[Math.Max(length, InitialCapacity)];
                t_body = buffer;
            }

            NativeArray<byte>.Copy(body, 0, buffer, 0, length);
            return TryRead(buffer, length, latencyMs);
        }

        /// <summary>
        /// Reads the first <paramref name="length"/> bytes of <paramref name="json"/>. Returns null when the body is
        /// not handled (see the class remarks).
        /// </summary>
        internal static ModerationResult? TryRead(byte[] json, int length, int latencyMs)
        {
            var reader = new Reader(json, length);
            if (!reader.ReadResponse(out Fields f))
            {
                return null;
            }

            // The VerdictSet constructor clamps each value through its indexer, as TryParseResponse's assignments do.
            var verdicts = new VerdictSet(f.Insult, f.Threat, f.Hate, f.Sexual, f.Spam, f.Trading);
            TargetVerdict? target = f.HasTarget ? new TargetVerdict(f.Choice, f.Confidence) : null;
            string model = f.ModelLength < 0 ? string.Empty : Model(json, f.ModelStart, f.ModelLength);
            string? id = f.IdLength < 0 ? null : Encoding.UTF8.GetString(json, f.IdStart, f.IdLength);
            return new ModerationResult(f.Action, f.Severity, verdicts, target, f.Degraded, f.Reason, f.Cached, model, latencyMs, id, (long)f.QuotaUsed, (long)f.QuotaLimit, ResultSource.Server, null);
        }

        /// <summary>
        /// True when <c>DownloadHandler.text</c> decodes a body with this Content-Type as UTF-8 without logging
        /// anything, so ASCII bytes read directly give the same characters. Accepted: no header, no <c>charset</c>, no
        /// <c>=</c> after it, or a charset value equal to <c>utf-8</c> in any letter case. The value is compared after
        /// trimming it as <c>DownloadHandler.text</c> does: spaces, then quotes, then spaces, then cut at the first
        /// <c>;</c>. Anything else, including a header with any character outside printable ASCII, is false (not
        /// handled) and left to the string path.
        /// </summary>
        internal static bool IsUtf8ContentType(string? contentType)
        {
            if (string.IsNullOrEmpty(contentType))
            {
                return true;
            }

            string header = contentType!;
            for (int i = 0; i < header.Length; i++)
            {
                if (header[i] < ' ' || header[i] > '~')
                {
                    return false;
                }
            }

            int charset = IndexOfCharset(header);
            if (charset < 0)
            {
                return true;
            }

            int equals = header.IndexOf('=', charset);
            if (equals < 0)
            {
                return true;
            }

            int start = equals + 1;
            int end = header.Length;
            TrimSpaces(header, ref start, ref end);
            while (start < end && (header[start] == '\'' || header[start] == '"'))
            {
                start++;
            }

            while (end > start && (header[end - 1] == '\'' || header[end - 1] == '"'))
            {
                end--;
            }

            TrimSpaces(header, ref start, ref end);
            int semicolon = header.IndexOf(';', start, end - start);
            if (semicolon >= 0)
            {
                end = semicolon;
            }

            return end - start == 5
                && (header[start] | 0x20) == 'u'
                && (header[start + 1] | 0x20) == 't'
                && (header[start + 2] | 0x20) == 'f'
                && header[start + 3] == '-'
                && header[start + 4] == '8';
        }

        /// <summary>
        /// First index of <c>charset</c> in any letter case, or -1. ASCII case folding is enough: the caller has
        /// already declined any header with a character outside printable ASCII.
        /// </summary>
        private static int IndexOfCharset(string header)
        {
            const string word = "charset";
            for (int i = 0; i + word.Length <= header.Length; i++)
            {
                int j = 0;
                while (j < word.Length && (header[i + j] | 0x20) == word[j])
                {
                    j++;
                }

                if (j == word.Length)
                {
                    return i;
                }
            }

            return -1;
        }

        private static void TrimSpaces(string s, ref int start, ref int end)
        {
            while (start < end && s[start] == ' ')
            {
                start++;
            }

            while (end > start && s[end - 1] == ' ')
            {
                end--;
            }
        }

        /// <summary>The model name as a string, reusing the last one when the characters are the same.</summary>
        private static string Model(byte[] json, int start, int length)
        {
            string? last = Volatile.Read(ref s_lastModel);
            if (last != null && last.Length == length)
            {
                int i = 0;
                while (i < length && last[i] == json[start + i])
                {
                    i++;
                }

                if (i == length)
                {
                    return last;
                }
            }

            string model = Encoding.UTF8.GetString(json, start, length);
            Volatile.Write(ref s_lastModel, model);
            return model;
        }

        /// <summary>The fields <see cref="ChatGuardClient.TryParseResponse"/> reads, with its defaults for missing values.</summary>
        private struct Fields
        {
            public ModerationAction Action;
            public double Severity;
            public bool HasTarget;
            public TargetChoice Choice;
            public double Confidence;
            public bool Degraded;
            public DegradedReason Reason;
            public bool Cached;
            public int ModelStart;
            public int ModelLength; // -1: missing or null ("" in the result)
            public int IdStart;
            public int IdLength; // -1: missing or null (null in the result)
            public double QuotaUsed;
            public double QuotaLimit;

            // Verdict probabilities as read, not yet clamped; 0 when the category or its "p" is missing or null.
            public double Insult;
            public double Threat;
            public double Hate;
            public double Sexual;
            public double Spam;
            public double Trading;

            public void SetProbability(VerdictCategory category, double p)
            {
                switch (category)
                {
                    case VerdictCategory.Insult:
                        Insult = p;
                        break;
                    case VerdictCategory.Threat:
                        Threat = p;
                        break;
                    case VerdictCategory.Hate:
                        Hate = p;
                        break;
                    case VerdictCategory.Sexual:
                        Sexual = p;
                        break;
                    case VerdictCategory.Spam:
                        Spam = p;
                        break;
                    case VerdictCategory.Trading:
                        Trading = p;
                        break;
                }
            }
        }

        /// <summary>
        /// Cursor over the body. ReadResponse, the other Read* methods and SkipValue return false when the body is not
        /// handled, and <see cref="NextMember"/> returns <see cref="Invalid"/>. Take, TakeNull and TakeLiteral return
        /// false when the next token is not the expected one, and KeyIs when the byte range holds other text. That
        /// false leaves the cursor in place for the caller's next alternative and alone does not mean "not handled".
        /// </summary>
        private struct Reader
        {
            private const int Invalid = -1;
            private const int End = 0;
            private const int Member = 1;

            private readonly byte[] _json;
            private readonly int _end;
            private int _pos;

            public Reader(byte[] json, int length)
            {
                _json = json;
                _end = length;
                _pos = 0;
            }

            public bool ReadResponse(out Fields f)
            {
                f = default;
                f.ModelLength = -1;
                f.IdLength = -1;
                SkipWhitespace();
                if (!Take((byte)'{'))
                {
                    return false;
                }

                int seen = 0;
                bool first = true;
                while (true)
                {
                    int step = NextMember(ref first, out int keyStart, out int keyLength);
                    if (step == End)
                    {
                        break;
                    }

                    if (step == Invalid)
                    {
                        return false;
                    }

                    int key = RootKey(keyStart, keyLength);
                    if ((seen & key) != 0)
                    {
                        return false;
                    }

                    seen |= key;
                    bool ok;
                    switch (key)
                    {
                        case KeyAction:
                            ok = ReadAction(out f.Action);
                            break;
                        case KeySeverity:
                            ok = ReadNumberOrNull(out f.Severity);
                            break;
                        case KeyVerdicts:
                            ok = ReadVerdicts(ref f);
                            break;
                        case KeyTarget:
                            ok = ReadTarget(ref f);
                            break;
                        case KeyDegraded:
                            ok = ReadBoolOrNull(out f.Degraded);
                            break;
                        case KeyDegradedReason:
                            ok = ReadDegradedReason(out f.Reason);
                            break;
                        case KeyCached:
                            ok = ReadBoolOrNull(out f.Cached);
                            break;
                        case KeyModel:
                            ok = ReadStringOrNull(out f.ModelStart, out f.ModelLength);
                            break;
                        case KeyId:
                            ok = ReadStringOrNull(out f.IdStart, out f.IdLength);
                            break;
                        case KeyQuota:
                            ok = ReadQuota(ref f);
                            break;
                        default:
                            ok = SkipValue(2);
                            break;
                    }

                    if (!ok)
                    {
                        return false;
                    }
                }

                // A missing action is not handled either (TryParseResponse returns null for it).
                SkipWhitespace();
                return _pos == _end && (seen & KeyAction) != 0;
            }

            private int RootKey(int start, int length)
            {
                switch (length)
                {
                    case 2: return KeyIs(start, length, "id") ? KeyId : 0;
                    case 5: return KeyIs(start, length, "model") ? KeyModel : KeyIs(start, length, "quota") ? KeyQuota : 0;
                    case 6: return KeyIs(start, length, "action") ? KeyAction : KeyIs(start, length, "target") ? KeyTarget : KeyIs(start, length, "cached") ? KeyCached : 0;
                    case 8: return KeyIs(start, length, "severity") ? KeySeverity : KeyIs(start, length, "verdicts") ? KeyVerdicts : KeyIs(start, length, "degraded") ? KeyDegraded : 0;
                    case 15: return KeyIs(start, length, "degraded_reason") ? KeyDegradedReason : 0;
                    default: return 0;
                }
            }

            // The enum readers below loop over each enum's range and compare with Core's wire names. That relies on
            // each enum having no gaps, and on VerdictCategory starting at 0. DegradedReason.None has no wire name, so
            // its loop starts at Quota.

            /// <summary>
            /// <c>action</c>: a string that is exactly a wire name. Null is not handled, since TryParseResponse returns
            /// null for it.
            /// </summary>
            private bool ReadAction(out ModerationAction action)
            {
                action = ModerationAction.Allow;
                if (!ReadString(out int start, out int length))
                {
                    return false;
                }

                for (var candidate = ModerationAction.Allow; candidate <= ModerationAction.Block; candidate++)
                {
                    if (KeyIs(start, length, ModerationActions.ToWireName(candidate)))
                    {
                        action = candidate;
                        return true;
                    }
                }

                return false;
            }

            /// <summary><c>degraded_reason</c>: null (None) or a string that is exactly a wire name.</summary>
            private bool ReadDegradedReason(out DegradedReason reason)
            {
                reason = DegradedReason.None;
                if (TakeNull())
                {
                    return true;
                }

                if (!ReadString(out int start, out int length))
                {
                    return false;
                }

                for (var candidate = DegradedReason.Quota; candidate <= DegradedReason.Offline; candidate++)
                {
                    if (KeyIs(start, length, DegradedReasons.ToWireName(candidate)!))
                    {
                        reason = candidate;
                        return true;
                    }
                }

                return false;
            }

            /// <summary>
            /// <c>verdicts</c>: null or an object. Each known category is null or <c>{"p": number or null}</c>; other
            /// keys are skipped.
            /// </summary>
            private bool ReadVerdicts(ref Fields f)
            {
                if (TakeNull())
                {
                    return true;
                }

                if (!Take((byte)'{'))
                {
                    return false;
                }

                int seen = 0;
                bool first = true;
                while (true)
                {
                    int step = NextMember(ref first, out int keyStart, out int keyLength);
                    if (step != Member)
                    {
                        return step == End;
                    }

                    int index = CategoryIndex(keyStart, keyLength);
                    if (index < 0)
                    {
                        if (!SkipValue(3))
                        {
                            return false;
                        }

                        continue;
                    }

                    if ((seen & (1 << index)) != 0 || !ReadProbability(out double p))
                    {
                        return false;
                    }

                    seen |= 1 << index;
                    f.SetProbability((VerdictCategory)index, p);
                }
            }

            private int CategoryIndex(int start, int length)
            {
                for (int i = 0; i < VerdictCategories.Count; i++)
                {
                    if (KeyIs(start, length, VerdictCategories.ToWireName((VerdictCategory)i)))
                    {
                        return i;
                    }
                }

                return -1;
            }

            /// <summary>
            /// One category: null (0) or an object whose <c>p</c> is a number or null (0). Other keys are skipped.
            /// </summary>
            private bool ReadProbability(out double p)
            {
                p = 0;
                if (TakeNull())
                {
                    return true;
                }

                if (!Take((byte)'{'))
                {
                    return false;
                }

                bool seenP = false;
                bool first = true;
                while (true)
                {
                    int step = NextMember(ref first, out int keyStart, out int keyLength);
                    if (step != Member)
                    {
                        return step == End;
                    }

                    if (!KeyIs(keyStart, keyLength, "p"))
                    {
                        if (!SkipValue(4))
                        {
                            return false;
                        }

                        continue;
                    }

                    if (seenP || !ReadNumberOrNull(out p))
                    {
                        return false;
                    }

                    seenP = true;
                }
            }

            /// <summary>
            /// <c>target</c>: null or an object with <c>choice</c> (null, or exactly a wire name) and <c>confidence</c>
            /// (number or null). Other keys are skipped. Without a choice there is no target, as in TryParseResponse.
            /// </summary>
            private bool ReadTarget(ref Fields f)
            {
                if (TakeNull())
                {
                    return true;
                }

                if (!Take((byte)'{'))
                {
                    return false;
                }

                bool seenChoice = false;
                bool seenConfidence = false;
                bool first = true;
                while (true)
                {
                    int step = NextMember(ref first, out int keyStart, out int keyLength);
                    if (step != Member)
                    {
                        return step == End;
                    }

                    if (KeyIs(keyStart, keyLength, "choice"))
                    {
                        if (seenChoice || !ReadChoice(ref f))
                        {
                            return false;
                        }

                        seenChoice = true;
                    }
                    else if (KeyIs(keyStart, keyLength, "confidence"))
                    {
                        if (seenConfidence || !ReadNumberOrNull(out f.Confidence))
                        {
                            return false;
                        }

                        seenConfidence = true;
                    }
                    else if (!SkipValue(3))
                    {
                        return false;
                    }
                }
            }

            /// <summary><c>choice</c>: null (no target) or a string that is exactly a wire name.</summary>
            private bool ReadChoice(ref Fields f)
            {
                if (TakeNull())
                {
                    return true;
                }

                if (!ReadString(out int start, out int length))
                {
                    return false;
                }

                for (var candidate = TargetChoice.OtherUser; candidate <= TargetChoice.Other; candidate++)
                {
                    if (KeyIs(start, length, TargetChoices.ToWireName(candidate)))
                    {
                        f.Choice = candidate;
                        f.HasTarget = true;
                        return true;
                    }
                }

                return false;
            }

            /// <summary>
            /// <c>quota</c>: null or an object whose <c>used</c> and <c>limit</c> are numbers or null (0). Other keys
            /// are skipped.
            /// </summary>
            private bool ReadQuota(ref Fields f)
            {
                if (TakeNull())
                {
                    return true;
                }

                if (!Take((byte)'{'))
                {
                    return false;
                }

                bool seenUsed = false;
                bool seenLimit = false;
                bool first = true;
                while (true)
                {
                    int step = NextMember(ref first, out int keyStart, out int keyLength);
                    if (step != Member)
                    {
                        return step == End;
                    }

                    if (KeyIs(keyStart, keyLength, "used"))
                    {
                        if (seenUsed || !ReadNumberOrNull(out f.QuotaUsed))
                        {
                            return false;
                        }

                        seenUsed = true;
                    }
                    else if (KeyIs(keyStart, keyLength, "limit"))
                    {
                        if (seenLimit || !ReadNumberOrNull(out f.QuotaLimit))
                        {
                            return false;
                        }

                        seenLimit = true;
                    }
                    else if (!SkipValue(3))
                    {
                        return false;
                    }
                }
            }

            private bool ReadNumberOrNull(out double value)
            {
                value = 0;
                return TakeNull() || ReadNumber(out value);
            }

            private bool ReadBoolOrNull(out bool value)
            {
                value = false;
                if (TakeLiteral("true"))
                {
                    value = true;
                    return true;
                }

                return TakeLiteral("false") || TakeNull();
            }

            /// <summary>A string or null; <paramref name="length"/> is -1 for null.</summary>
            private bool ReadStringOrNull(out int start, out int length)
            {
                start = 0;
                length = -1;
                return TakeNull() || ReadString(out start, out length);
            }

            /// <summary>
            /// Call after an object's <c>{</c> or after its previous member's value. Returns <see cref="Member"/> with
            /// the key's content range and the cursor on the value, <see cref="End"/> after taking the closing <c>}</c>,
            /// or <see cref="Invalid"/> when the body is not handled.
            /// </summary>
            private int NextMember(ref bool first, out int keyStart, out int keyLength)
            {
                keyStart = 0;
                keyLength = 0;
                SkipWhitespace();
                if (Take((byte)'}'))
                {
                    return End;
                }

                if (!first)
                {
                    if (!Take((byte)','))
                    {
                        return Invalid;
                    }

                    SkipWhitespace();
                }

                first = false;
                if (!ReadString(out keyStart, out keyLength))
                {
                    return Invalid;
                }

                SkipWhitespace();
                if (!Take((byte)':'))
                {
                    return Invalid;
                }

                SkipWhitespace();
                return Member;
            }

            /// <summary>
            /// Checks and skips one value of any type. <paramref name="depth"/> is the nesting level a container here
            /// would have; deeper than <see cref="MaxDepth"/> is not handled.
            /// </summary>
            private bool SkipValue(int depth)
            {
                if (_pos >= _end)
                {
                    return false;
                }

                switch (_json[_pos])
                {
                    case (byte)'"':
                        return ReadString(out _, out _);
                    case (byte)'t':
                        return TakeLiteral("true");
                    case (byte)'f':
                        return TakeLiteral("false");
                    case (byte)'n':
                        return TakeNull();
                    case (byte)'{':
                    {
                        if (depth > MaxDepth)
                        {
                            return false;
                        }

                        _pos++;
                        bool first = true;
                        while (true)
                        {
                            int step = NextMember(ref first, out _, out _);
                            if (step != Member)
                            {
                                return step == End;
                            }

                            if (!SkipValue(depth + 1))
                            {
                                return false;
                            }
                        }
                    }

                    case (byte)'[':
                    {
                        if (depth > MaxDepth)
                        {
                            return false;
                        }

                        _pos++;
                        SkipWhitespace();
                        if (Take((byte)']'))
                        {
                            return true;
                        }

                        while (true)
                        {
                            SkipWhitespace();
                            if (!SkipValue(depth + 1))
                            {
                                return false;
                            }

                            SkipWhitespace();
                            if (Take((byte)']'))
                            {
                                return true;
                            }

                            if (!Take((byte)','))
                            {
                                return false;
                            }
                        }
                    }

                    default:
                        return ReadNumber(out _);
                }
            }

            /// <summary>
            /// A string of printable ASCII, tab, LF or CR, with no backslash; gives the content range between the
            /// quotes. MiniJson's escape-free fast path returns exactly those characters for it.
            /// </summary>
            private bool ReadString(out int start, out int length)
            {
                start = 0;
                length = 0;
                if (!Take((byte)'"'))
                {
                    return false;
                }

                start = _pos;
                while (_pos < _end)
                {
                    byte b = _json[_pos];
                    if (b == (byte)'"')
                    {
                        length = _pos - start;
                        _pos++;
                        return true;
                    }

                    if (b == (byte)'\\' || b >= 0x7F || (b < 0x20 && b != (byte)'\t' && b != (byte)'\n' && b != (byte)'\r'))
                    {
                        return false;
                    }

                    _pos++;
                }

                return false;
            }

            /// <summary>
            /// Takes the same token as MiniJson's ReadNumber, the longest run of <c>+-0123456789.eE</c>, and parses it
            /// with the same <c>double.TryParse</c> overload.
            /// </summary>
            private bool ReadNumber(out double value)
            {
                value = 0;
                int start = _pos;
                while (_pos < _end && IsNumberByte(_json[_pos]))
                {
                    _pos++;
                }

                int length = _pos - start;
                if (length == 0 || length > MaxNumberLength)
                {
                    return false;
                }

                Span<char> chars = stackalloc char[MaxNumberLength];
                for (int i = 0; i < length; i++)
                {
                    chars[i] = (char)_json[start + i];
                }

                return double.TryParse(chars.Slice(0, length), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }

            private static bool IsNumberByte(byte b)
            {
                return (b >= (byte)'0' && b <= (byte)'9') || b == (byte)'-' || b == (byte)'+' || b == (byte)'.' || b == (byte)'e' || b == (byte)'E';
            }

            /// <summary>
            /// Skips space, tab, LF and CR only. Any other whitespace that MiniJson skips (<c>char.IsWhiteSpace</c>)
            /// makes the body not handled.
            /// </summary>
            private void SkipWhitespace()
            {
                while (_pos < _end)
                {
                    byte b = _json[_pos];
                    if (b != (byte)' ' && b != (byte)'\t' && b != (byte)'\n' && b != (byte)'\r')
                    {
                        return;
                    }

                    _pos++;
                }
            }

            private bool Take(byte expected)
            {
                if (_pos < _end && _json[_pos] == expected)
                {
                    _pos++;
                    return true;
                }

                return false;
            }

            private bool TakeNull()
            {
                return TakeLiteral("null");
            }

            private bool TakeLiteral(string literal)
            {
                if (_end - _pos < literal.Length)
                {
                    return false;
                }

                for (int i = 0; i < literal.Length; i++)
                {
                    if (_json[_pos + i] != literal[i])
                    {
                        return false;
                    }
                }

                _pos += literal.Length;
                return true;
            }

            /// <summary>True when <c>json[start..start+length)</c> is exactly <paramref name="ascii"/>.</summary>
            private bool KeyIs(int start, int length, string ascii)
            {
                if (length != ascii.Length)
                {
                    return false;
                }

                for (int i = 0; i < length; i++)
                {
                    if (_json[start + i] != ascii[i])
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }
}
