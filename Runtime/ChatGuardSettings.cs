#nullable enable
using System;
using ChatGuard.Core.Scoring;

namespace ChatGuard
{
    /// <summary>
    /// Settings for <see cref="ChatGuardClient"/>, for setting up the SDK from code without a
    /// <see cref="ChatGuardConfig"/> asset. Pass one to <see cref="ChatGuardSdk.Configure(ChatGuardSettings)"/> or
    /// <see cref="ChatGuardClient(ChatGuardSettings)"/>. Only <see cref="ApiKey"/> must be filled in; the defaults
    /// match a new config asset's.
    /// </summary>
    public sealed class ChatGuardSettings
    {
        /// <summary>The Chat Guard API's base URL, and the default for <see cref="BaseUrl"/>.</summary>
        public const string DefaultBaseUrl = "https://api.chatguard.dev";

        /// <summary>
        /// Your API key from the dashboard's API keys page, sent as the bearer token. Empty (the default) sends
        /// nothing, and <see cref="OfflineBehavior"/> answers every message. Game builds ship a publishable
        /// <c>cg_pub_</c> key, which requires <see cref="ModerationRequest.authorId"/>. Keep <c>cg_live_</c> server
        /// keys on your server or relay, such as a Dedicated Server build. Use <c>cg_test_</c> keys only in the Editor
        /// and development builds: their calls are free, but an organization gets 1,000 per UTC day, shared with the
        /// dashboard's test panel.
        /// </summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Base URL that requests go to; the client appends <c>/v1/moderate</c>. Keep <see cref="DefaultBaseUrl"/> (the
        /// default) unless your own proxy serves that path the same way. Must be an absolute http or https URL. Null or
        /// blank means the default, and a trailing slash is ignored.
        /// </summary>
        public string BaseUrl { get; set; } = DefaultBaseUrl;

        /// <summary>Largest accepted <see cref="TimeoutSeconds"/>: 10 minutes.</summary>
        public const float MaxTimeoutSeconds = 600f;

        /// <summary>
        /// Time budget for the whole request, in seconds (default 2). When it runs out, <see cref="OfflineBehavior"/>
        /// answers. Must be above 0 and at most <see cref="MaxTimeoutSeconds"/>. Rounded up to whole seconds: 0.5
        /// becomes 1.
        /// </summary>
        public float TimeoutSeconds { get; set; } = 2f;

        /// <summary>
        /// What the client answers when the server gives no usable answer: no API key, a network error or timeout, an
        /// HTTP error, or an unreadable response. Default <see cref="OfflineBehavior.LocalFilter"/>, which checks each
        /// message against built-in word lists on the device.
        /// </summary>
        public OfflineBehavior OfflineBehavior { get; set; } = OfflineBehavior.LocalFilter;

        /// <summary>
        /// Whether to re-check degraded server answers (default true), where the server's local filter decided instead
        /// of the model. The client then runs its own local filter too, keeps the higher probability in each category,
        /// and recomputes severity and action with <see cref="Thresholds"/>. Applies only with
        /// <see cref="OfflineBehavior.LocalFilter"/>.
        /// </summary>
        public bool LocalFilterWhenDegraded { get; set; } = true;

        /// <summary>
        /// Language code for messages that do not set <see cref="ModerationRequest.language"/>, in the form that field
        /// requires, such as <c>"en"</c> (the default) or <c>"pt-BR"</c>.
        /// </summary>
        public string DefaultLanguage { get; set; } = "en";

        /// <summary>
        /// Channel type for messages that do not set <see cref="ModerationRequest.channelType"/>: <c>"global"</c> (the
        /// default), <c>"team"</c>, <c>"dm"</c> or <c>"guild"</c>.
        /// </summary>
        public string ChannelType { get; set; } = "global";

        /// <summary>
        /// Age rating for messages that do not set <see cref="ModerationRequest.ageRating"/>, in the form that field
        /// requires, such as <c>"16+"</c> (the default). Null or empty sends none.
        /// </summary>
        public string? AgeRating { get; set; } = "16+";

        /// <summary>
        /// Thresholds for the client's own local-filter decisions: its <see cref="OfflineBehavior"/> answers and the
        /// degraded answers it re-checks (<see cref="LocalFilterWhenDegraded"/>). Null (the default) uses the built-in
        /// defaults, which new projects also start with. Your project's thresholds on the dashboard are not affected.
        /// </summary>
        public Thresholds? Thresholds { get; set; }

        /// <summary>
        /// Throws when a setting is invalid; the <see cref="ChatGuardClient"/> constructor calls it. An empty key and a
        /// blank URL are valid.
        /// </summary>
        /// <exception cref="ArgumentException"><see cref="TimeoutSeconds"/> is NaN, zero, negative or above
        /// <see cref="MaxTimeoutSeconds"/>, or <see cref="BaseUrl"/> is neither blank nor an absolute http or https
        /// URL.</exception>
        public void Validate()
        {
            if (float.IsNaN(TimeoutSeconds) || float.IsInfinity(TimeoutSeconds) || !(TimeoutSeconds > 0f) || TimeoutSeconds > MaxTimeoutSeconds)
            {
                throw new ArgumentException("ChatGuardSettings.TimeoutSeconds must be a positive finite number of seconds, at most " + MaxTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + " (got " + TimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + ").", nameof(TimeoutSeconds));
            }

            if (!string.IsNullOrWhiteSpace(BaseUrl) && !IsAbsoluteHttpUrl(BaseUrl.Trim()))
            {
                throw new ArgumentException("ChatGuardSettings.BaseUrl must be an absolute http:// or https:// URL, or empty for the default (" + DefaultBaseUrl + ").", nameof(BaseUrl));
            }
        }

        /// <summary>Returns a deep copy, <see cref="Thresholds"/> included.</summary>
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
