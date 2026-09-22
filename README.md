# Chat Guard for Unity

Send every chat message to Chat Guard before you deliver it and get back an `action`
(allow / flag / hide / block), six calibrated verdict probabilities, a severity and who the
message targets. Works on Unity 2021.3 LTS and newer, uses only `UnityWebRequest`, has no
third-party dependencies, and falls back to a built-in dictionary filter when offline.

## Install

Package Manager → **Add package from git URL** (or from disk while developing):

```
https://github.com/chatguard-dev/chat-guard-unity.git
```

## Where the key lives (read this first)

A `cg_live_` server key in a client build is a leaked key. Building a player with a `cg_live_` key in a
`ChatGuardConfig` asset under Resources fails on purpose (Dedicated Server builds excepted), and a server
key that reaches a player at runtime logs a warning. Three options, in order of preference:

- **Authoritative server / host** (Mirror, Netcode for GameObjects, Photon Fusion host mode,
  your own backend): the server calls Chat Guard with a `cg_live_` key and broadcasts the result.
- **No server** (peer-to-peer, client-hosted, Photon PUN/Chat): run the tiny relay in
  `Examples~/server-relay` and point clients at it. The relay holds the key.
- **Publishable key in the client** (`cg_pub_`, analytics-SDK style): create it under Project →
  API keys → kind "publishable". It can only call `POST /v1/moderate` and `GET /v1/quota`, it
  counts toward your quota like a server key (only model verdicts count, see the dashboard's Overview
  page), `author.id` is required, and each key has a lower
  rate limit plus a per-player bucket (2 requests/s, burst 10 by default), so one modified client
  cannot drain your budget. Caveats: results computed on a player's device are **advisory**, since
  a modified client can ignore or skip them, and anyone can extract the key and send their own
  messages; a server or relay should still moderate when one exists. `ChatGuardClient` logs a
  warning once if a `cg_live_` key is found in a player build.
- **Editor and tests**: use a `cg_test_` key (not metered, 1,000 requests/day).

The `ChatGuardConfig` asset has an empty key and an empty base URL by default (local filter only
until both are filled in). Never commit a config asset that contains a live key; publishable and
test keys are safe to commit only if you accept that they are public.

## 5-minute integration

1. Create a config: **Assets → Create → Chat Guard → Config**. Set `baseUrl` and the key (a
   `cg_pub_` or `cg_test_` key in client builds; `cg_live_` only on the server). Pick
   `offlineBehavior` (`LocalFilter` is the default). No asset at all is fine too, see
   [Configure from code](#configure-from-code-no-asset-needed).
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
   [Async/await without Tasks](#asyncawait-without-tasks) right below.

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
    ApiKey = "cg_pub_...",                  // cg_pub_ or cg_test_ in a client build, never cg_live_
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
- **Cancellation throws.** Awaiting an operation that was ended with `Cancel()` throws
  `OperationCanceledException`; catch it where you cancel, for example when a chat window is closed
  while a message is in flight. In an `async void` method an uncaught exception is reported through
  Unity's synchronization context, i.e. logged like any other unhandled exception.
- **WebGL works** because nothing uses threads or timers: the awaiter is plain callbacks on the
  main thread. Your own code around the `await` should stay away from `Task.Run` / `Task.Delay` on
  that platform for the reasons in [Why there are no Tasks](#why-there-are-no-tasks).

## Interception points per networking stack

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

**Photon Fusion** — moderate on the state authority (host/server) before replicating:

```csharp
[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
private void RPC_SubmitChat(string text, RpcInfo info = default) => Moderate(info.Source.PlayerId.ToString(), text, t => RPC_DeliverChat(t));
[Rpc(RpcSources.StateAuthority, RpcTargets.All)] private void RPC_DeliverChat(string text) { /* show */ }
```

**Photon PUN 2 / Photon Chat** — there is no trusted server. Route `PhotonNetwork.RaiseEvent` /
`ChatClient.PublishMessage` through the relay (`Examples~/server-relay`) and only publish the
text the relay returned as deliverable. If you must moderate on the sender's device, treat the
result as advisory (a modified client can skip it).

Each `Moderate` above is your own helper around `ChatGuardClient.Moderate` (or `ChatGuardSdk.Moderate`),
for example `ChatGuardSdk.Moderate(text, playerId, r => { if (r.ShouldDeliver) deliver(text); })`.

## Why there are no Tasks

WebGL has no threads. Anything built on `System.Threading.Tasks` that relies on the thread pool or
on timers (`Task.Run`, `Task.Delay`, `CancellationTokenSource.CancelAfter`, `ConfigureAwait(false)`)
either throws or never completes there. The SDK therefore exposes no `Task`s at all: `Moderate`
returns a `ModerationOperation` that you can yield (the same shape as `UnityWebRequestAsyncOperation`),
`await` (it has its own awaiter, see [Async/await without Tasks](#asyncawait-without-tasks)) or drive
with callbacks, `ModerateCoroutine` wraps it for `StartCoroutine`, and everything completes on the
main thread. That works identically on every platform, WebGL included. Not having `Task`s does not
mean giving up `await`; it only means the SDK never touches the thread pool or timers.

## Behaviour when the server cannot answer

`offlineBehavior` decides: `AllowAll`, `LocalFilter` (word lists for en, ru, sr, pl, tr, de, es,
pt, identical to the server's fallback), or `BlockAll`. Results answered on the device carry
`Source == Local`, `Degraded == true` and a `DegradedReason`: `offline` (no server configured, a
network error, or the request budget expired), `upstream` (a non-200 answer or an unreadable body) or
`upstream_rate_limit` (HTTP 429). `timeout` and `quota` come from the server's own `degraded_reason`;
those results keep `Source == Server`, and the client re-runs the local filter with your `thresholds`
override so your own limits still apply.

## Thresholds

Server-side thresholds are edited in the dashboard per project. The config's `overrideThresholds`
(or `ChatGuardSettings.Thresholds` from code) applies only to local decisions (offline and degraded). Probabilities are calibrated: 0.9 means
"nine times out of ten this is an insult".

## Feedback

`ModerationResult.Id` is the verdict id. Your server can report false positives/negatives with
`POST /v1/feedback { "verdict_id": "...", "kind": "false_positive" }` (see `Documentation~/api-reference.md`).

## About `Runtime/Core`

`Runtime/Core` mirrors the filtering and scoring library used by the Chat Guard service (word
lists, normalization, thresholds), so offline decisions match what the server would do with the
same settings. It is refreshed from the service's source tree on every package release; do not
edit it in place. The EditMode tests run the same `local-filter.json` vectors as the service's
own tests.
