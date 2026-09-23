#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Events;

namespace ChatGuard.Unity
{
    [Serializable]
    public sealed class ModerationResultEvent : UnityEvent<ModerationResult>
    {
    }

    /// <summary>
    /// No-code wiring: drop it on a GameObject, assign a config, call <see cref="Moderate(string)"/> from a UnityEvent
    /// or <see cref="Moderate(string, string)"/> from your chat code, and listen to <see cref="onModerated"/>
    /// (delivered on the main thread). <see cref="Moderate(string)"/> sends <see cref="PlayerId"/> as the author id; set
    /// it with <see cref="SetPlayerId"/> (a UnityEvent can call that too) once the player is known. Until then it sends
    /// a random id created on first use and kept in PlayerPrefs under <see cref="InstallIdKey"/>, one per installation
    /// of the game. That id names no device or account, but it is a persistent pseudonymous identifier, so mention it in
    /// the game's privacy notice. Dedicated Server builds never create it: there, a message without a player id is
    /// sent without an author id. Call the component on the main thread, like any MonoBehaviour.
    /// </summary>
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
        /// PlayerPrefs key of the per-installation id that <see cref="Moderate(string)"/> sends when no <see cref="PlayerId"/>
        /// is set. To answer a player's request to export or erase their data, read it on their device with
        /// <c>PlayerPrefs.GetString(InstallIdKey)</c> (empty when none was created) and show it where they can copy it.
        /// </summary>
        public const string InstallIdKey = "chatguard.install_id";

        // The per-installation id, read (or created) once on the main thread and then reused by every Hook.
        private static string? s_installId;

        // The main thread's managed id, recorded at startup and in Awake; -1 until then (edit mode), which is treated as
        // the main thread because Unity runs editor code there.
        private static int s_mainThreadId = -1;

        private ChatGuardClient? _client;
        private readonly List<ModerationOperation> _inFlight = new List<ModerationOperation>();

        /// <summary>
        /// Your id for the player who sends the messages given to <see cref="Moderate(string)"/>: one that stays the same
        /// between sessions, never a name or an email. Null or empty means not set, and then the per-installation id is
        /// sent instead (except in Dedicated Server builds). <see cref="Moderate(string, string)"/> ignores it.
        /// </summary>
        public string? PlayerId { get; set; }

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

        /// <summary>Sets <see cref="PlayerId"/>; the form a UnityEvent can call, for example when the player signs in.</summary>
        public void SetPlayerId(string playerId)
        {
            PlayerId = playerId;
        }

        /// <summary>
        /// Moderates one message from the local player, the form a UnityEvent (an input field's submit event, say) can
        /// call. The author id is <see cref="PlayerId"/> when set, otherwise the per-installation id (see the class
        /// summary); Dedicated Server builds send none.
        /// </summary>
        public void Moderate(string message)
        {
            Moderate(message, DefaultAuthorId(ChatGuardClient.IsDedicatedServerBuild));
        }

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
        /// The author id <see cref="Moderate(string)"/> sends: <see cref="PlayerId"/> when set; otherwise null in a Dedicated
        /// Server build, which never touches PlayerPrefs, and the per-installation id everywhere else.
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
        /// The per-installation id: read from PlayerPrefs (<see cref="InstallIdKey"/>) the first time it is needed, or
        /// created there as a random GUID ("N" format) when missing, and cached for the rest of the session. PlayerPrefs
        /// is only touched on the main thread; on another thread, before the id was first read, this is null and the
        /// message goes without an author id.
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

        /// <summary>True for 32 lowercase hex digits, the shape this component writes; anything else is replaced.</summary>
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

        /// <summary>Forgets the cached id (tests, and play mode without a domain reload); the next read goes to PlayerPrefs again.</summary>
        internal static void ResetInstallIdCache()
        {
            s_installId = null;
        }

        /// <summary>Runs on the main thread before the first scene loads, also when play mode starts without a domain reload.</summary>
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
