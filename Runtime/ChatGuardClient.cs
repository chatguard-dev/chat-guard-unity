#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using ChatGuard.Core;
using ChatGuard.Core.Filtering;
using ChatGuard.Core.Scoring;
using ChatGuard.Core.Text;
using UnityEngine.Networking;

namespace ChatGuard.Unity
{
    /// <summary>
    /// Plain C# client for POST /v1/moderate built on UnityWebRequest. Not a MonoBehaviour: create one per
    /// server/session and reuse it. Configure it from code with a <see cref="ChatGuardSettings"/> (no asset needed)
    /// or from a <see cref="ChatGuardConfig"/> asset. Falls back to the shared local filter according to
    /// <see cref="ChatGuardSettings.OfflineBehavior"/>. No Tasks or threads: <c>Moderate</c> returns a
    /// <see cref="ModerationOperation"/> that completes on the main thread, on every platform including WebGL, and
    /// optionally takes a <see cref="CancellationToken"/> that ends it early.
    /// </summary>
    public sealed class ChatGuardClient
    {
        private static LocalFilter? s_filter;
        private static bool s_warnedLiveKey;

        private readonly ChatGuardSettings _settings;
        private readonly string _baseUrl;
        private readonly string _moderateUrl;
        private readonly Uri? _moderateUri;
        private readonly string _authorizationHeader;
        private readonly int _timeoutSeconds;
        private readonly Thresholds _thresholds;
        private readonly SeverityWeights _weights = SeverityWeights.Default();

