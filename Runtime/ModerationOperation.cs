#nullable enable
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace ChatGuard.Unity
{
    /// <summary>
    /// Handle for one in-flight moderation call. Yield it in a coroutine (<c>yield return op;</c>), poll
    /// <see cref="IsDone"/>, subscribe to <see cref="Completed"/>, or <c>await</c> it (see <see cref="GetAwaiter"/>).
    /// End it early with <see cref="Cancel"/> or with the <see cref="CancellationToken"/> given to <c>Moderate</c>.
    /// Completes on the main thread; no Tasks or threads are involved, so it behaves the same on every platform
    /// including WebGL.
    /// </summary>
    public sealed class ModerationOperation : CustomYieldInstruction
    {
        private Action<ModerationResult>? _completed;
        private Action? _finished;
        private UnityWebRequest? _request;

        // Token given to Moderate. The fields are written before the token can call back and never cleared, so
        // the token's callback may read them from another thread; only the registration is released when done.
        private CancellationToken _token;
        private SynchronizationContext? _mainContext;
        private int _mainThreadId;
        private CancellationTokenRegistration _registration;

        // The token that ended the operation, for OperationCanceledException.CancellationToken; None after Cancel().
        private CancellationToken _cancelledBy;

        internal ModerationOperation(ModerationRequest request)
        {
            Request = request;
        }

        /// <summary>The request this operation moderates.</summary>
        public ModerationRequest Request { get; }

        /// <summary>True once a result is available or the operation was cancelled.</summary>
        public bool IsDone { get; private set; }

        /// <summary>
        /// True when <see cref="Cancel"/> or the token given to <c>Moderate</c> ended the operation; <see cref="Result"/>
        /// stays null.
        /// </summary>
        public bool IsCancelled { get; private set; }

        /// <summary>Null until done, and stays null when cancelled.</summary>
        public ModerationResult? Result { get; private set; }

        public override bool keepWaiting => !IsDone;

        /// <summary>
        /// Raised once with the result. Subscribing after completion invokes the handler immediately (same contract as
        /// <c>AsyncOperation.completed</c>). Never invoked once the operation is cancelled.
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
        /// has. Awaiting a cancelled operation throws <see cref="OperationCanceledException"/>; when the token given to
        /// <c>Moderate</c> cancelled it, the exception's <see cref="OperationCanceledException.CancellationToken"/> is
        /// that token.
        /// </summary>
        public Awaiter GetAwaiter()
        {
            return new Awaiter(this);
        }

        /// <summary>
        /// Aborts the in-flight request. Marks the operation done and cancelled; no callback is invoked. Cancelling the
        /// token given to <c>Moderate</c> does the same.
        /// </summary>
        public void Cancel()
        {
            if (IsDone)
            {
                return;
            }

            IsDone = true;
            IsCancelled = true;
            _completed = null;
            _registration.Dispose();
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

        /// <summary>
        /// Ends the operation like <see cref="Cancel"/> when <paramref name="cancellationToken"/> is cancelled, right away
        /// when it already is. Called on the main thread by <c>Moderate</c> before a request is sent. The registration is
        /// released when the operation finishes, so one long-lived token (a component's <c>destroyCancellationToken</c>)
        /// does not keep every finished operation alive. A token cancelled on another thread (a <c>CancelAfter</c> timer
        /// outside WebGL, for example) takes effect through the main thread's <see cref="SynchronizationContext"/>, so the
        /// request is still aborted and awaiters still resume on the main thread.
        /// </summary>
        internal void CancelOn(CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled || IsDone)
            {
                return;
            }

            _token = cancellationToken;
            if (cancellationToken.IsCancellationRequested)
            {
                CancelFromToken();
                return;
            }

            _mainContext = SynchronizationContext.Current;
            _mainThreadId = Environment.CurrentManagedThreadId;
            // Without a captured context: the callback runs on the cancelling thread and OnTokenCancelled decides where
            // Cancel runs. A captured context would make a background Cancel() wait for the main thread instead.
            CancellationTokenRegistration registration = cancellationToken.Register(state => ((ModerationOperation)state!).OnTokenCancelled(), this);
            if (IsDone)
            {
                // Another thread cancelled the token just before Register, which then ran the callback here and ended
                // the operation; the registration has nothing left to guard.
                registration.Dispose();
                return;
            }

            _registration = registration;
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
            _registration.Dispose();
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
        /// The token's callback, on whichever thread cancelled it. On the main thread (or when no main-thread context was
        /// captured) the operation is cancelled right away; from any other thread the cancel is posted to the main thread,
        /// because <see cref="UnityWebRequest.Abort"/> and the awaiter continuations belong there. The thread is compared by
        /// id: on Mono the callback runs in the registering execution context, which holds a copy of Unity's
        /// synchronization context, so <see cref="SynchronizationContext.Current"/> is never the captured instance. Reads
        /// only fields that were written before the registration and never change afterwards.
        /// </summary>
        private void OnTokenCancelled()
        {
            SynchronizationContext? main = _mainContext;
            if (main == null || Environment.CurrentManagedThreadId == _mainThreadId)
            {
                CancelFromToken();
                return;
            }

            main.Post(state => ((ModerationOperation)state!).CancelFromToken(), this);
        }

        /// <summary>Cancels on behalf of the token given to <c>Moderate</c>, so an awaiter's exception names it.</summary>
        private void CancelFromToken()
        {
            if (IsDone)
            {
                return;
            }

            _cancelledBy = _token;
            Cancel();
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

            /// <summary>
            /// The result once completed; throws <see cref="OperationCanceledException"/> when the operation was cancelled,
            /// carrying the cancelling token when it was the one given to <c>Moderate</c> (otherwise <see cref="CancellationToken.None"/>).
            /// </summary>
            public ModerationResult GetResult()
            {
                ModerationResult? result = _operation.Result;
                if (result != null)
                {
                    return result;
                }

                if (_operation.IsCancelled)
                {
                    throw new OperationCanceledException("The moderation operation was cancelled.", _operation._cancelledBy);
                }

                throw new InvalidOperationException("The moderation operation has not completed yet.");
            }
        }
    }
}
