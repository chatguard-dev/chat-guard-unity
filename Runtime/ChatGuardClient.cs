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
using Unity.Collections;
using UnityEngine.Networking;

namespace ChatGuard
{
    /// <summary>
    /// Client for the Chat Guard API (POST /v1/moderate), built on UnityWebRequest. It is a plain C# class, not a
    /// MonoBehaviour: create one and reuse it for every message. <see cref="ChatGuardSdk"/> wraps one shared instance.
    /// </summary>
    /// <remarks>
    /// Configure it from code with <see cref="ChatGuardSettings"/> or with a <see cref="ChatGuardConfig"/> asset.
    /// Without an API key, or when the server gives no usable answer, the client answers on its own as
    /// <see cref="ChatGuardSettings.OfflineBehavior"/> says. By default that is the local filter: built-in word lists
    /// checked on the device. Such results are degraded (<see cref="ModerationResult.Degraded"/>) and come from
    /// <see cref="ResultSource.Local"/>. <c>Moderate</c> returns a <see cref="ModerationOperation"/> that completes on
    /// the main thread; overloads take a <see cref="CancellationToken"/>. No Tasks or threads are used, so it works on
    /// every platform, WebGL included.
    /// </remarks>
    public sealed class ChatGuardClient
    {
        /// <summary>
        /// Largest number of thread entries (earlier chat lines) sent with one message. The model reads at most the
        /// last 5, fewer when they are long; the API accepts up to 50.
        /// </summary>
        private const int MaxThreadEntries = 5;

        /// <summary>Longest thread text the API accepts, in UTF-16 code units; longer text is cut before sending.</summary>
        private const int MaxThreadTextChars = 2000;

        /// <summary>Longest thread author the API accepts, in UTF-16 code units; a longer one is sent as empty.</summary>
        private const int MaxThreadAuthorChars = 128;

        /// <summary>
        /// Largest <c>account_age_days</c> and <c>prior_warnings</c> the API accepts; larger counts are sent as this.
        /// </summary>
        private const int MaxAuthorCount = 100000;

        /// <summary>
        /// Longest excerpt of an HTTP error body quoted in <see cref="ModerationResult.Error"/> when the body is not a
        /// problem description.
        /// </summary>
        private const int MaxErrorBodyChars = 200;

        /// <summary>Longest error text made from a problem description, not counting the "HTTP nnn: " prefix.</summary>
        private const int MaxProblemChars = 1000;

        private static LocalFilter? s_filter;
        private static bool s_warnedLiveKey;

        private readonly ChatGuardSettings _settings;
        private readonly string _baseUrl;
        private readonly string _moderateUrl;
        private readonly Uri? _moderateUri;
        private readonly string _authorizationHeader;
        private readonly string _gameHeader;
        private readonly int _timeoutSeconds;
        private readonly Thresholds _thresholds;
        private readonly SeverityWeights _weights = SeverityWeights.Default();

        /// <summary>
        /// Creates a client from plain settings. They are validated and copied, so later changes to
        /// <paramref name="settings"/> do not affect this client.
        /// </summary>
        /// <remarks>
        /// With <see cref="OfflineBehavior.LocalFilter"/>, the word lists for the default language and English are
        /// parsed here, not on the first local answer. In a player build, a <c>cg_live_</c> server key logs one warning
        /// per run, except in Dedicated Server builds.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="settings"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// A setting is invalid; see <see cref="ChatGuardSettings.Validate"/>.
        /// </exception>
        public ChatGuardClient(ChatGuardSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            settings.Validate();
            _settings = settings.Clone();
            _settings.ApiKey = _settings.ApiKey ?? string.Empty;
            _settings.BaseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl) ? ChatGuardSettings.DefaultBaseUrl : _settings.BaseUrl.Trim().TrimEnd('/');
            _settings.DefaultLanguage = _settings.DefaultLanguage ?? string.Empty;
            _settings.ChannelType = _settings.ChannelType ?? string.Empty;
            _baseUrl = _settings.BaseUrl;
            _moderateUrl = _baseUrl + "/v1/moderate";
            _authorizationHeader = "Bearer " + _settings.ApiKey;
            _gameHeader = GameIdentity(ReadBundleId());
            _timeoutSeconds = Math.Max(1, (int)Math.Ceiling(_settings.TimeoutSeconds));
            if (HasServer && (_baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || _baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                // Parsed once per client. Left null when System.Uri rejects the URL; Moderate then uses the string URL.
                Uri.TryCreate(_moderateUrl, UriKind.Absolute, out _moderateUri);
            }

            _thresholds = _settings.Thresholds ?? Thresholds.Default();
            WarnIfServerKeyInBuild(_settings.ApiKey);
            if (_settings.OfflineBehavior != OfflineBehavior.AllowAll && _settings.OfflineBehavior != OfflineBehavior.BlockAll)
            {
                // The local filter answers for this client (same test as Fallback's default branch). Evaluating an empty
                // message here parses the lists a local answer would use: the default language (English when it has no
                // list) plus English.
                Filter.Evaluate(NormalizedMessage.Create(string.Empty), _settings.DefaultLanguage);
            }
        }