        /// <summary>
        /// Primary constructor: builds a client from plain settings, no Resources asset involved. The settings are
        /// validated (<see cref="ChatGuardSettings.Validate"/>) and copied, so changing the object afterwards has no
        /// effect on this client. An empty key or base URL gives a local-filter-only client.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="settings"/> is null.</exception>
        /// <exception cref="ArgumentException">The timeout is not a positive finite number of seconds (at most <see cref="ChatGuardSettings.MaxTimeoutSeconds"/>) or the base URL is not an absolute http/https URL.</exception>
        public ChatGuardClient(ChatGuardSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            settings.Validate();
            _settings = settings.Clone();
            _settings.ApiKey = _settings.ApiKey ?? string.Empty;
            _settings.BaseUrl = (_settings.BaseUrl ?? string.Empty).TrimEnd('/');
            _settings.DefaultLanguage = _settings.DefaultLanguage ?? string.Empty;
            _settings.ChannelType = _settings.ChannelType ?? string.Empty;
            _baseUrl = _settings.BaseUrl;
            _moderateUrl = _baseUrl + "/v1/moderate";
            _authorizationHeader = "Bearer " + _settings.ApiKey;
            _timeoutSeconds = Math.Max(1, (int)Math.Ceiling(_settings.TimeoutSeconds));
            if (HasServer && (_baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || _baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                // Parsed once per client; left null when System.Uri rejects the URL so Moderate takes the string path.
                Uri.TryCreate(_moderateUrl, UriKind.Absolute, out _moderateUri);
            }

            _thresholds = _settings.Thresholds ?? Thresholds.Default();
            WarnIfServerKeyInBuild(_settings.ApiKey);
            if (_settings.OfflineBehavior != OfflineBehavior.AllowAll && _settings.OfflineBehavior != OfflineBehavior.BlockAll)
            {
                // Parse the word lists this client falls back to by default (same test as Fallback's default branch)
                // here, at construction, so that cost does not land on the first offline or degraded message.
                // Evaluating an empty message reuses the filter's own language resolution (the default language or the
                // fallback, plus English). A request that names another language parses that list on its first fallback.
                Filter.Evaluate(NormalizedMessage.Create(string.Empty), _settings.DefaultLanguage);
            }
        }

        /// <summary>Builds a client from a <see cref="ChatGuardConfig"/> asset (see <see cref="ChatGuardConfig.ToSettings"/>).</summary>
        public ChatGuardClient(ChatGuardConfig config)
            : this((config ?? throw new ArgumentNullException(nameof(config))).ToSettings())
        {
        }

        /// <summary>
        /// Positional shorthand for <see cref="ChatGuardClient(ChatGuardSettings)"/>; the defaults are the same as
        /// <see cref="ChatGuardSettings"/>, including <paramref name="ageRating"/> "16+" (pass null or empty to send
        /// no <c>channel.age_rating</c>).
        /// </summary>
        public ChatGuardClient(string apiKey, string baseUrl, float timeoutSeconds = 2f, OfflineBehavior offline = OfflineBehavior.LocalFilter, bool localWhenDegraded = true, Thresholds? thresholds = null, string language = "en", string channelType = "global", string? ageRating = "16+")
            : this(new ChatGuardSettings
            {
                ApiKey = apiKey ?? string.Empty,
                BaseUrl = baseUrl ?? string.Empty,
                TimeoutSeconds = timeoutSeconds,
                OfflineBehavior = offline,
                LocalFilterWhenDegraded = localWhenDegraded,
                Thresholds = thresholds,
                DefaultLanguage = language,
                ChannelType = channelType,
                AgeRating = ageRating,
            })
        {
        }

        /// <summary>
        /// A copy of the settings this client runs with (a trailing slash is trimmed from the base URL;
        /// <see cref="ChatGuardSettings.Thresholds"/> stays null when the built-in defaults are used), for diagnostics
        /// such as logging the base URL and offline behaviour at startup. It contains the API key, so do not log it
        /// verbatim. Changing the copy does not affect the client.
        /// </summary>
        public ChatGuardSettings Settings => _settings.Clone();

        /// <summary>
        /// The shared dictionary filter, created on first use so AllowAll/BlockAll clients never create it or the built-in
        /// catalog. Creating it parses nothing: the catalog parses each language's list on that language's first lookup
        /// (see the constructor's prewarm). A plain static field with no initializer keeps ChatGuardClient free of a type
        /// initializer (Mono runs beforefieldinit initializers while JIT-compiling methods that touch them). The field is
        /// read with Volatile.Read and set with Interlocked.CompareExchange, because a plain store does not guarantee that
        /// another thread sees a fully built filter on ARM64 (IL2CPP or Mono). A race can build a second filter, which is
        /// discarded; every caller gets the published one.
        /// </summary>
        private static LocalFilter Filter => Volatile.Read(ref s_filter) ?? CreateFilter();

        /// <summary>Builds the shared filter and publishes it unless another thread already has; returns the published one.</summary>
        private static LocalFilter CreateFilter()
        {
            var filter = new LocalFilter();
            return Interlocked.CompareExchange(ref s_filter, filter, null) ?? filter;
        }

        /// <summary>A cg_live_ key in a player build is a leaked server key; publishable keys are cg_pub_.</summary>
        private static void WarnIfServerKeyInBuild(string apiKey)
        {
            if (s_warnedLiveKey || !apiKey.StartsWith("cg_live_", StringComparison.Ordinal) || UnityEngine.Application.isEditor)
            {
                return;
            }

            s_warnedLiveKey = true;
            UnityEngine.Debug.LogWarning("Chat Guard: a cg_live_ server key is being used in a player build. Ship a cg_pub_ publishable key in clients (moderate-only, per-player limits) or move moderation to your server or relay.");
        }

        /// <summary>True when both an API key and a base URL are set; otherwise every call is answered by the offline fallback.</summary>
        public bool HasServer => _settings.ApiKey.Length > 0 && _baseUrl.Length > 0;

        /// <summary>
        /// Starts one moderation call. Yield the returned operation in a coroutine, poll
        /// <see cref="ModerationOperation.IsDone"/>, <c>await</c> it, or pass <paramref name="onCompleted"/>. Without a
        /// configured server (or when the request cannot be created) the operation completes synchronously with the
        /// offline fallback, so <paramref name="onCompleted"/> may run before this method returns. Always completes on the
        /// main thread.
        /// </summary>
        public ModerationOperation Moderate(ModerationRequest request, Action<ModerationResult>? onCompleted = null)
        {
            return Moderate(request, onCompleted, CancellationToken.None);
        }

        /// <summary>
        /// <see cref="Moderate(ModerationRequest, Action{ModerationResult}, CancellationToken)"/> without a callback, the
        /// usual form with <c>await</c>: <c>ModerationResult result = await client.Moderate(request, token);</c>.
        /// </summary>
        public ModerationOperation Moderate(ModerationRequest request, CancellationToken cancellationToken)
        {
            return Moderate(request, null, cancellationToken);
        }

        /// <summary>
        /// Starts one moderation call that <paramref name="cancellationToken"/> can end early. Cancelling the token does
        /// what <see cref="ModerationOperation.Cancel"/> does: the request is aborted, <paramref name="onCompleted"/> and
        /// <see cref="ModerationOperation.Completed"/> are not invoked, and <c>await</c> throws
        /// <see cref="OperationCanceledException"/> carrying the token. A token that is already cancelled gives back a
        /// cancelled operation without sending anything or computing a fallback. The SDK stops listening to the token
        /// when the operation finishes, so one long-lived token (a component's <c>destroyCancellationToken</c>) can serve
        /// every message. Cancel it on the main thread where you can; a token cancelled on another thread takes effect on
        /// the main thread, when Unity next runs posted work. For timeouts use
        /// <see cref="ChatGuardSettings.TimeoutSeconds"/>: <c>CancelAfter</c> relies on a timer thread and never fires
        /// on WebGL. Otherwise the same as <see cref="Moderate(ModerationRequest, Action{ModerationResult})"/>.
        /// </summary>
        public ModerationOperation Moderate(ModerationRequest request, Action<ModerationResult>? onCompleted, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var operation = new ModerationOperation(request);
            if (cancellationToken.IsCancellationRequested)
            {
                operation.CancelOn(cancellationToken); // ends it now: nothing is sent and onCompleted never runs
                return operation;
            }

            if (onCompleted != null)
            {
                operation.Completed += onCompleted;
            }

            if (!HasServer)
            {
                operation.Complete(Fallback(request, DegradedReason.Offline, "no API key or base URL configured"));
                return operation;
            }

            // Registered before the request exists, so a cancel from here on finds it attached and aborts it.
            operation.CancelOn(cancellationToken);
            if (operation.IsDone)
            {
                return operation; // the token was cancelled on another thread just before it was registered
            }

            UnityWebRequest? uwr = null;
            try
            {
                var sw = Stopwatch.StartNew();
                byte[] body = Encoding.UTF8.GetBytes(BuildRequestJson(request));
                // The Uri overload skips UnityWebRequest's per-call URL re-parsing (two Uri objects and a regex) and
                // yields the same url. _moderateUri is null for base URLs without an http(s) scheme and for URLs
                // System.Uri rejects; those use the string overload, so they behave and fail exactly as
                // UnityWebRequest(string) does.
                uwr = _moderateUri != null ? new UnityWebRequest(_moderateUri, UnityWebRequest.kHttpVerbPOST) : new UnityWebRequest(_moderateUrl, UnityWebRequest.kHttpVerbPOST);
                uwr.uploadHandler = new UploadHandlerRaw(body);
                uwr.downloadHandler = new DownloadHandlerBuffer();
                uwr.timeout = _timeoutSeconds;
                uwr.SetRequestHeader("Content-Type", "application/json");
                uwr.SetRequestHeader("Accept", "application/json");
                uwr.SetRequestHeader("Authorization", _authorizationHeader);
                operation.Attach(uwr);

                UnityWebRequest sent = uwr;
                // `completed` invokes immediately when the web request has already finished, so there is no race here.
                uwr.SendWebRequest().completed += _ => Finish(operation, sent, sw);
            }
            catch (Exception ex)
            {
                uwr?.Dispose();
                operation.Complete(Fallback(request, DegradedReason.Offline, ex.Message));
            }

            return operation;
        }

        /// <summary>
        /// Coroutine form for <c>StartCoroutine</c>: starts <see cref="Moderate(ModerationRequest, Action{ModerationResult})"/>, waits for it, then invokes
        /// <paramref name="onCompleted"/> with the result (not invoked when the operation was cancelled).
        /// </summary>
        public IEnumerator ModerateCoroutine(ModerationRequest request, Action<ModerationResult> onCompleted)
        {
            if (onCompleted == null)
            {
                throw new ArgumentNullException(nameof(onCompleted));
            }

            ModerationOperation operation = Moderate(request);
            yield return operation;
            if (operation.Result != null)
            {
                onCompleted(operation.Result);
            }
        }

        /// <summary>Runs on the main thread when the UnityWebRequest finishes, including after an abort from Cancel().</summary>
        private void Finish(ModerationOperation operation, UnityWebRequest uwr, Stopwatch sw)
        {
            try
            {
                if (operation.IsDone)
                {
                    return; // cancelled (aborted) or already completed: nothing to report, only release the request
                }

                ModerationResult result;
                try
                {
                    result = Interpret(uwr, operation.Request, sw);
                }
                catch (Exception ex)
                {
                    result = Fallback(operation.Request, DegradedReason.Upstream, ex.Message);
                }

                operation.Complete(result);
            }
            finally
            {
                uwr.Dispose();
            }
        }

        private ModerationResult Interpret(UnityWebRequest uwr, ModerationRequest request, Stopwatch sw)
        {
#if UNITY_2020_2_OR_NEWER
            bool transportError = uwr.result == UnityWebRequest.Result.ConnectionError || uwr.result == UnityWebRequest.Result.DataProcessingError;
#else
            bool transportError = uwr.isNetworkError;
#endif
            if (transportError)
            {
                return Fallback(request, DegradedReason.Offline, uwr.error);
            }

            if (uwr.responseCode != 200)
            {
                DegradedReason reason = uwr.responseCode == 429 ? DegradedReason.UpstreamRateLimit : DegradedReason.Upstream;
                return Fallback(request, reason, "HTTP " + uwr.responseCode + ": " + Truncate(uwr.downloadHandler.text));
            }

            ModerationResult? parsed = TryParseResponse(uwr.downloadHandler.text, (int)sw.ElapsedMilliseconds);
            if (parsed == null)
            {
                return Fallback(request, DegradedReason.Upstream, "unparseable response");
            }

            if (parsed.Degraded && _settings.LocalFilterWhenDegraded && _settings.OfflineBehavior == OfflineBehavior.LocalFilter)
            {
                return MergeWithLocal(parsed, request);
            }

            return parsed;
        }

        /// <summary>Serializes the request; only set fields are written (the API rejects negative counts).</summary>
        public string BuildRequestJson(ModerationRequest request)
        {
            // Written directly rather than by building a Dictionary tree for MiniJson.Write (the serializer up to 0.2.1).
            // The output must stay byte-identical to that tree's: same keys, order and omission rules, strings escaped by
            // MiniJson.WriteString, counts as invariant integers written only when >= 0. The RequestJson_*_ExactString
            // tests in ClientJsonTests pin this. A null thread entry inside the last-5 window throws
            // NullReferenceException when its author is read
            // (RequestJson_NullThreadEntry_OutsideWindowIgnored_InsideWindowThrows).
            List<ThreadEntry>? thread = request.thread;
            int threadStart = thread != null ? Math.Max(0, thread.Count - 5) : 0;
            string channelType = string.IsNullOrEmpty(request.channelType) ? _settings.ChannelType : request.channelType!;
            string language = string.IsNullOrEmpty(request.language) ? _settings.DefaultLanguage : request.language!;
            string? ageRating = string.IsNullOrEmpty(request.ageRating) ? _settings.AgeRating : request.ageRating;
            var sb = new StringBuilder(EstimateRequestJsonLength(request, thread, threadStart, channelType, language, ageRating));
            sb.Append("{\"message\":");
            MiniJson.WriteString(sb, request.message ?? string.Empty);
            if (!string.IsNullOrEmpty(request.authorId) || request.accountAgeDays >= 0 || request.priorWarnings >= 0)
            {
                sb.Append(",\"author\":{");
                bool first = true;
                if (!string.IsNullOrEmpty(request.authorId))
                {
                    sb.Append("\"id\":");
                    MiniJson.WriteString(sb, request.authorId!);
                    first = false;
                }

                if (request.accountAgeDays >= 0)
                {
                    sb.Append(first ? "\"account_age_days\":" : ",\"account_age_days\":");
                    sb.Append(request.accountAgeDays.ToString(CultureInfo.InvariantCulture));
                    first = false;
                }

                if (request.priorWarnings >= 0)
                {
                    sb.Append(first ? "\"prior_warnings\":" : ",\"prior_warnings\":");
                    sb.Append(request.priorWarnings.ToString(CultureInfo.InvariantCulture));
                }

                sb.Append('}');
            }

            if (thread != null && thread.Count > 0)
            {
                sb.Append(",\"thread\":[");
                for (int i = threadStart; i < thread.Count; i++)
                {
                    ThreadEntry entry = thread[i];
                    sb.Append(i == threadStart ? "{\"author\":" : ",{\"author\":");
                    MiniJson.WriteString(sb, entry.author ?? "unknown");
                    sb.Append(",\"text\":");
                    MiniJson.WriteString(sb, entry.text ?? string.Empty);
                    sb.Append('}');
                }

                sb.Append(']');
            }

            if (!string.IsNullOrEmpty(channelType) || !string.IsNullOrEmpty(language) || !string.IsNullOrEmpty(ageRating))
            {
                sb.Append(",\"channel\":{");
                bool first = true;
                if (!string.IsNullOrEmpty(channelType))
                {
                    sb.Append("\"type\":");
                    MiniJson.WriteString(sb, channelType);
                    first = false;
                }

                if (!string.IsNullOrEmpty(language))
                {
                    sb.Append(first ? "\"language\":" : ",\"language\":");
                    MiniJson.WriteString(sb, language);
                    first = false;
                }

                if (!string.IsNullOrEmpty(ageRating))
                {
                    sb.Append(first ? "\"age_rating\":" : ",\"age_rating\":");
                    MiniJson.WriteString(sb, ageRating!);
                }

                sb.Append('}');
            }

            if (!string.IsNullOrEmpty(request.requestId))
            {
                sb.Append(",\"request_id\":");
                MiniJson.WriteString(sb, request.requestId!);
            }

            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>
        /// Initial builder capacity for <see cref="BuildRequestJson"/>: every string that is written (message, author id,
        /// the thread entries that are sent, channel values, request id) plus their keys and separators. 128 covers the
        /// fixed keys and brackets, 48 the two counts, and 32 each thread entry's keys (or "unknown" for a null author).
        /// Only a hint (escapes can make the JSON longer), so it is computed in long, never throws, reads only the last-5
        /// window and is capped at 64K chars.
        /// </summary>
        private static int EstimateRequestJsonLength(ModerationRequest request, List<ThreadEntry>? thread, int threadStart, string? channelType, string? language, string? ageRating)
        {
            long estimate = 128L + (request.message?.Length ?? 0) + (request.authorId?.Length ?? 0) + (request.requestId?.Length ?? 0)
                + (channelType?.Length ?? 0) + (language?.Length ?? 0) + (ageRating?.Length ?? 0);
            if (request.accountAgeDays >= 0 || request.priorWarnings >= 0)
            {
                estimate += 48;
            }

            if (thread != null)
            {
                for (int i = threadStart; i < thread.Count; i++)
                {
                    ThreadEntry? entry = thread[i];
                    estimate += 32 + (entry?.author?.Length ?? 0) + (entry?.text?.Length ?? 0);
                }
            }

            return (int)Math.Min(estimate, 65536L);
        }

        /// <summary>Parses a /v1/moderate response body. Returns null when the shape is not recognised.</summary>
        public static ModerationResult? TryParseResponse(string json, int latencyMs)
        {
            Dictionary<string, object?>? root;
            try
            {
                root = MiniJson.AsObject(MiniJson.Parse(json));
            }
            catch (FormatException)
            {
                return null;
            }

            if (root == null || !ModerationActions.TryParse(MiniJson.GetString(root, "action"), out ModerationAction action))
            {
                return null;
            }

            var verdicts = new VerdictSet();
            Dictionary<string, object?>? verdictObj = MiniJson.GetObject(root, "verdicts");
            foreach (VerdictCategory category in VerdictCategories.All)
            {
                verdicts[category] = MiniJson.GetNumber(MiniJson.GetObject(verdictObj, VerdictCategories.ToWireName(category)), "p");
            }

            TargetVerdict? target = null;
            Dictionary<string, object?>? targetObj = MiniJson.GetObject(root, "target");
            if (targetObj != null && TargetChoices.TryParse(MiniJson.GetString(targetObj, "choice"), out TargetChoice choice))
            {
                target = new TargetVerdict(choice, MiniJson.GetNumber(targetObj, "confidence"));
            }

            DegradedReasons.TryParse(MiniJson.GetString(root, "degraded_reason"), out DegradedReason reason);
            Dictionary<string, object?>? quota = MiniJson.GetObject(root, "quota");
            return new ModerationResult(
                action,
                MiniJson.GetNumber(root, "severity"),
                verdicts,
                target,
                MiniJson.GetBool(root, "degraded"),
                reason,
                MiniJson.GetBool(root, "cached"),
                MiniJson.GetString(root, "model") ?? string.Empty,
                latencyMs,
                MiniJson.GetString(root, "id"),
                (long)MiniJson.GetNumber(quota, "used"),
                (long)MiniJson.GetNumber(quota, "limit"),
                ResultSource.Server,
                null);
        }

        private ModerationResult Fallback(ModerationRequest request, DegradedReason reason, string? error)
        {
            switch (_settings.OfflineBehavior)
            {
                case OfflineBehavior.AllowAll:
                    return new ModerationResult(ModerationAction.Allow, 0, new VerdictSet(), null, true, reason, false, "offline/allow-all", 0, null, 0, 0, ResultSource.Local, error);
                case OfflineBehavior.BlockAll:
                    return new ModerationResult(ModerationAction.Block, SeverityCalculator.MaxSeverity, new VerdictSet(), null, true, reason, false, "offline/block-all", 0, null, 0, 0, ResultSource.Local, error);
                default:
                    ModerationOutcome outcome = LocalModeration.Evaluate(request.message, string.IsNullOrEmpty(request.language) ? _settings.DefaultLanguage : request.language, reason, Filter, null, _thresholds, _weights);
                    return new ModerationResult(outcome.Action, outcome.Severity, outcome.Verdicts, null, true, reason, false, LocalModeration.ModelName(Filter.Catalog), 0, null, 0, 0, ResultSource.Local, error);
            }
        }

        private ModerationResult MergeWithLocal(ModerationResult server, ModerationRequest request)
        {
            NormalizedMessage normalized = NormalizedMessage.Create(request.message);
            LocalFilterResult local = Filter.Evaluate(normalized, string.IsNullOrEmpty(request.language) ? _settings.DefaultLanguage : request.language);
            VerdictSet merged = server.Verdicts.Clone();
            foreach (VerdictCategory category in VerdictCategories.All)
            {
                if (local.Verdicts[category] > merged[category])
                {
                    merged[category] = local.Verdicts[category];
                }
            }

            double severity = SeverityCalculator.Compute(merged, null, _weights);
            ModerationAction action = ActionMapper.Map(merged, severity, null, _thresholds);
            return new ModerationResult(action, severity, merged, server.Target, true, server.DegradedReason, server.Cached, server.Model, server.LatencyMs, server.Id, server.QuotaUsed, server.QuotaLimit, ResultSource.Server, null);
        }

        private static string Truncate(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return text!.Length > 200 ? text.Substring(0, 200) : text;
        }
    }
}
