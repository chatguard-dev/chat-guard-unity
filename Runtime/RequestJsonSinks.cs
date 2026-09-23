#nullable enable
using System;
using System.Globalization;
using System.Text;

namespace ChatGuard.Unity
{
    /// <summary>
    /// Output of <see cref="ChatGuardClient"/>'s request writer, which holds the one definition of the /v1/moderate body
    /// (keys, order, omission rules). <see cref="StringRequestJsonSink"/> produces the string of
    /// <see cref="ChatGuardClient.BuildRequestJson"/>; <see cref="Utf8RequestJsonSink"/> produces exactly the bytes
    /// <c>Encoding.UTF8.GetBytes</c> gives for that string, without creating the string.
    /// </summary>
    internal interface IRequestJsonSink
    {
        /// <summary>Appends <paramref name="ascii"/> as is; only called with ASCII keys, brackets and separators.</summary>
        void WriteLiteral(string ascii);

        /// <summary>Appends <paramref name="value"/> as a quoted JSON string escaped like <see cref="MiniJson.WriteString"/>.</summary>
        void WriteString(string value);

        /// <summary>Appends <paramref name="value"/> (never negative) as invariant decimal digits.</summary>
        void WriteNonNegativeInt(int value);
    }

    /// <summary>Appends the request JSON to a <see cref="StringBuilder"/>; the output of <see cref="ChatGuardClient.BuildRequestJson"/>.</summary>
    internal readonly struct StringRequestJsonSink : IRequestJsonSink
    {
        public StringRequestJsonSink(StringBuilder builder)
        {
            Builder = builder;
        }

        public StringBuilder Builder { get; }

        public void WriteLiteral(string ascii)
        {
            Builder.Append(ascii);
        }

        public void WriteString(string value)
        {
            MiniJson.WriteString(Builder, value);
        }

        public void WriteNonNegativeInt(int value)
        {
            Builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// Writes the request JSON as UTF-8 into a byte buffer reused per thread, so a request body costs no string and no
    /// <c>byte[]</c>. The bytes equal <c>Encoding.UTF8.GetBytes</c> of <see cref="StringRequestJsonSink"/>'s output for the
    /// same calls: <see cref="MiniJson.WriteString"/>'s escapes, ASCII as single bytes, other characters UTF-8 encoded,
    /// a surrogate pair as one 4-byte sequence, and every lone surrogate as U+FFFD (EF BF BD), which is what
    /// <c>Encoding.UTF8</c>'s replacement fallback writes; the character after a lone high surrogate is encoded normally.
    /// Get one from <see cref="Rent"/>, read <see cref="Buffer"/> up to <see cref="Length"/>, then call
    /// <see cref="Release"/>. Nothing between those calls may start another request on the same thread (in
    /// <see cref="ChatGuardClient.Moderate(ModerationRequest, Action{ModerationResult}, System.Threading.CancellationToken)"/>
    /// no user code runs in between).
    /// </summary>
    internal struct Utf8RequestJsonSink : IRequestJsonSink
    {
        private const int InitialCapacity = 1024;

        /// <summary>A buffer that grew past this size is dropped after use, so one huge message is not kept alive.</summary>
        private const int RetainLimit = 64 * 1024;

        /// <summary>Largest body this sink writes; the largest byte array the runtimes allow is slightly below int.MaxValue.</summary>
        private const int MaxLength = 0x7FFFFFC7;

        // Per thread, so a Moderate call made off the main thread cannot write into a buffer another thread is using.
        [ThreadStatic]
        private static byte[]? t_buffer;

        private byte[] _buffer;
        private int _length;

        private Utf8RequestJsonSink(byte[] buffer)
        {
            _buffer = buffer;
            _length = 0;
        }

        /// <summary>The bytes written so far are <c>Buffer[0..Length)</c>; the array may be longer.</summary>
        public byte[] Buffer => _buffer;

        public int Length => _length;

        /// <summary>Starts an empty body in this thread's reusable buffer (created on first use).</summary>
        public static Utf8RequestJsonSink Rent()
        {
            return new Utf8RequestJsonSink(t_buffer ?? new byte[InitialCapacity]);
        }

        /// <summary>Keeps the (possibly grown) buffer for this thread's next request, unless it grew past 64 KB.</summary>
        public void Release()
        {
            if (_buffer.Length <= RetainLimit)
            {
                t_buffer = _buffer;
            }
        }

        public void WriteLiteral(string ascii)
        {
            Ensure(ascii.Length);
            byte[] b = _buffer;
            int n = _length;
            for (int i = 0; i < ascii.Length; i++)
            {
                b[n++] = (byte)ascii[i];
            }

            _length = n;
        }

        public void WriteNonNegativeInt(int value)
        {
            int digits = 1;
            for (int rest = value; rest >= 10; rest /= 10)
            {
                digits++;
            }

            Ensure(digits);
            int end = _length + digits;
            for (int p = end - 1; p >= _length; p--)
            {
                _buffer[p] = (byte)('0' + (value % 10));
                value /= 10;
            }

            _length = end;
        }

        public void WriteString(string value)
        {
            Ensure(EncodedLength(value));
            byte[] b = _buffer;
            int n = _length;
            b[n++] = (byte)'"';
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c < 0x80)
                {
                    if (c >= ' ' && c != '"' && c != '\\')
                    {
                        b[n++] = (byte)c;
                        continue;
                    }

                    b[n++] = (byte)'\\';
                    switch (c)
                    {
                        case '"': b[n++] = (byte)'"'; break;
                        case '\\': b[n++] = (byte)'\\'; break;
                        case '\n': b[n++] = (byte)'n'; break;
                        case '\r': b[n++] = (byte)'r'; break;
                        case '\t': b[n++] = (byte)'t'; break;
                        case '\b': b[n++] = (byte)'b'; break;
                        case '\f': b[n++] = (byte)'f'; break;
                        default:
                            // "\u" plus ((int)c).ToString("x4"): c is below 0x20, so "00", then 0 or 1, then a lowercase hex digit.
                            int low = c & 0xF;
                            b[n++] = (byte)'u';
                            b[n++] = (byte)'0';
                            b[n++] = (byte)'0';
                            b[n++] = (byte)('0' + (c >> 4));
                            b[n++] = (byte)(low < 10 ? '0' + low : 'a' + (low - 10));
                            break;
                    }
                }
                else if (c < 0x800)
                {
                    b[n++] = (byte)(0xC0 | (c >> 6));
                    b[n++] = (byte)(0x80 | (c & 0x3F));
                }
                else if (!char.IsSurrogate(c))
                {
                    b[n++] = (byte)(0xE0 | (c >> 12));
                    b[n++] = (byte)(0x80 | ((c >> 6) & 0x3F));
                    b[n++] = (byte)(0x80 | (c & 0x3F));
                }
                else if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    int codePoint = char.ConvertToUtf32(c, value[i + 1]);
                    i++;
                    b[n++] = (byte)(0xF0 | (codePoint >> 18));
                    b[n++] = (byte)(0x80 | ((codePoint >> 12) & 0x3F));
                    b[n++] = (byte)(0x80 | ((codePoint >> 6) & 0x3F));
                    b[n++] = (byte)(0x80 | (codePoint & 0x3F));
                }
                else
                {
                    // A lone surrogate: U+FFFD, as Encoding.UTF8 writes it.
                    b[n++] = 0xEF;
                    b[n++] = 0xBF;
                    b[n++] = 0xBD;
                }
            }

            b[n++] = (byte)'"';
            _length = n;
        }