        /// <summary>
        /// Creates a client from a <see cref="ChatGuardConfig"/> asset (<see cref="ChatGuardConfig.ToSettings"/>), with
        /// the same validation as <see cref="ChatGuardClient(ChatGuardSettings)"/>.
        /// </summary>
        public ChatGuardClient(ChatGuardConfig config)
            : this((config ?? throw new ArgumentNullException(nameof(config))).ToSettings())
        {
        }

        /// <summary>
        /// Shorthand for <see cref="ChatGuardClient(ChatGuardSettings)"/>: <c>new ChatGuardClient(apiKey)</c> is enough.
        /// Each parameter sets the matching <see cref="ChatGuardSettings"/> property and has the same default.
        /// </summary>
        public ChatGuardClient(string apiKey, string? baseUrl = null, float timeoutSeconds = 2f, OfflineBehavior offline = OfflineBehavior.LocalFilter, bool localWhenDegraded = true, Thresholds? thresholds = null, string language = "en", string channelType = "global", string? ageRating = "16+")
            : this(new ChatGuardSettings
            {
                ApiKey = apiKey ?? string.Empty,
                BaseUrl = baseUrl ?? ChatGuardSettings.DefaultBaseUrl,
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
        /// A copy of the settings this client runs with, for diagnostics such as logging the base URL at startup. It
        /// includes the API key, so keep the key out of your logs. Changing the copy does not affect the client.
        /// </summary>
        /// <remarks>
        /// <see cref="ChatGuardSettings.BaseUrl"/> is the URL requests go to, with a blank one replaced by the default
        /// and trailing slashes trimmed. <see cref="ChatGuardSettings.Thresholds"/> stays null when the defaults apply.
        /// </remarks>
        public ChatGuardSettings Settings => _settings.Clone();

        /// <summary>
        /// The local filter, shared by all clients and created on first use, so AllowAll and BlockAll clients never load
        /// its word-list catalog. Each language's list is parsed on its first lookup.
        /// </summary>
        /// <remarks>
        /// <c>s_filter</c> has no initializer, so ChatGuardClient has no type initializer (Mono runs beforefieldinit
        /// initializers while JIT-compiling methods that touch them). Volatile.Read and CompareExchange make other
        /// threads see a fully built filter on ARM64; a filter built by a losing racer is discarded.
        /// </remarks>
        private static LocalFilter Filter => Volatile.Read(ref s_filter) ?? CreateFilter();

        /// <summary>Builds and publishes the filter unless another thread got there first; returns the published one.</summary>
        private static LocalFilter CreateFilter()
        {
            var filter = new LocalFilter();
            return Interlocked.CompareExchange(ref s_filter, filter, null) ?? filter;
        }

        /// <summary>
        /// HTTP header that carries the game's bundle id (<c>Application.identifier</c>, such as <c>com.studio.game</c>).
        /// It names the game, not the player. Chat Guard uses it to spot one game spread across several Free-plan
        /// organizations.
        /// </summary>
        public const string GameHeaderName = "X-ChatGuard-App";

        /// <summary>
        /// The value sent in <see cref="GameHeaderName"/>: <paramref name="bundleId"/> when it is 1 to 200 ASCII letters,
        /// digits, dots, dashes or underscores (the form the server reads), otherwise empty, which sends no header.
        /// </summary>
        internal static string GameIdentity(string? bundleId)
        {
            if (string.IsNullOrEmpty(bundleId) || bundleId!.Length > 200)
            {
                return string.Empty;
            }

            foreach (char c in bundleId)
            {
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '.' || c == '-' || c == '_'))
                {
                    return string.Empty;
                }
            }

            return bundleId;
        }

