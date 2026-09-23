# Chat Guard for Unity

Chat Guard checks every chat message in about 300 ms, before other players see it. You tune
thresholds and rules live from the dashboard, and the next message uses them, with no new build. It
works with any multiplayer setup; [Where to call it](#where-to-call-it) shows how. Stores, consoles
and laws expect games with chat to moderate it, and Chat Guard takes that work off your studio.

For each message your game gets back an `action` (allow, flag, hide or block), six calibrated scores
(insult, threat, hate, sexual, spam and real-money trading), a severity from 0 to 3 and who the
message targets. The package works on Unity 2021.3 LTS and newer, uses only `UnityWebRequest` and has
no third-party dependencies.

## Install

Package Manager → **Add package from git URL** (or from disk while developing):

```
https://github.com/chatguard-dev/chat-guard-unity.git
```

## Where the key lives (read this first)

A `cg_live_` server key in a client build is a leaked key. Building a player with a `cg_live_` key in a
`ChatGuardConfig` asset under Resources fails on purpose (Dedicated Server builds excepted), and a server
key that reaches a player at runtime logs a warning. Three options, in order of preference:

- **A server you run** (a dedicated server on Netcode for GameObjects, Mirror, FishNet or Photon
  Fusion, or a backend such as Nakama, Colyseus or your own): the server calls Chat Guard with a
  `cg_live_` key before it passes a message on. Unity servers use this package; other servers send
  one HTTP request per message (see [Where to call it](#where-to-call-it)).
- **No game server of your own** (Photon PUN, Photon Chat, peer-to-peer, or a match hosted on a
  player's machine): run the example relay in `Examples~/server-relay` and have your game send
  messages to it. The relay is a small ASP.NET Core service that you host. It holds the key, so the
  key never reaches players' devices. Replace its shared-secret check with your own player
  authentication before you ship.
- **Publishable key in the client** (`cg_pub_`, analytics-SDK style): create it under Project →
  API keys → kind "publishable". It can only call `POST /v1/moderate` and `GET /v1/quota`, it
  counts toward your quota like a server key (only messages the model checks count, see the
  dashboard's Overview page), `author.id` is required, and each key has a lower rate limit plus a
  per-player bucket (2 requests/s, burst 10 by default), so one modified client cannot drain your
  budget. Caveats: results computed on a player's device are **advisory**, since a modified client
  can ignore or skip them, and anyone can extract the key and send their own messages. Prefer a
  server or the relay when you have one.

In the Editor and in tests, use a `cg_test_` key (not metered; 1,000 requests a day per
organization, shared with the dashboard's test panel). Test keys are for the Editor and development
builds: a release build that holds one in a `ChatGuardConfig` fails. A WebGL build runs in a
browser, where only a `cg_pub_` key works, even for testing (see [WebGL builds](#webgl-builds)).

The `ChatGuardConfig` asset has an empty key and an empty base URL by default (local filter only
until both are filled in). Never commit a config asset that contains a live key; publishable and
test keys are safe to commit only if you accept that they are public.

## 5-minute integration

1. Create a config: **Assets → Create → Chat Guard → Config**. Set `baseUrl` and a key: `cg_pub_` in
   a client build (`cg_test_` only in the Editor and development builds), `cg_live_` only on a server
   (see [Where the key lives](#where-the-key-lives-read-this-first)). `offlineBehavior` decides what
   happens [if Chat Guard can't be reached](#if-chat-guard-cant-be-reached); `LocalFilter` is the
   default. No asset at all is fine too, see [Configure from code](#configure-from-code-no-asset-needed).
2. Save it as **`Assets/Resources/ChatGuardConfig.asset`** so the static API finds it by itself, then
   moderate before delivery:

```csharp
using ChatGuard.Core;
using ChatGuard.Unity;

public sealed class ChatServer : MonoBehaviour
{
    // Called on the server when a player submits a line.
    public void OnPlayerMessage(string playerId, string text)
    {
        ChatGuardSdk.Moderate(text, playerId, result =>
        {
            if (result.ShouldDeliver)          // allow or flag
                BroadcastChat(playerId, text, flagged: result.Action == ModerationAction.Flag);
            else if (result.Action == ModerationAction.Hide)
                SendToSenderOnly(playerId, text);   // shadow-hide
            else
                WarnPlayer(playerId, result);       // block

            if (result.Degraded)               // local filter answered; consider a lighter policy
                Debug.Log($"Chat Guard degraded: {result.DegradedReason}");
        });
    }
}
```

   The callback runs on the main thread, so it can touch UI and networking directly. If there is no
   config asset in `Resources`, the SDK logs one warning and answers with the local filter only; in
   that case call `ChatGuardSdk.Configure(new ChatGuardSettings { ... })` (or `Configure(config)` /
   `Configure(apiKey, baseUrl)`) once at startup, for example from a bootstrap scene. The
   **Chat Guard Hook** component does this for you while its `configureStaticApi` box is ticked.

3. Prefer coroutines? `Moderate` returns a `ModerationOperation` that you can yield, poll
   (`IsDone`) or cancel:

```csharp
private IEnumerator SendChat(string playerId, string text)
{
    ModerationOperation op = ChatGuardSdk.Moderate(text, playerId);
    yield return op;                       // op.Cancel() aborts the request; Result then stays null
    if (op.Result != null && op.Result.ShouldDeliver)
        BroadcastChat(playerId, text);
}
```

   `StartCoroutine(ChatGuardSdk.ModerateCoroutine(text, playerId, result => ...))` is the same
   thing packaged as a coroutine. Prefer `await`? The same operation is awaitable, see
   [Async/await without Tasks](#asyncawait-without-tasks) below.

4. Need several clients (one per server or session) or full control over the settings? Use the
   instance API; `ChatGuardSdk` is only a thin wrapper around it:

```csharp
private ChatGuardClient client;

private void Awake() => client = new ChatGuardClient(config);   // or new ChatGuardClient(settings) / (apiKey, baseUrl)

public void OnPlayerMessage(string playerId, string text)
{
    client.Moderate(new ModerationRequest(text, playerId)
    {
        channelType = "team",   // global | team | dm | guild
        language = "en",
    }, result => { /* same as above */ });
}
```

   `client.ModerateCoroutine(request, onCompleted)` exists too.

5. Or use the no-code component **Chat Guard Hook** (`ChatGuardUnityHook`): assign the config,
   call `Moderate(message, authorId)` from a UnityEvent, and wire `onDeliver` / `onSuppress` /
   `onModerated`. In-flight requests are cancelled when the component is destroyed.
6. **Window → Chat Guard → Tester** lets you paste a message and see the verdicts with the
   config's (test) key.
7. Import the **Basic Chat** sample (Package Manager → Samples) for a runnable chat UI with three
   threshold sliders and a verdict log.

## Configure from code (no asset needed)

The Resources asset is optional. `ChatGuardSettings` is a plain C# object with the same options and
defaults as the asset, so a bootstrap script can configure the SDK statically:

```csharp
using ChatGuard.Unity;

ChatGuardSdk.Configure(new ChatGuardSettings
{
    ApiKey = "cg_pub_...",                  // cg_pub_ in a client build (cg_test_ only in development builds), never cg_live_
    BaseUrl = "https://api.chatguard.dev",
    DefaultLanguage = "en",
    ChannelType = "team",
    OfflineBehavior = OfflineBehavior.LocalFilter,
});
```

- **This replaces the Resources asset entirely.** Once `Configure` has been called, `ChatGuardSdk`
  never looks for `Assets/Resources/ChatGuardConfig.asset`. The asset remains a convenient zero-code
  option (fill it in the inspector, drop it in `Resources`, done); `config.ToSettings()` converts an
  asset when you want to start from one and tweak it in code.
- **Everything the asset offers is there:** `TimeoutSeconds`, `OfflineBehavior`,
  `LocalFilterWhenDegraded`, `DefaultLanguage`, `ChannelType`, `AgeRating` (default `"16+"`; null or
  empty sends none) and `Thresholds` (a `ChatGuard.Core.Scoring.Thresholds`; null means the built-in
  defaults; used only for local decisions). An empty `ApiKey` or `BaseUrl` means local filter only.
  `TimeoutSeconds` is rounded up to whole seconds (minimum 1 s) because `UnityWebRequest` counts
  whole seconds; a timeout that is not a positive finite number of seconds (or is above 600), or a
  `BaseUrl` that is not an absolute http/https URL, makes `Configure` throw `ArgumentException`.
  `Configure(apiKey, baseUrl)` and the positional `new ChatGuardClient(apiKey, baseUrl, ...)` use
  exactly these defaults.
- **`Configure` may be called again** at any time to swap settings, for example after the player
  picked a region or your server handed out a key; the next `Moderate` uses the new client and
  in-flight operations finish on the old one.
- **The instance API accepts the same object:** `new ChatGuardClient(settings)`. The settings are
  validated and copied, so editing the object afterwards does not affect the client, and
  `client.Settings` returns a copy of what it runs with (it includes the key, so do not log it
  verbatim).

## If Chat Guard can't be reached

Chat keeps working. If the package gets no usable answer, because there is no network, no answer
came within `timeoutSeconds` (2 s by default) or the API answered with an error, `offlineBehavior`
decides what your game gets:

- `LocalFilter` (the default): a built-in word filter answers on the device, with word lists for
  English, Russian, Serbian, Polish, Turkish, German, Spanish and Portuguese, the same lists the
  service falls back to. It is simpler than the model, so you might hide rather than block while it
  answers.
- `AllowAll`: every message is allowed.
- `BlockAll`: every message is blocked.

These results carry `Source == Local`, `Degraded == true`, details in `Error`, and a `DegradedReason`:
`offline` (no server configured, a network error, or the request budget expired), `upstream` (a
non-200 answer or an unreadable body) or `upstream_rate_limit` (HTTP 429).

The service has the same safety net. When its model can't answer (`timeout`, `upstream` or
`upstream_rate_limit`), or a Free plan is over its allowance (`quota`), it answers with its own word
filter. On Free and with test keys, `upstream_rate_limit` can also mean that a shared per-minute
allowance ran out. Those results have `Degraded == true` but keep `Source == Server`. With
`LocalFilter` and `localFilterWhenDegraded` (on by default), the package also runs its own filter on
the message and decides the action with your local thresholds (your `thresholds` override, or the
built-in defaults).

## Async/await without Tasks

`ModerationOperation` has its own awaiter, so you can `await` it directly, with no `Task` in sight:

```csharp
public async void OnPlayerMessage(string playerId, string text)
{
    ModerationResult result = await ChatGuardSdk.Moderate(text, playerId);
    if (result.ShouldDeliver)
        BroadcastChat(playerId, text, flagged: result.Action == ModerationAction.Flag);
    else
        WarnPlayer(playerId, result);
}
```

- **Where it works.** In `async void` methods (as above, the usual shape for Unity event handlers),
  in `async Awaitable` methods on Unity 2023.1+, and in UniTask code (`async UniTask` /
  `async UniTaskVoid`). Anything that can `await` an object with a `GetAwaiter()` method can await
  the operation; `ModerationOperation.GetAwaiter()` returns a small struct that implements
  `INotifyCompletion` from `System.Runtime.CompilerServices`, which is not `System.Threading.Tasks`.
- **No `Task` is created.** The SDK still exposes no `Task` anywhere; `await` only registers a
  continuation on the operation. Convert to a `Task`/`UniTask` yourself if a caller needs one, but
  keep the WebGL caveats below in mind.
- **The continuation runs on the main thread**, straight from the `UnityWebRequest` completion
  callback (no `SynchronizationContext` hop, no thread pool), so the code after `await` can touch UI
  and networking. When the operation already finished (offline fallback), `await` does not suspend:
  the code after it runs inline, right after `Moderate` returns and before your async method returns
  to its caller. Only the `onCompleted` callback form runs user code before `Moderate` itself returns.
- **Cancellation throws.** Awaiting an operation that was ended with `Cancel()` or by its
  cancellation token (see [Cancellation tokens](#cancellation-tokens)) throws
  `OperationCanceledException`; catch it where you cancel, for example when a chat window is closed
  while a message is in flight. In an `async void` method an uncaught exception is reported through
  Unity's synchronization context, i.e. logged like any other unhandled exception.
- **WebGL works** because nothing uses threads or timers: the awaiter is plain callbacks on the
  main thread. Your own code around the `await` should stay away from `Task.Run` / `Task.Delay` on
  that platform for the reasons in [Why there are no Tasks](#why-there-are-no-tasks).

### Cancellation tokens

Every `Moderate` has an overload that takes a `CancellationToken`, so a message in flight stops
together with whatever owns it:

```csharp
public sealed class ChatWindow : MonoBehaviour
{
    public async void Send(string playerId, string text)
    {
        try
        {
            ModerationResult result = await ChatGuardSdk.Moderate(text, playerId, destroyCancellationToken);
            if (result.ShouldDeliver)
                AppendLine(playerId, text);
        }
        catch (OperationCanceledException)
        {
            // The window was destroyed while the message was in flight: nothing to show.
        }
    }
}
```

- **The overloads.** `ChatGuardSdk.Moderate(text, playerId, token)` and
  `ChatGuardSdk.Moderate(text, playerId, onCompleted, token)`, the same two with a
  `ModerationRequest`, and `client.Moderate(request, token)` / `client.Moderate(request, onCompleted,
  token)` on the instance API. `destroyCancellationToken` exists from Unity 2022.2; on 2021.3 cancel
  your own `CancellationTokenSource` in `OnDestroy`, and with UniTask use
  `this.GetCancellationTokenOnDestroy()`.
- **Cancelling the token is the same as `op.Cancel()`.** The request is aborted, `onCompleted` and
  `Completed` are not invoked, and `await` throws `OperationCanceledException` whose
  `CancellationToken` is your token. A token that is already cancelled gives back a cancelled
  operation without sending anything; one cancelled after the result arrived changes nothing.
- **One token can serve every message.** The SDK stops listening to the token when the operation
  finishes, so a long-lived token does not keep finished operations in memory.
- **Cancel on the main thread where you can** (`destroyCancellationToken` is cancelled there). A
  token cancelled on another thread still aborts the request on the main thread, when Unity next
  runs posted work (normally the next frame), so the code after `await` stays on the main thread.
- **Timeouts are `TimeoutSeconds`, not `CancelAfter`.** `CancellationTokenSource.CancelAfter` and
  the `CancellationTokenSource(TimeSpan)` constructor need a timer thread, which WebGL does not
  have, so they do not fire there.
- **With UniTask**, pass the token and await as usual:
  `await ChatGuardSdk.Moderate(text, playerId, this.GetCancellationTokenOnDestroy())`. Avoid
  `op.ToUniTask()` and `op.WithCancellation(token)`: UniTask adds those to every coroutine object,
  and for the operation they return a `UniTask` without the result, check it only once per frame,
  and a cancelled token does not abort the request. Need a `UniTask<ModerationResult>`, for
  `UniTask.WhenAll` for example? Wrap it:
  `async UniTask<ModerationResult> ModerateAsync(ModerationOperation op) => await op;`.

## Where to call it

Call Chat Guard at the point every message passes before other players see it: on the server when
you have one. On a Unity server, that is the RPC that receives the message.

**Netcode for GameObjects** — the server owns chat:

```csharp
[ServerRpc(RequireOwnership = false)]
private void SubmitChatServerRpc(string text, ServerRpcParams rpc = default)
{
    string playerId = rpc.Receive.SenderClientId.ToString();
    Moderate(playerId, text, delivered => DeliverChatClientRpc(playerId, delivered));
}
[ClientRpc] private void DeliverChatClientRpc(string playerId, string text) { /* show */ }
```

**Mirror** — same pattern with `[Command]` on the server and `[ClientRpc]` to fan out:

```csharp
[Command] private void CmdSendChat(string text) => Moderate(connectionToClient.connectionId.ToString(), text, t => RpcReceiveChat(t));
[ClientRpc] private void RpcReceiveChat(string text) { /* show */ }
```

**FishNet** — same pattern with `[ServerRpc]` and `[ObserversRpc]`. `RequireOwnership = false` lets
any client send, and FishNet fills in the trailing `NetworkConnection` with the sender:

```csharp
[ServerRpc(RequireOwnership = false)]
private void SubmitChat(string text, NetworkConnection conn = null) => Moderate(conn.ClientId.ToString(), text, t => DeliverChat(t));
[ObserversRpc] private void DeliverChat(string text) { /* show */ }
```

**Photon Fusion** — moderate on the state authority (host/server) before replicating:

```csharp
[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
private void RPC_SubmitChat(string text, RpcInfo info = default) => Moderate(info.Source.PlayerId.ToString(), text, t => RPC_DeliverChat(t));
[Rpc(RpcSources.StateAuthority, RpcTargets.All)] private void RPC_DeliverChat(string text) { /* show */ }
```

Each `Moderate` above is your own helper around `ChatGuardClient.Moderate` (or `ChatGuardSdk.Moderate`),
for example `ChatGuardSdk.Moderate(text, playerId, r => { if (r.ShouldDeliver) deliver(text); })`.
The snippets pass per-connection ids to stay short. In your game, pass a stable id you can map to the
player, never a name or an email: player export and erasure look messages up by that id.

**Nakama** — call the HTTP API from a before hook on `ChannelMessageSend`
(`initializer.registerRtBefore` in TypeScript, `RegisterBeforeRt` in Go). Parse
`envelope.channelMessageSend.content`, the JSON your client sent, and send its text to
`POST /v1/moderate` with `nk.httpRequest` and `ctx.userId` as `author.id`. Keep the `cg_live_` key
in `runtime.env` and read it from `ctx.env`. Return the envelope to deliver the message. To drop it,
throw an error (in Go, return one): the sender's send fails with your error, and they stay connected.
Don't drop a message by returning `null`, because Nakama then closes the sender's socket.
`nk.httpRequest` throws if the call fails, and that drops the message too unless you catch it.
Request and response fields are in the [API reference](Documentation~/api-reference.md).

**Colyseus** — call the HTTP API in the room's handler for your chat message (`this.onMessage`, or
its entry in the `messages` map), before you broadcast anything. Send the text to
`POST /v1/moderate` with your player's id as `author.id`. Then `this.broadcast` the message on
`allow` or `flag`, `client.send` it back to its sender alone on `hide`, and tell the sender on
`block`. Request and response fields are in the [API reference](Documentation~/api-reference.md).

**Any other server** — send one `POST /v1/moderate` per message and act on its `action`. Every field
is in the [API reference](Documentation~/api-reference.md), and the dashboard has examples in C#,
TypeScript, Python, Go and Java.

**Photon PUN 2 and Photon Chat** — there is no server of yours between players, so the sending game
asks for the check. Before `PhotonNetwork.RaiseEvent` or `ChatClient.PublishMessage`, send the text
to the relay (`Examples~/server-relay`, the small service you run) and publish the message only
when the answer's `action` is `allow` or `flag`. Photon Chat can also ask your own web service
first: publish (or send private messages) with `forwardAsWebhook: true` and set `PathPublishMessage`
in your Chat app's webhook settings; the service calls Chat Guard and answers with a non-zero
`ResultCode` to cancel the message. The example relay doesn't handle that webhook, so that service
is yours to write. Both ways keep your key off players' devices, but a modified client can skip the
check, so treat the result as advisory.

## WebGL builds

A WebGL build calls the API from a web page, so the browser's CORS rules apply. The API allows
`POST /v1/moderate` and `GET /v1/quota` from any origin, so there is nothing to set up, but two
things differ from other platforms:

- **A `cg_pub_` key is required.** Anyone can read the key out of a web build. When a request
  comes from a browser, the API refuses server (`cg_live_`) and test (`cg_test_`) keys with HTTP 403.
  The client then answers locally with `DegradedReason.Upstream`, and `Error` explains why. The same
  applies to Build and Run for local testing. Play mode in the Editor is not a browser, so a
  `cg_test_` key still works there.
- **The first message in each 2 h window pays one extra round trip.** The `Authorization` and
  `Content-Type: application/json` headers make the browser send an `OPTIONS` preflight before the
  first `POST`. The browser caches the answer for 2 hours in Chrome, Edge and Firefox, and for
  10 minutes in Safari. Until the cache expires, messages go straight through. If your players are
  far from the API, leave room for the extra round trip in `timeoutSeconds`.

A same-origin relay (`Examples~/server-relay` served from your game's domain) avoids the preflight
and keeps a `cg_live_` key off the page. It is the better fit when you already run a backend for the
web build.

## Why there are no Tasks

WebGL has no threads. Anything that relies on the thread pool or on timers (`Task.Run`,
`Task.Delay`, `ConfigureAwait(false)`, `CancellationTokenSource.CancelAfter`) either throws or never
completes there. The SDK therefore exposes no `Task`s at all: `Moderate` returns a
`ModerationOperation` that you can yield (the same shape as `UnityWebRequestAsyncOperation`),
`await` (it has its own awaiter, see [Async/await without Tasks](#asyncawait-without-tasks)) or drive
with callbacks, `ModerateCoroutine` wraps it for `StartCoroutine`, and everything completes on the
main thread. That works identically on every platform, WebGL included. Not having `Task`s does not
mean giving up `await`; it only means the SDK never touches the thread pool or timers. Cancellation
tokens are fine: `CancellationToken` lives in `System.Threading`, and cancelling one only runs
callbacks, so `Moderate` accepts one (see [Cancellation tokens](#cancellation-tokens)).

## Thresholds and rules

Thresholds and rules live in the dashboard, per project, not in your game. Save a change and the
next message uses it: no new build, no store review, no waiting for players to update. Thresholds
are on every plan; block, allow and context rules come with Indie and up. Probabilities are
calibrated: 0.9 means "nine times out of ten this is an insult".

The config's `overrideThresholds` (or `ChatGuardSettings.Thresholds` from code) applies only to
decisions the package makes itself, offline or degraded (see
[If Chat Guard can't be reached](#if-chat-guard-cant-be-reached)).

## Feedback

`ModerationResult.Id` is the verdict id. Your server can report false positives/negatives with
`POST /v1/feedback { "verdict_id": "...", "kind": "false_positive" }` (see `Documentation~/api-reference.md`).

## Help and support

- **Questions and bugs:** the `#help` forum on the Chat Guard Discord, https://chatguard.dev/discord.
  Include your Unity version, the package version (`package.json`) and the platform.
- **Feature ideas:** `#feature-requests` on the same server; upvote an existing post instead of repeating it.
- **Account, billing or player-data requests:** support@chatguard.dev, so we can look at your organization privately.
- **Service status:** `#status` on Discord and https://github.com/chatguard-dev/status.

Never post a server key (`cg_live_…` or `cg_test_…`) in public; the Discord server blocks messages
that contain one. If a key leaks, revoke it on the dashboard's Keys page.

## About `Runtime/Core`

`Runtime/Core` mirrors the filtering and scoring library used by the Chat Guard service (word
lists, normalization, thresholds), so a message the package answers on the device gets the same
result the service's own word filter would give it with the same settings. It is refreshed from the
service's source tree on every package release; do not edit it in place. The EditMode tests run the
same `local-filter.json` vectors as the service's own tests.
