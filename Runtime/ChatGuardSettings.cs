#nullable enable
using System;
using ChatGuard.Core.Scoring;

namespace ChatGuard.Unity
{
    /// <summary>
    /// Plain C# settings for <see cref="ChatGuardClient"/>: the code-only way to configure the SDK, with no
    /// <see cref="ChatGuardConfig"/> asset in <c>Resources</c>. Pass one to
    /// <see cref="ChatGuardSdk.Configure(ChatGuardSettings)"/> or <see cref="ChatGuardClient(ChatGuardSettings)"/>;
    /// the defaults are the same as the asset's. Leaving <see cref="ApiKey"/> or <see cref="BaseUrl"/> empty means
    /// "local filter only": every message is answered by the built-in dictionary filter and nothing is sent.
    /// A client build may only contain a publishable (<c>cg_pub_</c>) or test (<c>cg_test_</c>) key; <c>cg_live_</c>
    /// server keys belong on your game server or relay (see README, "Where the key lives").
    /// </summary>
    public sealed class ChatGuardSettings
    {
        /// <summary>
        /// API key sent as the bearer token. Empty (the default) disables the server and uses the local filter only.
        /// Only <c>cg_pub_</c> or <c>cg_test_</c> keys may ship in a client build.
        /// </summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Origin of the Chat Guard API or your relay, for example <c>https://api.chatguard.dev</c> (an absolute
        /// http/https URL; a trailing slash is ignored). Empty (the default) disables the server and uses the local
        /// filter only.
        /// </summary>
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>Largest accepted <see cref="TimeoutSeconds"/> (10 minutes).</summary>
        public const float MaxTimeoutSeconds = 600f;

        /// <summary>
        /// Whole request budget in seconds (default 2); on expiry <see cref="OfflineBehavior"/> applies. Must be a
        /// positive finite number no larger than <see cref="MaxTimeoutSeconds"/> (600). <c>UnityWebRequest</c> only
        /// takes whole seconds, so the budget is rounded up to whole seconds with a minimum of 1 s (0.5 becomes 1,
        /// 2.2 becomes 3).
        /// </summary>
        public float TimeoutSeconds { get; set; } = 2f;

        /// <summary>What to answer when the server cannot be reached (default <see cref="OfflineBehavior.LocalFilter"/>).</summary>
        public OfflineBehavior OfflineBehavior { get; set; } = OfflineBehavior.LocalFilter;

        /// <summary>
        /// Re-run the local filter with <see cref="Thresholds"/> when the server answers with <c>degraded=true</c>
        /// (default true). Only applies with <see cref="OfflineBehavior.LocalFilter"/>.
        /// </summary>
        public bool LocalFilterWhenDegraded { get; set; } = true;

        /// <summary>Language sent with every message that does not set its own (default "en"); also picks the local word lists.</summary>
        public string DefaultLanguage { get; set; } = "en";

        /// <summary>Channel type sent with every message that does not set its own: global | team | dm | guild (default "global").</summary>
        public string ChannelType { get; set; } = "global";

        /// <summary>Age rating sent with every message that does not set its own, for example "16+" (the default); null or empty sends none.</summary>
        public string? AgeRating { get; set; } = "16+";

        /// <summary>
        /// Thresholds for local decisions (offline and degraded results). Null (the default) uses the built-in
        /// defaults, which are the server's defaults; server-side thresholds are edited in the dashboard and are not
        /// affected by this value. The client keeps its own copy.
        /// </summary>
        public Thresholds? Thresholds { get; set; }

        /// <summary>
        /// Throws <see cref="ArgumentException"/> when <see cref="TimeoutSeconds"/> is not a positive finite number
        /// of seconds (NaN, infinity, zero and negatives are rejected) or exceeds <see cref="MaxTimeoutSeconds"/>, or
        /// when <see cref="BaseUrl"/> is neither empty nor an absolute http/https URL. Called by the
        /// <see cref="ChatGuardClient"/> constructor; an empty key or URL is valid (local filter only).
        /// </summary>
        public void Validate()
        {
            if (float.IsNaN(TimeoutSeconds) || float.IsInfinity(TimeoutSeconds) || !(TimeoutSeconds > 0f) || TimeoutSeconds > MaxTimeoutSeconds)
            {
                throw new ArgumentException("ChatGuardSettings.TimeoutSeconds must be a positive finite number of seconds, at most " + MaxTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + " (got " + TimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + ").", nameof(TimeoutSeconds));
            }

            if (!string.IsNullOrEmpty(BaseUrl) && !IsAbsoluteHttpUrl(BaseUrl))
            {
                throw new ArgumentException("ChatGuardSettings.BaseUrl must be empty (local filter only) or an absolute http:// or https:// URL, for example https://api.chatguard.dev.", nameof(BaseUrl));
            }
        }

        /// <summary>A deep copy (the thresholds are cloned too); used by the client so later edits do not leak in.</summary>
        public ChatGuardSettings Clone()
        {
            return new ChatGuardSettings
            {
                ApiKey = ApiKey,
                BaseUrl = BaseUrl,
                TimeoutSeconds = TimeoutSeconds,
                OfflineBehavior = OfflineBehavior,
                LocalFilterWhenDegraded = LocalFilterWhenDegraded,
                DefaultLanguage = DefaultLanguage,
                ChannelType = ChannelType,
                AgeRating = AgeRating,
                Thresholds = Thresholds?.Clone(),
            };
        }

        private static bool IsAbsoluteHttpUrl(string value)
        {
            Uri? uri;
            return Uri.TryCreate(value, UriKind.Absolute, out uri)
                && uri != null
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }
    }
}
