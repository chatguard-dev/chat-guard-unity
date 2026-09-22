#nullable enable
using System;
using System.Collections;
using ChatGuard.Core;
using ChatGuard.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ChatGuard.Tests
{
    public class OfflineClientPlayModeTests
    {
        // 127.0.0.1:9 (discard) refuses connections immediately on most systems; the request timeout bounds the rest.
        private const string UnreachableUrl = "http://127.0.0.1:9";
        private static readonly string TestKey = "cg_test_" + new string('x', 32);

        [UnityTest]
        public IEnumerator UnreachableServer_FallsBackToLocalFilter()
        {
            var client = new ChatGuardClient(TestKey, UnreachableUrl, timeoutSeconds: 1f);
            ModerationOperation op = client.Moderate(new ModerationRequest("you idiot", "p1"));
            yield return op;

            Assert.That(op.IsDone, Is.True, "request did not complete");
            Assert.That(op.IsCancelled, Is.False);
            Assert.That(op.Result, Is.Not.Null);
            ModerationResult result = op.Result!;
            Assert.That(result.Source, Is.EqualTo(ResultSource.Local));
            Assert.That(result.Degraded, Is.True);
            Assert.That(result.Action, Is.EqualTo(ModerationAction.Hide));
        }

        [UnityTest]
        public IEnumerator Cancel_CompletesWithoutResultAndNeverInvokesCompleted()
        {
            var client = new ChatGuardClient(TestKey, UnreachableUrl, timeoutSeconds: 1f);
            int completedCalls = 0;
            ModerationOperation op = client.Moderate(new ModerationRequest("you idiot", "p1"), _ => completedCalls++);
            op.Completed += _ => completedCalls++;
            Assert.That(op.IsDone, Is.False, "a network request must not complete synchronously");

            op.Cancel();
            Assert.That(op.IsDone, Is.True);
            Assert.That(op.IsCancelled, Is.True);
            Assert.That(op.keepWaiting, Is.False);

            // Let the aborted UnityWebRequest report back so the client's Finish runs (it must only dispose).
            for (int i = 0; i < 5; i++)
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
        public IEnumerator UnreachableServer_AwaitDeliversLocalFilterResult()
        {
            var client = new ChatGuardClient(TestKey, UnreachableUrl, timeoutSeconds: 1f);
            ModerationOperation op = client.Moderate(new ModerationRequest("you idiot", "p1"));
            Assert.That(op.IsDone, Is.False, "a network request must not complete synchronously");

            ModerationResult? awaited = null;
            Exception? error = null;
            AwaitInto(op, r => awaited = r, ex => error = ex);
            Assert.That(awaited, Is.Null, "the continuation must not run before the request finished");

            // Connection refused plus the 1 s request timeout bounds this; poll for a few seconds at most.
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
            Assert.That(awaited.Action, Is.EqualTo(ModerationAction.Hide));
        }

        /// <summary>Awaits the operation and hands the outcome to a callback; catches everything so nothing escapes the async void.</summary>
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
    }
}
