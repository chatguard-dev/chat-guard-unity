# Changelog

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
