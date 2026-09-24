#nullable enable
using System;
using System.Collections;
using System.Threading;
using UnityEngine;

namespace ChatGuard
{
    /// <summary>
    /// The simplest way in: configure once at startup, then call
    /// <see cref="Moderate(string, string, Action{ModerationResult})"/> for each chat message. Main thread only.
    /// </summary>
    /// <remarks>
    /// Configure from code with <see cref="Configure(string, string)"/> (just the API key) or
    /// <see cref="Configure(ChatGuardSettings)"/>. Or save a <see cref="ChatGuardConfig"/> asset as
    /// <c>Assets/Resources/ChatGuardConfig.asset</c> to have it loaded on first use. All static calls share one
    /// <see cref="ChatGuardClient"/>; create your own clients when you need several.
    /// </remarks>
    public static class ChatGuardSdk
    {
        /// <summary>
        /// Resources path of the optional config asset saved as <c>Assets/Resources/ChatGuardConfig.asset</c> (see
        /// <see cref="Client"/>).
        /// </summary>
        public const string DefaultConfigResource = "ChatGuardConfig";

        private static ChatGuardClient? s_client;

        /// <summary>
        /// True once a shared client exists, set up by <c>Configure</c> or created on first use, even without an API
        /// key. <see cref="Reset"/> clears it. To check for a key, read <see cref="ChatGuardClient.HasServer"/>.
        /// </summary>
        public static bool IsConfigured => s_client != null;

        /// <summary>
        /// The shared client behind every static call. Without <c>Configure</c>, first use creates it from the
        /// <see cref="DefaultConfigResource"/> asset. Without that asset, it logs a warning and sends nothing: the
        /// local filter, a built-in word-list filter, answers every message.
        /// </summary>
        public static ChatGuardClient Client
        {
            get
            {
                if (s_client == null)
                {
                    s_client = CreateDefaultClient();
                }

                return s_client;
            }
        }

        /// <summary>
        /// Sets up the shared client from code. The settings are copied, so later edits to <paramref name="settings"/>
        /// have no effect. Calling it again replaces the shared client; messages in flight finish with the old one.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="settings"/> is null.</exception>
        /// <exception cref="ArgumentException">The settings are invalid (see <see cref="ChatGuardSettings.Validate"/>).</exception>
        public static void Configure(ChatGuardSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            s_client = new ChatGuardClient(settings);
        }

        /// <summary>
        /// Sets up the shared client from any <see cref="ChatGuardConfig"/> asset, not only the Resources one, like
        /// <see cref="Configure(ChatGuardSettings)"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// The asset's values are invalid (see <see cref="ChatGuardSettings.Validate"/>).
        /// </exception>
        public static void Configure(ChatGuardConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            s_client = new ChatGuardClient(config);
        }

        /// <summary>Uses an existing client as the shared one.</summary>
        public static void Configure(ChatGuardClient client)
        {
            s_client = client ?? throw new ArgumentNullException(nameof(client));
        }

        /// <summary>
        /// Sets up the shared client with only an API key: <c>ChatGuardSdk.Configure("cg_pub_...")</c>. Everything else
        /// keeps its <see cref="ChatGuardSettings"/> default.
        /// </summary>
        /// <param name="apiKey">
        /// Your API key. Game builds use a <c>cg_pub_</c> key and must never ship a <c>cg_live_</c> server key (see
        /// <see cref="ChatGuardSettings.ApiKey"/>). Null or empty sends nothing; the local filter then answers every
        /// message.
        /// </param>
        /// <param name="baseUrl">
        /// Your proxy's base URL, if you run one (see <see cref="ChatGuardSettings.BaseUrl"/>). Null or blank means
        /// <see cref="ChatGuardSettings.DefaultBaseUrl"/>.
        /// </param>
        /// <exception cref="ArgumentException">
        /// <paramref name="baseUrl"/> is neither blank nor an absolute http or https URL.
        /// </exception>
        public static void Configure(string apiKey, string? baseUrl = null)
        {
            Configure(new ChatGuardSettings { ApiKey = apiKey ?? string.Empty, BaseUrl = baseUrl ?? ChatGuardSettings.DefaultBaseUrl });
        }

        /// <summary>
        /// Forgets the shared client, for tests or hot reload. Unless <c>Configure</c> runs first, the next static call
        /// creates the default one again (see <see cref="Client"/>).
        /// </summary>
        public static void Reset()
        {
            s_client = null;
        }

