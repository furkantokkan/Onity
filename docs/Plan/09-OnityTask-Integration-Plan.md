# 09 - OnityTask Integration Plan

This document defines the target and phased plan for replacing UniTask usage in
normal Onity projects with an Onity-owned async layer.

## Target

Make UniTask unnecessary for standard Onity projects.

Onity should provide one Unity-first async API for gameplay, scene loading,
network requests, reactive bridges, cancellation, and diagnostics, without
adding Cysharp/UniTask or any other non-Unity runtime dependency.

The target user-facing result:

```csharp
await OnityTask.NextFrame();
await OnityTask.Delay(1.5f);
await OnityTask.LoadScene("Game", OnProgress);
await OnityTask.PostJson<LoginRequest, LoginResponse>(url, request);
```

The target architecture result:

- Onity owns the async primitive: `OnityTask` and `OnityTask<T>`.
- Onity owns the Unity scheduler: PlayerLoop-based frame, delay, and operation
  continuations.
- Onity owns the common Unity bridges: `AsyncOperation`, scene loading,
  `UnityWebRequest`, cancellation, and context lifetimes.
- Onity Reactive can return Onity-native tasks from async operators.
- UniTask becomes an optional migration/interop topic, not the default
  recommendation.

## Non-goals

- Do not add UniTask as a runtime dependency.
- Do not clone every UniTask API before the real Onity use cases require it.
- Do not replace every .NET `Task` use in one pass.
- Do not move managed async work into Burst jobs.
- Do not publish zero-allocation claims until allocation benchmarks exist.

## Current baseline

Onity already has the beginning of this layer:

- `OnityAsync`
- `OnitySceneLoader`
- `OnityAsyncOperationExtensions`
- `OnityTaskTracker`
- `OnityTimeoutController`
- cancellation helper extensions

Today this is mostly a `Task`-based async helper surface. It is good enough for
correctness and API shape, but it is not yet a full Onity-owned awaitable
runtime. The next work should keep the existing behavior stable while moving
toward a lower-allocation OnityTask core.

## Architecture Direction

Split the async work into two layers.

### Engine-free async core

Module target: `Onity.Async`.

Responsibilities:

- `OnityTask`
- `OnityTask<T>`
- awaiters
- pooled task sources
- cancellation and timeout helpers
- task tracking diagnostics
- `Task` interop through `AsTask` / `FromTask`

This layer must not reference `UnityEngine`.

### Unity async bridge

Module target: `Onity.Unity.Async`.

Responsibilities:

- PlayerLoop scheduler
- `NextFrame`, `NextFixedFrame`, `NextLateFrame`
- scaled and unscaled delay
- `WaitUntil` / `WaitWhile`
- `AsyncOperation` await bridge
- scene loading helpers
- `UnityWebRequest` await bridge
- context-bound cancellation helpers

This layer can reference Unity APIs and should remain the only place where
Unity-specific async behavior lives.

### Reactive bridge

Module target: `Onity.Reactive`.

Responsibilities:

- `FirstOnityTask`
- `ToOnityTask`
- cancellation-aware reactive awaits
- event stream awaits from `IMessageBroker` / `OnityEventHub`

Reactive should not depend on Unity. Unity-specific observable awaits stay in
the Unity bridge.

## API Target

### Frame and delay

```csharp
await OnityTask.NextFrame();
await OnityTask.NextFixedFrame();
await OnityTask.NextLateFrame();

await OnityTask.Delay(1.5f);
await OnityTask.DelayUnscaled(1.5f);

await OnityTask.WaitUntil(() => m_isReady, cancellationToken);
```

### Scene loading

```csharp
await OnityTask.LoadScene("Loading");

await OnityTask.LoadScene(
    "Game",
    progress => m_progressBar.value = progress,
    cancellationToken);
```

### UnityWebRequest and POST

```csharp
LoginResponse response = await OnityTask.PostJson<LoginRequest, LoginResponse>(
    url,
    request,
    cancellationToken);
```

HTTP helpers should live in the Unity async bridge, not in the engine-free core.
They should expose typed request/response methods so gameplay code does not
spread raw URL, JSON, header, retry, and error parsing logic everywhere.

### Reactive/event awaits

```csharp
DamageEvent damage = await broker.Observe<DamageEvent>()
    .Where(x => x.TargetId == playerId)
    .FirstOnityTask(cancellationToken);
```

### Migration from UniTask

Before:

```csharp
await SceneManager.LoadSceneAsync("Game")
    .ToUniTask(progressProvider, cancellationToken: cancellationToken);
```

After:

```csharp
await OnityTask.LoadScene("Game", OnProgress, cancellationToken);
```

## Phased Plan

### Phase 0 - Baseline and measurement

Goal: know what the current Task-based implementation costs before replacing it.

- Add async timing and allocation benchmarks for:
  - `NextFrame`
  - `Delay`
  - `AsyncOperation`
  - scene load
  - `UnityWebRequest`
  - reactive `FirstAsync`
- Record Editor/Mono and IL2CPP player results.
- Keep UniTask only as an optional benchmark/reference dependency in comparison
  tooling, never in Onity runtime.

Exit gate:

- A benchmark report exists and names the hot allocation/timing paths.

### Phase 1 - OnityTask facade

Goal: give users the Onity API first, without destabilizing runtime behavior.

- Add `OnityTask` static entry points that wrap the existing async helpers.
- Keep current `Task` internals initially.
- Add docs and samples using `OnityTask`, not UniTask.
- Keep `OnityAsync` as a lower-level or compatibility surface until migration is
  complete.