        /// <summary>
        /// <c>Application.identifier</c>, or empty when it cannot be read (Unity allows it on the main thread only).
        /// </summary>
        private static string ReadBundleId()
        {
            try
            {
                return UnityEngine.Application.identifier ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Logs one warning per run when a player build uses a <c>cg_live_</c> server key, which players can extract.
        /// Silent in the Editor and in Dedicated Server builds, which may hold one (the build check allows it there too).
        /// </summary>
        private static void WarnIfServerKeyInBuild(string apiKey)
        {
            if (s_warnedLiveKey || !apiKey.StartsWith("cg_live_", StringComparison.Ordinal) || UnityEngine.Application.isEditor || IsDedicatedServerBuild)
            {
                return;
            }

            s_warnedLiveKey = true;
            UnityEngine.Debug.LogWarning("Chat Guard: a cg_live_ server key is being used in a player build. Ship a cg_pub_ publishable key in clients (moderate-only, per-player limits) or move moderation to your server or relay.");
        }

        /// <summary>
        /// True in Dedicated Server builds (the <c>UNITY_SERVER</c> define). A property rather than a constant, so code
        /// that reads it compiles without unreachable-code warnings.
        /// </summary>
        internal static bool IsDedicatedServerBuild
        {
            get
            {
#if UNITY_SERVER
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// True when an API key is set. Without one, nothing is sent and <see cref="ChatGuardSettings.OfflineBehavior"/>
        /// answers every call.
        /// </summary>
        public bool HasServer => _settings.ApiKey.Length > 0;

        /// <summary>
        /// Starts moderating one message. Yield the returned operation in a coroutine, poll
        /// <see cref="ModerationOperation.IsDone"/>, <c>await</c> it, or pass <paramref name="onCompleted"/>. Call it on
        /// the main thread; the operation completes there.
        /// </summary>
        /// <remarks>
        /// Network errors, timeouts and bad server answers do not throw: the operation completes with the
        /// <see cref="ChatGuardSettings.OfflineBehavior"/> answer, and <see cref="ModerationResult.Error"/> says why.
        /// Without an API key, or when the web request cannot be created, it completes before this method returns, so
        /// <paramref name="onCompleted"/> has already run.
        /// </remarks>
        public ModerationOperation Moderate(ModerationRequest request, Action<ModerationResult>? onCompleted = null)
        {
            return Moderate(request, onCompleted, CancellationToken.None);
        }

        /// <summary>
        /// <see cref="Moderate(ModerationRequest, Action{ModerationResult}, CancellationToken)"/> without a callback; the
        /// usual form with <c>await</c>: <c>ModerationResult result = await client.Moderate(request, token);</c>.
        /// </summary>
        public ModerationOperation Moderate(ModerationRequest request, CancellationToken cancellationToken)
        {
            return Moderate(request, null, cancellationToken);
        }

        /// <summary>
        /// Starts moderating one message; <paramref name="cancellationToken"/> can end it early. Otherwise the same as
        /// <see cref="Moderate(ModerationRequest, Action{ModerationResult})"/>.
        /// </summary>
        /// <remarks>
        /// Canceling the token acts like <see cref="ModerationOperation.Cancel"/>: the request is aborted, no callback
        /// runs, and <c>await</c> throws <see cref="OperationCanceledException"/> carrying the token. An already
        /// canceled token returns a canceled operation and sends nothing. The client stops listening to the token
        /// when the operation finishes, so one long-lived token, such as <c>destroyCancellationToken</c>, can serve
        /// every message. A cancel from another thread takes effect on the main thread when Unity next runs posted
        /// work. For timeouts use <see cref="ChatGuardSettings.TimeoutSeconds"/>: <c>CancelAfter</c> needs a timer
        /// thread and never fires on WebGL.
        /// </remarks>
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
                operation.Complete(Fallback(request, DegradedReason.Offline, "no API key configured"));
                return operation;
            }

            // Registered before the request exists, so a cancel from here on finds it attached and aborts it.
            operation.CancelOn(cancellationToken);
            if (operation.IsDone)
            {
                return operation; // the token was canceled on another thread just before it was registered
            }

            ModerationWebRequest? uwr = null;
            try
            {
                long startTimestamp = Stopwatch.GetTimestamp();
                Utf8RequestJsonSink body = WriteRequestUtf8(request);
                // The Uri overload skips UnityWebRequest's per-call URL re-parsing (two Uri objects and a regex). Without
                // a parsed Uri, the string overload keeps UnityWebRequest(string)'s behavior and errors.
                uwr = _moderateUri != null ? new ModerationWebRequest(_moderateUri, this, operation, startTimestamp) : new ModerationWebRequest(_moderateUrl, this, operation, startTimestamp);
                uwr.uploadHandler = CreateUploadHandler(body.Buffer, body.Length);
                body.Release(); // the bytes are in native memory now; the buffer serves this thread's next request
                uwr.downloadHandler = new DownloadHandlerBuffer();
                uwr.timeout = _timeoutSeconds;
                uwr.SetRequestHeader("Content-Type", "application/json");
                uwr.SetRequestHeader("Accept", "application/json");
                uwr.SetRequestHeader("Authorization", _authorizationHeader);
                if (_gameHeader.Length > 0)
                {
                    uwr.SetRequestHeader(GameHeaderName, _gameHeader);
                }
                operation.Attach(uwr);

                // An AsyncOperation.completed handler added after the request finished runs at once, so there is no race.
                uwr.SendWebRequest().completed += ModerationWebRequest.CompletedHandler;
            }
            catch (Exception ex)
            {
                uwr?.Dispose();
                operation.Complete(Fallback(request, DegradedReason.Offline, ex.Message));
            }

            return operation;
        }

        /// <summary>
        /// Coroutine form for <c>StartCoroutine</c>: starts
        /// <see cref="Moderate(ModerationRequest, Action{ModerationResult})"/>, waits for the result, then passes it to
        /// <paramref name="onCompleted"/>. Stopping the coroutine does not abort the request, only drops its result; to
        /// cancel, use <see cref="Moderate(ModerationRequest, CancellationToken)"/>.
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

        /// <summary>
        /// Copies the first <paramref name="length"/> bytes of the body buffer into a native array that the returned
        /// handler owns, so disposing the request frees it. <c>UploadHandlerRaw(byte[])</c> would first need an
        /// exact-length managed copy. If the copy fails, the array is disposed here.
        /// </summary>
        private static UploadHandlerRaw CreateUploadHandler(byte[] buffer, int length)
        {
            var data = new NativeArray<byte>(length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            try
            {
                NativeArray<byte>.Copy(buffer, 0, data, 0, length);
            }
            catch
            {
                data.Dispose();
                throw;
            }

            return new UploadHandlerRaw(data, true);
        }

        /// <summary>
        /// Runs on the main thread when the web request finishes, also after Cancel() aborted it. Completes the operation
        /// unless it is already done, and always disposes the request.
        /// </summary>
        private void Finish(ModerationWebRequest uwr)
        {
            ModerationOperation operation = uwr.Operation;
            try
            {
                if (operation.IsDone)
                {
                    return; // canceled (aborted) or already completed: nothing to report, only release the request
                }

                ModerationResult result;
                try
                {
                    result = Interpret(uwr, operation.Request, uwr.StartTimestamp);
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

        /// <summary>
        /// Turns a finished request into a result, or into the local answer with the matching
        /// <see cref="DegradedReason"/> when the request failed.
        /// </summary>
        private ModerationResult Interpret(UnityWebRequest uwr, ModerationRequest request, long startTimestamp)
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
                return Fallback(request, reason, DescribeHttpError(uwr.responseCode, uwr.downloadHandler.text));
            }

            // The server's usual body is read straight from the bytes, with the same result as TryParseResponse; anything
            // else goes through DownloadHandler.text.
            ModerationResult? parsed = TryReadPlainResponse(uwr, startTimestamp) ?? TryParseResponse(uwr.downloadHandler.text, ElapsedMilliseconds(startTimestamp));
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

        /// <summary>
        /// Reads a 200 body straight from <c>DownloadHandler.nativeData</c> with <see cref="ModerationResponseReader"/>,
        /// without creating the body string. Null when the reader does not handle the body, when the Content-Type names a
        /// charset other than UTF-8 (<c>DownloadHandler.text</c> would decode with it), or when anything throws.
        /// </summary>
        private static ModerationResult? TryReadPlainResponse(UnityWebRequest uwr, long startTimestamp)
        {
            try
            {
                if (!ModerationResponseReader.IsUtf8ContentType(uwr.GetResponseHeader("Content-Type")))
                {
                    return null;
                }

                NativeArray<byte>.ReadOnly body = uwr.downloadHandler.nativeData;
                return ModerationResponseReader.TryRead(body, ElapsedMilliseconds(startTimestamp));
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Whole milliseconds since <paramref name="startTimestamp"/>, as <c>Stopwatch.ElapsedMilliseconds</c> computes them.
        /// </summary>
        private static int ElapsedMilliseconds(long startTimestamp)
        {
            return (int)((Stopwatch.GetTimestamp() - startTimestamp) * 1000 / Stopwatch.Frequency);
        }

        /// <summary>
        /// Returns the JSON body that <c>Moderate</c> sends for <paramref name="request"/>. Unset fields take this
        /// client's defaults or are left out, and the thread and counts are fitted to the API's limits, as the
        /// <see cref="ModerationRequest"/> and <see cref="ThreadEntry"/> fields describe.
        /// </summary>
        public string BuildRequestJson(ModerationRequest request)
        {
            var sink = new StringRequestJsonSink(new StringBuilder(EstimateRequestJsonLength(request)));
            WriteRequestJson(ref sink, request);
            return sink.Builder.ToString();
        }

        /// <summary>
        /// The body <see cref="Moderate(ModerationRequest, Action{ModerationResult}, CancellationToken)"/> uploads:
        /// exactly the bytes of <c>Encoding.UTF8.GetBytes(BuildRequestJson(request))</c>, written into this thread's
        /// reusable buffer without creating the string. Read <see cref="Utf8RequestJsonSink.Buffer"/> up to
        /// <see cref="Utf8RequestJsonSink.Length"/>, then call <see cref="Utf8RequestJsonSink.Release"/>.
        /// </summary>
        internal Utf8RequestJsonSink WriteRequestUtf8(ModerationRequest request)
        {
            Utf8RequestJsonSink sink = Utf8RequestJsonSink.Rent();
            WriteRequestJson(ref sink, request);
            return sink;
        }

        /// <summary>
        /// The one definition of the request body, written to <paramref name="sink"/>: a string for
        /// <see cref="BuildRequestJson"/> or UTF-8 bytes for <see cref="WriteRequestUtf8"/>, so the two cannot drift
        /// apart.
        /// </summary>
        private void WriteRequestJson<TSink>(ref TSink sink, ModerationRequest request)
            where TSink : struct, IRequestJsonSink
        {
            // Keys come in a fixed order and unset fields are left out. The thread and counts are fitted to the API's
            // limits so it does not refuse the whole message with a 400. The RequestJson_*_ExactString tests in
            // ClientJsonTests pin this output; RequestBytesTests pins the UTF-8 sink to the string's bytes.
            List<ThreadEntry>? thread = request.thread;
            int threadStart = ThreadWindowStart(thread);
            string channelType = ChannelTypeFor(request);
            string language = LanguageFor(request);
            string? ageRating = AgeRatingFor(request);
            sink.WriteLiteral("{\"message\":");
            sink.WriteString(request.message ?? string.Empty);
            if (!string.IsNullOrEmpty(request.authorId) || request.accountAgeDays >= 0 || request.priorWarnings >= 0)
            {
                sink.WriteLiteral(",\"author\":{");
                bool first = true;
                if (!string.IsNullOrEmpty(request.authorId))
                {
                    sink.WriteLiteral("\"id\":");
                    sink.WriteString(request.authorId!);
                    first = false;
                }

                if (request.accountAgeDays >= 0)
                {
                    sink.WriteLiteral(first ? "\"account_age_days\":" : ",\"account_age_days\":");
                    sink.WriteNonNegativeInt(Math.Min(request.accountAgeDays, MaxAuthorCount));
                    first = false;
                }

                if (request.priorWarnings >= 0)
                {
                    sink.WriteLiteral(first ? "\"prior_warnings\":" : ",\"prior_warnings\":");
                    sink.WriteNonNegativeInt(Math.Min(request.priorWarnings, MaxAuthorCount));
                }

                sink.WriteLiteral("}");
            }

            if (thread != null && threadStart < thread.Count)
            {
                sink.WriteLiteral(",\"thread\":[");
                bool first = true;
                for (int i = threadStart; i < thread.Count; i++)
                {
                    ThreadEntry? entry = thread[i];
                    if (!IsSendable(entry))
                    {
                        continue;
                    }

                    sink.WriteLiteral(first ? "{\"author\":" : ",{\"author\":");
                    sink.WriteString(ThreadAuthor(entry!.author));
                    sink.WriteLiteral(",\"text\":");
                    sink.WriteString(ThreadText(entry.text));
                    sink.WriteLiteral("}");
                    first = false;
                }

                sink.WriteLiteral("]");
            }

            if (!string.IsNullOrEmpty(channelType) || !string.IsNullOrEmpty(language) || !string.IsNullOrEmpty(ageRating))
            {
                sink.WriteLiteral(",\"channel\":{");
                bool first = true;
                if (!string.IsNullOrEmpty(channelType))
                {
                    sink.WriteLiteral("\"type\":");
                    sink.WriteString(channelType);
                    first = false;
                }

                if (!string.IsNullOrEmpty(language))
                {
                    sink.WriteLiteral(first ? "\"language\":" : ",\"language\":");
                    sink.WriteString(language);
                    first = false;
                }

                if (!string.IsNullOrEmpty(ageRating))
                {
                    sink.WriteLiteral(first ? "\"age_rating\":" : ",\"age_rating\":");
                    sink.WriteString(ageRating!);
                }

                sink.WriteLiteral("}");
            }

            if (!string.IsNullOrEmpty(request.requestId))
            {
                sink.WriteLiteral(",\"request_id\":");
                sink.WriteString(request.requestId!);
            }

            sink.WriteLiteral("}");
        }

        /// <summary>
        /// Index of the first thread entry sent. The sent window runs to the end of the list and holds the last
        /// <see cref="MaxThreadEntries"/> sendable entries (<see cref="IsSendable"/>); unsendable ones inside it are
        /// skipped. The list's count when no entry is sendable, 0 for a null list.
        /// </summary>
        private static int ThreadWindowStart(List<ThreadEntry>? thread)
        {
            if (thread == null)
            {
                return 0;
            }

            int start = thread.Count;
            int kept = 0;
            for (int i = thread.Count - 1; i >= 0 && kept < MaxThreadEntries; i--)
            {
                if (IsSendable(thread[i]))
                {
                    start = i;
                    kept++;
                }
            }

            return start;
        }

        /// <summary>True when the entry exists and has text; any other entry makes the API refuse the whole message.</summary>
        private static bool IsSendable(ThreadEntry? entry)
        {
            return entry != null && !string.IsNullOrEmpty(entry.text);
        }

        /// <summary>
        /// The author label sent for a thread entry: its own, or empty when it is null or too long for the API. The model
        /// reads an empty label as an unlabeled line, not as one more player.
        /// </summary>
        private static string ThreadAuthor(string? author)
        {
            return author == null || author.Length > MaxThreadAuthorChars ? string.Empty : author;
        }

        /// <summary>
        /// A sendable entry's text, cut to its first 2,000 UTF-16 code units when longer; the model reads at most the
        /// first 500 anyway. A surrogate pair split by the cut is left out whole.
        /// </summary>
        private static string ThreadText(string text)
        {
            if (text.Length <= MaxThreadTextChars)
            {
                return text;
            }

            return text.Substring(0, char.IsHighSurrogate(text[MaxThreadTextChars - 1]) ? MaxThreadTextChars - 1 : MaxThreadTextChars);
        }

        /// <summary>The channel type a request is sent with: its own, or the client default when empty.</summary>
        private string ChannelTypeFor(ModerationRequest request)
        {
            return string.IsNullOrEmpty(request.channelType) ? _settings.ChannelType : request.channelType!;
        }

        /// <summary>The language a request is sent with: its own, or the client default when empty.</summary>
        private string LanguageFor(ModerationRequest request)
        {
            return string.IsNullOrEmpty(request.language) ? _settings.DefaultLanguage : request.language!;
        }

        /// <summary>The age rating a request is sent with: its own, or the client default (possibly null) when empty.</summary>
        private string? AgeRatingFor(ModerationRequest request)
        {
            return string.IsNullOrEmpty(request.ageRating) ? _settings.AgeRating : request.ageRating;
        }

        /// <summary>
        /// Initial StringBuilder capacity for <see cref="BuildRequestJson"/>: the length of every string written, plus
        /// 128 for the fixed keys, 48 for the counts and 32 per sent thread entry. Only a hint, since escapes lengthen
        /// the JSON. Summed in long so it cannot overflow, and capped at 65,536.
        /// </summary>
        private int EstimateRequestJsonLength(ModerationRequest request)
        {
            List<ThreadEntry>? thread = request.thread;
            int threadStart = ThreadWindowStart(thread);
            long estimate = 128L + (request.message?.Length ?? 0) + (request.authorId?.Length ?? 0) + (request.requestId?.Length ?? 0)
                + ChannelTypeFor(request).Length + LanguageFor(request).Length + (AgeRatingFor(request)?.Length ?? 0);
            if (request.accountAgeDays >= 0 || request.priorWarnings >= 0)
            {
                estimate += 48;
            }

            if (thread != null)
            {
                for (int i = threadStart; i < thread.Count; i++)
                {
                    ThreadEntry? entry = thread[i];
                    if (IsSendable(entry))
                    {
                        estimate += 32 + ThreadAuthor(entry!.author).Length + Math.Min(entry.text.Length, MaxThreadTextChars);
                    }
                }
            }

            return (int)Math.Min(estimate, 65536L);
        }

        /// <summary>
        /// Parses a POST /v1/moderate response body, such as one your server or relay passed on, into a result from
        /// <see cref="ResultSource.Server"/> with <paramref name="latencyMs"/> as its latency. Returns null when
        /// <paramref name="json"/> is null, not a JSON object, or has no known <c>action</c>; never throws for malformed
        /// or truncated JSON.
        /// </summary>
        public static ModerationResult? TryParseResponse(string? json, int latencyMs)
        {
            if (json == null)
            {
                return null;
            }

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

        /// <summary>
        /// The local answer <see cref="ChatGuardSettings.OfflineBehavior"/> gives when the server gave none. It is
        /// degraded, with <see cref="ResultSource.Local"/>, <paramref name="reason"/> and <paramref name="error"/>.
        /// </summary>
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

        /// <summary>
        /// Re-checks a degraded server answer as <see cref="ChatGuardSettings.LocalFilterWhenDegraded"/> describes,
        /// recomputing verdicts, severity and action. The server's other fields and <see cref="ResultSource.Server"/>
        /// are kept.
        /// </summary>
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

        /// <summary>
        /// The <see cref="ModerationResult.Error"/> text for a non-200 answer: "HTTP nnn: " and the body. A problem
        /// description (the API's JSON error body) becomes plain text of up to 1,000 characters, so a normal detail stays
        /// whole. Any other body is cut to its first 200 characters.
        /// </summary>
        internal static string DescribeHttpError(long statusCode, string? body)
        {
            string prefix = "HTTP " + statusCode.ToString(CultureInfo.InvariantCulture) + ": ";
            string? problem = DescribeProblem(body);
            if (problem != null)
            {
                return prefix + problem;
            }

            if (string.IsNullOrEmpty(body))
            {
                return prefix;
            }

            return prefix + (body!.Length > MaxErrorBodyChars ? body.Substring(0, MaxErrorBodyChars) : body);
        }

        /// <summary>
        /// Plain text of a problem description, such as "Organization suspended. This organization is suspended, so its
        /// API keys are refused. Contact support@chatguard.dev. (code: org_suspended)". Null when the body is over 65,536
        /// characters or is not a JSON object with a title, detail, errors, code or retry_after.
        /// </summary>
        private static string? DescribeProblem(string? body)
        {
            if (body == null || body.Length > 64 * 1024 || !body.TrimStart().StartsWith("{", StringComparison.Ordinal))
            {
                return null;
            }

            Dictionary<string, object?>? root;
            try
            {
                root = MiniJson.AsObject(MiniJson.Parse(body));
            }
            catch (FormatException)
            {
                return null;
            }

            if (root == null)
            {
                return null;
            }

            var text = new StringBuilder();
            AppendSentence(text, MiniJson.GetString(root, "title"));
            AppendSentence(text, MiniJson.GetString(root, "detail"));
            Dictionary<string, object?>? errors = MiniJson.GetObject(root, "errors");
            if (errors != null)
            {
                foreach (KeyValuePair<string, object?> field in errors)
                {
                    List<object?>? messages = MiniJson.AsArray(field.Value);
                    if (messages == null)
                    {
                        continue;
                    }

                    foreach (object? message in messages)
                    {
                        if (message is string line && line.Length > 0)
                        {
                            AppendSentence(text, field.Key + ": " + line);
                        }
                    }
                }
            }

            string? code = MiniJson.GetString(root, "code");
            bool hasRetryAfter = root.TryGetValue("retry_after", out object? retryAfter) && retryAfter is double;
            if (!string.IsNullOrEmpty(code) || hasRetryAfter)
            {
                text.Append(text.Length > 0 ? " (" : "(");
                if (!string.IsNullOrEmpty(code))
                {
                    text.Append("code: ").Append(code);
                }

                if (hasRetryAfter)
                {
                    text.Append(!string.IsNullOrEmpty(code) ? ", retry_after: " : "retry_after: ").Append(((double)retryAfter!).ToString(CultureInfo.InvariantCulture));
                }

                text.Append(')');
            }

            if (text.Length == 0)
            {
                return null;
            }

            return text.Length > MaxProblemChars ? text.ToString(0, MaxProblemChars) : text.ToString();
        }

        /// <summary>
        /// Appends a trimmed sentence after ". ", or after a space when the text so far ends a sentence; skips blank ones.
        /// </summary>
        private static void AppendSentence(StringBuilder text, string? sentence)
        {
            if (string.IsNullOrWhiteSpace(sentence))
            {
                return;
            }

            if (text.Length > 0)
            {
                char last = text[text.Length - 1];
                text.Append(last == '.' || last == '!' || last == '?' ? " " : ". ");
            }

            text.Append(sentence!.Trim());
        }

        /// <summary>
        /// The UnityWebRequest of one <c>Moderate</c> call. It carries the call's state, so one static completion handler
        /// serves every request and no closure, delegate or Stopwatch is created per call.
        /// </summary>
        private sealed class ModerationWebRequest : UnityWebRequest
        {
            /// <summary>
            /// The completion handler of every request, created once. It lives here because ChatGuardClient has no type
            /// initializer on purpose (see <see cref="Filter"/>).
            /// </summary>
            internal static readonly Action<UnityEngine.AsyncOperation> CompletedHandler = OnCompleted;

            internal ModerationWebRequest(Uri uri, ChatGuardClient client, ModerationOperation operation, long startTimestamp)
                : base(uri, kHttpVerbPOST)
            {
                Client = client;
                Operation = operation;
                StartTimestamp = startTimestamp;
            }

            internal ModerationWebRequest(string url, ChatGuardClient client, ModerationOperation operation, long startTimestamp)
                : base(url, kHttpVerbPOST)
            {
                Client = client;
                Operation = operation;
                StartTimestamp = startTimestamp;
            }

            /// <summary>The client whose <see cref="Finish"/> interprets the response.</summary>
            internal ChatGuardClient Client { get; }

            internal ModerationOperation Operation { get; }

            /// <summary><see cref="Stopwatch.GetTimestamp"/> taken when the call started; the result's latency counts from it.</summary>
            internal long StartTimestamp { get; }

            /// <summary>Runs on the main thread when the request finishes, including after an abort from Cancel().</summary>
            private static void OnCompleted(UnityEngine.AsyncOperation asyncOperation)
            {
                var request = (ModerationWebRequest)((UnityWebRequestAsyncOperation)asyncOperation).webRequest;
                request.Client.Finish(request);
            }
        }
    }
}
