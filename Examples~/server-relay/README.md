# Server relay example

For games without an authoritative server (peer-to-peer or client-hosted), the Chat Guard API key
must still stay off player machines. Run this tiny ASP.NET service next to your backend and have
clients call it instead of Chat Guard directly. It needs the .NET 10 SDK.

Unity hides folders ending in `~`, so run it from a clone of the package repository:

```bash
git clone https://github.com/chatguard-dev/chat-guard-unity.git
cd chat-guard-unity/Examples~/server-relay
CHATGUARD_API_KEY=cg_live_... RELAY_SHARED_SECRET=change-me dotnet run
```

`dotnet run` keeps running. In another terminal, send it a message:

```bash
curl -X POST localhost:5000/chat -H 'Content-Type: application/json' -H 'X-Relay-Secret: change-me' \
  -d '{"message":"you idiot","author_id":"p1","channel_type":"global","language":"en"}'
```

The relay forwards to the Chat Guard API (`https://api.chatguard.dev`); set `CHATGUARD_BASE_URL` only
to send requests somewhere else. When the game leaves out `language`, the relay sends none, so the
project's default language applies. When Chat Guard answers with an error, or no answer comes within
3 seconds, the relay answers `allow` with `degraded: true` and a `degraded_reason` the Unity package
reads (`upstream`, `upstream_rate_limit` or `timeout`); change that to your own policy. Replace the
shared-secret check with your own player authentication before shipping.
