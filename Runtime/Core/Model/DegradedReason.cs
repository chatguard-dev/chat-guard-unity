#nullable enable
using System;

namespace ChatGuard.Core
{
    /// <summary>
    /// Why a result is degraded: the moderation model (Jev) did not judge the message, so a fallback decided. The
    /// fallback is the local filter, or whatever <c>OfflineBehavior</c> selects when the Unity client gets no usable
    /// server answer.
    /// </summary>
    public enum DegradedReason
    {
        /// <summary>Not degraded: the model answered, or a project block rule matched before it was asked.</summary>
        None = 0,

        /// <summary>
        /// Set by the server: the organization used up its plan's verdict allowance, and the plan has no overage,
        /// such as Free.
        /// </summary>
        Quota = 1,

        /// <summary>
        /// On the server: the model call failed or gave an unreadable answer. In the Unity client: the Chat Guard API
        /// answered with a status other than 200 or 429, or an unreadable body.
        /// </summary>
        Upstream = 2,

        /// <summary>
        /// A rate limit was hit. On the server: the model provider answered 429, or a server limit on model calls ran
        /// out, such as the per-minute allowance all Free organizations share, or the separate one test keys share. In
        /// the Unity client: the Chat Guard API answered 429.
        /// </summary>
        UpstreamRateLimit = 3,

        /// <summary>
        /// Set by the server: the model call ran out of time. A Unity client timeout reports <see cref="Offline"/>.
        /// </summary>
        Timeout = 4,

        /// <summary>
        /// Set by the Unity client: no API key is set, or the request could not be sent, hit a network error or timed
        /// out.
        /// </summary>
        Offline = 5,
    }

    public static class DegradedReasons
    {
        /// <summary>
        /// The name in API JSON (<c>degraded_reason</c>), such as <c>upstream_rate_limit</c>; null for
        /// <see cref="DegradedReason.None"/>.
        /// </summary>
        public static string? ToWireName(DegradedReason reason)
        {
            switch (reason)
            {
                case DegradedReason.None: return null;
                case DegradedReason.Quota: return "quota";
                case DegradedReason.Upstream: return "upstream";
                case DegradedReason.UpstreamRateLimit: return "upstream_rate_limit";
                case DegradedReason.Timeout: return "timeout";
                case DegradedReason.Offline: return "offline";
                default: throw new ArgumentOutOfRangeException(nameof(reason), reason, null);
            }
        }

        /// <summary>
        /// Parses a name from API JSON, ignoring case and surrounding spaces. Null or empty gives
        /// <see cref="DegradedReason.None"/> and true; an unknown name gives None and false.
        /// </summary>
        public static bool TryParse(string? name, out DegradedReason reason)
        {
            switch ((name ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "": reason = DegradedReason.None; return true;
                case "quota": reason = DegradedReason.Quota; return true;
                case "upstream": reason = DegradedReason.Upstream; return true;
                case "upstream_rate_limit": reason = DegradedReason.UpstreamRateLimit; return true;
                case "timeout": reason = DegradedReason.Timeout; return true;
                case "offline": reason = DegradedReason.Offline; return true;
                default: reason = DegradedReason.None; return false;
            }
        }
    }
}
