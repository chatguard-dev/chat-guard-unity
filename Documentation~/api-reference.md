# API reference

Base URL: `https://api.chatguard.dev` (EU). All bodies are JSON in snake_case. A machine readable
description is `openapi.json` next to this file (also served at `https://api.chatguard.dev/openapi/v1.json`).
It is generated from the code and covers the paths, the shapes of requests and responses, the base
URL, which token each route takes and the fixed value sets (such as `action` and `channel.type`). It
does not list every error response or the limits on values: those are on this page
([Authentication](#authentication), and the constraints and errors under each endpoint).

## Authentication

- Moderation endpoints (`/v1/*`): `Authorization: Bearer <key>`. Keys are created per project in
  the dashboard and shown once. Three kinds:

  | Kind | Prefix | May call | Metered | Notes |
  |---|---|---|---|---|
  | Server | `cg_live_` | all `/v1` endpoints | yes (model verdicts) | keep on your server or relay |
  | Test | `cg_test_` | all `/v1` endpoints | no | 1,000 requests per organization per UTC day, shared by all test keys and the dashboard test panel; for the Editor and development builds |
  | Publishable | `cg_pub_` | `POST /v1/moderate`, `GET /v1/quota` only (others return 403) | yes (model verdicts) | safe to ship in a client build; `author.id` required; lower per-key limit plus a per-author bucket; results on a player's device are advisory |
- A `/v1` request without a key, or with a key that is unknown or revoked, gets `401` with the
  `detail` "Missing or invalid API key. Send Authorization: Bearer <key> with a key from the
  dashboard."
- Dashboard endpoints (`/api/*`): `Authorization: Bearer <JWT>` obtained from `POST /api/auth/refresh`
  after an OAuth sign-in. Browser origins are restricted to the dashboard (CORS).
- `POST /api/auth/refresh` reads the HttpOnly `cg_refresh` cookie that sign-in sets (30 days, path
  `/api/auth`), replaces it with a new one and returns `200 { "access_token", "expires_in", "user" }`
  (the access token lasts 15 minutes). Without the cookie it returns `204` with no body: nobody is
  signed in. An unknown, expired or revoked cookie (each one works once; sign-out revokes it too)
  returns `401` and is cleared. Treat `204` and `401` alike as signed out.

### Browser clients (CORS)

`POST /v1/moderate` and `GET /v1/quota` accept calls from a web page on any origin, which is how a
Unity WebGL build reaches the API. Because the request sends `Authorization` and
`Content-Type: application/json`, the browser first sends an `OPTIONS` preflight. The answer allows
any origin (`Access-Control-Allow-Origin: *`, no credentials) and may be cached for 2 hours
(`Access-Control-Max-Age: 7200`; Chrome and Firefox keep it that long, Safari at most 10 minutes).
So per page origin and endpoint, only the first request in each window pays the extra round trip.
Every response carries the CORS headers, errors (`401`, `403`, `429`) included, and scripts can
read `Retry-After`.

- Only publishable keys work from a browser. A server or test key on a request that carries an
  `Origin` header is refused with `403`, because anyone can read a key from a web page.
- The other `/v1` endpoints (`/v1/feedback`, `/v1/evidence`) do not allow browser origins. Call
  them from your server.
- A relay calls the API server-to-server. If it forwards requests as a reverse proxy, it must not
  pass the browser's `Origin` header on, or its server key is refused.

## POST /v1/moderate

Request (only `message` is required, plus `author.id` with a publishable key). The optional header
`X-ChatGuard-App` carries the game's bundle id (the Unity SDK sends `Application.identifier`, for
example `com.studio.game`). It identifies the game, not a player, and is only used to spot one game
spread across several Free organizations; a malformed value is ignored.

```json
{
  "message": "you are trash, uninstall the game",
  "author": { "id": "opaque-player-id", "account_age_days": 3, "prior_warnings": 2 },
  "thread": [ { "author": "opaque-other", "text": "gg everyone" } ],
  "channel": { "type": "global", "language": "en", "age_rating": "16+" },
  "request_id": "b2b7f2c6-1f0d-4f74-9d7f-3d5e0c1a9a11"
}
```

Constraints:

- `message`: required, not blank, at most 2,000 characters.
- `author.id`: at most 128 characters; required with a publishable (`cg_pub_`) key.
  `author.account_age_days` and `author.prior_warnings`: whole numbers from 0 to 100,000.
- `thread`: at most 50 entries. Every entry needs a non-empty `text` of at most 2,000 characters;
  its `author` is optional, at most 128 characters. The model only reads the end of the thread:
  the last 5 entries, each cut to 500 characters, and of those only as many of the newest as fit in
  about 600 tokens. Earlier entries are accepted but not read, and with evidence logging on only
  the part the model read is stored (see [Evidence](#evidence)).
- Use each line's player id as its `author`: the same value you send as `author.id` when that
  player writes. Erasing a player finds their lines in other players' stored thread context by this
  label (see [erasure](#delete-v1evidenceprojectidauthor_opaque_id)). The Unity SDK sample does
  this.
- The model never sees these labels. Before the call, the API replaces them with neutral stand-ins:
  lines whose `author` exactly matches this request's `author.id` (letter case counts) become
  `sender`, and each other label becomes `player 2`, `player 3` and so on, in the order it first
  appears in the lines the model reads (`player 1` onward when `author.id` is missing or empty).
  Lines with a missing or empty `author`, or labeled `unknown` (what Unity package 0.4.0 sends when
  it has no author), stay unlabeled. Stored evidence keeps the labels you sent.
- `channel.type`: one of global, team, dm, guild. `channel.language`: like `en` or `pt-BR` (two
  letters, optionally a dash and 2 to 4 more). `channel.age_rating`: one or two digits and a plus,
  like `16+`.
- `request_id`: 1 to 64 characters. The same id within 10 minutes replays the same response.

A request that breaks any of these gets `400` with an `errors` map that names each field (for
example `thread[3].text`). A body that is not valid JSON, or a value of the wrong type (such as a
number sent as a string), is refused with `400` too. A rejected request is not counted toward the
quota.

Response:

```json
{
  "id": "019968a3-4b2e-7c1d-9f0a-1e2d3c4b5a69",
  "action": "hide",
  "severity": 1.815,
  "verdicts": {
    "insult": { "p": 0.91 }, "threat": { "p": 0.03 }, "hate": { "p": 0.05 },
    "sexual": { "p": 0.01 }, "spam": { "p": 0.02 }, "trading": { "p": 0.0 }
  },
  "target": { "choice": "other_user", "confidence": 0.84 },
  "degraded": false,
  "degraded_reason": null,
  "cached": false,
  "quota": { "used": 1234, "limit": 10000, "window_ends_at": "2026-09-23T00:00:00+00:00" },
  "model": "jev-1.13.0",
  "latency_ms": 212
}
```

- `action`: allow | flag | hide | block, computed from the project's thresholds (dashboard →
  Thresholds). Defaults: block if threat ≥ 0.85 or severity ≥ 2.5; hide if insult/hate/sexual
  ≥ 0.80; flag if any category ≥ 0.55 or the model's Score confidence < 0.5.
- `severity`: 0–3. `target`: null when the model was not consulted.
- `degraded`: true when the model could not be consulted (`degraded_reason` is `quota`,
  `upstream`, `upstream_rate_limit` or `timeout`) and the local dictionary filter answered
  instead. `upstream_rate_limit` also covers the shared model allowance of Free organizations
  and of test traffic running out for the minute (see [Rate limits](#rate-limits)). Local verdicts are 0 or 1 and `model` is `local-filter/<word-list version>`. A
  block-rule hit is also answered locally, but with `degraded: false`, `target: null` and the same
  `local-filter/…` model. A response carries a model verdict exactly when `model` does not start
  with `local-filter/`; those are the responses counted toward the quota (except on test keys and
  idempotent replays, see "What counts toward the quota").
- `cached`: verdicts came from the 10-minute cache (same normalized message, project, resolved
  language, context rules and channel type); the action is still recomputed with current
  thresholds.
- `quota`: rolling 30-UTC-day usage of the organization, counted in model verdicts: `used` grows by
  one only when the response carries a model verdict (a fresh Jev evaluation or a hit in the
  10-minute verdict cache). Not counted: block-rule hits, degraded answers, test keys, idempotent
  `request_id` replays and rejected requests; see "What counts toward the quota" below. For test
  keys `quota` shows the organization's daily test allowance instead.

Errors: `400` validation problem (`errors` map), `401` missing or invalid key, `403` server or test key sent
from a browser (see [Browser clients](#browser-clients-cors)) or an organization suspended for
breaking the terms (`code: org_suspended`, on every `/v1` endpoint), `429` per-key or organization
rate limit or the daily test allowance (`Retry-After` header and `retry_after` seconds), `5xx`
unexpected.

## GET /v1/quota

`{ "used": 1234, "limit": 10000, "window_ends_at": "…", "plan": "free", "overage": false }`

`used` is the number of model verdicts the organization received in the rolling 30-UTC-day window
(all projects pooled); `limit` is the plan's included allowance; `overage` is true on plans that
keep checking above the allowance and bill the extra messages (every paid plan). The same counter
feeds the `quota` object of every `/v1/moderate` response, the dashboard usage chart and the daily
usage events sent to billing. With a test key this endpoint still shows the organization's plan
quota, not the daily test allowance.

Model verdicts are what counts. Free has its allowance per rolling 30 days, and above it answers
come from the local filter (`degraded_reason: "quota"`). Paid plans include an allowance each
billing month, and only messages above it are billed as overage, at the price on the
[pricing page](https://chatguard.dev/#pricing). Billing months follow the subscription's own
dates, so a rolling 30-day `used` above `limit` is not by itself what the next invoice bills.

### What counts toward the quota

A request counts at most once, and only when it receives a **model verdict**:

- Counted: a fresh Jev evaluation, and a hit in the 10-minute verdict cache (`cached: true`).
- Not counted: block-rule hits (answered by the project's block list without calling the model),
  degraded answers (`degraded: true`: quota exceeded, upstream failure, timeout, upstream rate
  limit), test keys (`cg_test_`), idempotent replays of a repeated `request_id`, and rejected
  requests (`400`, `429`).

Live (`cg_live_`) and publishable (`cg_pub_`) keys follow the same rule. In a response, a model
verdict is recognizable by `model` not starting with `local-filter/`; block-rule hits and degraded
answers both report `local-filter/<word-list version>` (only the latter sets `degraded: true`).

## POST /v1/feedback

`{ "verdict_id": "<id from the moderate response>", "kind": "false_positive" | "false_negative", "note": "optional" }`
(`request_id` may be given instead of `verdict_id`). Returns `201 { "id", "verdict_id" }`. Verdicts
are persisted asynchronously; a `404` shortly after moderation means "retry in a moment".

`kind` must be one of the two values above, either `verdict_id` or `request_id` is required, and
`note` is at most 1,000 characters. A request that breaks one of these, or has no body, gets `400`
with an `errors` map.

## DELETE /v1/evidence/{projectId}?author_opaque_id=…

GDPR erasure of one player in one project, at once:

- the player's evidence rows are deleted;
- on the player's verdict rows the author id, the message hash and the `request_id` are cleared;
- the player's lines are removed from the thread context stored with other players' evidence,
  wherever a line's `author` equals the erased id (so send player ids as thread authors, see
  [POST /v1/moderate](#post-v1moderate));
- the notes of feedback on the player's verdicts are cleared.

The scores, actions and times on those verdict rows stay until the rows expire. Call it with a
server or test key of the project; the key must belong to `projectId`. The id is matched exactly
and URL-encoded like any query value (`?author_opaque_id=steam%2F123` for `steam/123`). Returns
`{ "evidence_deleted": 3, "verdicts_anonymized": 12, "evidence_redacted": 2, "feedback_notes_cleared": 1 }`
(`evidence_redacted`: other players' evidence rows that lost the player's thread lines), or `400`
without `author_opaque_id`. The dashboard's "Erase a player" (project Settings) does the same, and
its "Export a player" downloads everything first: the player's verdicts, the feedback on them with
its notes, their evidence and their lines in other players' stored thread context.

The path form `DELETE /v1/evidence/{projectId}/{authorOpaqueId}` still works for ids without `/`.
**Ids containing `/` must use the query form:** an encoded slash is not decoded in a path segment, so
`…/steam%2F123` looks for the literal id `steam%2F123` and erases nothing.

## Evidence

Evidence logging is off by default. When an owner or admin turns it on for a project (paid plans),
each message sent to `POST /v1/moderate` is stored with its text and the part of the thread the
model read: at most the last 5 entries, each cut to 500 characters. Rows are kept for the project's
evidence retention (1 day up to the plan's maximum) and deleted by an hourly job once they expire.

- Shortening the retention, or moving to a plan with a shorter maximum, brings the expiry of rows
  already stored forward to the new limit; it never makes one later. A plan change also lowers a
  project's retention setting to the new maximum. On a plan without evidence logging no evidence
  is kept, so stored rows go at the next hourly run.
- Turning evidence logging off stops new rows; rows already stored expire on their dates.
- Owners and admins can delete all of a project's stored evidence at once, on every plan: "Delete
  stored evidence now" in the project Settings, or `DELETE /api/projects/{projectId}/evidence`
  (returns `{ "deleted": 42 }`).
- Export (Studio and Enterprise, owners and admins): `GET /api/projects/{projectId}/evidence/export`
  returns a JSON array of at most 100,000 rows, oldest first (by time, then id). When more rows
  remain, the response has an `X-ChatGuard-Next-Cursor` header: pass its value as `?after=` to get
  the next part. No header means that part was the last. An `after` that is not such a value gets
  `400`. The dashboard offers "Export the next part" until it is done.

## Health

`GET /healthz` (liveness) and `GET /readyz` (Postgres and Redis reachable; 503 otherwise).

## Rate limits

Per key (token bucket shared across API instances), configurable per tier:

| Tier | Server key (`cg_live_`) | Publishable key (`cg_pub_`) |
|---|---|---|
| Free | 20 req/s, burst 100 | 10 req/s, burst 50 |
| Indie | 50 req/s, burst 200 | 25 req/s, burst 100 |
| Studio | 200 req/s, burst 1000 | 100 req/s, burst 500 |
| Enterprise | 500 req/s, burst 2000 | 250 req/s, burst 1000 |

Publishable keys additionally get a bucket per `(key, author.id)` of 2 req/s with burst 10; a `429`
names the author bucket in `detail`. A coarse per-IP limit of 1,000 req/s applies per instance.

Free organizations also have one limit over all their keys together, 1 req/s with burst 20, so
extra keys do not add rate; a `429` names the organization in `detail`. Test keys and the dashboard
test panel share 1,000 requests per organization per UTC day, however many keys and projects there
are.

Fresh model calls also draw on shared per-minute allowances: 150 per minute for all Free
organizations together and 60 per minute for all test keys and test panels together. Paid
organizations have no such allowance.
When one runs out, the message is answered by the local filter with `degraded: true` and
`degraded_reason: "upstream_rate_limit"`, as when the model provider's own limit is reached; such
answers are not counted toward the quota. Verdict-cache hits use no allowance.

## Dashboard API (summary)

JSON, and a JWT is required except on the rows marked public:

| Method and path | Purpose |
|---|---|
| `GET /api/auth/providers`, `GET /api/auth/login/{google\|github}`, `POST /api/auth/refresh`, `POST /api/auth/logout`, `GET /api/auth/me` | OAuth sign-in and session (only `me` needs the JWT) |
| `GET/POST /api/orgs`, `GET/PATCH /api/orgs/{id}`, `GET /api/orgs/{id}/usage` | organizations; `POST` answers `409` (`code: free_org_limit`) while you already own a Free organization, and `429` after 3 new organizations in 24 hours |
| `GET/POST /api/orgs/{id}/members`, `PATCH/DELETE /api/orgs/{id}/members/{mid}` | members and invitations |
| `GET/POST /api/orgs/{id}/projects` | projects |
| `GET/PATCH/DELETE /api/projects/{id}` | project settings, thresholds, evidence toggle; `context_rule_limits` holds the context rule limits (next row) |
| `GET/POST /api/projects/{id}/keys`, `DELETE …/keys/{kid}` | API keys |
| `GET/POST /api/projects/{id}/rules`, `PATCH/DELETE …/rules/{rid}` | allow/block/context rules. Context rules: at most 20 per project (`409` past it), 300 characters each, and 1,200 characters all together, which is what the model reads with each message; a create or edit that takes them past that answers `400`. A project already past it (rules saved before the limit) has its context rules read oldest first up to 1,200 characters, and edits that shorten them are still accepted |
| `GET /api/projects/{id}/usage`, `…/verdicts`, `…/evidence`, `…/feedback` | logs and usage |
| `GET /api/projects/{id}/evidence/export?after=…` | evidence export in parts of up to 100,000 rows, oldest first; `X-ChatGuard-Next-Cursor` carries the `after` value of the next part (see [Evidence](#evidence)) |
| `DELETE /api/projects/{id}/evidence` | deletes all of the project's stored evidence, `200 { "deleted": <count> }`; owners and admins, every plan |
| `DELETE /api/projects/{id}/evidence/authors?author_opaque_id=…` | erasure from the dashboard (owners and admins), same scope as [`DELETE /v1/evidence`](#delete-v1evidenceprojectidauthor_opaque_id); the path form `…/evidence/authors/{author}` still works for ids without `/` |
| `GET /api/projects/{id}/authors/export?author_opaque_id=…` | everything the project stores for one player as JSON, for access requests: `verdicts`, `feedback` on them (with notes), `evidence`, and `thread_lines` (their lines in other players' stored thread context); owners and admins, every plan |
| `POST /api/projects/{id}/test` | dashboard test panel: runs the pipeline with optional draft thresholds, weights and rules (draft context rules within the same limits as saving); not metered or logged; returns raw and adjusted verdicts plus the reasons for the action |
| `GET /api/orgs/{id}/billing`, `POST …/billing/checkout`, `…/change`, `…/cancel`, `…/portal` | Polar billing |
| `POST /webhooks/polar` | Polar webhooks, signed by Polar (Standard Webhooks) instead of a JWT |
| `GET /api/plans` | tier configuration (public) |
| `GET /api/turnstile` | the bot-check site key for creating an organization, `null` while the check is off (public); `POST /api/orgs` then needs `turnstile_token` |
