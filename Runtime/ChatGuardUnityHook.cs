#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace ChatGuard.Unity
{
    [Serializable]
    public sealed class ModerationResultEvent : UnityEvent<ModerationResult>
    {
    }

    /// <summary>
    /// No-code wiring: drop it on a GameObject, assign a config, call <see cref="Moderate(string, string)"/> from
    /// your chat code or a UnityEvent, and listen to <see cref="onModerated"/> (delivered on the main thread).
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

        private ChatGuardClient? _client;
        private readonly List<ModerationOperation> _inFlight = new List<ModerationOperation>();

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
            if (configureStaticApi && config != null)
            {
                ChatGuardSdk.Configure(config);
            }
        }

        public void Moderate(string message)
        {
            Moderate(message, null);
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