Exit gate:

- Existing async behavior remains green.
- Standard docs no longer recommend UniTask for scene loading or basic gameplay
  async.

### Phase 2 - Unity async bridge

Goal: cover the common Unity async cases with Onity-native names.

- Add `AsyncOperation.AsOnityTask(...)`.
- Add scene load/unload/activation helpers under `OnityTask`.
- Add `UnityWebRequest.SendAsOnityTask(...)`.
- Add typed `GetJson` / `PostJson` helpers if the API shape stays small and
  testable.
- Add context lifetime cancellation helpers so a scene/context can cancel
  outstanding async work on destroy.

Exit gate:

- Scene loading, progress, cancellation, and web request tests pass in Editor
  and IL2CPP player smoke coverage.

### Phase 3 - Real awaitable core

Goal: replace Task-backed hot paths with an Onity-owned awaitable.

- Implement `readonly struct OnityTask`.
- Implement `readonly struct OnityTask<T>`.
- Implement custom awaiters.
- Add pooled task sources for async operations that need completion storage.
- Route exceptions and cancellation through predictable Onity semantics.
- Integrate with `OnityTaskTracker`.

Exit gate:

- Awaiting `OnityTask` and `OnityTask<T>` works in Editor/Mono and IL2CPP.
- Existing facade APIs can return the real Onity awaitable without user code
  changes.

### Phase 4 - PlayerLoop scheduler

Goal: remove unnecessary `Task` allocation from frame and delay hot paths.

- Add an Onity PlayerLoop scheduler.
- Implement `NextFrame`, `NextFixedFrame`, `NextLateFrame`.
- Implement scaled and unscaled delay.
- Implement `WaitUntil` / `WaitWhile`.
- Avoid per-frame managed allocation after warmup.

Exit gate:

- Steady-state scheduler benchmark is allocation-free after warmup.
- Cancellation does not leave leaked continuations.

### Phase 5 - Reactive and messaging bridge

Goal: make event streams and reactive streams awaitable without UniTask.

- Add `FirstOnityTask`.
- Add `ToOnityTask`.
- Add cancellation-aware event awaits.
- Keep event filtering in the observable chain (`Where`, `Select`, etc.) before
  awaiting.

Exit gate:

- A gameplay service can wait for a typed event, filter it, and continue through
  OnityTask only.

### Phase 6 - Migration docs and analyzer support

Goal: make migration from UniTask obvious and catch common async mistakes.

- Add `docs/Migration/From-UniTask.md`.
- Add examples for scene loading, progress, cancellation, web requests, and
  reactive awaits.
- Add analyzer coverage where useful:
  - forgotten OnityTask await
  - fire-and-forget without explicit handling
  - async work started from a context without cancellation ownership

Exit gate:

- A user can migrate a normal UniTask scene-loader example to Onity by following
  one guide.

### Phase 7 - Deprecate UniTask as a default recommendation

Goal: OnityTask becomes the standard path.

- Remove UniTask from normal Onity usage examples.
- Keep UniTask mentioned only as:
  - an inspiration/reference library
  - an optional migration topic
  - an optional interop bridge if a project already depends on it

Exit gate:

- The public docs present OnityTask as the default async path.
- Onity still has zero non-Unity third-party runtime dependencies.

## Performance Gates

Do not treat OnityTask as finished until the measured path is better than the
current Task-backed baseline for the common Unity cases.

Required evidence:

- Editor/Mono timing and allocation table.
- Windows IL2CPP player timing and allocation table.
- `NextFrame` and `Delay` steady-state allocation after warmup.
- `AsyncOperation` await allocation and progress callback behavior.
- `UnityWebRequest` await behavior with success, failure, timeout, and
  cancellation.
- Cancellation leak test for destroyed contexts.

Target direction:

- No per-frame managed allocation in the scheduler after warmup.
- No allocation per progress tick.
- No reflection or dynamic code requirement on IL2CPP.
- Exceptions and cancellation are visible to the caller, not swallowed.

## Risks

| Risk | Mitigation |
|---|---|
| Custom awaitable semantics become harder than expected | Ship facade first; replace internals only after benchmarks and tests exist |
| IL2CPP/AOT behavior diverges from Editor/Mono | Run a player smoke test for every async milestone |
| `OnityTask` becomes a broad UniTask clone | Implement only Onity-owned use cases first: scene, operation, delay, web, reactive await |
| Task-backed and OnityTask-backed paths diverge | Keep one facade and swap internals beneath it |
| Cancellation leaks continuations | Tie owner/context cancellation into tests and diagnostics |

## First Implementation Slice

The first slice should be small:

1. Add `OnityTask` facade methods for scene loading, delay, and frame waits.
2. Keep existing `Task` internals for this slice.
3. Add docs examples that use `OnityTask`.
4. Add baseline async benchmarks.
5. Add Editor tests for progress, cancellation, and exception flow.
6. Add one IL2CPP smoke scene that awaits an `OnityTask`.

This gives users the intended API immediately and gives the project hard data
before replacing the internals with a custom awaitable runtime.

## Open Decisions

- Should `OnityTask` use a custom async method builder, or is a custom awaiter
  enough for the first real awaitable release?
- Should HTTP helpers be core Onity API or a separate
  `Onity.Unity.Networking` surface?
- Should `OnityAsync` remain forever as an alias/facade, or be deprecated once
  `OnityTask` is stable?
- Should UniTask interop live in a separate optional package such as
  `Onity.UniTask`, or only in migration documentation?
