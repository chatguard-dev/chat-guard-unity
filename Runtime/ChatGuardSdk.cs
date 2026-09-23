#nullable enable
using System;
using System.Collections;
using System.Threading;
using UnityEngine;

namespace ChatGuard.Unity
{
    /// <summary>
    /// The simplest way in. Configure once at startup from code with <see cref="Configure(string, string)"/> (only
    /// the key is needed) or <see cref="Configure(ChatGuardSettings)"/> (no asset needed), or put a
    /// <see cref="ChatGuardConfig"/> at <c>Assets/Resources/ChatGuardConfig.asset</c> and it is picked up on first
    /// use; the Resources asset is optional. Then call
    /// <see cref="Moderate(string, string, Action{ModerationResult})"/>. Wraps one shared
    /// <see cref="ChatGuardClient"/>; use the instance API directly when you need several clients or full control.
    /// Main thread only.
    /// </summary>
    public static class ChatGuardSdk
    {
        /// <summary>Resource loaded when nothing was configured: <c>Assets/Resources/ChatGuardConfig.asset</c>. Optional; <c>Configure</c> replaces it.</summary>
        public const string DefaultConfigResource = "ChatGuardConfig";

        private static ChatGuardClient? s_client;

        /// <summary>True once <c>Configure</c> was called or the default client was created lazily; <see cref="Reset"/> clears it.</summary>
        public static bool IsConfigured => s_client != null;

        /// <summary>
        /// The shared client: the one given to <c>Configure</c>, or, when nothing was configured, one created on first
        /// use from the optional <see cref="DefaultConfigResource"/> asset. When neither exists, one warning is logged
        /// and a local-filter-only client is used. Read <see cref="ChatGuardClient.Settings"/> to see what it runs with.
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
        /// Configures the shared client from code; no Resources asset is needed. The settings are validated and
        /// copied (see <see cref="ChatGuardClient(ChatGuardSettings)"/>). Calling it again replaces the shared
        /// client with one built from the new settings.
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

        /// <summary>Configures the shared client from a <see cref="ChatGuardConfig"/> asset (any asset, not only the Resources one).</summary>
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
        /// Shorthand for <see cref="Configure(ChatGuardSettings)"/> with only a key: <c>ChatGuardSdk.Configure("cg_pub_...")</c>.
        /// Requests go to <see cref="ChatGuardSettings.DefaultBaseUrl"/> unless <paramref name="baseUrl"/> names another
        /// origin; everything else keeps its <see cref="ChatGuardSettings"/> default (2 s timeout, local filter when
        /// offline, language "en", channel "global", age rating "16+").
        /// </summary>
        public static void Configure(string apiKey, string? baseUrl = null)
        {
            Configure(new ChatGuardSettings { ApiKey = apiKey ?? string.Empty, BaseUrl = baseUrl ?? ChatGuardSettings.DefaultBaseUrl });
        }

        /// <summary>Forgets the shared client (tests, hot reload). The next call creates a new one.</summary>
        public static void Reset()
        {
            s_client = null;
        }

        /// <summary>Moderates one message. The callback runs on the main thread, possibly before this returns (offline fallback).</summary>
        public static ModerationOperation Moderate(string message, string? authorId = null, Action<ModerationResult>? onCompleted = null)
        {
            return Client.Moderate(new ModerationRequest(message, authorId), onCompleted);
        }

        public static ModerationOperation Moderate(ModerationRequest request, Action<ModerationResult>? onCompleted = null)
        {
            return Client.Moderate(request, onCompleted);
        }

        /// <summary>
        /// Moderates one message that <paramref name="cancellationToken"/> can end early, the usual form with <c>await</c>:
        /// <c>ModerationResult result = await ChatGuardSdk.Moderate(text, playerId, destroyCancellationToken);</c>.
        /// Cancelling the token is the same as <see cref="ModerationOperation.Cancel"/>; see
        /// <see cref="ChatGuardClient.Moderate(ModerationRequest, Action{ModerationResult}, CancellationToken)"/>.
        /// </summary>
        public static ModerationOperation Moderate(string message, string? authorId, CancellationToken cancellationToken)
        {
            return Client.Moderate(new ModerationRequest(message, authorId), null, cancellationToken);
        }

        /// <summary>Callback form with a token: <paramref name="onCompleted"/> is not invoked once the token is cancelled.</summary>
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

        /// <summary>For <c>StartCoroutine</c>: waits for the result and then invokes <paramref name="onCompleted"/>.</summary>
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

        /// <summary>Keeps "Enter Play Mode without domain reload" from reusing a client created in a previous run.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLoad()
        {
            Reset();
        }
    }
}
