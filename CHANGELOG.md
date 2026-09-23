# Changelog

## 0.3.0 — 2026-09-23

Cancellation tokens for `Moderate`, a stricter build check for test keys and the game's bundle id on every request
(first three entries), then performance work with no API changes. Normalized text, JSON bodies and results are
identical to 0.2.1, checked by differential tests on .NET and on Unity 2021.3's Mono; apart from those three entries,
the two fixes at the end are the only intended behaviour changes. Figures are for Unity 2021.3's embedded Mono runtime (the one the editor and Mono
players use); the server round trip figures were measured in a Mono player, and its IL2CPP figure is labelled.

- `Moderate` takes an optional `CancellationToken`: `ChatGuardSdk.Moderate(text, playerId, token)` and
  `ChatGuardSdk.Moderate(text, playerId, onCompleted, token)`, the same two with a `ModerationRequest`, and
  `ChatGuardClient.Moderate(request, token)` / `Moderate(request, onCompleted, token)`. Cancelling the token does what
  `op.Cancel()` does: the request is aborted, `onCompleted` and `Completed` are not invoked, and `await` throws
  `OperationCanceledException` carrying the token (after `op.Cancel()` it carries `CancellationToken.None`). A token
  that is already cancelled gives back a cancelled operation without sending a request. The SDK stops listening to the
  token when the operation finishes, so one long-lived token can serve every message, and a token cancelled on
  another thread takes effect on the main thread. A call without a token costs nothing extra; a live token adds about
  144 B per message (about 304 B when `Moderate` is called from inside an `async` method), because the SDK registers
  with it without capturing the execution context. `async` methods that await the operation keep their own
  `AsyncLocal` values as before; only a continuation passed straight to the awaiter's `OnCompleted` now runs in the
  context of whoever cancelled the token. Works with `destroyCancellationToken` (Unity 2022.2+) and UniTask's
  `GetCancellationTokenOnDestroy()`. 0.2.0 dropped tokens together with `ModerateAsync`; a token needs no thread or
  timer, so the SDK still uses no `System.Threading.Tasks` and tokens work on WebGL (`CancelAfter` is the exception:
  it needs a timer thread; use `TimeoutSeconds` for timeouts).
- Release builds refuse `cg_test_` keys: `ChatGuardBuildCheck` fails a non-development player build, Dedicated
  Server builds included, when a `ChatGuardConfig` under a Resources folder holds a test key, and says to ship a
  `cg_pub_` key or tick Development Build. Test keys are for the Editor and development builds; an organization's test
  keys and its dashboard test panel now share 1,000 requests per UTC day. Server (`cg_live_`) keys are still refused in
  players and allowed in Dedicated Server builds.
- Requests carry the game's bundle id (`Application.identifier`, for example `com.studio.game`) in an
  `X-ChatGuard-App` header (`ChatGuardClient.GameHeaderName`). It names the game, not the player, and lets Chat Guard
  notice one game spread across several Free organizations; nothing is refused because of it. A bundle id the API
  would not accept (anything but ASCII letters, digits, dots, dashes and underscores, or more than 200 characters) is
  left out. The value is read once per `ChatGuardClient`, so it adds no per-message allocation, and WebGL builds can
  send it because the API allows any header in its CORS preflight.
- Word lists load lazily. Each language is parsed on first use instead of all eight at once, and the generated
  list data is only created for the languages that are parsed. Clients with `OfflineBehavior.AllowAll` or
  `BlockAll` never load the lists. Local-filter clients parse their default language (or the fallback language)
  and English when they are constructed. Any other language a request names is parsed on the main thread the
  first time a message in that language falls back to the local filter, once per language: about 1.5–2 ms and
  150–240 KB, and about 3 ms and 0.4 MB for Serbian, whose list holds Latin and Cyrillic spellings. The first
  `ChatGuardClient` construction allocates about 0.2 MB instead of 3.1 MB with English as the default language,
  and about 0.35–0.6 MB with another default language, which is parsed as well. Word-list memory kept for the
  app's lifetime (the parsed lists plus the string literals the runtime keeps) drops from about 0.55 MB for all
  eight languages, which every client paid in 0.2.1, to about 75 KB when only English is loaded, more for each
  further language, and under 1 KB for `AllowAll`/`BlockAll` clients.
- The offline and degraded path allocates 62–72% less per message (about 2.9 KB instead of 10.5 KB for a
  60-character message) and runs 40–75% faster:
  - The local filter no longer computes a SHA-256 hash of each message; nothing in the client read it.
    `NormalizedMessage.Hash` now computes it on first read.
  - The normalizer skips steps that would not change the text: Unicode normalization for ASCII text, lowercasing
    for ASCII text without capital letters, and rebuilding the message token by token when no token needs a
    rewrite and the words are already separated by single spaces with no leading whitespace.
  - Category sorting no longer boxes enum values.
- A server round trip allocates 92–94% less: about 1.2 KB per message instead of 21.3 KB in 0.2.1 for a
  60-character message with author and a five-entry thread, and about 1.3 KB instead of 17.4 KB through
  `ChatGuardSdk.Moderate(text, playerId, onCompleted)`. On IL2CPP (Android, arm64) the round trip is under
  1.3 KB per message.
  - The request is written as UTF-8 into a reused buffer and copied into a native array that `UploadHandlerRaw`
    takes over, with no JSON string or `byte[]` allocated per call.
  - The response is read from the downloaded bytes by a reader that handles the server's plain JSON and passes
    anything else to `DownloadHandler.text` and `TryParseResponse`, so results are unchanged.
  - `BuildRequestJson` and `TryParseResponse` themselves allocate about half of what they did in 0.2.1: request
    JSON is written directly instead of through a dictionary tree, and the JSON reader returns strings without
    escape sequences as a single substring and parses numbers in place. A round trip whose response the byte
    reader passes on (one with an escape sequence, for example) allocates about 8.5 KB instead of 21.3 KB.
  - There is no per-call closure or `Stopwatch`, and the request URL and the `Authorization` header are prepared
    once per client.
  - What remains is `UnityWebRequest`'s own objects (about 0.6 KB per request, which Unity cannot reuse), the
    operation and the result. A live `CancellationToken` adds about 144 B per message.
