#nullable enable
using System;
using System.Security.Cryptography;
using System.Text;

namespace ChatGuard.Core.Text
{
    /// <summary>SHA-256 helper behind <see cref="NormalizedMessage.Hash"/>.</summary>
    public static class MessageHasher
    {
        private const string HexDigits = "0123456789abcdef";

        /// <summary>
        /// Returns the SHA-256 of the UTF-8 bytes of <paramref name="text"/> as 64 lowercase hex characters. Does not
        /// normalize: for a chat message, use <see cref="NormalizedMessage.Hash"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
        public static string Sha256Hex(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                // Same lowercase hex as b.ToString("x2") per byte, without a string per byte.
                var chars = new char[hash.Length * 2];
                for (int i = 0; i < hash.Length; i++)
                {
                    chars[2 * i] = HexDigits[hash[i] >> 4];
                    chars[(2 * i) + 1] = HexDigits[hash[i] & 0xF];
                }

                return new string(chars);
            }
        }
    }
}
