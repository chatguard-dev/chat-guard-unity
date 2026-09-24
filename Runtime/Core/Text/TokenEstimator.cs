#nullable enable
namespace ChatGuard.Core.Text
{
    /// <summary>
    /// Rough count of the tokens a text costs the moderation model (Jev), whose tokenizer is not published. The server
    /// uses it to fit the chat thread into its token budget and to reserve rate-limit budget before each model call.
    /// </summary>
    /// <remarks>
    /// The full model input (message and context JSON plus the moderation questions) measured about 1.7 characters
    /// per token; for free text, 3 is conservative. The rate limiter reserves this estimate of the JSON plus a fixed
    /// question allowance (<c>QuestionTokenEstimate</c>) and does not correct it from the exact
    /// <c>usage.input_tokens</c>.
    /// </remarks>
    public static class TokenEstimator
    {
        public const int CharsPerToken = 3;

        /// <summary>
        /// Returns the length of <paramref name="text"/> in UTF-16 code units divided by <see cref="CharsPerToken"/>,
        /// rounded up. Null or empty returns 0.
        /// </summary>
        public static int Estimate(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            return (text!.Length + CharsPerToken - 1) / CharsPerToken;
        }
    }
}
