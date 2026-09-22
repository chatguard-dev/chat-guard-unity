#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
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
    /// <see cref="ChatGuardSettings.OfflineBehavior"/>. No Tasks or threads: <see cref="Moderate"/> returns a
    /// <see cref="ModerationOperation"/> that completes on the main thread, on every platform including WebGL.
    /// </summary>
    public sealed class ChatGuardClient
    {
        private static readonly LocalFilter Filter = new LocalFilter();
        private static bool s_warnedLiveKey;

        private readonly ChatGuardSettings _settings;
        private readonly string _baseUrl;
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
            _thresholds = _settings.Thresholds ?? Thresholds.Default();
            WarnIfServerKeyInBuild(_settings.ApiKey);
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
        /// <see cref="ModerationOperation.IsDone"/>, or pass <paramref name="onCompleted"/>. Without a configured server
        /// (or when the request cannot be created) the operation completes synchronously with the offline fallback, so
        /// <paramref name="onCompleted"/> may run before this method returns. Always completes on the main thread.
        /// </summary>
        public ModerationOperation Moderate(ModerationRequest request, Action<ModerationResult>? onCompleted = null)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var operation = new ModerationOperation(request);
            if (onCompleted != null)
            {
                operation.Completed += onCompleted;
            }

            if (!HasServer)
            {
                operation.Complete(Fallback(request, DegradedReason.Offline, "no API key or base URL configured"));
                return operation;
            }

            UnityWebRequest? uwr = null;
            try
            {
                var sw = Stopwatch.StartNew();
                byte[] body = Encoding.UTF8.GetBytes(BuildRequestJson(request));
                uwr = new UnityWebRequest(_baseUrl + "/v1/moderate", "POST");
                uwr.uploadHandler = new UploadHandlerRaw(body);
                uwr.downloadHandler = new DownloadHandlerBuffer();
                uwr.timeout = Math.Max(1, (int)Math.Ceiling(_settings.TimeoutSeconds));
                uwr.SetRequestHeader("Content-Type", "application/json");
                uwr.SetRequestHeader("Accept", "application/json");
                uwr.SetRequestHeader("Authorization", "Bearer " + _settings.ApiKey);
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
        /// Coroutine form for <c>StartCoroutine</c>: starts <see cref="Moderate"/>, waits for it, then invokes
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
            var root = new Dictionary<string, object?> { ["message"] = request.message ?? string.Empty };
            if (!string.IsNullOrEmpty(request.authorId) || request.accountAgeDays >= 0 || request.priorWarnings >= 0)
            {
                var author = new Dictionary<string, object?>();
                if (!string.IsNullOrEmpty(request.authorId))
                {
                    author["id"] = request.authorId;
                }

                if (request.accountAgeDays >= 0)
                {
                    author["account_age_days"] = request.accountAgeDays;
                }

                if (request.priorWarnings >= 0)
                {
                    author["prior_warnings"] = request.priorWarnings;
                }

                root["author"] = author;
            }

            if (request.thread != null && request.thread.Count > 0)
            {
                var thread = new List<object?>();
                int start = Math.Max(0, request.thread.Count - 5);
                for (int i = start; i < request.thread.Count; i++)
                {
                    thread.Add(new Dictionary<string, object?> { ["author"] = request.thread[i].author ?? "unknown", ["text"] = request.thread[i].text ?? string.Empty });
                }

                root["thread"] = thread;
            }

            var channel = new Dictionary<string, object?>();
            string channelType = string.IsNullOrEmpty(request.channelType) ? _settings.ChannelType : request.channelType!;
            string language = string.IsNullOrEmpty(request.language) ? _settings.DefaultLanguage : request.language!;
            string? ageRating = string.IsNullOrEmpty(request.ageRating) ? _settings.AgeRating : request.ageRating;
            if (!string.IsNullOrEmpty(channelType))
            {
                channel["type"] = channelType;
            }

            if (!string.IsNullOrEmpty(language))
            {
                channel["language"] = language;
            }

            if (!string.IsNullOrEmpty(ageRating))
            {
                channel["age_rating"] = ageRating;
            }

            if (channel.Count > 0)
            {
                root["channel"] = channel;
            }

            if (!string.IsNullOrEmpty(request.requestId))
            {
                root["request_id"] = request.requestId;
            }

            return MiniJson.Write(root);
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
