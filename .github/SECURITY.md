# Security

## Reporting a security problem

If you find a security problem in Chat Guard, tell us privately:

- **On GitHub:** [report a vulnerability](https://github.com/chatguard-dev/chat-guard-unity/security/advisories/new).
  Only you and the maintainers can see the report.
- **By email:** support@chatguard.dev.

Please don't open a public issue or post about it on Discord.

Say what you found, how to reproduce it and what someone could do with it. Never include API keys
(`cg_live_…`, `cg_test_…` or `cg_pub_…`), player ids or players' messages. Use placeholders, and
if you need to point at one request, give its verdict id or request id. If one of your keys has
leaked, revoke it on the dashboard's **API keys** page first.

## What's in scope

- The Chat Guard package for Unity in this repository: `Runtime`, `Editor`, the samples and the
  server relay example in `Examples~/server-relay`.
- The Chat Guard API at `https://api.chatguard.dev`.
