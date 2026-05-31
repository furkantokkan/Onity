---
title: "ADR 0002: Reactive Thread-Pool Scheduling"
parent: "Architecture Decisions"
nav_order: 2
---

# ADR 0002: Reactive Thread-Pool Scheduling

## Status

Accepted.

## Date

2026-05-30.

## Context

Onity.Reactive already had `SelectAwait` and `WhereAwait`, which run callbacks
through `Task.Run`, and Unity frame streams exposed `OnityUnityThreadMode`
options. That left an API gap: users could not explicitly choose a managed
thread-pool hop or run CPU-bound pure C# reactive work with configurable
parallelism.

Unity Job/Burst is not a replacement for this gap. Managed reactive observers,
closures, `Subject<T>`, `ReactiveProperty<T>`, and DI-backed services are
managed object graphs and cannot execute inside Burst jobs. At the same time,
pure managed CPU work is a valid use case for .NET thread-pool scheduling as
long as UnityEngine API access is kept on the main thread.

## Decision

Add managed thread-pool scheduling to `Onity.Reactive` as engine-free core API:

- `ObserveOnThreadPool<T>()` re-posts source values onto a .NET thread-pool
  worker and preserves source order.
- `SelectOnThreadPool<TSource, TResult>(...)` runs CPU-bound selectors on the
  .NET thread pool with configurable max concurrency.

Keep Unity main-thread re-marshalling as a separate bridge concern through
`ObserveOnMainThread()` in `Onity.Unity.Reactive`.

Keep `OnityUnityThreadMode.JobMultiThread`, `BurstJobMultiThread`, and
`DotsEventDriven` as Unity frame-stream boundaries. They do not imply managed
operator execution inside jobs or Burst.

## Alternatives Considered

### Expand `OnityUnityThreadMode` into managed operator scheduling

Rejected.

`OnityUnityThreadMode` is specific to Unity frame streams. Reusing it for core
managed operators would couple `Onity.Reactive` to Unity concerns and blur the
main-thread safety rule.

### Run managed observers inside Unity Jobs or Burst

Rejected.

Managed delegates, closures, arbitrary reference types, and DI-resolved
services are not Burst-safe. This would either fail technically or force Onity
to expose a separate data-oriented API that is no longer the same reactive
surface.

### Only document `SelectAwait` as the threading path

Rejected.

`SelectAwait` is sequential and async-oriented. It is useful for one-at-a-time
async workflows, but it is not explicit enough for CPU-bound concurrent work.

## Consequences

- `Onity.Reactive` remains engine-free and keeps `noEngineReferences: true`.
- Thread-pool operators allocate and schedule work by design; they are opt-in
  and not part of the allocation-free synchronous hot path.
- Downstream observers after thread-pool operators run off the Unity main
  thread. Gameplay/UI chains must call `ObserveOnMainThread()` before touching
  UnityEngine APIs.
- `SelectOnThreadPool` emits in completion order when max concurrency is greater
  than one. Users who require source order should pass `maxConcurrency: 1` or
  use `SelectAwait`.
- Job/Burst/DOTS work remains appropriate for blittable bridge workloads, not
  managed reactive observer execution.

## Verification

- EditMode tests cover ordered `ObserveOnThreadPool` delivery, concurrent
  `SelectOnThreadPool` execution up to the configured limit, source-order
  preservation with `maxConcurrency: 1`, and invalid max concurrency.
- `dotnet build Onity.Reactive.csproj -nologo` passes on Unity `2022.3.62f3`
  project files.
