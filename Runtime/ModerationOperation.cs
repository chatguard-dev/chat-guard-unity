#nullable enable
using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Networking;

namespace ChatGuard.Unity
{
    /// <summary>
    /// Handle for one in-flight moderation call. Yield it in a coroutine (<c>yield return op;</c>), poll
    /// <see cref="IsDone"/>, subscribe to <see cref="Completed"/>, or <c>await</c> it (see <see cref="GetAwaiter"/>).
    /// Completes on the main thread; no Tasks or threads are involved, so it behaves the same on every platform
    /// including WebGL.
    /// </summary>
    public sealed class ModerationOperation : CustomYieldInstruction
    {
        private Action<ModerationResult>? _completed;
        private Action? _finished;
        private UnityWebRequest? _request;

        internal ModerationOperation(ModerationRequest request)
        {
            Request = request;
        }

        /// <summary>The request this operation moderates.</summary>
        public ModerationRequest Request { get; }

        /// <summary>True once a result is available or <see cref="Cancel"/> was called.</summary>
        public bool IsDone { get; private set; }

        /// <summary>True when <see cref="Cancel"/> ended the operation; <see cref="Result"/> stays null.</summary>
        public bool IsCancelled { get; private set; }

        /// <summary>Null until done, and stays null when cancelled.</summary>
        public ModerationResult? Result { get; private set; }

        public override bool keepWaiting => !IsDone;

        /// <summary>
        /// Raised once with the result. Subscribing after completion invokes the handler immediately (same contract as
        /// <c>AsyncOperation.completed</c>). Never invoked after <see cref="Cancel"/>.
        /// </summary>
        public event Action<ModerationResult> Completed
        {
            add
            {
                if (value == null)
                {
                    return;
                }

                if (IsDone)
                {
                    if (Result != null)
                    {
                        Invoke(value, Result);
                    }

                    return;
                }

                _completed += value;
            }
            remove
            {
                _completed -= value;
            }
        }

        /// <summary>
        /// Makes the operation awaitable: <c>ModerationResult result = await op;</c>. The awaiter is a plain struct
        /// implementing <see cref="INotifyCompletion"/> (System.Runtime.CompilerServices), so no Tasks are involved
        /// and it works from <c>async void</c> methods, Unity 2023.1+ <c>async Awaitable</c> methods and third-party
        /// awaitable libraries.
        /// The continuation runs synchronously on the main thread when the operation finishes, inline when it already
        /// has. Awaiting a cancelled operation throws <see cref="OperationCanceledException"/>.
        /// </summary>
        public Awaiter GetAwaiter()
        {
            return new Awaiter(this);
        }

        /// <summary>Aborts the in-flight request. Marks the operation done and cancelled; no callback is invoked.</summary>
        public void Cancel()
        {
            if (IsDone)
            {
                return;
            }

            IsDone = true;
            IsCancelled = true;
            _completed = null;
            UnityWebRequest? request = _request;
            _request = null;
            if (request != null)
            {
                try
                {
                    request.Abort();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }

            FireFinished();
        }

        internal void Attach(UnityWebRequest request)
        {
            _request = request;
        }

        internal void Complete(ModerationResult result)
        {
            if (IsDone)
            {
                return;
            }

            Result = result;
            IsDone = true;
            _request = null;
            Action<ModerationResult>? handlers = _completed;
            _completed = null;
            if (handlers != null)
            {
                foreach (Delegate handler in handlers.GetInvocationList())
                {
                    Invoke((Action<ModerationResult>)handler, result);
                }
            }

            FireFinished();
        }

        /// <summary>
        /// Registers an awaiter continuation. Unlike <see cref="Completed"/> it also runs after <see cref="Cancel"/>,
        /// because an <c>await</c> must always resume (and then throw) rather than hang.
        /// </summary>
        private void RegisterFinished(Action continuation)
        {
            if (IsDone)
            {
                Invoke(continuation);
                return;
            }

            _finished += continuation;
        }

        /// <summary>Runs the awaiter continuations; called once, from <see cref="Complete"/> or <see cref="Cancel"/>, after the <see cref="Completed"/> handlers.</summary>
        private void FireFinished()
        {
            Action? continuations = _finished;
            _finished = null;
            if (continuations == null)
            {
                return;
            }

            foreach (Delegate continuation in continuations.GetInvocationList())
            {
                Invoke((Action)continuation);
            }
        }

        /// <summary>User callbacks must not break the SDK's own cleanup (the request is disposed after this returns).</summary>
        private static void Invoke(Action<ModerationResult> handler, ModerationResult result)
        {
            try
            {
                handler(result);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private static void Invoke(Action continuation)
        {
            try
            {
                continuation();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        /// <summary>
        /// Awaiter for <see cref="ModerationOperation"/>; obtained through <see cref="GetAwaiter"/> (the compiler does
        /// that for <c>await</c>). The continuation runs once the operation finishes for any reason, completed or
        /// cancelled, on the main thread. Only <see cref="INotifyCompletion"/> is used, which lives in
        /// System.Runtime.CompilerServices; nothing here depends on Tasks, threads or timers.
        /// </summary>
        public readonly struct Awaiter : INotifyCompletion
        {
            private readonly ModerationOperation _operation;

            internal Awaiter(ModerationOperation operation)
            {
                _operation = operation;
            }

            /// <summary>True once the operation is done (completed or cancelled); the compiler then calls <see cref="GetResult"/> inline.</summary>
            public bool IsCompleted => _operation.IsDone;

            /// <summary>Runs <paramref name="continuation"/> on the main thread when the operation finishes, or immediately when it already has.</summary>
            public void OnCompleted(Action continuation)
            {
                if (continuation == null)
                {
                    throw new ArgumentNullException(nameof(continuation));
                }

                _operation.RegisterFinished(continuation);
            }

            /// <summary>The result once completed; throws <see cref="OperationCanceledException"/> when the operation was cancelled.</summary>
            public ModerationResult GetResult()
            {
                ModerationResult? result = _operation.Result;
                if (result != null)
                {
                    return result;
                }

                if (_operation.IsCancelled)
                {
                    throw new OperationCanceledException("The moderation operation was cancelled.");
                }

                throw new InvalidOperationException("The moderation operation has not completed yet.");
            }
        }
    }
}
