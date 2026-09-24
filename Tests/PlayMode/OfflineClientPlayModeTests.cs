#nullable enable
using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using ChatGuard.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ChatGuard.Tests
{
    public class OfflineClientPlayModeTests
    {
        // Nothing listens on port 9 (discard) on most systems, so the connection is refused at once. Where something
        // does, the request timeout ends it. A refused request can finish before Moderate returns, so tests that need
        // the request in flight use a SilentServer instead.
        private const string UnreachableUrl = "http://127.0.0.1:9";
        private static readonly string TestKey = "cg_test_" + new string('x', 32);

        [UnityTest]
        public IEnumerator UnreachableServer_FallsBackToLocalFilter()
        {
            var client = new ChatGuardClient(TestKey, UnreachableUrl, timeoutSeconds: 1f);
            ModerationOperation op = client.Moderate(new ModerationRequest("you idiot", "p1"));
            yield return op;

            Assert.That(op.IsDone, Is.True, "request did not complete");
            Assert.That(op.IsCanceled, Is.False);
            Assert.That(op.Result, Is.Not.Null);
            ModerationResult result = op.Result!;
            Assert.That(result.Source, Is.EqualTo(ResultSource.Local));
            Assert.That(result.Degraded, Is.True);
            Assert.That(result.Action, Is.EqualTo(ModerationAction.Hide));
        }

        [UnityTest]
        [UnityPlatform(exclude = new[] { RuntimePlatform.WebGLPlayer })]
        public IEnumerator Cancel_CompletesWithoutResultAndNeverInvokesCompleted()
        {
            using var server = new SilentServer();
            var client = new ChatGuardClient(TestKey, server.Url, timeoutSeconds: 10f);
            int completedCalls = 0;
            ModerationOperation op = client.Moderate(new ModerationRequest("you idiot", "p1"), _ => completedCalls++);
            op.Completed += _ => completedCalls++;
            Assert.That(op.IsDone, Is.False, "the request must still be in flight");

            op.Cancel();
            Assert.That(op.IsDone, Is.True);
            Assert.That(op.IsCanceled, Is.True);
            Assert.That(op.keepWaiting, Is.False);

            // The aborted UnityWebRequest reports back to the client's Finish within milliseconds, and Finish must then
            // only dispose it. Half a second leaves ample margin.
            float until = Time.realtimeSinceStartup + 0.5f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            Assert.That(op.Result, Is.Null);
            Assert.That(completedCalls, Is.EqualTo(0));
            op.Completed += _ => completedCalls++;
            Assert.That(completedCalls, Is.EqualTo(0), "subscribing after Cancel must not invoke the handler");
        }

        [UnityTest]
        public IEnumerator StaticApi_ModerateCoroutine_FallsBackToLocalFilter()
        {
            ChatGuardSdk.Configure(TestKey, UnreachableUrl);
            try
            {
                Assert.That(ChatGuardSdk.IsConfigured, Is.True);
                ModerationResult? got = null;
                IEnumerator e = ChatGuardSdk.ModerateCoroutine("you idiot", "p1", r => got = r);
                while (e.MoveNext())
                {
                    yield return e.Current;
                }

                Assert.That(got, Is.Not.Null);
                Assert.That(got!.Source, Is.EqualTo(ResultSource.Local));
                Assert.That(got.Degraded, Is.True);
                Assert.That(got.Action, Is.EqualTo(ModerationAction.Hide));
            }
            finally
            {
                ChatGuardSdk.Reset();
            }
        }

        [UnityTest]
        [UnityPlatform(exclude = new[] { RuntimePlatform.WebGLPlayer })]
        public IEnumerator TimedOutRequest_AwaitDeliversLocalFilterResult()
        {
            using var server = new SilentServer();
            var client = new ChatGuardClient(TestKey, server.Url, timeoutSeconds: 1f);
            ModerationOperation op = client.Moderate(new ModerationRequest("you idiot", "p1"));
            Assert.That(op.IsDone, Is.False, "the request must still be in flight");

            ModerationResult? awaited = null;
            Exception? error = null;
            AwaitInto(op, r => awaited = r, ex => error = ex);
            Assert.That(awaited, Is.Null, "the continuation must not run before the request finished");

            // The 1 s timeout ends the request well within 5 s.
            float deadline = Time.realtimeSinceStartup + 5f;
            while (awaited == null && error == null && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(error, Is.Null);
            Assert.That(awaited, Is.Not.Null, "await did not resume within 5 s");
            Assert.That(op.IsDone, Is.True);
            Assert.That(awaited, Is.SameAs(op.Result));
            Assert.That(awaited!.Source, Is.EqualTo(ResultSource.Local));
            Assert.That(awaited.Degraded, Is.True);
            Assert.That(awaited.DegradedReason, Is.EqualTo(DegradedReason.Offline));
            Assert.That(awaited.Action, Is.EqualTo(ModerationAction.Hide));
        }

        // CancelAfter timers and worker threads cancel tokens off the main thread, yet UnityWebRequest.Abort and the
        // code after await must run on the main thread. An off-thread Abort throws, and Cancel's log of it fails this
        // test.
        [UnityTest]
        [UnityPlatform(exclude = new[] { RuntimePlatform.WebGLPlayer })]
        public IEnumerator Token_CanceledOnAnotherThread_TakesEffectOnTheMainThread()
        {
            // The request's 10 s timeout outlasts the 5 s wait.
            using var server = new SilentServer();
            var client = new ChatGuardClient(TestKey, server.Url, timeoutSeconds: 10f);
            using var cts = new CancellationTokenSource();
            ModerationOperation op = client.Moderate(new ModerationRequest("you idiot", "p1"), cts.Token);
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            int resumedOn = -1;
            Exception? error = null;
            AwaitInto(op, _ => { }, ex =>
            {
                error = ex;
                resumedOn = Thread.CurrentThread.ManagedThreadId;
            });

            var canceler = new Thread(() => cts.Cancel());
            canceler.Start();
            canceler.Join();
            Assert.That(op.IsDone, Is.False, "the cancel must wait for the main thread instead of running on the canceler");

            float deadline = Time.realtimeSinceStartup + 5f;
            while (!op.IsDone && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(op.IsCanceled, Is.True, "the cancel did not reach the main thread within 5 s");
            Assert.That(op.Result, Is.Null);
            Assert.That(error, Is.TypeOf<OperationCanceledException>());
            Assert.That(((OperationCanceledException)error!).CancellationToken, Is.EqualTo(cts.Token));
            Assert.That(resumedOn, Is.EqualTo(mainThread), "code after await must run on the main thread");
        }

        /// <summary>
        /// Awaits the operation and hands the result or exception to a callback, so nothing escapes the async void.
        /// </summary>
        private static async void AwaitInto(ModerationOperation op, Action<ModerationResult> onResult, Action<Exception> onError)
        {
            try
            {
                ModerationResult result = await op;
                onResult(result);
            }
            catch (Exception ex)
            {
                onError(ex);
            }
        }

        /// <summary>
        /// A loopback port where the OS accepts connections into the listen backlog and nothing answers, so a request
        /// to it stays in flight until its timeout or an abort. WebGL has no sockets, so the tests that use it are
        /// excluded there.
        /// </summary>
        private sealed class SilentServer : IDisposable
        {
            private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);

            internal SilentServer()
            {
                _listener.Start();
                Url = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port;
            }

            internal string Url { get; }

            public void Dispose()
            {
                _listener.Stop();
            }
        }
    }
}
