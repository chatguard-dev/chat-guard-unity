#nullable enable
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace ChatGuard
{
    /// <summary>
    /// Handle for one moderation call, returned by <c>Moderate</c>. Yield it in a coroutine, poll
    /// <see cref="IsDone"/>, subscribe to <see cref="Completed"/>, or <c>await</c> it. End it early with
    /// <see cref="Cancel"/> or with the <see cref="CancellationToken"/> given to <c>Moderate</c>.
    /// </summary>
    /// <remarks>
    /// It completes on the main thread, and may already be done when <c>Moderate</c> returns (for example without an
    /// API key). No Tasks or threads are involved, so it behaves the same on every platform, WebGL included.
    /// </remarks>
    public sealed class ModerationOperation : CustomYieldInstruction
    {
        private Action<ModerationResult>? _completed;
        private Action? _finished;
        private UnityWebRequest? _request;

        // Null unless Moderate got a token that can be canceled, so operations without one allocate nothing extra.
        // Set before the token can call back and never cleared.
        private TokenLink? _tokenLink;

        internal ModerationOperation(ModerationRequest request)
        {
            Request = request;
        }

        /// <summary>The request this operation moderates.</summary>
        public ModerationRequest Request { get; }

        /// <summary>True once a result is available or the operation was canceled.</summary>
        public bool IsDone { get; private set; }

        /// <summary>
        /// True when <see cref="Cancel"/> or the token given to <c>Moderate</c> ended the operation.
        /// <see cref="Result"/> then stays null.
        /// </summary>
        public bool IsCanceled { get; private set; }

        /// <summary>The result once the operation completes; null before that and after a cancel.</summary>
        public ModerationResult? Result { get; private set; }

        public override bool keepWaiting => !IsDone;

        /// <summary>
        /// Raised once, on the main thread, with the result. A handler added after completion runs immediately, as with
        /// <c>AsyncOperation.completed</c>. Not raised for a canceled operation. An exception from a handler is logged
        /// and does not stop the others.
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
        /// Makes the operation awaitable: <c>ModerationResult result = await op;</c>. Works in <c>async void</c>
        /// methods, in <c>async Awaitable</c> methods (Unity 2023.1+) and with third-party async libraries such as
        /// UniTask.
        /// </summary>
        /// <remarks>
        /// The code after <c>await</c> runs on the main thread as soon as the operation finishes, or inline when it
        /// already has. Awaiting a canceled operation throws <see cref="OperationCanceledException"/> (see
        /// <see cref="Awaiter.GetResult"/>).
        /// </remarks>
        public Awaiter GetAwaiter()
        {
            return new Awaiter(this);
        }

        /// <summary>
        /// Ends the operation early and aborts its web request. Does nothing once the operation is done. Call it on the
        /// main thread.
        /// </summary>
        /// <remarks>
        /// <see cref="IsDone"/> and <see cref="IsCanceled"/> become true. No <see cref="Completed"/> handler runs,
        /// including the callback given to <c>Moderate</c>, and an <c>await</c> on the operation throws
        /// <see cref="OperationCanceledException"/>. Canceling the token given to <c>Moderate</c> has the same effect.
        /// To cancel from another thread, cancel that token instead; the SDK moves the cancel to the main thread.
        /// </remarks>
        public void Cancel()
        {
            if (IsDone)
            {
                return;
            }

            IsDone = true;
            IsCanceled = true;
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
        /// Cancels the operation like <see cref="Cancel"/> when <paramref name="cancellationToken"/> is canceled, or
        /// right away when it already is. <c>Moderate</c> calls it before creating the request, on the main thread, so
        /// the context and thread id captured here are the main thread's. A token that cannot be canceled is ignored.
        /// </summary>
        /// <remarks>
        /// The registration is released when the operation finishes, so one long-lived token, such as a component's
        /// <c>destroyCancellationToken</c>, does not keep finished operations alive.
        /// </remarks>
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
                // Another thread canceled the token just before Register, so Register ran the callback here and the
                // operation is already canceled. The registration has nothing left to guard.
                registration.Dispose();
                return;
            }

            link.Registration = registration;
        }

        /// <summary>
        /// Registers <see cref="OnTokenCanceled"/> without capturing the execution context, which
        /// <see cref="CancellationToken.Register(Action{object}, object)"/> otherwise captures on every call (about
        /// 0.9 KB on Unity's Mono). The callback does not need that context: it only cancels or posts the cancel, and
        /// an awaiting async method restores its own when it resumes.
        /// </summary>
        /// <remarks>
        /// When flow is already suppressed, Register captures nothing and <see cref="ExecutionContext.SuppressFlow"/>
        /// would throw, so it is left alone. Inside an async method, suppressing copies that method's context (about
        /// 160 B on Unity's Mono), still far less than a capture.
        /// </remarks>
        private CancellationTokenRegistration RegisterWithoutExecutionContext(CancellationToken cancellationToken)
        {
            bool suppress = !ExecutionContext.IsFlowSuppressed();
            AsyncFlowControl flow = suppress ? ExecutionContext.SuppressFlow() : default;
            try
            {
                // This overload captures no synchronization context either, so the callback runs on the canceling
                // thread and OnTokenCanceled picks where to cancel. A captured one would make a background Cancel()
                // wait for the main thread.
                return cancellationToken.Register(static state => ((ModerationOperation)state!).OnTokenCanceled(), this);
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
        /// The token's callback, run on whichever thread canceled the token. On the main thread, or when no
        /// main-thread context was captured, it cancels right away. From any other thread it posts the cancel to the
        /// main thread, because <see cref="UnityWebRequest.Abort"/> and the awaiter continuations belong there.
        /// </summary>
        /// <remarks>
        /// Threads are compared by id, not by <see cref="SynchronizationContext.Current"/>. On Mono, code in a captured
        /// execution context sees a copy of Unity's synchronization context, so comparing references can fail even on
        /// the main thread.
        /// </remarks>
        private void OnTokenCanceled()
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

        /// <summary>
        /// Cancels on behalf of the token given to <c>Moderate</c>, so an awaiter's exception carries that token.
        /// </summary>
        private void CancelFromToken()
        {
            if (IsDone)
            {
                return;
            }

            _tokenLink!.CanceledOperation = true;
            Cancel();
        }

        /// <summary>
        /// The token an awaiter's <see cref="OperationCanceledException"/> carries: the one given to <c>Moderate</c>
        /// when it ended the operation, <see cref="CancellationToken.None"/> after <see cref="Cancel"/>.
        /// </summary>
        private CancellationToken CanceledBy()
        {
            TokenLink? link = _tokenLink;
            return link != null && link.CanceledOperation ? link.Token : CancellationToken.None;
        }

        /// <summary>
        /// Registers an awaiter continuation, or runs it at once when the operation is done. Unlike
        /// <see cref="Completed"/> it also runs after <see cref="Cancel"/>, because an <c>await</c> must resume and throw
        /// rather than hang.
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

        /// <summary>
        /// Runs the awaiter continuations once, after the <see cref="Completed"/> handlers, from <see cref="Complete"/>
        /// or <see cref="Cancel"/>.
        /// </summary>
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

        /// <summary>
        /// Runs user code and logs anything it throws, so a failing callback cannot skip the remaining callbacks and
        /// continuations or escape into the SDK's own code.
        /// </summary>
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
        /// What an operation keeps for a token given to <c>Moderate</c> that can be canceled. The readonly fields are
        /// set before the token can call back, so the callback may read them on any thread.
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
            internal bool CanceledOperation;

            internal TokenLink(CancellationToken token, SynchronizationContext? mainContext, int mainThreadId)
            {
                Token = token;
                MainContext = mainContext;
                MainThreadId = mainThreadId;
            }
        }

        /// <summary>The awaiter behind <c>await op</c>; the compiler uses it for you.</summary>
        public readonly struct Awaiter : INotifyCompletion
        {
            private readonly ModerationOperation _operation;

            internal Awaiter(ModerationOperation operation)
            {
                _operation = operation;
            }

            /// <summary>True once the operation is done (completed or canceled); the compiler then calls <see cref="GetResult"/> inline.</summary>
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

            /// <summary>Returns the operation's result; the compiler calls it when <c>await</c> resumes.</summary>
            /// <exception cref="OperationCanceledException">
            /// The operation was canceled. Its <see cref="OperationCanceledException.CancellationToken"/> is the token
            /// given to <c>Moderate</c> when that token ended the operation, otherwise
            /// <see cref="CancellationToken.None"/>.
            /// </exception>
            /// <exception cref="InvalidOperationException">The operation has not finished yet.</exception>
            public ModerationResult GetResult()
            {
                ModerationResult? result = _operation.Result;
                if (result != null)
                {
                    return result;
                }

                if (_operation.IsCanceled)
                {
                    throw new OperationCanceledException("The moderation operation was canceled.", _operation.CanceledBy());
                }

                throw new InvalidOperationException("The moderation operation has not completed yet.");
            }
        }
    }
}
