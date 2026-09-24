# Changelog

## 0.5.0 — 2026-09-24

A shorter namespace, a rebuilt Basic Chat sample, a README that says up front what the service costs, and a
license of its own for the GitHub copy.

- The namespace is now `ChatGuard`. `ChatGuard.Core` and the class names are unchanged.
- `ModerationOperation.IsCancelled` is now `IsCanceled`, the spelling .NET uses (`Task.IsCanceled`).
- `package.json`: the author is Proper Assets (https://chatguard.dev), and `unityRelease` is `42f1`, the 2021.3
  patch the package is tested on. It now lists two dependencies: the UnityWebRequest module
  (`com.unity.modules.unitywebrequest`), so the package also compiles in a project that had that module turned
  off, and Unity UI (`com.unity.ugui`), which the Basic Chat sample uses.
- Basic Chat sample, rebuilt in Chat Guard's own look: paper windows on the pink page, a verdict log whose aligned
  stamps give each line's action, reason and time, and Last check with the selected line's six scores, severity and
  target. A player picker next to the message field chooses who speaks, one of five players, each with its own
  player id; **Play example** sends the example chat from chatguard.dev, one line every 820 ms; **Clear** empties
  the log, the chat and the context. Global chat shows only what other players get. Its sliders are labeled as
  local-decision thresholds: they apply only when the package answers on the device. On a portrait screen the log,
  Last check and the thresholds share one window as tabs, with 44 px controls. The UI is built in code with Unity
  UI, and checks still in flight are canceled when the sample is destroyed.
- The Basic Chat sample bundles the fonts Inter, Inter Tight and JetBrains Mono (SIL Open Font License 1.1), with
  each family's license text beside them. The new `Third-Party Notices.txt` at the package root lists them.
- Basic Chat logs no obsolete-API warning on Unity 2023.1 and newer: it finds the EventSystem through
  `EventSystem.current` instead of `FindObjectOfType`. In a project that reads input only through the Input System
  package, the EventSystem it creates gets an `InputSystemUIInputModule` instead of a `StandaloneInputModule`, which
  could not read input there. The sample now has its own assembly definition, which turns that on when the Input
  System package is installed.
- README: a note at the top that a Chat Guard account is required, with the Free plan's 10,000 messages every
  30 days, paid plans from $29 a month with extra messages at $0.25 per 1,000, and links to the rate limits,
  pricing, terms and privacy. The quick start says to open the sample scene,
  `Assets/Samples/Chat Guard/<version>/Basic Chat/BasicChat.unity`, before pressing Play, and what the sample
  shows. A Third-party notices section names the sample's fonts and their license. A Privacy section
  covers the bundle id every request carries (`X-ChatGuard-App`, which names the game, not a player; for Free
  organizations the service keeps it for 30 days), that messages are not used to train models, and that
  aggregate statistics that identify no player may improve the built-in word lists. A License section says
  which license covers each copy and that you may include the package, changed or unchanged, in the builds of
  your games and apps. The relay example lives in the GitHub repository and is not part of the Asset Store
  copy. The pinned-release example uses `#v0.5.0`.
- License: the GitHub copy now has its own license (`LICENSE.md` in the repository); releases before 0.5.0 keep
  the license they were published with. Copies from the Unity Asset Store are covered by the Unity Asset Store
  EULA.

## 0.4.1 — 2026-09-23

The Chat Guard Hook sends a player id, the build check also reads the configs your scenes use, and
requests the API would refuse are fixed before they are sent.

- `ChatGuardUnityHook.PlayerId` and `SetPlayerId(string)`, which a UnityEvent can call. `Moderate(message)` now
  sends `PlayerId` as the player id. Without one it sends a random id that the Hook creates the first time it
  needs one and keeps in `PlayerPrefs` under `chatguard.install_id` (`ChatGuardUnityHook.InstallIdKey`), one per
  installation, so per-player limits, export and erasure work and a publishable key gets the id it requires. It
  used to send no player id. The install id names no device or account, but it is a persistent pseudonymous
  identifier: mention it in your privacy notice, or set `PlayerId` before the first message so it is never
  created. To answer a player's request to export or erase their data, read their install id on the device with
  `PlayerPrefs.GetString(ChatGuardUnityHook.InstallIdKey)`, for example on a support screen. Dedicated Server
  builds never create it; there a message without a `PlayerId` still goes without a player id. The Hook only
  touches `PlayerPrefs` on the main thread. `Moderate(message, authorId)` is unchanged.
- The build check also inspects the `ChatGuardConfig` assets that the build's scenes, any asset under a Resources
  folder or the Preloaded Assets in Player Settings use, directly or through other assets (the config of a Hook in
  a scene, for example), not only the ones under Resources. The rules are the same: no `cg_live_` key in a player
  build (Dedicated Server builds are exempt), no `cg_test_` key in a release build. It reads the scene list of the
  build being made, so build scripts with their own list are covered too.
- Requests the API would refuse with a 400 because of one context field are fixed before sending. Thread entries
  that are null or have empty text (the default of `ThreadEntry.text`) are skipped and the last 5 of the others
  are sent; a null entry used to make `Moderate` fall back to the local filter, and an empty one made every
  message fall back while it stayed among the last five. Thread text over 2,000 characters is cut to 2,000, a
  thread author that is null or over 128 characters is sent as an empty label (it used to be `unknown`, which the
  model read as one more player), and `accountAgeDays` and `priorWarnings` above 100,000 are sent as 100,000.
- `ModerationResult.Error` for an HTTP error whose body is a problem description (the API's JSON errors) gives
  its title, detail, field errors, `code` and `retry_after` as plain text, in full. A suspended organization's
  answer used to be cut at 200 characters, in the middle of the support address; it now reads
  `HTTP 403: Organization suspended. This organization is suspended, so its API keys are refused. Contact
  support@chatguard.dev. (code: org_suspended)`. Other bodies are still quoted up to 200 characters.
- `Examples~/server-relay` answers with a `degraded_reason` the package reads (`upstream`, `upstream_rate_limit`
  for a 429, `timeout`) instead of `relay_upstream`, which read as no reason. A network error or a timeout gets
  the same degraded `allow` instead of an HTTP 500. It sends `language` only when the game gives one, so the
  project's default language applies instead of English, and it builds without nullable warnings.
- README: account, billing and technical help go to support@chatguard.dev, plans and custom volume to
  sales@chatguard.dev, and player requests to **Export a player** / **Erase a player** or `DELETE /v1/evidence`,
  never by email. Bugs go to GitHub issues and security problems to private reports. It also covers Git as a
  prerequisite for adding the package from a git URL, running the relay from a clone, the player id as the
  thread author label, the Hook's player id and how to find a player's install id, a troubleshooting row for a
  suspended organization, and `QuotaUsed` as usage over a rolling 30 days.
- The package repository has a bug report form that asks for the Unity, package and networking versions and
  warns never to paste keys or players' messages, a security policy (`.github/SECURITY.md`) with private
  reporting, and links for questions (Discord), account and billing (support@chatguard.dev) and plans
  (sales@chatguard.dev).

## 0.4.0 — 2026-09-23

The API key is the only setting you need.

- Requests go to `https://api.chatguard.dev` (`ChatGuardSettings.DefaultBaseUrl`) unless a base URL says otherwise.
  `ChatGuardSdk.Configure("cg_pub_...")` and `new ChatGuardClient("cg_pub_...")` work with the key alone (their
  `baseUrl` parameter is optional now), `ChatGuardSettings.BaseUrl` defaults to the API, and a new `ChatGuardConfig`
  asset comes with the URL filled in.
- **Behavior change:** a blank base URL (null, empty or whitespace) now means the default API instead of "local filter
  only", so a config asset saved by an earlier version with a key and an empty URL starts calling the API. Only an
  empty key keeps a client on the local filter: `HasServer` is true whenever a key is set, and the offline error reads
  "no API key configured". A base URL with spaces around it is trimmed.
- The tester window only points out a missing key, and the warning logged when nothing was configured shows
  `ChatGuardSdk.Configure("<your key>")`.
- The runtime warning about a `cg_live_` key no longer fires in Dedicated Server builds, which may hold one (the build
  check already allowed them).
- Basic Chat sample: without a config assigned it uses the key given to `ChatGuardSdk.Configure` (or the Resources
  asset) instead of always running the local filter.
- `Examples~/server-relay` calls `https://api.chatguard.dev` unless `CHATGUARD_BASE_URL` is set; it used to default to
  `http://localhost:5000`, its own address.
- README rewritten for onboarding: a five-step quick start, which key goes where for each setup (with a script that
  picks the key per build), integration examples for Netcode for GameObjects (2.7 and newer, and the older RPC pair for
  1.x and 2.0 to 2.6), Mirror, FishNet, Photon Fusion 2, Photon PUN 2, Photon Chat, Unity Vivox, Nakama, Colyseus and
  any other server, and tables for handling the result, configuration and troubleshooting. Test keys are no longer
  described as safe to commit: they can call every `/v1` endpoint, player-data erasure included.

## 0.3.0 — 2026-09-23

Cancellation tokens for `Moderate`, a stricter build check for test keys and the game's bundle id on every request
(first three entries), then performance work with no API changes. Normalized text, JSON bodies and results are
identical to 0.2.1, checked by differential tests on .NET and on Unity 2021.3's Mono; apart from those three entries,
the two fixes at the end are the only intended behavior changes. Figures are for Unity 2021.3's embedded Mono runtime (the one the editor and Mono
players use); the server round trip figures were measured in a Mono player, and its IL2CPP figure is labeled.

- `Moderate` takes an optional `CancellationToken`: `ChatGuardSdk.Moderate(text, playerId, token)` and
  `ChatGuardSdk.Moderate(text, playerId, onCompleted, token)`, the same two with a `ModerationRequest`, and
  `ChatGuardClient.Moderate(request, token)` / `Moderate(request, onCompleted, token)`. Canceling the token does what
  `op.Cancel()` does: the request is aborted, `onCompleted` and `Completed` are not invoked, and `await` throws
  `OperationCanceledException` carrying the token (after `op.Cancel()` it carries `CancellationToken.None`). A token
  that is already canceled gives back a canceled operation without sending a request. The SDK stops listening to the
  token when the operation finishes, so one long-lived token can serve every message, and a token canceled on
  another thread takes effect on the main thread. A call without a token costs nothing extra; a live token adds about
  144 B per message (about 304 B when `Moderate` is called from inside an `async` method), because the SDK registers
  with it without capturing the execution context. `async` methods that await the operation keep their own
  `AsyncLocal` values as before; only a continuation passed straight to the awaiter's `OnCompleted` now runs in the
  context of whoever canceled the token. Works with `destroyCancellationToken` (Unity 2022.2+) and UniTask's
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
- `package.json` links the changelog and the license; the package is published under
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
  `Task` is created, the continuation runs on the main thread, awaiting a canceled operation throws
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
  `ChatGuardSdk` default in `Awake`; in-flight requests are canceled in `OnDestroy`.
- The editor tester window and the Basic Chat sample use the new API.

## 0.1.0 — 2026-09-22

- Initial release: `ChatGuardClient`, `ChatGuardConfig`, `ChatGuardUnityHook`, editor tester window,
  local fallback filter shared with the server (`Runtime/Core`), Basic Chat sample.
