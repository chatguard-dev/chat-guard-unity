#nullable enable
using System;
using System.Collections.Generic;
using ChatGuard.Core;

namespace ChatGuard.Unity
{
    /// <summary>What the game sends for one chat message. Only `message` is required.</summary>
    [Serializable]
    public sealed class ModerationRequest
    {
        public string message = string.Empty;

        /// <summary>Opaque player id (never a real name or email).</summary>
        public string? authorId;

        /// <summary>Days since the player's account was created; negative sends none, and more than 100,000 is sent as 100,000.</summary>
        public int accountAgeDays = -1;

        /// <summary>Warnings the player already had; negative sends none, and more than 100,000 is sent as 100,000.</summary>
        public int priorWarnings = -1;

        /// <summary>
        /// Recent messages for context, oldest first. The last 5 entries that have text are sent; entries that are null
        /// or have no text are skipped, because the API refuses a message whose thread holds one.
        /// </summary>
        public List<ThreadEntry> thread = new List<ThreadEntry>();

        /// <summary>global | team | dm | guild</summary>
        public string? channelType;

        public string? language;

        /// <summary>e.g. "16+"</summary>
        public string? ageRating;

        /// <summary>Client-generated id; the same id within 10 minutes replays the same answer.</summary>
        public string? requestId;

        public ModerationRequest()
        {
        }

        public ModerationRequest(string message, string? authorId = null)
        {
            this.message = message;
            this.authorId = authorId;
        }
    }

    [Serializable]
    public sealed class ThreadEntry
    {
        /// <summary>
        /// The player id of whoever wrote the line, the same id you pass as the author id when that player writes.
        /// Erasing a player also removes their lines from other players' stored context by matching this label.
        /// </summary>
        public string author = string.Empty;

        /// <summary>The line itself. Entries with empty text are not sent; text over 2,000 characters is cut.</summary>
        public string text = string.Empty;

        public ThreadEntry()
        {
        }

        public ThreadEntry(string author, string text)
        {
            this.author = author;
            this.text = text;
        }
    }

    public enum ResultSource
    {
        /// <summary>Verdicts came from the Chat Guard server.</summary>
        Server,

        /// <summary>The local dictionary filter produced the result (offline, timeout, server error, or degraded).</summary>
        Local,
    }

    /// <summary>Typed result of one moderation call. Mirrors the /v1/moderate response.</summary>
    public sealed class ModerationResult
    {
        public ModerationResult(
            ModerationAction action,
            double severity,
            VerdictSet verdicts,
            TargetVerdict? target,
            bool degraded,
            DegradedReason degradedReason,
            bool cached,
            string model,
            int latencyMs,
            string? id,
            long quotaUsed,
            long quotaLimit,
            ResultSource source,
            string? error)
        {
            Action = action;
            Severity = severity;
            Verdicts = verdicts;
            Target = target;
            Degraded = degraded;
            DegradedReason = degradedReason;
            Cached = cached;
            Model = model;
            LatencyMs = latencyMs;
            Id = id;
            QuotaUsed = quotaUsed;
            QuotaLimit = quotaLimit;
            Source = source;
            Error = error;
        }

        public ModerationAction Action { get; }

        /// <summary>0 (fine) to 3 (severe).</summary>
        public double Severity { get; }

        public VerdictSet Verdicts { get; }

        public TargetVerdict? Target { get; }

        public bool Degraded { get; }

        public DegradedReason DegradedReason { get; }

        public bool Cached { get; }

        public string Model { get; }

        public int LatencyMs { get; }

        /// <summary>Server verdict id (use it with POST /v1/feedback); null for local results.</summary>
        public string? Id { get; }

        public long QuotaUsed { get; }

        public long QuotaLimit { get; }

        public ResultSource Source { get; }

        /// <summary>Why the server could not be used, when Source is Local.</summary>
        public string? Error { get; }

        public bool ShouldDeliver => Action == ModerationAction.Allow || Action == ModerationAction.Flag;

        public override string ToString()
        {
            return ModerationActions.ToWireName(Action) + " (severity " + Severity.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + ", " + Model + (Degraded ? ", degraded: " + DegradedReasons.ToWireName(DegradedReason) : string.Empty) + ")";
        }
    }
}
