#nullable enable
namespace ChatGuard.Core.Text
{
    /// <summary>A message after normalization, with its tokens and hash computed once.</summary>
    public sealed class NormalizedMessage
    {
        private NormalizedMessage(string original, string normalized, string[] tokens, string hash)
        {
            Original = original;
            Normalized = normalized;
            Tokens = tokens;
            Hash = hash;
        }

        public string Original { get; }

        public string Normalized { get; }

        public string[] Tokens { get; }

        /// <summary>Lowercase hex SHA-256 of <see cref="Normalized"/>.</summary>
        public string Hash { get; }

        public static NormalizedMessage Create(string? original)
        {
            string text = original ?? string.Empty;
            string normalized = TextNormalizer.Normalize(text);
            string[] tokens = TextNormalizer.Tokenize(normalized);
            string hash = MessageHasher.Sha256Hex(normalized);
            return new NormalizedMessage(text, normalized, tokens, hash);
        }
    }
}
