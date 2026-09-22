# Server relay example

For games without an authoritative server (peer-to-peer or client-hosted), the Chat Guard API key
must still stay off player machines. Run this tiny ASP.NET service next to your backend and have
clients call it instead of Chat Guard directly.

```bash
CHATGUARD_API_KEY=cg_live_... CHATGUARD_BASE_URL=https://api.example.com RELAY_SHARED_SECRET=change-me \
  dotnet run --project Examples~/server-relay
curl -X POST localhost:5000/chat -H 'Content-Type: application/json' -H 'X-Relay-Secret: change-me' \
  -d '{"message":"you idiot","author_id":"p1","channel_type":"global","language":"en"}'
```

Replace the shared-secret check with your own player authentication before shipping.
