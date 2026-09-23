#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using ChatGuard.Core;
using ChatGuard.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ChatGuard.Tests
{
    /// <summary>
    /// Runs real <see cref="ChatGuardClient.Moderate(ModerationRequest, Action{ModerationResult}, CancellationToken)"/>
    /// calls against a local HTTP server with canned bodies and checks every result field (latency aside) against the
    /// string path: TryParseResponse of the body text decoded as DownloadHandler.text decodes it, or the local fallback
    /// with the same reason and error. It also checks the uploaded bytes against
    /// Encoding.UTF8.GetBytes(BuildRequestJson(request)) and that completion runs on the main thread. HttpListener is
    /// not available on WebGL.
    /// </summary>
    [UnityPlatform(exclude = new[] { RuntimePlatform.WebGLPlayer })]
    public class ServerClientPlayModeTests
    {
        private const string Sample = "{\"id\":\"0199b2c4-7d1e-7a3b-9c4d-5e6f7a8b9c0d\",\"action\":\"hide\",\"severity\":1.815,\"verdicts\":{\"insult\":{\"p\":0.91},\"threat\":{\"p\":0.03},\"hate\":{\"p\":0.05},\"sexual\":{\"p\":0.01},\"spam\":{\"p\":0.02},\"trading\":{\"p\":0.0}},\"target\":{\"choice\":\"other_user\",\"confidence\":0.84},\"degraded\":false,\"degraded_reason\":null,\"cached\":false,\"quota\":{\"used\":12345,\"limit\":50000,\"window_ends_at\":\"2026-09-23T00:00:00+00:00\"},\"model\":\"jev-1.13.0\",\"latency_ms\":212}";
        private const string Json = "application/json; charset=utf-8";
        private static readonly string TestKey = "cg_test_" + new string('x', 32);

        /// <summary>Canned answers by route name (the first path segment): status, Content-Type (null: none) and body.</summary>
        private static readonly Dictionary<string, (int Status, string? ContentType, byte[] Body)> Routes = new Dictionary<string, (int, string?, byte[])>
        {
            ["normal"] = (200, Json, Utf8(Sample)),
            ["nocharset"] = (200, "application/json", Utf8(Sample.Replace("\"cached\":false", "\"cached\":true"))),
            ["degraded"] = (200, Json, Utf8(Sample.Replace("\"degraded\":false,\"degraded_reason\":null", "\"degraded\":true,\"degraded_reason\":\"quota\"").Replace("\"target\":{\"choice\":\"other_user\",\"confidence\":0.84}", "\"target\":null"))),
            ["nulltarget"] = (200, Json, Utf8(Sample.Replace("\"target\":{\"choice\":\"other_user\",\"confidence\":0.84}", "\"target\":null"))),
            ["pretty"] = (200, Json, Utf8("{\n  \"action\": \"flag\",\n  \"severity\": 0.4,\n  \"verdicts\": { \"spam\": { \"p\": 0.6 } },\n  \"extra\": [1, {\"a\": null}],\n  \"model\": \"jev-1.13.0\",\n  \"id\": \"abc\"\n}\n")),
            ["escaped"] = (200, Json, Utf8(Sample.Replace("jev-1.13.0", "jev\\/1.13.0").Replace("\"id\":\"0199", "\"id\":\"\\u0030199"))),
            ["rawutf8"] = (200, Json, Utf8(Sample.Replace("jev-1.13.0", "j\u00e9v-1.13.0"))),
            ["latin1"] = (200, "application/json; charset=iso-8859-1", Utf8(Sample)),
            ["utf16"] = (200, "application/json; charset=utf-16", Utf8(Sample)),
            ["truncated"] = (200, Json, Utf8("{\"action\":\"hide\",")),
            ["deep"] = (200, Json, Utf8("{\"action\":\"hide\",\"deep\":" + new string('[', 100000) + new string(']', 100000) + "}")),
            ["empty"] = (200, Json, Array.Empty<byte>()),
            ["garbage"] = (200, Json, Utf8("not json")),
            ["r429"] = (429, "text/plain", Utf8("rate limited")),
            ["r500"] = (500, Json, Utf8("{\"error\":\"boom\"}")),
        };

        private readonly object _gate = new object();
        private readonly Dictionary<string, byte[]> _received = new Dictionary<string, byte[]>();
        private HttpListener? _listener;
        private string _baseUrl = string.Empty;

        private static byte[] Utf8(string s)
        {
            return Encoding.UTF8.GetBytes(s);
        }

        [OneTimeSetUp]
        public void StartServer()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            _baseUrl = "http://127.0.0.1:" + port;
            _listener = new HttpListener();
            _listener.Prefixes.Add(_baseUrl + "/");
            _listener.Start();
            _listener.BeginGetContext(Serve, _listener);
        }

        [OneTimeTearDown]
        public void StopServer()
        {
            _listener?.Close();
            _listener = null;
        }

        /// <summary>Runs on a thread-pool thread: answers one request from <see cref="Routes"/> and keeps its body.</summary>
        private void Serve(IAsyncResult asyncResult)
        {
            var listener = (HttpListener)asyncResult.AsyncState;
            HttpListenerContext context;
            try
            {
                context = listener.EndGetContext(asyncResult);
                listener.BeginGetContext(Serve, listener);
            }
            catch (Exception)
            {
                return; // the listener was closed
            }

            try
            {
                string route = context.Request.Url.AbsolutePath.Split('/')[1];
                var body = new MemoryStream();
                context.Request.InputStream.CopyTo(body);
                lock (_gate)
                {
                    _received[route] = body.ToArray();
                }

                (int status, string? contentType, byte[] payload) = Routes[route];
                context.Response.StatusCode = status;
                if (contentType != null)
                {
                    context.Response.ContentType = contentType;
                }

                context.Response.ContentLength64 = payload.Length;
                context.Response.OutputStream.Write(payload, 0, payload.Length);
                context.Response.OutputStream.Close();
            }
            catch (Exception)
            {
                context.Response.Abort();
            }
        }

        private ChatGuardClient Client(string route, bool localWhenDegraded = false)
        {
            return new ChatGuardClient(new ChatGuardSettings { ApiKey = TestKey, BaseUrl = _baseUrl + "/" + route, TimeoutSeconds = 5f, LocalFilterWhenDegraded = localWhenDegraded });
        }

        private static ModerationRequest Request(string route)
        {
            return new ModerationRequest("you idiot \u00e9 " + route, "p1")
            {
                accountAgeDays = 3,
                thread = new List<ThreadEntry> { new ThreadEntry("p2", "hi \"there\"\n"), new ThreadEntry("p3", "\ud83c\udfae gg") },
                requestId = "req-" + route,
            };
        }

        /// <summary>
        /// The string path's result for a route: the body decoded like DownloadHandler.text (the Content-Type charset,
        /// else UTF-8) and parsed by TryParseResponse, or the local fallback for HTTP errors and unparseable bodies.
        /// </summary>
        private static ModerationResult Expected(string route, ModerationRequest request)
        {
            (int status, string? contentType, byte[] body) = Routes[route];
            Encoding encoding = Encoding.UTF8;
            int charset = contentType?.IndexOf("charset=", StringComparison.OrdinalIgnoreCase) ?? -1;
            if (charset >= 0)
            {
                encoding = Encoding.GetEncoding(contentType!.Substring(charset + "charset=".Length));
            }

            string text = body.Length == 0 ? string.Empty : encoding.GetString(body);
            if (status != 200)
            {
                return LocalFallback(request, status == 429 ? DegradedReason.UpstreamRateLimit : DegradedReason.Upstream, "HTTP " + status + ": " + (text.Length > 200 ? text.Substring(0, 200) : text));
            }

            return ChatGuardClient.TryParseResponse(text, 0) ?? LocalFallback(request, DegradedReason.Upstream, "unparseable response");
        }

        private static ModerationResult LocalFallback(ModerationRequest request, DegradedReason reason, string error)
        {
            ModerationResult local = new ChatGuardClient(new ChatGuardSettings { LocalFilterWhenDegraded = false }).Moderate(request).Result!;
            return new ModerationResult(local.Action, local.Severity, local.Verdicts, null, true, reason, false, local.Model, 0, null, 0, 0, ResultSource.Local, error);
        }

        private static IEnumerator WaitFor(ModerationOperation op)
        {
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!op.IsDone && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(op.IsDone, Is.True, "the request did not complete within 10 s");
        }

        private static void AssertSameResult(ModerationResult expected, ModerationResult actual, string context)
        {
            Assert.That(actual.Action, Is.EqualTo(expected.Action), "Action: " + context);
            Assert.That(BitConverter.DoubleToInt64Bits(actual.Severity), Is.EqualTo(BitConverter.DoubleToInt64Bits(expected.Severity)), "Severity: " + context);
            foreach (VerdictCategory category in VerdictCategories.All)
            {
                Assert.That(BitConverter.DoubleToInt64Bits(actual.Verdicts[category]), Is.EqualTo(BitConverter.DoubleToInt64Bits(expected.Verdicts[category])), category + ": " + context);
            }

            Assert.That(actual.Target == null, Is.EqualTo(expected.Target == null), "Target: " + context);
            if (expected.Target != null)
            {
                Assert.That(actual.Target!.Choice, Is.EqualTo(expected.Target.Choice), "Target.Choice: " + context);
                Assert.That(BitConverter.DoubleToInt64Bits(actual.Target.Confidence), Is.EqualTo(BitConverter.DoubleToInt64Bits(expected.Target.Confidence)), "Target.Confidence: " + context);
            }

            Assert.That(actual.Degraded, Is.EqualTo(expected.Degraded), "Degraded: " + context);
            Assert.That(actual.DegradedReason, Is.EqualTo(expected.DegradedReason), "DegradedReason: " + context);
            Assert.That(actual.Cached, Is.EqualTo(expected.Cached), "Cached: " + context);
            Assert.That(actual.Model, Is.EqualTo(expected.Model), "Model: " + context);
            Assert.That(actual.Id, Is.EqualTo(expected.Id), "Id: " + context);
            Assert.That(actual.QuotaUsed, Is.EqualTo(expected.QuotaUsed), "QuotaUsed: " + context);
            Assert.That(actual.QuotaLimit, Is.EqualTo(expected.QuotaLimit), "QuotaLimit: " + context);
            Assert.That(actual.Source, Is.EqualTo(expected.Source), "Source: " + context);
            Assert.That(actual.Error, Is.EqualTo(expected.Error), "Error: " + context);
            if (expected.Source == ResultSource.Local)
            {
                Assert.That(actual.LatencyMs, Is.EqualTo(0), "LatencyMs: " + context);
            }
            else
            {
                Assert.That(actual.LatencyMs, Is.GreaterThanOrEqualTo(0), "LatencyMs: " + context);
            }
        }

        [UnityTest]
        public IEnumerator EveryRoute_MatchesTheStringPath()
        {
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            foreach (string route in Routes.Keys)
            {
                ChatGuardClient client = Client(route);
                ModerationRequest request = Request(route);
                int completedOn = -1;
                ModerationOperation op = client.Moderate(request, _ => completedOn = Thread.CurrentThread.ManagedThreadId);
                // No IsDone check here: on a single-core device a local round trip can finish before Moderate returns.
                yield return WaitFor(op);

                ModerationResult actual = op.Result!;
                if (actual.Error != null && actual.Error.IndexOf("Insecure connection", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Assert.Ignore("This Unity version blocks plain HTTP to the local test server: " + actual.Error);
                }

                AssertSameResult(Expected(route, request), actual, route);
                Assert.That(completedOn, Is.EqualTo(mainThread), route + ": completion must run on the main thread");
                byte[]? received;
                lock (_gate)
                {
                    _received.TryGetValue(route, out received);
                }

                Assert.That(received, Is.EqualTo(Encoding.UTF8.GetBytes(client.BuildRequestJson(request))), route + ": uploaded body");
            }
        }

        /// <summary>
        /// A 200 body cut short, or nested far too deep for any real response, is reported as an unparseable response
        /// with the local fallback, not with a parser exception's message (and the deep one does not overflow the stack).
        /// </summary>
        [UnityTest]
        public IEnumerator MalformedBodies_AreReportedAsUnparseable()
        {
            foreach (string route in new[] { "truncated", "deep" })
            {
                ModerationOperation op = Client(route).Moderate(Request(route));
                yield return WaitFor(op);
                ModerationResult result = op.Result!;
                if (result.Error != null && result.Error.IndexOf("Insecure connection", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Assert.Ignore("This Unity version blocks plain HTTP to the local test server: " + result.Error);
                }

                Assert.That(result.Source, Is.EqualTo(ResultSource.Local), route);
                Assert.That(result.Degraded, Is.True, route);
                Assert.That(result.DegradedReason, Is.EqualTo(DegradedReason.Upstream), route);
                Assert.That(result.Error, Is.EqualTo("unparseable response"), route);
            }
        }

        /// <summary>
        /// Guards the allocation saving, which no result field shows: the byte reader reuses the last model string, the
        /// string path (MiniJson) creates a new one per response. So two plain responses share one Model instance, and two
        /// responses left to the string path (here because of a non-UTF-8 charset) do not.
        /// </summary>
        [UnityTest]
        public IEnumerator PlainServerBody_IsReadFromTheResponseBytes()
        {
            var models = new string[4];
            string[] routes = { "normal", "normal", "latin1", "latin1" };
            for (int i = 0; i < routes.Length; i++)
            {
                ModerationOperation op = Client(routes[i]).Moderate(Request(routes[i]));
                yield return WaitFor(op);
                Assert.That(op.Result!.Source, Is.EqualTo(ResultSource.Server), op.Result.Error);
                models[i] = op.Result.Model;
            }

            Assert.That(ReferenceEquals(models[0], models[1]), Is.True, "plain bodies are read by the byte reader");
            Assert.That(ReferenceEquals(models[2], models[3]), Is.False, "a latin-1 body is decoded by DownloadHandler.text");
            Assert.That(models[2], Is.EqualTo(models[0]));
        }

        [UnityTest]
        public IEnumerator DegradedResponse_IsMergedWithTheLocalFilter()
        {
            ModerationRequest request = Request("degraded");
            ModerationOperation op = Client("degraded", localWhenDegraded: true).Moderate(request);
            yield return WaitFor(op);

            ModerationResult server = Expected("degraded", request);
            ModerationResult merged = op.Result!;
            Assert.That(merged.Source, Is.EqualTo(ResultSource.Server));
            Assert.That(merged.Degraded, Is.True);
            Assert.That(merged.DegradedReason, Is.EqualTo(DegradedReason.Quota));
            Assert.That(merged.Error, Is.Null);
            Assert.That(merged.Model, Is.EqualTo(server.Model));
            Assert.That(merged.Id, Is.EqualTo(server.Id));
            Assert.That(merged.QuotaUsed, Is.EqualTo(server.QuotaUsed));
            Assert.That(merged.QuotaLimit, Is.EqualTo(server.QuotaLimit));
            Assert.That(merged.Target, Is.Null);
            foreach (VerdictCategory category in VerdictCategories.All)
            {
                Assert.That(merged.Verdicts[category], Is.GreaterThanOrEqualTo(server.Verdicts[category]), category.ToString());
            }

            Assert.That(merged.Verdicts.Insult, Is.EqualTo(1), "the local filter flags \"idiot\"");
        }

        [UnityTest]
        public IEnumerator ConcurrentRequests_EachGetTheirOwnResult()
        {
            string[] routes = { "normal", "nulltarget", "escaped", "r429" };
            var ops = new ModerationOperation[routes.Length];
            var requests = new ModerationRequest[routes.Length];
            for (int i = 0; i < routes.Length; i++)
            {
                requests[i] = Request(routes[i]);
                ops[i] = Client(routes[i]).Moderate(requests[i]);
            }

            ModerationRequest cancelledRequest = Request("normal");
            ModerationOperation cancelled = Client("normal").Moderate(cancelledRequest);
            // As in EveryRoute: a local round trip can finish before Moderate returns (seen on a 1-core device), and
            // then there is nothing left to cancel. Completion only happens on the main thread, so an operation still in
            // flight here is still in flight at Cancel().
            bool cancelledInFlight = !cancelled.IsDone;
            int cancelledCallbacks = 0;
            cancelled.Completed += _ => cancelledCallbacks++;
            cancelled.Cancel();

            for (int i = 0; i < routes.Length; i++)
            {
                yield return WaitFor(ops[i]);
            }

            for (int i = 0; i < routes.Length; i++)
            {
                Assert.That(ops[i].Request, Is.SameAs(requests[i]));
                AssertSameResult(Expected(routes[i], requests[i]), ops[i].Result!, routes[i]);
            }

            for (int i = 0; i < 5; i++)
            {
                yield return null;
            }

            if (cancelledInFlight)
            {
                Assert.That(cancelled.IsCancelled, Is.True);
                Assert.That(cancelled.Result, Is.Null);
                Assert.That(cancelledCallbacks, Is.EqualTo(0));
            }
            else
            {
                Assert.That(cancelled.IsCancelled, Is.False, "Cancel after completion changes nothing");
                AssertSameResult(Expected("normal", cancelledRequest), cancelled.Result!, "normal (finished inside Moderate)");
                Assert.That(cancelledCallbacks, Is.EqualTo(1), "subscribing after completion invokes the handler once");
            }
        }
    }
}