        /// <summary>
        /// Moderates one chat message. <paramref name="onCompleted"/> runs on the main thread, possibly before this
        /// method returns (see <see cref="ChatGuardClient.Moderate(ModerationRequest, Action{ModerationResult})"/>).
        /// </summary>
        /// <param name="message">The chat text, within the limits of <see cref="ModerationRequest.message"/>.</param>
        /// <param name="authorId">
        /// Your stable, opaque id for the player, never a real name or email. Required with a <c>cg_pub_</c> key (see
        /// <see cref="ModerationRequest.authorId"/>).
        /// </param>
        /// <param name="onCompleted">Receives the result. Not called if the operation is canceled.</param>
        public static ModerationOperation Moderate(string message, string? authorId = null, Action<ModerationResult>? onCompleted = null)
        {
            return Client.Moderate(new ModerationRequest(message, authorId), onCompleted);
        }

        /// <summary>
        /// Like <see cref="Moderate(string, string, Action{ModerationResult})"/>, with a full
        /// <see cref="ModerationRequest"/> that can add context such as recent messages, channel type or language.
        /// </summary>
        public static ModerationOperation Moderate(ModerationRequest request, Action<ModerationResult>? onCompleted = null)
        {
            return Client.Moderate(request, onCompleted);
        }

        /// <summary>
        /// Moderates one message; canceling <paramref name="cancellationToken"/> ends it early, like
        /// <see cref="ModerationOperation.Cancel"/>. The usual form with <c>await</c>:
        /// <c>ModerationResult result = await ChatGuardSdk.Moderate(text, playerId, destroyCancellationToken);</c>
        /// (<c>destroyCancellationToken</c> needs Unity 2022.2+). See
        /// <see cref="ChatGuardClient.Moderate(ModerationRequest, Action{ModerationResult}, CancellationToken)"/>.
        /// </summary>
        public static ModerationOperation Moderate(string message, string? authorId, CancellationToken cancellationToken)
        {
            return Client.Moderate(new ModerationRequest(message, authorId), null, cancellationToken);
        }

        /// <summary>
        /// Callback form with a token; <paramref name="onCompleted"/> is skipped if the token is canceled first.
        /// </summary>
        public static ModerationOperation Moderate(string message, string? authorId, Action<ModerationResult>? onCompleted, CancellationToken cancellationToken)
        {
            return Client.Moderate(new ModerationRequest(message, authorId), onCompleted, cancellationToken);
        }

        public static ModerationOperation Moderate(ModerationRequest request, CancellationToken cancellationToken)
        {
            return Client.Moderate(request, null, cancellationToken);
        }

        public static ModerationOperation Moderate(ModerationRequest request, Action<ModerationResult>? onCompleted, CancellationToken cancellationToken)
        {
            return Client.Moderate(request, onCompleted, cancellationToken);
        }

        /// <summary>
        /// Coroutine form for <c>StartCoroutine</c>: waits for the result, then calls <paramref name="onCompleted"/>.
        /// Stopping the coroutine skips the callback but does not abort the request; to cancel, pass a
        /// <see cref="CancellationToken"/> to <c>Moderate</c>.
        /// </summary>
        public static IEnumerator ModerateCoroutine(string message, string? authorId, Action<ModerationResult> onCompleted)
        {
            return Client.ModerateCoroutine(new ModerationRequest(message, authorId), onCompleted);
        }

        public static IEnumerator ModerateCoroutine(ModerationRequest request, Action<ModerationResult> onCompleted)
        {
            return Client.ModerateCoroutine(request, onCompleted);
        }

        private static ChatGuardClient CreateDefaultClient()
        {
            ChatGuardConfig? config = Resources.Load<ChatGuardConfig>(DefaultConfigResource);
            if (config != null)
            {
                return new ChatGuardClient(config);
            }

            Debug.LogWarning("Chat Guard: ChatGuardSdk.Configure was not called and no Resources/" + DefaultConfigResource + ".asset was found; using the local filter only. Call ChatGuardSdk.Configure(\"<your key>\") at startup, or create a config via Assets > Create > Chat Guard > Config, paste the key into it and save it as Assets/Resources/" + DefaultConfigResource + ".asset.");
            return new ChatGuardClient(new ChatGuardSettings());
        }

        /// <summary>
        /// Forgets the shared client when Play mode starts, so a client from the previous run is not reused when domain
        /// reload is off.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLoad()
        {
            Reset();
        }
    }
}
