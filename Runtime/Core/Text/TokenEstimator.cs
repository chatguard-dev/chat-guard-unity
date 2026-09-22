#nullable enable
namespace ChatGuard.Core.Text
{
    /// <summary>
    /// Rough token estimate for budgeting Jev state. The Jev docs publish no tokenizer; a live
    /// measurement on 2026-09-22 gave ≈1.7 characters per token for JSON state plus questions, so
    /// chars/3 is used as a conservative estimate for free text (the API refills its rate-limit
    /// bucket from the exact usage.input_tokens reported by each response).
    /// </summary>
    public static class TokenEstimator
    {
        public const int CharsPerToken = 3;

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
