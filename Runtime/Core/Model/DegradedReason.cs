#nullable enable
using System;

namespace ChatGuard.Core
{
    /// <summary>Why a response was produced by the local filter instead of Jev.</summary>
    public enum DegradedReason
    {
        /// <summary>Not degraded.</summary>
        None = 0,

        /// <summary>The organization is over its message quota and the plan has no overage.</summary>
        Quota = 1,

        /// <summary>Jev returned an error (5xx, 529) or an unparseable response.</summary>
        Upstream = 2,

        /// <summary>Jev or the shared limiter returned 429.</summary>
        UpstreamRateLimit = 3,

        /// <summary>The Jev call exceeded its latency budget.</summary>
        Timeout = 4,

        /// <summary>The client had no connectivity (Unity offline mode).</summary>
        Offline = 5,
    }

    public static class DegradedReasons
    {
        /// <summary>Wire name, or null for <see cref="DegradedReason.None"/>.</summary>
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
