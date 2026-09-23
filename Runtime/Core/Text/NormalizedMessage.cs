#nullable enable
using System.Threading;

namespace ChatGuard.Core.Text
{
    /// <summary>
    /// A message after normalization, with its tokens computed once. The hash is computed on first read of
    /// <see cref="Hash"/>: the Unity client never reads it, so it does not pay for SHA-256 on every message.
    /// </summary>
    public sealed class NormalizedMessage
    {
        // Filled on first read of Hash (Volatile.Read/Interlocked.CompareExchange, see Hash).
        private string? _hash;

        private NormalizedMessage(string original, string normalized, string[] tokens)
        {
            Original = original;
            Normalized = normalized;
            Tokens = tokens;
        }

        public string Original { get; }

        public string Normalized { get; }

        public string[] Tokens { get; }

        /// <summary>
        /// Lowercase hex SHA-256 of <see cref="Normalized"/>, computed on first read. Concurrent first reads may
        /// each compute it; every caller gets the first published value, fully built on ARM64 (IL2CPP or Mono) too.
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

        public static NormalizedMessage Create(string? original)
        {
            string text = original ?? string.Empty;
            string normalized = TextNormalizer.Normalize(text);
            string[] tokens = TextNormalizer.Tokenize(normalized);
            return new NormalizedMessage(text, normalized, tokens);
        }
    }
}
