# Chat Guard for Unity

Chat Guard checks every chat message in about 300 ms, before other players see it. You tune
thresholds and rules live from the dashboard, and the next message uses them, with no new build. It
works with any multiplayer setup, and [Integrations](#integrations) has the code for the common ones.
Stores, consoles and laws expect games with chat to moderate it, and Chat Guard takes that work off
your studio.

For each message your game gets back an `action` (allow, flag, hide or block), six calibrated scores
(insult, threat, hate, sexual, spam and real-money trading), a severity from 0 to 3 and who the
message targets.

- **Setup is one line:** `ChatGuardSdk.Configure("your key")`.
- **Unity 2021.3 LTS and newer**, on every platform, WebGL included.
- **Callback, coroutine or `await`**, or a no-code component. No `Task`s and no threads.
- **No dependencies:** the package uses only `UnityWebRequest`.

## Contents

- [Quick start](#quick-start)
- [Where the key lives](#where-the-key-lives)
- [Integrations](#integrations): [Netcode for GameObjects](#netcode-for-gameobjects), [Mirror](#mirror),
  [FishNet](#fishnet), [Photon Fusion 2](#photon-fusion-2), [Photon PUN 2](#photon-pun-2),
  [Photon Chat](#photon-chat), [Unity Vivox](#unity-vivox), [Nakama](#nakama), [Colyseus](#colyseus),
  [any other server](#any-other-server), [no server: the relay](#no-server-of-your-own-the-relay)
- [Handling the result](#handling-the-result)
- [Ways to call it](#ways-to-call-it)
- [Configuration](#configuration)
- [If Chat Guard can't be reached](#if-chat-guard-cant-be-reached)
- [WebGL builds](#webgl-builds)
- [Thresholds and rules](#thresholds-and-rules)
- [Troubleshooting](#troubleshooting)
- [Help and support](#help-and-support)

## Quick start

### 1. Install the package

In Unity, open **Window → Package Manager**, click **+ → Add package from git URL** and paste:

```
https://github.com/chatguard-dev/chat-guard-unity.git
```

Adding a package from a git URL needs Git installed and on your `PATH`, because Unity runs it to
fetch the package. If `git --version` doesn't work in a terminal, install Git and restart Unity
and Unity Hub first.

To pin a release, add its tag:
`https://github.com/chatguard-dev/chat-guard-unity.git#v0.4.1`. The [changelog](CHANGELOG.md) lists
what changed in each release.

### 2. Get a test key

Sign in at [app.chatguard.dev](https://app.chatguard.dev) with Google or GitHub and create an
organization with a first project (one project per game). Open **API keys**, click **Create key**,
give the key a name, choose **Test (cg_test_)**, click **Create key** again and copy the key: it is
shown only once. Test keys are free in the Editor and in development builds, up to 1,000 requests a
day for the whole organization.

### 3. Add the key

Add this script anywhere under `Assets`:

```csharp
using ChatGuard.Unity;
using UnityEngine;

public static class ChatGuardSetup
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init() => ChatGuardSdk.Configure("cg_test_...");
}
```

That is the whole setup. The key is the only setting you must give; requests go to
`https://api.chatguard.dev`. Unity calls `Init` before the first scene loads, so no GameObject is
needed. Keep the script out of public repositories while it holds a test key (see
[Where the key lives](#where-the-key-lives)).

Rather use the Inspector? Choose **Assets → Create → Chat Guard → Config**, paste the key into
**Api Key** and save the asset as `Assets/Resources/ChatGuardConfig.asset`. The package loads it the
first time a message is checked.

### 4. Check a message before other players see it

```csharp
using ChatGuard.Core;   // ModerationAction
using ChatGuard.Unity;

void OnChatMessage(string playerId, string text)
{
    ChatGuardSdk.Moderate(text, playerId, result =>
    {
        if (result.ShouldDeliver)                          // allow or flag
            ShowToEveryone(playerId, text);
        else if (result.Action == ModerationAction.Hide)   // only the sender sees it
            ShowToSenderOnly(playerId, text);
        else                                               // block
            TellSender(playerId, "Message not delivered");
    });
}
```

The callback runs on the main thread when the answer arrives, so it can touch UI and networking
directly. `playerId` is your own id for the player, one that stays the same between sessions, never
a name or an email: it ties messages to a player for per-player limits, export and erasure.

Where this code goes depends on your networking: on your server when you have one, otherwise in the
sender's game. [Integrations](#integrations) shows it for each stack.

### 5. See it work

- **Package Manager → Chat Guard → Samples → Basic Chat:** import it and press Play for a chat window
  with threshold sliders and a verdict log. It uses the key from step 3.
- **Window → Chat Guard → Tester:** pick a config asset that holds your key, type a message and see
  the verdict without entering Play mode.
- In the dashboard, the **Verdict log** lists every check, and a change on **Thresholds** applies from
  the next message.

Before you ship, replace the test key with the kind your setup needs. The next section says which.

## Where the key lives

The key decides who may call Chat Guard and which plan pays, so where it sits matters. There are three
kinds, all created on the dashboard's **API keys** page:

| Kind | Prefix | Where it goes | What it can call | Counts toward your plan |
|---|---|---|---|---|
| Server | `cg_live_` | your game server, backend or relay; never a game build | every `/v1` endpoint | yes |
| Publishable | `cg_pub_` | game builds | checks (`POST /v1/moderate`) and usage (`GET /v1/quota`) | yes |
| Test | `cg_test_` | the Editor, CI and development builds | every `/v1` endpoint | no, 1,000 requests a day per organization |

Only messages the model checks count toward your plan; block-rule hits and fallback answers are free.

Which kind your game needs depends on where the check runs:

| Your setup | Where the check runs | Key |
|---|---|---|
| A dedicated server: Netcode for GameObjects, Mirror, FishNet, Photon Fusion in Server mode | the server build, before it passes the message on | server |
| A backend or server framework: Nakama, Colyseus, your own service | that server, over HTTP | server |
| A player hosts the match: host mode, Photon Fusion in Host or Shared mode | the host (in Shared mode, the Master Client), which is a player's device | publishable, or your relay |
| No server of your own: Photon PUN 2, Photon Chat, Unity Vivox, peer-to-peer | a player's game: the sender's, or the Master Client's in PUN | publishable, or your relay |
| A WebGL build calls Chat Guard itself | the player's browser | publishable |

**Server keys** can do everything your project allows, so a server key inside a game build is a
leaked key: anyone can pull it out and spend your plan. The package guards against it. A player
build fails on purpose when a `ChatGuardConfig` asset it ships holds a `cg_live_` key (Dedicated
Server builds are exempt). A build ships the config assets under a `Resources` folder and the ones
its scenes, `Resources` assets or Preloaded Assets (**Player Settings**) use, such as the config of a
[Hook](#no-code-the-chat-guard-hook-component) in a scene. A server key that reaches a player at runtime logs a warning. On a server, read the key
from an environment variable or your secret store.

**Publishable keys** are made to ship inside the game, the way analytics keys are. They can only
check messages and read usage, every request must name the player (`author.id`), and each key has a
lower rate limit plus a per-player allowance (2 requests a second, bursts of 10), so one modified
client cannot drain your plan. Two things come with that: anyone can pull the key out of your game
and send their own messages, and a check on a player's device is advisory, because a modified client
can skip it. When you have a server, check there.

**Test keys** are free for the Editor, tests, CI and development builds. All of an organization's
test keys share 1,000 requests a day with the dashboard's test panel, and a release build that holds
one in a config asset fails. A WebGL build runs in a browser, where only publishable keys work, even
for testing (see [WebGL builds](#webgl-builds)).

### Picking the key per build

The build check reads the config assets a build ships (under `Resources`, or used by what the build
includes); it can't see keys you pass from code. Let Unity's scripting defines pick the key for each build:

```csharp
using System;
using ChatGuard.Unity;
using UnityEngine;

public static class ChatGuardSetup
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
#if UNITY_EDITOR || (DEVELOPMENT_BUILD && !UNITY_WEBGL)   // browsers only accept publishable keys
        ChatGuardSdk.Configure("cg_test_...");   // test key: free, for development
#elif UNITY_SERVER
        // Dedicated server: the server key comes from the machine, never from the build.
        ChatGuardSdk.Configure(Environment.GetEnvironmentVariable("CHATGUARD_API_KEY"));
#else
        ChatGuardSdk.Configure("cg_pub_...");    // publishable key for game builds
#endif
        if (!ChatGuardSdk.Client.HasServer)
            Debug.LogWarning("Chat Guard has no key: the local word filter answers every message.");
    }
}
```

Keep only the branches you need: a game that checks messages on its server needs no key in its game
build. Never commit a config asset or a script that holds a server key, and keep test keys private
where you can too: they can call every `/v1` endpoint, player-data erasure included, and anyone with
a development build can pull one out. Publishable keys are fine to commit if you accept that they are
public.

## Integrations

Call Chat Guard at the one point every message passes before other players see it. With a server,
that point is the server: players can't skip the check there, and the server key stays on your
machines. Without one, it is the sender's game, or your relay.

Each Unity example below is one component on a networked object that every player has, such as a
chat object in the scene. Your chat input calls `Send`. The server checks the line and then sends it
to everyone (`allow`, `flag`), back to its sender only (`hide`), or nowhere, with a notice to the
sender (`block`). The empty method bodies are where your chat UI takes over.

The examples pass each library's connection or player number as the player id to stay short. In your
game, pass an id that stays the same for a player between sessions, such as your account id, never a
name or an email: per-player limits, export and erasure all look messages up by that id.

### Netcode for GameObjects

The server owns chat: players send their line to the server, the server checks it and sends it on.
On Netcode 2.7 or newer (Unity 6):

```csharp
using ChatGuard.Core;
using ChatGuard.Unity;
using Unity.Netcode;

public sealed class NetworkChat : NetworkBehaviour
{
    public void Send(string text) => SubmitRpc(text);

    // Any player may call this; it runs on the server (on the host in host mode).
    [Rpc(SendTo.Server)]
    private void SubmitRpc(string text, RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        ChatGuardSdk.Moderate(text, sender.ToString(), result =>
        {
            if (this == null || !IsSpawned) return;                  // despawned while waiting
            if (result.ShouldDeliver)
                ShowLineRpc(sender, text);                           // everyone
            else if (result.Action == ModerationAction.Hide)
                ShowLineToRpc(sender, text, RpcTarget.Single(sender, RpcTargetUse.Temp));   // the sender only
            else
                NoticeRpc("Message not delivered", RpcTarget.Single(sender, RpcTargetUse.Temp));
        });
    }

    // InvokePermission.Server matters: without it a modified client could call these itself
    // (the server relays client-sent RPCs) and skip the check.
    [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
    private void ShowLineRpc(ulong author, string text) { /* add the line to your chat UI */ }

    [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
    private void ShowLineToRpc(ulong author, string text, RpcParams rpcParams) { /* same */ }

    [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
    private void NoticeRpc(string notice, RpcParams rpcParams) { /* show it to the player */ }
}
```

On Netcode 1.x (Unity 2021.3 and 2022.3) and 2.0 to 2.6, use the older `[ServerRpc]` and
`[ClientRpc]` pair instead. Those versions have no `InvokePermission`, let the server trust a sender
id the client writes, and relay universal RPCs without checking who sent them. With the older pair
the sender id comes from the connection, and no client can make a `[ClientRpc]` run on other
players' screens. The one exception is the host's own screen, so the server draws that one itself:

```csharp
using ChatGuard.Core;
using ChatGuard.Unity;
using Unity.Netcode;

public sealed class NetworkChat : NetworkBehaviour
{
    public void Send(string text) => SubmitServerRpc(text);

    [ServerRpc(RequireOwnership = false)]   // any player may call it
    private void SubmitServerRpc(string text, ServerRpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        ChatGuardSdk.Moderate(text, sender.ToString(), result =>
        {
            if (this == null || !IsSpawned) return;                   // despawned while waiting
            if (result.ShouldDeliver)
                Show(sender, text, toEveryone: true);                  // allow, flag
            else if (result.Action == ModerationAction.Hide)
                Show(sender, text, toEveryone: false);                 // the sender only
            else
                Show(sender, "Message not delivered", toEveryone: false);
        });
    }

    // Server side. The host's own screen is drawn here rather than through the ClientRpc, because a
    // client could forge that RPC at the host (never at other players), so its body skips the host.
    private void Show(ulong author, string line, bool toEveryone)
    {
        bool senderIsHost = author == NetworkManager.ServerClientId;
        if (IsHost && (toEveryone || senderIsHost))
            ShowLine(author, line);
        if (toEveryone)
            ShowLineClientRpc(author, line);
        else if (!senderIsHost)
            ShowLineClientRpc(author, line, new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { author } } });
    }

    [ClientRpc]
    private void ShowLineClientRpc(ulong author, string line, ClientRpcParams clientRpcParams = default)
    {
        if (!IsServer) ShowLine(author, line);
    }

    private void ShowLine(ulong author, string line) { /* add the line to your chat UI */ }
}
```

Netcode 1.14 is the last release for Unity 2021.3; 1.15 needs 2022.3.

### Mirror

The same shape with a `[Command]` that any player may call, `[ClientRpc]` for everyone and
`[TargetRpc]` for the sender. Mirror fills in `sender` itself; clients never pass it.

```csharp
using ChatGuard.Core;
using ChatGuard.Unity;
using Mirror;

public sealed class NetworkChat : NetworkBehaviour
{
    public void Send(string text) => CmdSubmit(text);

    [Command(requiresAuthority = false)]
    private void CmdSubmit(string text, NetworkConnectionToClient sender = null)
    {
        int author = sender.connectionId;
        ChatGuardSdk.Moderate(text, author.ToString(), result =>
        {
            if (this == null) return;                                 // destroyed while waiting
            if (result.ShouldDeliver)
                RpcShowLine(author, text);                            // everyone
            else if (result.Action == ModerationAction.Hide)
                TargetShowLine(sender, author, text);                 // the sender only
            else
                TargetNotice(sender, "Message not delivered");
        });
    }

    [ClientRpc]
    private void RpcShowLine(int author, string text) { /* add the line to your chat UI */ }

    [TargetRpc]
    private void TargetShowLine(NetworkConnectionToClient target, int author, string text) { /* same */ }

    [TargetRpc]
    private void TargetNotice(NetworkConnectionToClient target, string notice) { /* show it to the player */ }
}
```

In host mode, use Mirror 96.9.4 or newer: older versions don't fill in `sender` correctly when the
host player sends a line.

### FishNet

The same shape with `[ServerRpc]`, `[ObserversRpc]` and `[TargetRpc]`:

```csharp
using ChatGuard.Core;
using ChatGuard.Unity;
using FishNet.Connection;
using FishNet.Object;

public sealed class NetworkChat : NetworkBehaviour
{
    public void Send(string text) => SubmitChat(text);

    // Keep "= null" on the last parameter: then FishNet fills in the real sender. Without the
    // default value the client passes that argument and could name someone else.
    [ServerRpc(RequireOwnership = false)]
    private void SubmitChat(string text, NetworkConnection sender = null)
    {
        int author = sender.ClientId;
        ChatGuardSdk.Moderate(text, author.ToString(), result =>
        {
            if (this == null) return;                                 // destroyed while waiting
            if (result.ShouldDeliver)
                ShowLine(author, text);                               // everyone
            else if (result.Action == ModerationAction.Hide)
                ShowLineTo(sender, author, text);                     // the sender only
            else
                NoticeTo(sender, "Message not delivered");
        });
    }

    [ObserversRpc]
    private void ShowLine(int author, string text) { /* add the line to your chat UI */ }

    [TargetRpc]
    private void ShowLineTo(NetworkConnection target, int author, string text) { /* same */ }

    [TargetRpc]
    private void NoticeTo(NetworkConnection target, string notice) { /* show it to the player */ }
}
```

### Photon Fusion 2

Check on the state authority before the line is replicated. That is your server in Server mode, but
the host in Host mode and, for scene objects, the Shared Mode Master Client in Shared mode: both are
players' devices, so use a publishable key there.

```csharp
using ChatGuard.Core;
using ChatGuard.Unity;
using Fusion;

public sealed class NetworkChat : NetworkBehaviour
{
    public void Send(string text) => RPC_Submit(text);

    // Without SourceIsHostPlayer, lines the host player sends arrive with Source == PlayerRef.None.
    [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
    public void RPC_Submit(string text, RpcInfo info = default)
    {
        PlayerRef sender = info.Source;
        string playerId = Runner.GetPlayerUserId(sender) ?? sender.PlayerId.ToString();
        ChatGuardSdk.Moderate(text, playerId, result =>
        {
            if (Object == null || !Object.IsValid) return;           // despawned while waiting
            if (result.ShouldDeliver)
                RPC_ShowLine(sender, text);                          // everyone
            else if (result.Action == ModerationAction.Hide)
                RPC_ShowLineTo(sender, sender, text);                // the sender only
            else
                RPC_NoticeTo(sender, "Message not delivered");
        });
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_ShowLine(PlayerRef author, string text) { /* add the line to your chat UI */ }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_ShowLineTo([RpcTarget] PlayerRef target, PlayerRef author, string text) { /* same */ }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_NoticeTo([RpcTarget] PlayerRef target, string notice) { /* show it to the player */ }
}
```

Fusion wants RPC method names that start or end with `RPC`. An RPC carries at most 512 bytes on the
default channels, so keep chat lines short (Chat Guard itself accepts up to 2,000 characters).
`Runner.GetPlayerUserId` returns the Photon user id, which stays the same between sessions, unlike a
`PlayerRef`.

### Photon PUN 2

PUN runs no code of yours on a server, so the check runs on a player's device with a publishable
key. Let the Master Client check every line, and have everyone else show only lines that came
through it. Then a modified client can't skip the check unless it is the Master Client itself:

```csharp
using ChatGuard.Core;
using ChatGuard.Unity;
using Photon.Pun;
using Photon.Realtime;

// On a scene object with a PhotonView.
public sealed class NetworkChat : MonoBehaviourPun
{
    public void Send(string text) => photonView.RPC(nameof(Submit), RpcTarget.MasterClient, text);

    [PunRPC]
    private void Submit(string text, PhotonMessageInfo info)
    {
        Player sender = info.Sender;
        if (!PhotonNetwork.IsMasterClient || sender == null) return;
        string playerId = string.IsNullOrEmpty(sender.UserId) ? sender.ActorNumber.ToString() : sender.UserId;
        ChatGuardSdk.Moderate(text, playerId, result =>
        {
            if (this == null) return;                                // left the room while waiting
            if (result.ShouldDeliver)
                photonView.RPC(nameof(ShowLine), RpcTarget.All, sender.ActorNumber, text);   // everyone
            else if (result.Action == ModerationAction.Hide)
                photonView.RPC(nameof(ShowLine), sender, sender.ActorNumber, text);          // the sender only
            else
                photonView.RPC(nameof(ShowNotice), sender, "Message not delivered");
        });
    }

    [PunRPC]
    private void ShowLine(int actorNumber, string text, PhotonMessageInfo info)
    {
        if (info.Sender == null || !info.Sender.IsMasterClient) return;   // only lines the Master Client checked
        /* add the line to your chat UI */
    }

    [PunRPC]
    private void ShowNotice(string notice, PhotonMessageInfo info)
    {
        if (info.Sender == null || !info.Sender.IsMasterClient) return;
        /* show it to the player */
    }
}
```

The Master Client sees other players' `UserId` only in rooms created with
`RoomOptions.PublishUserId = true`; set the id through custom authentication so players can't pick
their own. Photon's webhooks can't cancel an event or RPC. Only server plugins can, and those need
Enterprise Cloud or a self-hosted Photon Server.

### Photon Chat

Check on the sender's device with a publishable key, then publish:

```csharp
using ChatGuard.Core;
using ChatGuard.Unity;
using Photon.Chat;

// In your IChatClientListener; chatClient connected with new AuthenticationValues(playerId).
public void Send(string channel, string text)
{
    ChatGuardSdk.Moderate(text, chatClient.UserId, result =>
    {
        if (result.ShouldDeliver)
            chatClient.PublishMessage(channel, text);                // everyone in the channel
        else if (result.Action == ModerationAction.Hide)
            ShowLine(channel, chatClient.UserId, text);              // only this player sees it
        else
            ShowNotice("Message not delivered");
    });
}
```

Photon Chat can also ask a web service of yours first, which keeps a server key off players'
devices. Publish with `PublishMessage(channel, text, forwardAsWebhook: true)` and set `BaseUrl` and
`PathPublishMessage` in your Chat app's webhook settings. Photon then posts the message to your
service as JSON (`ChannelName`, `UserId`, `Message` and more). Your service calls Chat Guard and
answers `{"ResultCode":0}` to publish or `{"ResultCode":1,"DebugMessage":"..."}` to cancel.
`FailIfUnavailable` decides what happens when your service is down. The example relay doesn't handle
this webhook, so that service is yours to write, and because each client opts in per message, a
modified client can still skip it.

### Unity Vivox

Vivox text chat has no hook where your own check could run before delivery, so check on the
sender's device with a publishable key:

```csharp
using ChatGuard.Core;
using ChatGuard.Unity;
using Unity.Services.Vivox;

public async void Send(string channel, string text)
{
    ModerationResult result = await ChatGuardSdk.Moderate(text, VivoxService.Instance.SignedInPlayerId);
    if (result.ShouldDeliver)
        await VivoxService.Instance.SendChannelTextMessageAsync(channel, text);   // everyone in the channel
    else if (result.Action == ModerationAction.Hide)
        ShowLine(channel, text);                                                   // only this player sees it
    else
        ShowNotice("Message not delivered");
}
```

### Nakama

Check in a before hook on `ChannelMessageSend`, which sees every chat message before Nakama delivers
it. A TypeScript runtime module (Nakama 3.x, compiled to ES5 like any Nakama TypeScript module):

```ts
// The key lives in the server config, never in the game:
//   runtime:
//     env:
//       - "CHATGUARD_API_KEY=cg_live_..."

function InitModule(ctx: nkruntime.Context, logger: nkruntime.Logger, nk: nkruntime.Nakama, initializer: nkruntime.Initializer) {
  // Nakama finds hooks by reading InitModule: register a top-level function by name.
  initializer.registerRtBefore("ChannelMessageSend", beforeChannelMessageSend);
}

function beforeChannelMessageSend(ctx: nkruntime.Context, logger: nkruntime.Logger, nk: nkruntime.Nakama,
    envelope: nkruntime.EnvelopeChannelMessageSend): nkruntime.EnvelopeChannelMessageSend | void {
  const content = JSON.parse(envelope.channelMessageSend.content);   // the JSON object your client sent
  const text: string = typeof content.text === "string" ? content.text : "";
  let action = text.length > 2000 ? "block" : "allow";   // "allow": your policy when no answer comes
  if (action === "allow") {
    try {
      const res = nk.httpRequest("https://api.chatguard.dev/v1/moderate", "post", {
        "Authorization": "Bearer " + ctx.env["CHATGUARD_API_KEY"],
        "Content-Type": "application/json",
      }, JSON.stringify({ message: text, author: { id: ctx.userId } }), 2000);
      if (res.code === 200) action = JSON.parse(res.body).action;
    } catch (e) {
      logger.warn("Chat Guard not reached: %s", String(e));   // network error or timeout
    }
  }

  if (action === "allow" || action === "flag") {
    return envelope;   // deliver
  }
  // hide or block: drop this one message; the sender's send fails with this text and they stay
  // connected. Never return null here: Nakama would close the sender's socket.
  throw { message: "Message not delivered", code: nkruntime.Codes.PERMISSION_DENIED } as nkruntime.Error;
}
```

- `text` is whatever field your client puts the line in (Nakama only accepts a JSON object as
  `content`). Chat Guard answers 400 for lines over 2,000 characters, so the hook refuses those
  itself instead of letting them through unchecked.
- The runtime is synchronous: `nk.httpRequest` blocks the hook (its timeout is in milliseconds) and
  throws on network errors, but not on a non-200 answer. Always catch it, or the error text reaches
  the player.
- A before hook can't show a message to its sender alone, so `hide` is dropped like `block`.
- The Go runtime works the same way: `initializer.RegisterBeforeRt("ChannelMessageSend", ...)`,
  return `in, nil` to deliver and `nil, runtime.NewError("Message not delivered", 7)` to drop.

### Colyseus

Check in the room's message handler, before anything is broadcast (Colyseus 0.18, Node 22 or newer):

```ts
import { Room, type Client } from "colyseus";

export class ChatRoom extends Room {
  messages = {
    chat: async (client: Client, message: { text: string }) => {
      const text = String(message?.text ?? "");
      const playerId = client.auth?.playerId ?? client.sessionId;   // what your onAuth returned
      const action = text.length > 2000 ? "block" : await moderate(text, playerId);
      if (action === "allow" || action === "flag") this.broadcast("chat", { from: client.sessionId, text });  // everyone
      else if (action === "hide") client.send("chat", { from: client.sessionId, text });                       // the sender only
      else client.send("notice", "Message not delivered");                                                     // block
    },
  };
}

// Never throws: Colyseus doesn't await message handlers, and an unhandled rejection stops the server.
async function moderate(message: string, playerId: string): Promise<string> {
  try {
    const res = await fetch("https://api.chatguard.dev/v1/moderate", {
      method: "POST",
      headers: { "Authorization": `Bearer ${process.env.CHATGUARD_API_KEY}`, "Content-Type": "application/json" },
      body: JSON.stringify({ message, author: { id: playerId } }),
      signal: AbortSignal.timeout(2000),
    });
    if (res.ok) return (await res.json()).action;
  } catch {
    // no answer within 2 s, or no network
  }
  return "allow";   // your policy when no answer comes
}
```

- In the game, `room.Send("chat", new { text })` sends a line and
  `room.OnMessage<ChatLine>("chat", line => ...)` receives them.
- `client.sessionId` changes with every connection. Return a stable player id from `onAuth` and read
  it from `client.auth`, as above.
- Colyseus runs a player's messages concurrently, so a quick later line can overtake a slow earlier
  one. Chain the checks per player if order matters to your game.

### Any other server

Send one `POST /v1/moderate` per message and act on `action`:

```bash
curl -X POST https://api.chatguard.dev/v1/moderate \
  -H "Authorization: Bearer cg_live_..." -H "Content-Type: application/json" \
  -d '{"message":"you absolute idiot","author":{"id":"player-42"},"channel":{"type":"team","language":"en"}}'
```

Every request and response field is in the [API reference](Documentation~/api-reference.md), and the
dashboard's **Overview** page has the same call in C#, TypeScript, Python, Go and Java. Give it a
short timeout (2 s is plenty) and decide what your game does when no answer comes.

### No server of your own: the relay

`Examples~/server-relay` is a small ASP.NET Core service (.NET 10) that you host. Your game sends it
the message, and it calls Chat Guard with a server key that never reaches players' devices. Unity
hides folders ending in `~`, so run it from a clone of this repository:

```bash
git clone https://github.com/chatguard-dev/chat-guard-unity.git
cd chat-guard-unity/Examples~/server-relay
CHATGUARD_API_KEY=cg_live_... RELAY_SHARED_SECRET=change-me dotnet run
```

The game posts `{ "message", "author_id", "channel_type", "language" }` to `/chat` and gets Chat
Guard's response back, which `ChatGuardClient.TryParseResponse(json, latencyMs)` turns into a
`ModerationResult`. When the game leaves out `language`, the relay sends none, so your project's
default language applies. When Chat Guard answers with an error or can't be reached in 3 seconds, the
example relay answers `allow` with `degraded: true` and a `degraded_reason` the package reads
(`upstream`, `upstream_rate_limit` or `timeout`). Change that to your own policy, and replace its
shared-secret check with your own player authentication before you ship. A modified client can
still skip the relay, so as with a publishable key, the check is advisory. The
[relay's README](Examples~/server-relay/README.md) has a request to try it with.

## Handling the result

| `action` | What it means | What your game does |
|---|---|---|
| `allow` | Nothing wrong. | Show it to everyone. |
| `flag` | Borderline. | Show it, and mark it for your team to review. |
| `hide` | Abusive. | Show it to the sender only, so they don't learn it was hidden. |
| `block` | Severe, a threat for example. | Show it to nobody, and tell the sender. |

`result.ShouldDeliver` is true for `allow` and `flag`. Your project's thresholds turn the scores into
the action. By default a threat of 0.85 or a severity of 2.5 blocks, an insult, hate or sexual score
of 0.80 hides, and any score of 0.55 flags; change them on the dashboard's **Thresholds** page.

The rest of the result, for logging, analytics or your own policy:

| Member | What it holds |
|---|---|
| `Verdicts` | Six calibrated probabilities from 0 to 1: `Insult`, `Threat`, `Hate`, `Sexual`, `Spam` and `Trading` (real-money trading). |
| `Severity` | 0 (fine) to 3 (severe). |
| `Target` | Who the message is aimed at: `Choice` (`OtherUser`, `Group`, `Self`, `Nobody` or `Other`) and `Confidence`. Null when the model wasn't asked. |
| `Id` | The verdict id, for [feedback](#feedback) on a wrong call. Null for answers made on the device. |
| `Degraded`, `DegradedReason` | The model didn't answer (a word filter or your `OfflineBehavior` did), and why ([details](#if-chat-guard-cant-be-reached)). |
| `Source`, `Error` | `Server`, or `Local` when the package answered on the device; `Error` then says why the server wasn't used. |
| `LatencyMs`, `Cached`, `Model` | The round trip in milliseconds, whether the scores came from the 10-minute cache, and what answered: the model's version, `local-filter/<version>`, or `offline/allow-all` / `offline/block-all`. |
| `QuotaUsed`, `QuotaLimit` | Your organization's usage over the last 30 days, a rolling window (on paid plans the bill counts per billing month). For test keys, the daily test allowance. |

`ModerationAction`, `VerdictSet`, `VerdictCategory`, `TargetVerdict`, `TargetChoice` and
`DegradedReason` live in the `ChatGuard.Core` namespace and `Thresholds` in `ChatGuard.Core.Scoring`;
everything else is in `ChatGuard.Unity`.

### Feedback

When Chat Guard gets one wrong, report it from your server with the verdict id:
`POST /v1/feedback { "verdict_id": "...", "kind": "false_positive" }` (or `false_negative`). Reports
show up on the dashboard's **Feedback** page. Publishable keys can't send feedback. The fields are in
the [API reference](Documentation~/api-reference.md).

## Ways to call it

`Moderate` starts one check and returns a `ModerationOperation`. Pick the form that fits your code;
all of them complete on the main thread, on every platform, WebGL included.

### Callback

`ChatGuardSdk.Moderate(text, playerId, result => ...)`, as in the [quick start](#quick-start). When the
answer is known at once (no key, for example), the callback runs before `Moderate` returns.

### Coroutine

`Moderate` returns an operation you can yield, poll (`IsDone`) or cancel:

```csharp
private IEnumerator SendChat(string playerId, string text)
{
    ModerationOperation op = ChatGuardSdk.Moderate(text, playerId);
    yield return op;                       // op.Cancel() aborts the request; Result then stays null
    if (op.Result != null && op.Result.ShouldDeliver)
        BroadcastChat(playerId, text);
}
```

`StartCoroutine(ChatGuardSdk.ModerateCoroutine(text, playerId, result => ...))` does the same in one
call.

### Async/await without Tasks

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

### Several clients

`ChatGuardSdk` is a thin wrapper around one shared `ChatGuardClient`. Create clients yourself when you
need several, one per project for example:

```csharp
[SerializeField] private ChatGuardConfig config;
private ChatGuardClient client;

private void Awake() => client = new ChatGuardClient(config);   // or new ChatGuardClient(settings) / (apiKey)

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

### No code: the Chat Guard Hook component

Add **Chat Guard Hook** (`ChatGuardUnityHook`) to a GameObject, assign a config asset, and wire
`onDeliver`, `onSuppress` and `onModerated` to your chat UI. A UnityEvent, such as an input field's
submit event, calls `Moderate(message)`. From code you can also call `Moderate(message, authorId)`,
which sends exactly the id you pass. In-flight requests are cancelled when the component is
destroyed. While its `configureStaticApi` box is ticked (the default), its `Awake` also calls
`ChatGuardSdk.Configure(config)`, which replaces anything configured before it.

`Moderate(message)` sends the Hook's `PlayerId` as the player id. Set it once you know who the
player is: from code (`hook.PlayerId = accountId;`) or from a UnityEvent with `SetPlayerId(string)`.
Until then the Hook sends a random id instead. It creates that id the first time it needs one and
keeps it in `PlayerPrefs` under `chatguard.install_id` (`ChatGuardUnityHook.InstallIdKey`), one per
installation of the game. That way per-player limits work, a publishable key gets the player id it
requires, and a player's data can still be exported or erased (see below). Dedicated Server builds
never create it: there, a message without a `PlayerId` goes without a player id.

The install id is random and names no device or account, but it is a persistent pseudonymous
identifier: it stays the same until the game's `PlayerPrefs` are cleared. Mention it in your game's
privacy notice, or set `PlayerId` before the first message so it is never created.

To answer a player's request to export or erase their data, you need the id their messages were
sent with. If you set `PlayerId`, it's an id you already know. Otherwise, read the install id on the
player's device with `PlayerPrefs.GetString(ChatGuardUnityHook.InstallIdKey)` and show it where the
player can copy it, such as a settings or support screen, so they can include it in their request.
An empty string means the Hook never created one on that device.

### Why there are no Tasks

WebGL has no threads. Anything that relies on the thread pool or on timers (`Task.Run`,
`Task.Delay`, `ConfigureAwait(false)`, `CancellationTokenSource.CancelAfter`) either throws or never
completes there. The SDK therefore exposes no `Task`s at all: `Moderate` returns a
`ModerationOperation` that you can yield (the same shape as `UnityWebRequestAsyncOperation`),
`await` or drive with callbacks, and everything completes on the main thread. That works identically
on every platform, WebGL included. Cancellation tokens are fine: `CancellationToken` lives in
`System.Threading`, and cancelling one only runs callbacks.

## Configuration

`ChatGuardSdk.Configure("your key")` is enough for most games. For more control, pass a
`ChatGuardSettings`: the same options as the config asset, with the same defaults.

```csharp
ChatGuardSdk.Configure(new ChatGuardSettings
{
    ApiKey = "cg_pub_...",
    DefaultLanguage = "de",
    ChannelType = "team",
    AgeRating = "12+",
});
```

| Setting | Default | What it does |
|---|---|---|
| `ApiKey` | empty | The key. Empty means nothing is sent and `OfflineBehavior` answers every message (the local word filter by default). |
| `BaseUrl` | `https://api.chatguard.dev` | Where requests go. Change it only for a proxy of your own that serves `/v1/moderate` the same way; empty means the default. |
| `TimeoutSeconds` | `2` | The whole request budget, rounded up to whole seconds (1 to 600). When it runs out, `OfflineBehavior` answers. |
| `OfflineBehavior` | `LocalFilter` | What to answer when Chat Guard can't be reached: `LocalFilter`, `AllowAll` or `BlockAll`. |
| `LocalFilterWhenDegraded` | `true` | When the service answers with its own word filter, decide the action again on the device with your local thresholds. Only with `LocalFilter`. |
| `DefaultLanguage` | `"en"` | Sent with messages that don't set `language`, and picks the local word list. Free uses the project's language; paid plans honour this one. |
| `ChannelType` | `"global"` | Sent with messages that don't set `channelType`: `global`, `team`, `dm` or `guild`. |
| `AgeRating` | `"16+"` | Sent with messages that don't set `ageRating`; sexual content is judged against it. Null or empty sends none. |
| `Thresholds` | null (built-in defaults) | A `ChatGuard.Core.Scoring.Thresholds` for decisions made on the device only; the dashboard's thresholds are separate. |

- **The config asset has the same fields** in the Inspector, with the same defaults;
  `config.ToSettings()` converts one when you want to start from an asset and adjust it in code.
- **`Configure` replaces the Resources asset.** Once it was called, `ChatGuardSdk` never loads
  `Assets/Resources/ChatGuardConfig.asset`.
- **Call `Configure` again at any time**, for example after your server hands out a key: the next
  `Moderate` uses the new settings, and messages in flight finish with the old ones.
- **Settings are checked and copied.** A timeout that is not a positive number of seconds up to 600,
  or a `BaseUrl` that is neither empty nor an absolute http or https URL, throws `ArgumentException`. Editing the
  object afterwards changes nothing; `client.Settings` returns a copy of what a client runs with,
  key included, so don't log it verbatim.

Per message, a `ModerationRequest` can override the channel, language and age rating and add context
the model weighs:

```csharp
ChatGuardSdk.Moderate(new ModerationRequest(text, playerId)
{
    channelType = "team",
    thread = recentLines,   // List<ThreadEntry>: the last few lines before this one (up to 5 are used)
    accountAgeDays = 3,
    priorWarnings = 1,
}, result => { /* ... */ });
```

Give each `ThreadEntry` the player id of whoever wrote the line as its `author`
(`new ThreadEntry(otherPlayerId, line)`), the same id you pass when that player writes. Erasing a
player also removes their lines from other players' stored context, and it finds them by that label.
Entries without text are skipped, since the API refuses a message whose context holds one.

## If Chat Guard can't be reached

Chat keeps working. If the package gets no usable answer, because there is no network, no answer
came within `TimeoutSeconds` (2 s by default) or the API answered with an error, `OfflineBehavior`
decides what your game gets:

- `LocalFilter` (the default): a built-in word filter answers on the device, with word lists for
  English, Russian, Serbian, Polish, Turkish, German, Spanish and Portuguese, the same lists the
  service falls back to. It is simpler than the model, so you might hide rather than block while it
  answers.
- `AllowAll`: every message is allowed.
- `BlockAll`: every message is blocked.

These results carry `Source == Local`, `Degraded == true`, details in `Error`, and a `DegradedReason`:
`Offline` (no key, a network error, or the request budget ran out), `Upstream` (a non-200 answer or
an unreadable body) or `UpstreamRateLimit` (HTTP 429).

The service has the same safety net. When its model can't answer (`Timeout`, `Upstream` or
`UpstreamRateLimit`), or a Free plan is over its allowance (`Quota`), it answers with its own word
filter. On Free and with test keys, `UpstreamRateLimit` can also mean that a shared per-minute
allowance ran out. Those results have `Degraded == true` but keep `Source == Server`. With
`LocalFilter` and `LocalFilterWhenDegraded` (on by default), the package also runs its own filter on
the message and decides the action with your local thresholds (your `Thresholds`, or the built-in
defaults).

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
  far from the API, leave room for the extra round trip in `TimeoutSeconds`.

A same-origin relay ([the relay](#no-server-of-your-own-the-relay) served from your game's domain)
avoids the preflight and keeps a `cg_live_` key off the page. It is the better fit when you already
run a backend for the web build.

## Thresholds and rules

Thresholds and rules live in the dashboard, per project, not in your game. Save a change and the
next message uses it: no new build, no store review, no waiting for players to update. Thresholds
are on every plan; block, allow and context rules come with Indie and up. Probabilities are
calibrated: 0.9 means "nine times out of ten this is an insult". The **Thresholds** page has a test
column that tries values against real messages before you save them.

`ChatGuardSettings.Thresholds` (the config asset's `overrideThresholds`) applies only to decisions
the package makes itself, offline or degraded (see
[If Chat Guard can't be reached](#if-chat-guard-cant-be-reached)).

## Troubleshooting

| What you see | Why | What to do |
|---|---|---|
| The warning "ChatGuardSdk.Configure was not called and no Resources/ChatGuardConfig.asset was found", or `Error` is "no API key configured" | No key reached the package, so the local filter answers. | Call `ChatGuardSdk.Configure(key)` before the first message, or save the config asset as `Assets/Resources/ChatGuardConfig.asset`. |
| `Error` starts with `HTTP 401` | The key is wrong or was revoked. | Copy it again from **API keys**. A key is shown once; create a new one if it's lost. |
| Package Manager can't add the package: no git executable was found | Unity needs Git to fetch a package from a git URL. | Install Git, check that `git --version` works in a terminal, then restart Unity and Unity Hub. |
| `Error` starts with `HTTP 403` in a WebGL build | Browsers may only use publishable keys. | Use a `cg_pub_` key there. |
| `Error` starts with `HTTP 403: Organization suspended` (code `org_suspended`) | Your organization is suspended, so Chat Guard refuses its keys and the package answers on the device. | See the notice on the dashboard, or email support@chatguard.dev. |
| `Error` starts with `HTTP 400` | The API refused a field of the request, and `Error` names it. With a publishable key it is usually `author.id`: publishable keys need the player's id. | Pass `playerId` (the `authorId` argument) with every message, or fix the field `Error` names (the limits are in the [API reference](Documentation~/api-reference.md#post-v1moderate)). |
| `DegradedReason.UpstreamRateLimit` | With `Source == Local` and `HTTP 429`: a rate limit (the key's, the organization's, or with a publishable key the player's: 2 requests a second, bursts of 10) or the daily test allowance. With `Source == Server`: a shared per-minute allowance (Free and test keys) or the model provider's limit. | Back off for the `retry_after` seconds that `Error` shows, send test traffic more slowly, and see [Rate limits](Documentation~/api-reference.md#rate-limits). |
| The build fails: "… holds a cg_live_ server key" | A server key would ship inside the game. | Put a `cg_pub_` key in the asset, or check messages on your server. |
| The build fails: "… holds a cg_test_ test key, and this is a release build" | Test keys are for development builds. | Ship a `cg_pub_` key, or tick **Development Build** to keep testing. |

## Help and support

- **Questions:** the `#help` forum on the Chat Guard Discord, https://chatguard.dev/discord.
  Include your Unity version, the package version (`package.json`) and the platform.
- **Feature ideas:** `#feature-requests` on the same server; upvote an existing post instead of repeating it.
- **Found a bug?** [Open an issue](https://github.com/chatguard-dev/chat-guard-unity/issues/new/choose).
  Leave keys and players' messages out of it.
- **Account, billing and technical help:** support@chatguard.dev. On Indie we aim to reply within 2
  business days, on Studio within 1, and on Enterprise as agreed. Free plans get help on Discord, and
  by email for account or billing problems.
- **Plans and custom volume:** sales@chatguard.dev.
- **Player requests (access, erasure):** use **Export a player** or **Erase a player** on your
  project's **Settings** page, or call `DELETE /v1/evidence` from your server
  ([API reference](Documentation~/api-reference.md)). Never email player ids or message text; if you
  write to us about a record, give its verdict id or request id.
- **Security issues:** report them privately through
  [GitHub](https://github.com/chatguard-dev/chat-guard-unity/security/advisories/new) or to
  support@chatguard.dev, never in a public issue. [SECURITY.md](.github/SECURITY.md) has the details.
- **Service status:** `#status` on Discord and https://github.com/chatguard-dev/status.

Never post a server or test key (`cg_live_…` or `cg_test_…`) in public; the Discord server blocks
messages that contain one. If a key leaks, revoke it on the dashboard's **API keys** page.

## About `Runtime/Core`

`Runtime/Core` mirrors the filtering and scoring library used by the Chat Guard service (word
lists, normalization, thresholds), so a message the package answers on the device gets the same
result the service's own word filter would give it with the same settings. It is refreshed from the
service's source tree on every package release; do not edit it in place. The EditMode tests run the
same `local-filter.json` vectors as the service's own tests.
