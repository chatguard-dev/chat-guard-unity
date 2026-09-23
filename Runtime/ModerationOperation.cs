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

        // Everything about the token given to Moderate. Created only for a token that can be cancelled, so an operation
        // without one carries nothing but this null reference. Set before the token can call back and never cleared.
        private TokenLink? _tokenLink;

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
            _tokenLink?.Registration.Dispose();
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
        /// when it already is. Called on the main thread by <c>Moderate</c> before a request is sent. Does nothing, and
        /// allocates nothing, for a token that cannot be cancelled (<see cref="CancellationToken.None"/>, the default). The
        /// registration is released when the operation finishes, so one long-lived token (a component's
        /// <c>destroyCancellationToken</c>) does not keep every finished operation alive. A token cancelled on another
        /// thread (a <c>CancelAfter</c> timer outside WebGL, for example) takes effect through the main thread's
        /// <see cref="SynchronizationContext"/>, so the request is still aborted and awaiters still resume on the main
        /// thread.
        /// </summary>
        internal void CancelOn(CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled || IsDone)
            {
                return;
            }

            var link = new TokenLink(cancellationToken, SynchronizationContext.Current, Environment.CurrentManagedThreadId);
            _tokenLink = link;
            if (cancellationToken.IsCancellationRequested)
            {
                CancelFromToken();
                return;
            }

            CancellationTokenRegistration registration = RegisterWithoutExecutionContext(cancellationToken);
            if (IsDone)
            {
                // Another thread cancelled the token just before Register, which then ran the callback here and ended
                // the operation; the registration has nothing left to guard.
                registration.Dispose();
                return;
            }

            link.Registration = registration;
        }

        /// <summary>
        /// Registers <see cref="OnTokenCancelled"/> with the token without capturing the execution context, which
        /// <see cref="CancellationToken.Register(Action{object}, object)"/> otherwise does on every call (about 0.9 KB on
        /// Unity's Mono). Nothing needs that context: the callback only cancels the operation or posts the cancel to the
        /// main thread's <see cref="SynchronizationContext"/> captured in <see cref="CancelOn"/>, and the awaiter
        /// continuations a cancel runs are resumed as on completion, where an async method's builder restores the
        /// method's own context. Flow is suppressed only around Register and restored on this thread in a finally. When
        /// it is already suppressed, Register captures nothing anyway and <see cref="ExecutionContext.SuppressFlow"/>
        /// would throw, so it is left as it is. Inside an async method, suppressing first copies that method's
        /// execution context (about 160 B on Unity's Mono), still far less than a capture. The callback lambda captures
        /// nothing, so the compiler caches a single delegate for every call.
        /// </summary>
        private CancellationTokenRegistration RegisterWithoutExecutionContext(CancellationToken cancellationToken)
        {
            bool suppress = !ExecutionContext.IsFlowSuppressed();
            AsyncFlowControl flow = suppress ? ExecutionContext.SuppressFlow() : default;
            try
            {
                // Without a captured synchronization context either (useSynchronizationContext is false): the callback
                // runs on the cancelling thread and OnTokenCancelled decides where Cancel runs. A captured context would
                // make a background Cancel() wait for the main thread instead.
                return cancellationToken.Register(static state => ((ModerationOperation)state!).OnTokenCancelled(), this);
            }
            finally
            {
                if (suppress)
                {
                    flow.Undo();
                }
            }
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
            _tokenLink?.Registration.Dispose();
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
        /// id: <see cref="SynchronizationContext.Current"/> depends on the context the cancelling code runs in, not only on
        /// the thread (on Mono, code running in a captured execution context sees a copy of Unity's synchronization
        /// context). Reads only <see cref="TokenLink"/> fields that were set before the registration and never change
        /// afterwards.
        /// </summary>
        private void OnTokenCancelled()
        {
            TokenLink link = _tokenLink!;
            SynchronizationContext? main = link.MainContext;
            if (main == null || Environment.CurrentManagedThreadId == link.MainThreadId)
            {
                CancelFromToken();
                return;
            }

            main.Post(static state => ((ModerationOperation)state!).CancelFromToken(), this);
        }

        /// <summary>Cancels on behalf of the token given to <c>Moderate</c>, so an awaiter's exception names it.</summary>
        private void CancelFromToken()
        {
            if (IsDone)
            {
                return;
            }

            _tokenLink!.CancelledOperation = true;
            Cancel();
        }

        /// <summary>
        /// The token behind a cancellation, for <see cref="OperationCanceledException.CancellationToken"/>: the token given
        /// to <c>Moderate</c> when it ended the operation, <see cref="CancellationToken.None"/> after <see cref="Cancel"/>.
        /// </summary>
        private CancellationToken CancelledBy()
        {
            TokenLink? link = _tokenLink;
            return link != null && link.CancelledOperation ? link.Token : CancellationToken.None;
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
        /// The state an operation keeps for the token given to <c>Moderate</c>, allocated only when that token can be
        /// cancelled. The readonly fields are set before the token can call back, so the callback may read them on any
        /// thread. <see cref="Registration"/> is set once at the end of <see cref="CancelOn"/>, after Register has
        /// returned, and is disposed where the operation finishes; <see cref="CancelledOperation"/> is set only by
        /// <see cref="CancelFromToken"/>, just before it cancels the operation.
        /// </summary>
        private sealed class TokenLink
        {
            internal readonly CancellationToken Token;

            /// <summary>The synchronization context <c>Moderate</c> ran with, where a cancel from another thread is posted.</summary>
            internal readonly SynchronizationContext? MainContext;

            internal readonly int MainThreadId;

            /// <summary>Released when the operation finishes; stays default when the token ended it before or during Register.</summary>
            internal CancellationTokenRegistration Registration;

            /// <summary>True when this token, not <see cref="Cancel"/>, ended the operation.</summary>
            internal bool CancelledOperation;

            internal TokenLink(CancellationToken token, SynchronizationContext? mainContext, int mainThreadId)
            {
                Token = token;
                MainContext = mainContext;
                MainThreadId = mainThreadId;
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
                    throw new OperationCanceledException("The moderation operation was cancelled.", _operation.CancelledBy());
                }

                throw new InvalidOperationException("The moderation operation has not completed yet.");
            }
        }
    }
}
