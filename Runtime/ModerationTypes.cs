#nullable enable
using System;
using System.Collections.Generic;
using ChatGuard.Core;

namespace ChatGuard
{
    /// <summary>
    /// One chat message to moderate, with optional context. Set <see cref="message"/>, and <see cref="authorId"/> when
    /// using a publishable key.
    /// </summary>
    [Serializable]
    public sealed class ModerationRequest
    {
        /// <summary>
        /// The chat text. The server refuses a blank message or one over 2,000 characters (UTF-16 code units), and
        /// <see cref="ChatGuardSettings.OfflineBehavior"/> answers instead.
        /// </summary>
        public string message = string.Empty;

        /// <summary>
        /// Your stable, opaque id for the player who wrote the message, never a real name or email. Null or empty sends
        /// none. The server refuses an id over 128 characters, and a missing one with a publishable (<c>cg_pub_</c>)
        /// key.
        /// </summary>
        public string? authorId;

        /// <summary>
        /// Days since the player's account was created, capped at 100,000 when sent. Negative (the default) sends none.
        /// </summary>
        public int accountAgeDays = -1;

        /// <summary>
        /// Warnings the player has already had, capped at 100,000 when sent. Negative (the default) sends none.
        /// </summary>
        public int priorWarnings = -1;

        /// <summary>
        /// Earlier messages for context, oldest first. Only the last 5 entries that have text are sent.
        /// </summary>
        public List<ThreadEntry> thread = new List<ThreadEntry>();

        /// <summary>
        /// Where the message was sent: <c>global</c>, <c>team</c>, <c>dm</c> or <c>guild</c>. The server refuses any
        /// other value, and <see cref="ChatGuardSettings.OfflineBehavior"/> answers instead. Null or empty uses
        /// <see cref="ChatGuardSettings.ChannelType"/>.
        /// </summary>
        public string? channelType;

        /// <summary>
        /// Language code such as <c>en</c> or <c>pt-BR</c>: two letters, optionally a dash and 2 to 4 more letters. The
        /// server refuses any other form, and <see cref="ChatGuardSettings.OfflineBehavior"/> answers instead. Null or
        /// empty uses <see cref="ChatGuardSettings.DefaultLanguage"/>. The code also picks the local filter's word
        /// lists. If your plan does not include all languages, the server uses your project's language instead.
        /// </summary>
        public string? language;

        /// <summary>
        /// Age rating the model judges sexual content against: one or two digits and a plus, such as <c>16+</c>. The
        /// server refuses any other form, and <see cref="ChatGuardSettings.OfflineBehavior"/> answers instead. Null or
        /// empty uses <see cref="ChatGuardSettings.AgeRating"/>.
        /// </summary>
        public string? ageRating;

        /// <summary>
        /// Your id for this request, 1 to 64 characters, for safe retries: the same id within 10 minutes replays the
        /// first answer. Null or empty sends none.
        /// </summary>
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
        /// Player id of whoever wrote the line, the same id you send as that player's author id. Erasing or exporting a
        /// player's data finds their lines in others' stored context by this label. Null, or over 128 characters, is
        /// sent as an empty label.
        /// </summary>
        public string author = string.Empty;

        /// <summary>The line's text, cut to 2,000 characters (UTF-16 code units) when longer.</summary>
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

    /// <summary>Where a <see cref="ModerationResult"/> came from.</summary>
    public enum ResultSource
    {
        /// <summary>
        /// The Chat Guard server answered. The client may have re-checked a degraded answer on the device (see
        /// <see cref="ChatGuardSettings.LocalFilterWhenDegraded"/>).
        /// </summary>
        Server,

        /// <summary>
        /// The client answered on its own, as <see cref="ChatGuardSettings.OfflineBehavior"/> says, because it had no
        /// usable server answer. <see cref="ModerationResult.Error"/> says why.
        /// </summary>
        Local,
    }

    /// <summary>
    /// The result of one moderation call, from the server or from the client's fallback (see <see cref="Source"/>).
    /// </summary>
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

        /// <summary>What to do with the message. <see cref="ShouldDeliver"/> tells whether everyone sees it.</summary>
        public ModerationAction Action { get; }

        /// <summary>How serious the message is, from 0 (fine) to 3 (severe); can fall between whole levels.</summary>
        public double Severity { get; }

        /// <summary>One probability from 0 to 1 per <see cref="VerdictCategory"/>, such as insult or threat.</summary>
        public VerdictSet Verdicts { get; }

        /// <summary>
        /// Who the message is aimed at, as the model judged it. Null when the model did not judge the message: local
        /// and degraded results, and project block-rule hits.
        /// </summary>
        public TargetVerdict? Target { get; }

        /// <summary>
        /// True when the moderation model did not judge the message and a fallback decided.
        /// <see cref="DegradedReason"/> says why.
        /// </summary>
        public bool Degraded { get; }

        /// <summary>Why the result is degraded, or <c>None</c> when it is not.</summary>
        public DegradedReason DegradedReason { get; }

        /// <summary>True when the server reused model verdicts for the same message from its 10-minute cache.</summary>
        public bool Cached { get; }

        /// <summary>
        /// What decided: the model's name and version, <c>local-filter/&lt;word-list version&gt;</c> for a local
        /// filter, or <c>offline/allow-all</c> or <c>offline/block-all</c> for the client's other fallbacks.
        /// </summary>
        public string Model { get; }

        /// <summary>
        /// Milliseconds from the start of the call until the server answered, measured on the client; 0 for local
        /// results.
        /// </summary>
        public int LatencyMs { get; }

        /// <summary>
        /// The server's verdict id, or null for local results. To report a wrong verdict, send it to
        /// <c>POST /v1/feedback</c> from your server; publishable keys cannot call that endpoint.
        /// </summary>
        public string? Id { get; }

        /// <summary>
        /// Model verdicts your organization received in the rolling 30-day window, as of this answer, or today's use of
        /// the daily test allowance with a <c>cg_test_</c> key. 0 for local results.
        /// </summary>
        public long QuotaUsed { get; }

        /// <summary>
        /// Your plan's allowance for that window, or the daily test allowance for a test key. 0 for local results.
        /// </summary>
        public long QuotaLimit { get; }

        public ResultSource Source { get; }

        /// <summary>
        /// Why the client fell back, such as <c>no API key configured</c> or <c>HTTP 401: ...</c>. Null for server
        /// results.
        /// </summary>
        public string? Error { get; }

        /// <summary>True for <c>Allow</c> and <c>Flag</c>, the actions that show the message to everyone.</summary>
        public bool ShouldDeliver => Action == ModerationAction.Allow || Action == ModerationAction.Flag;

        public override string ToString()
        {
            return ModerationActions.ToWireName(Action) + " (severity " + Severity.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + ", " + Model + (Degraded ? ", degraded: " + DegradedReasons.ToWireName(DegradedReason) : string.Empty) + ")";
        }
    }
}
