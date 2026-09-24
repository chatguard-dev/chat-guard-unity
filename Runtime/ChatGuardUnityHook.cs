#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Events;

namespace ChatGuard
{
    [Serializable]
    public sealed class ModerationResultEvent : UnityEvent<ModerationResult>
    {
    }

    /// <summary>
    /// A component that moderates chat with no code required: add it to a GameObject and assign a <see cref="config"/>.
    /// Then call <see cref="Moderate(string)"/> from a UnityEvent (or <see cref="Moderate(string, string)"/> from code)
    /// and react through <see cref="onModerated"/>, <see cref="onDeliver"/> and <see cref="onSuppress"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use the component on the main thread only. Its events run there too, possibly before <c>Moderate</c> returns
    /// (see <see cref="ModerationOperation"/>). <see cref="onDeliver"/> and <see cref="onSuppress"/> receive the
    /// original message text. Destroying the component cancels its in-flight calls, and their events never run.
    /// </para>
    /// <para>
    /// With <see cref="configureStaticApi"/> on (the default), <c>Awake</c> also passes <see cref="config"/>, when set,
    /// to <see cref="ChatGuardSdk.Configure(ChatGuardConfig)"/>. That replaces the client behind static
    /// <see cref="ChatGuardSdk"/> calls.
    /// </para>
    /// <para>
    /// Until <see cref="PlayerId"/> is set, <see cref="Moderate(string)"/> sends a random per-installation id as the
    /// author id, created on first use and kept in PlayerPrefs under <see cref="InstallIdKey"/>. It names no device or
    /// account, but it is a persistent pseudonymous identifier, so mention it in your game's privacy notice. Dedicated
    /// Server builds never create it and send no author id instead.
    /// </para>
    /// </remarks>
    [AddComponentMenu("Chat Guard/Chat Guard Hook")]
    public sealed class ChatGuardUnityHook : MonoBehaviour
    {
        public ChatGuardConfig? config;

        [Tooltip("Also make this config the default for the static ChatGuardSdk API.")]
        public bool configureStaticApi = true;

        [Tooltip("Invoked for every moderated message, including local fallbacks.")]
        public ModerationResultEvent onModerated = new ModerationResultEvent();

        [Tooltip("Invoked only when the message should be delivered (allow or flag).")]
        public UnityEvent<string> onDeliver = new UnityEvent<string>();

        [Tooltip("Invoked when the message should not be shown (hide or block).")]
        public UnityEvent<string> onSuppress = new UnityEvent<string>();

        /// <summary>
        /// PlayerPrefs key of the per-installation id sent while <see cref="PlayerId"/> is empty. For a player's data
        /// export or erasure request, read it on their device with <c>PlayerPrefs.GetString(InstallIdKey)</c> and show
        /// it where they can copy it. Empty means none was created on that device.
        /// </summary>
        public const string InstallIdKey = "chatguard.install_id";

        // The per-installation id, loaded (or created) once on the main thread, then shared by every Hook.
        private static string? s_installId;

        // The main thread's managed id, set at startup and in Awake. While it is -1 (Edit mode), any thread counts as
        // the main thread, since Unity runs editor code there.
        private static int s_mainThreadId = -1;

        private ChatGuardClient? _client;
        private readonly List<ModerationOperation> _inFlight = new List<ModerationOperation>();

        /// <summary>
        /// Your id for the local player, sent as the author id by <see cref="Moderate(string)"/> only. Use a stable id,
        /// never a real name or email (see <see cref="ModerationRequest.authorId"/>). While it is null or empty, the
        /// per-installation id is sent instead, or none in Dedicated Server builds.
        /// </summary>
        public string? PlayerId { get; set; }

        /// <summary>
        /// The client this component sends through, created from <see cref="config"/> on first use. Later changes to
        /// <see cref="config"/> do not affect it.
        /// </summary>
        /// <exception cref="InvalidOperationException"><see cref="config"/> is not set.</exception>
        /// <exception cref="ArgumentException">
        /// The config is invalid (see <see cref="ChatGuardSettings.Validate"/>).
        /// </exception>
        public ChatGuardClient Client
        {
            get
            {
                if (_client == null)
                {
                    if (config == null)
                    {
                        throw new InvalidOperationException("ChatGuardUnityHook needs a ChatGuardConfig");
                    }

                    _client = new ChatGuardClient(config);
                }

                return _client;
            }
        }