        /// <summary>The exact number of bytes <see cref="WriteString"/> writes for <paramref name="value"/>, quotes included.</summary>
        private static long EncodedLength(string value)
        {
            long n = 2;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c < 0x80)
                {
                    if (c >= ' ' && c != '"' && c != '\\')
                    {
                        n += 1;
                    }
                    else if (c == '"' || c == '\\' || c == '\n' || c == '\r' || c == '\t' || c == '\b' || c == '\f')
                    {
                        n += 2;
                    }
                    else
                    {
                        n += 6;
                    }
                }
                else if (c < 0x800)
                {
                    n += 2;
                }
                else if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    n += 4;
                    i++;
                }
                else
                {
                    n += 3; // any other BMP character, or a lone surrogate written as U+FFFD
                }
            }

            return n;
        }

        /// <summary>Grows the buffer (at least doubling) so that <paramref name="count"/> more bytes fit.</summary>
        private void Ensure(long count)
        {
            long needed = _length + count;
            if (needed <= _buffer.Length)
            {
                return;
            }

            if (needed > MaxLength)
            {
                throw new OutOfMemoryException("The request body is too large.");
            }

            var grown = new byte[(int)Math.Min(Math.Max(needed, 2L * _buffer.Length), MaxLength)];
            System.Buffer.BlockCopy(_buffer, 0, grown, 0, _length);
            _buffer = grown;
        }
    }
}
