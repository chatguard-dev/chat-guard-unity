#nullable enable
using System.Threading;

namespace ChatGuard.Core.Text
{
    /// <summary>
    /// A chat message with its normalized text and word-list tokens, computed once by <see cref="Create"/>.
    /// </summary>
    public sealed class NormalizedMessage
    {
        // Null until Hash is first read: the Unity client never reads it, so it never pays for SHA-256.
        private string? _hash;

        private NormalizedMessage(string original, string normalized, string[] tokens)
        {
            Original = original;
            Normalized = normalized;
            Tokens = tokens;
        }

        /// <summary>The message as passed to <see cref="Create"/>; empty when it was null.</summary>
        public string Original { get; }

        /// <summary>The <see cref="TextNormalizer.Normalize"/> output, which regex project rules match.</summary>
        public string Normalized { get; }

        /// <summary>
        /// The <see cref="TextNormalizer.Tokenize"/> output: letters and digits only. Word lists and non-regex project
        /// rules match these.
        /// </summary>
        public string[] Tokens { get; }

        /// <summary>
        /// SHA-256 of <see cref="Normalized"/> as 64 lowercase hex characters, computed on first read. Safe to read
        /// from any thread. The server's verdict cache and log use it in place of the text, which is stored only when
        /// the project turns on evidence logging.
        /// </summary>
        public string Hash
        {
            get
            {
                string? hash = Volatile.Read(ref _hash);
                if (hash == null)
                {
                    string computed = MessageHasher.Sha256Hex(Normalized);
                    hash = Interlocked.CompareExchange(ref _hash, computed, null) ?? computed;
                }

                return hash;
            }
        }

        /// <summary>Normalizes and tokenizes <paramref name="original"/>; null counts as an empty message.</summary>
        public static NormalizedMessage Create(string? original)
        {
            string text = original ?? string.Empty;
            string normalized = TextNormalizer.Normalize(text);
            string[] tokens = TextNormalizer.Tokenize(normalized);
            return new NormalizedMessage(text, normalized, tokens);
        }
    }
}
