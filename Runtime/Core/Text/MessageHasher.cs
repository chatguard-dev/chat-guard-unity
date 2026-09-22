#nullable enable
using System;
using System.Security.Cryptography;
using System.Text;

namespace ChatGuard.Core.Text
{
    /// <summary>SHA-256 of the normalized message; the only message-derived value persisted by default.</summary>
    public static class MessageHasher
    {
        public static string Sha256Hex(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                {
                    sb.Append(b.ToString("x2"));
                }

                return sb.ToString();
            }
        }
    }
}