- Truncated or malformed JSON: `TryParseResponse` returns null for every such body, as documented; some truncated
  ones threw `IndexOutOfRangeException` (`{"action":"hide",` for example). It also returns null for a null string.
  `MiniJson.Parse` throws `FormatException` for them instead, and rejects nesting deeper than 128 levels with a
  `FormatException`, so a deeply nested body can no longer overflow the stack, which ends the game. For such a
  200 response `Moderate` falls back as `OfflineBehavior` says (degraded reason `upstream`) with the error
  "unparseable response" instead of the exception's message.
- Basic Chat sample: it now shows its text on Unity 2021.3; it was loading the built-in font name that only
  exists from 2022.2. It also no longer writes the slider values into the `ChatGuardConfig` asset you assign to it.

## 0.2.1 — 2026-09-22

- Player builds fail early when a `ChatGuardConfig` asset under a Resources folder holds a `cg_live_` server key
  (`ChatGuardBuildCheck`, an `IPreprocessBuildWithReport`). Dedicated Server builds are exempt; publishable and test
  keys pass. This is a build-time complement to the runtime warning that already fires when a server key runs in a
  player.
- `package.json` links the changelog and the MIT license; the package is published under
  https://github.com/chatguard-dev/chat-guard-unity.

## 0.2.0 — 2026-09-22

- **Breaking:** `ChatGuardClient.ModerateAsync` and `UnityWebRequestAwaiter` are removed; the package no
  longer uses `System.Threading.Tasks` anywhere (WebGL has no threads). `ChatGuardClient.Moderate(request,
  onCompleted)` returns a yieldable `ModerationOperation` (`IsDone`, `Result`, `Completed`, `Cancel()`),
  and `ModerateCoroutine(request, onCompleted)` wraps it for `StartCoroutine`. Everything completes on the
  main thread; there are no cancellation tokens, `op.Cancel()` aborts the request instead.
- `ModerationOperation` is awaitable without Tasks: `ModerationResult result = await ChatGuardSdk.Moderate(text,
  playerId);` works from `async void` methods, Unity 2023.1+ `async Awaitable` methods and UniTask code.
  `GetAwaiter()` returns a struct implementing `INotifyCompletion` (`System.Runtime.CompilerServices`); no
  `Task` is created, the continuation runs on the main thread, awaiting a cancelled operation throws
  `OperationCanceledException`, and it works on WebGL.
- New static API for the common case: `ChatGuardSdk.Moderate(text, playerId, result => ...)`,
  `ChatGuardSdk.ModerateCoroutine(...)`, the `Configure(settings | config | client | apiKey, baseUrl)`
  overloads and `Reset()`. Without `Configure` it loads the optional `Assets/Resources/ChatGuardConfig.asset`;
  when that is missing it logs one warning and uses the local filter only.
- New `ChatGuardSettings` (plain C#, same options and defaults as the asset) configures the SDK from code with
  no Resources asset: `ChatGuardSdk.Configure(new ChatGuardSettings { ApiKey = ..., BaseUrl = ..., ... })` or
  `new ChatGuardClient(settings)`, now the primary constructor (the `ChatGuardConfig` and positional
  constructors build a settings object). The settings are validated (`Validate()` throws `ArgumentException`
  for a timeout that is not a positive finite number of seconds or is above 600, or a base URL that is not an
  absolute http/https URL; an empty key or URL means local filter only) and copied. `ChatGuardConfig.ToSettings()`
  converts an asset and `ChatGuardClient.Settings` returns a copy of the active settings.
- `ChatGuardSdk.Configure(apiKey, baseUrl)` and the positional `ChatGuardClient(apiKey, baseUrl, ...)` constructor
  now send `channel.age_rating` `"16+"` by default, the same as `ChatGuardSettings.AgeRating` and the asset (pass
  null or empty `ageRating` to send none).
- The `ChatGuardConfig` asset's `baseUrl` now defaults to empty, the same as `ChatGuardSettings.BaseUrl` (an
  empty key or URL means local filter only until filled in); the example URL moved into the field's tooltip. The
  tester window says which of the two is missing.
- `ChatGuardSettings.TimeoutSeconds` is capped at 600 s (`ChatGuardSettings.MaxTimeoutSeconds`) and NaN or
  infinity are rejected; the budget is rounded up to whole seconds with a minimum of 1 s for `UnityWebRequest`.
- `ChatGuardUnityHook`: new `configureStaticApi` toggle (on by default) makes its config the
  `ChatGuardSdk` default in `Awake`; in-flight requests are cancelled in `OnDestroy`.
- The editor tester window and the Basic Chat sample use the new API.

## 0.1.0 — 2026-09-22

- Initial release: `ChatGuardClient`, `ChatGuardConfig`, `ChatGuardUnityHook`, editor tester window,
  local fallback filter shared with the server (`Runtime/Core`), Basic Chat sample.
