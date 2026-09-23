# API reference

Base URL: `https://api.chatguard.dev` (EU). All bodies are JSON in snake_case. The complete machine
readable description is `openapi.json` next to this file (also served at `https://api.chatguard.dev/openapi/v1.json`).

## Authentication

- Moderation endpoints (`/v1/*`): `Authorization: Bearer <key>`. Keys are created per project in
  the dashboard and shown once. Three kinds:

  | Kind | Prefix | May call | Metered | Notes |
  |---|---|---|---|---|
  | Server | `cg_live_` | all `/v1` endpoints | yes (model verdicts) | keep on your server or relay |
  | Test | `cg_test_` | all `/v1` endpoints | no | 1,000 requests per organization per UTC day, shared by all test keys and the dashboard test panel; for the Editor and development builds |
  | Publishable | `cg_pub_` | `POST /v1/moderate`, `GET /v1/quota` only (others return 403) | yes (model verdicts) | safe to ship in a client build; `author.id` required; lower per-key limit plus a per-author bucket; results on a player's device are advisory |
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

Request (only `message` is required):

```json
{
  "message": "you are trash, uninstall the game",
  "author": { "id": "opaque-player-id", "account_age_days": 3, "prior_warnings": 2 },
  "thread": [ { "author": "opaque-other", "text": "gg everyone" } ],
  "channel": { "type": "global", "language": "en", "age_rating": "16+" },
  "request_id": "b2b7f2c6-1f0d-4f74-9d7f-3d5e0c1a9a11"
}
```

Constraints: `message` ≤ 2000 chars; `channel.type` ∈ global, team, dm, guild; `language` like
`en` or `pt-BR`; `age_rating` like `16+`; `thread` is truncated to the last 5 entries and about 600
tokens; `request_id` (≤ 64 chars) replays the same response for 10 minutes.

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

Errors: `400` validation problem (`errors` map), `401` invalid key, `403` server or test key sent
from a browser (see [Browser clients](#browser-clients-cors)) or an organization suspended for
breaking the terms (`code: org_suspended`, on every `/v1` endpoint), `429` per-key or organization
rate limit or the daily test allowance (`Retry-After` header and `retry_after` seconds), `5xx`
unexpected.

## GET /v1/quota

`{ "used": 1234, "limit": 10000, "window_ends_at": "…", "plan": "free", "overage": false }`

`used` is the number of model verdicts the organization received in the rolling 30-UTC-day window
(all projects pooled); `limit` is the tier's allowance. The same counter feeds the `quota` object of
every `/v1/moderate` response, the dashboard usage chart and the daily usage events sent to Polar,
so billed usage equals model verdicts.

### What counts toward the quota

A request counts at most once, and only when it receives a **model verdict** (rule decided
2026-09-22):

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

## DELETE /v1/evidence/{projectId}?author_opaque_id=…

GDPR erasure: hard-deletes the player's evidence rows and removes the id from verdict rows. The key
must belong to `projectId`. The id is matched exactly and URL-encoded like any query value
(`?author_opaque_id=steam%2F123` for `steam/123`). Returns
`{ "evidence_deleted": 3, "verdicts_anonymized": 12 }`, or `400` without `author_opaque_id`.

The path form `DELETE /v1/evidence/{projectId}/{authorOpaqueId}` still works for ids without `/`.
**Ids containing `/` must use the query form:** an encoded slash is not decoded in a path segment, so
`…/steam%2F123` looks for the literal id `steam%2F123` and erases nothing.

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

Publishable keys additionally get a bucket per `(key, author.id)` of 2 req/s with burst 10
(`Moderation:PubPerAuthorRps/Burst`); a `429` names the author bucket in `detail`. A coarse per-IP
limit of 1,000 req/s applies per instance.

Free organizations also have one limit over all their keys together, 1 req/s with burst 20
(`Plans:Tiers:free:OrgRateLimitRps/Burst`), so extra keys do not add rate; a `429` names the
organization in `detail`. Test keys and the dashboard test panel share 1,000 requests per
organization per UTC day (`Moderation:TestDailyCap`), however many keys and projects there are.

Fresh model calls also draw on shared per-minute allowances: 150 per minute for all Free
organizations together (`Moderation:FreeLanePerMinute`) and 60 per minute for all test keys and
test panels together (`Moderation:TestLanePerMinute`). Paid organizations have no such allowance.
When one runs out, the message is answered by the local filter with `degraded: true` and
`degraded_reason: "upstream_rate_limit"`, as when the model provider's own limit is reached; such
answers are not counted toward the quota. Verdict-cache hits use no allowance.

## Dashboard API (summary)

All under `/api`, JWT required, JSON:

| Method and path | Purpose |
|---|---|
| `GET /api/auth/providers`, `GET /api/auth/login/{google\|github}`, `POST /api/auth/refresh`, `POST /api/auth/logout`, `GET /api/auth/me` | OAuth sign-in and session |
| `GET/POST /api/orgs`, `GET/PATCH /api/orgs/{id}`, `GET /api/orgs/{id}/usage` | organizations; `POST` answers `409` (`code: free_org_limit`) while you already own a Free organization, and `429` after 3 new organizations in 24 hours |
| `GET/POST /api/orgs/{id}/members`, `PATCH/DELETE /api/orgs/{id}/members/{mid}` | members and invitations |
| `GET/POST /api/orgs/{id}/projects` | projects |
| `GET/PATCH/DELETE /api/projects/{id}` | project settings, thresholds, evidence toggle |
| `GET/POST /api/projects/{id}/keys`, `DELETE …/keys/{kid}` | API keys |
| `GET/POST /api/projects/{id}/rules`, `PATCH/DELETE …/rules/{rid}` | allow/block/context rules |
| `GET /api/projects/{id}/usage`, `…/verdicts`, `…/evidence`, `…/evidence/export`, `…/feedback` | logs and usage |
| `DELETE /api/projects/{id}/evidence/authors?author_opaque_id=…` | erasure from the dashboard (owners and admins); the path form `…/evidence/authors/{author}` still works for ids without `/` |
| `GET /api/projects/{id}/authors/export?author_opaque_id=…` | everything the project stores for one player (verdicts and evidence) as JSON, for access requests; owners and admins, every plan |
| `POST /api/projects/{id}/test` | dashboard test panel: runs the pipeline with optional draft thresholds, weights and rules; not metered or logged; returns raw and adjusted verdicts plus the reasons for the action |
| `GET /api/orgs/{id}/billing`, `POST …/billing/checkout`, `…/change`, `…/cancel`, `…/portal` | Polar billing |
| `POST /webhooks/polar` | Polar webhooks (Standard Webhooks signature) |
| `GET /api/plans` | tier configuration (public) |