        private void Awake()
        {
            s_mainThreadId = Thread.CurrentThread.ManagedThreadId;
            if (configureStaticApi && config != null)
            {
                ChatGuardSdk.Configure(config);
            }
        }

        /// <summary>Sets <see cref="PlayerId"/> from a UnityEvent, such as your sign-in event.</summary>
        public void SetPlayerId(string playerId)
        {
            PlayerId = playerId;
        }

        /// <summary>
        /// Moderates one message from the local player, with <see cref="PlayerId"/> (or its fallback) as the author id.
        /// Wire it to a UnityEvent, such as an input field's submit event. Errors, such as a missing config, are
        /// logged, not thrown.
        /// </summary>
        public void Moderate(string message)
        {
            Moderate(message, DefaultAuthorId(ChatGuardClient.IsDedicatedServerBuild));
        }

        /// <summary>
        /// Moderates one message with the author id you pass, such as another player's. Ignores <see cref="PlayerId"/>;
        /// null or empty sends no author id. Errors are logged, not thrown.
        /// </summary>
        public void Moderate(string message, string? authorId)
        {
            try
            {
                ModerationOperation operation = Client.Moderate(new ModerationRequest(message, authorId));
                _inFlight.Add(operation);
                operation.Completed += result =>
                {
                    _inFlight.Remove(operation);
                    Dispatch(message, result);
                };
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, this);
            }
        }

        /// <summary>
        /// The author id <see cref="Moderate(string)"/> sends: <see cref="PlayerId"/> when set, else the
        /// per-installation id, or null with <paramref name="dedicatedServer"/> so Dedicated Server builds never touch
        /// PlayerPrefs.
        /// </summary>
        internal string? DefaultAuthorId(bool dedicatedServer)
        {
            if (!string.IsNullOrEmpty(PlayerId))
            {
                return PlayerId;
            }

            return dedicatedServer ? null : InstallId;
        }

        /// <summary>
        /// The per-installation id, loaded from PlayerPrefs once per session, or created there as a random GUID ("N"
        /// format). PlayerPrefs is main-thread only, so until the main thread has loaded the id, other threads get null
        /// and send no author id.
        /// </summary>
        internal static string? InstallId
        {
            get
            {
                if (s_installId == null && (s_mainThreadId < 0 || s_mainThreadId == Thread.CurrentThread.ManagedThreadId))
                {
                    s_installId = LoadOrCreateInstallId();
                }

                return s_installId;
            }
        }

        private static string LoadOrCreateInstallId()
        {
            string id = PlayerPrefs.GetString(InstallIdKey, string.Empty);
            if (IsInstallId(id))
            {
                return id;
            }

            id = Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(InstallIdKey, id);
            PlayerPrefs.Save();
            return id;
        }

        /// <summary>True for 32 lowercase hex digits, the shape written here; anything else is replaced.</summary>
        private static bool IsInstallId(string value)
        {
            if (value.Length != 32)
            {
                return false;
            }

            foreach (char c in value)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Forgets the cached id so the next read goes to PlayerPrefs again. For tests.</summary>
        internal static void ResetInstallIdCache()
        {
            s_installId = null;
        }

        /// <summary>
        /// Records the main thread and drops the cached id before the first scene loads. Also runs when Play mode
        /// starts without a domain reload, where statics keep the last session's values.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLoad()
        {
            s_mainThreadId = Thread.CurrentThread.ManagedThreadId;
            s_installId = null;
        }

        private void Dispatch(string message, ModerationResult result)
        {
            onModerated.Invoke(result);
            if (result.ShouldDeliver)
            {
                onDeliver.Invoke(message);
            }
            else
            {
                onSuppress.Invoke(message);
            }
        }

        private void OnDestroy()
        {
            foreach (ModerationOperation operation in _inFlight)
            {
                operation.Cancel();
            }

            _inFlight.Clear();
        }
    }
}
