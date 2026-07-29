# Onity Core Engineering

This document defines the engineering baseline for `Assets/Onity`.

It is intentionally implementation-focused and should be kept aligned with the
actual runtime/editor code.

## 1. Goals

- Deliver a Unity-first, low-friction DI + messaging + reactive core.
- Preserve Zenject-familiar ergonomics with simpler defaults.
- Keep hot paths allocation-aware and benchmark-oriented.
- Support optional DOTS bridges without moving managed DI into Burst jobs.

## 2. Non-Goals

- Full parity with every advanced Zenject binder edge case.
- Full framework replacement claims without benchmark evidence.
- Managed container resolution inside Burst jobs.

## 3. Package Boundaries

Core package:

- `Assets/Onity`

Split-ready optional packages:

- `Assets/Onity.Physics`
- `Assets/Onity.SkillStats`

Reference and comparison packages:

- `Assets/ThirdParty`
- `Assets/Onity/Benchmarks`

## 4. Assembly and Domain Model

Core runtime/editor domains:

- `Onity.Core`: shared primitives and utilities.
- `Onity.DI`: container, binding builders, injection pipeline.
- `Onity.Messaging`: broker and pub/sub abstractions.
- `Onity.Reactive`: observable primitives and operators.
- `Onity.Factory`: factory contracts and implementations.
- `Onity.Pooling`: pool abstractions and integrations.
- `Onity.Unity`: contexts, async helpers, scene flow, Unity bridges.
- `Onity.DOTS`: DOTS bridge systems and ECS integration points.
- `Onity.Editor`: diagnostics, validation, menu tooling.

Design rule:

- Keep directional dependencies one-way from high-level feature modules into
  lower-level abstractions.
- Avoid circular assembly references.

## 5. Runtime Lifecycle Architecture

### 5.1 Project Bootstrap

- `ProjectContextBootstrap` uses
  `RuntimeInitializeOnLoadMethod(BeforeSceneLoad)`.
- It attempts to load `ProjectContext` from:
  - `Resources/Onity/ProjectContext` (`ProjectContextBootstrap.ResourcePath`).

### 5.2 Context Initialization

All contexts derive from `OnityContext` and follow:

1. `Awake`
2. Create container (with optional parent)
3. Register default bindings
4. Execute installers
5. `Build()`
6. Auto-inject hierarchy (optional)

Then in `Start`:

- Run async post-build callbacks via `BuildAsync()` (optional).

### 5.3 SEP Scene Flow

Scene flow is provided in core via:

- `OnitySceneFlow`
- `OnitySceneLoader`
- `OnitySceneTransitionStore`
- `OnitySceneInitiator`
- `OnitySceneFlowProfile`
- `OnitySceneFlowStateMachine`

Expected pattern:

1. Bootstrap scene enters once.
2. State machine resolves target group or concrete scene to a transition plan.
3. Scene transition requests are stored as typed payload.
4. Loading scene consumes request and loads target scene.
5. Target scene initiator consumes active payload and initializes.

## 6. DI Engineering Rules

- Prefer constructor injection for service classes.
- Use method/field/property injection only when constructor injection is not
  practical (MonoBehaviour lifecycle, legacy compatibility).
- Bind to interfaces where substitution is meaningful.
- Keep installers declarative; no gameplay logic in installers.
- Use `RegisterBuildCallbackAsync` for post-build startup work that needs await.
- Do not register new bindings after container build.

## 7. Messaging Engineering Rules

- Use `IPublisher<TMessage>` and `ISubscriber<TMessage>` for decoupled flow.
- The broker convenience `Publish<T>(...)` / `Subscribe<T>(...)` are extension
  methods in `MessageBrokerExtensions` over `IMessageBroker.GetPublisher<T>()` /
  `GetSubscriber<T>()`; `OnityEventHub` is the ergonomic facade
  (`Publish<T>`, `Subscribe<T>`, `Observe<T>()`) for central manager-style access.
- Channels are keyed by message **Type only**. There is **no** keyed (per-key)
  messaging, no buffered/replay, no async handlers, no priority, and no
  request-response. Model "current state new listeners need" as a
  `ReactiveProperty<T>` in DI; model transient notifications as messages.
- `MessageChannel<T>` implements `IPublisher<T>` + `ISubscriber<T>` + `IDisposable`
  and **also** `IOnityObservable<T>`, so the `Observe<T>()` reactive bridge
  (`OnityMessageReactiveExtensions`, on `IMessageBroker` / `ISubscriber<T>` /
  `OnityEventHub`) composes events into the full operator chain.
- Prefer explicit message types over shared object payloads.
- Subscription lifetimes must be disposable and tied to scope/object lifetime
  (`AddTo(this)` in a MonoBehaviour, `AddTo(CompositeDisposable)` in plain C#).
- No per-publish allocation on steady-state paths is a target for hot channels;
  `MessageChannel<T>` uses the same `SubscriptionEntry[]` design as `Subject<T>`
  (steady-state `Publish` designed allocation-free, re-entrancy-safe).

## 8. Reactive Engineering Rules

The shipped reactive surface and its caveats are owned by
`docs/Plan/03-Reactive-Design.md`. This section mirrors it; keep the two in
sync and prefer the design doc when they disagree.

- The only public observable contract is `IOnityObservable<T>`. A second
  contract, `IOnityObservableV2<T>` (subscribe-with-`OnityObserver<T>`), is
  **internal** - the async/time operators are authored on v2 and bridged back
  to public `IOnityObservable<T>` via internal `ToV2`/`ToLegacy` adapters. Do
  not expose `IOnityObservableV2<T>` in public API or docs.
- Shipped factories (`OnityObservable` static): `Empty<T>()`, `Return<T>(value)`,
  `FromEvent<T>(addHandler, removeHandler)`. There is **no** `Never`/`Create`/
  `Defer` - those are Phase 2 (not yet shipped).
- Shipped synchronous operators (`OnityObservableExtensions`): `Where`, `Select`,
  `DistinctUntilChanged`, `Skip`, `SkipWhile`, `Take`, `TakeWhile`, `StartWith`,
  `Merge`, `CombineLatest` (2-arity only), `Sample`, `Scan`, `Pairwise`,
  `Subscribe`, `TakeUntilCancellation`, `FirstAsync`, `ToTask`.
- Shipped async/time operators (`OnityObservableAsyncExtensions`): `Debounce`,
  `ThrottleLast`, `TakeUntil(CancellationToken)`, `TakeUntil(Task)`,
  `SelectAwait`, `WhereAwait`. The time operator is named **`ThrottleLast`, not
  `Throttle`** - there is no leading-edge `Throttle`.
- `SelectAwait`/`WhereAwait` run the user callback on the thread pool (via
  `Task.Run`) and therefore **resume off the main thread**. Do not touch
  `UnityEngine` members directly in a `Subscribe` placed immediately after them.
- Shipped thread-pool operators (`OnityObservableExtensions`):
  `ObserveOnThreadPool` and `SelectOnThreadPool`. They are for pure managed work;
  any downstream code that touches `UnityEngine` must re-marshal with
  `ObserveOnMainThread()` in `Onity.Unity.Reactive`.
- `SelectOnThreadPool` emits results as worker tasks finish when max concurrency
  is greater than one. Use `maxConcurrency: 1` when source order matters.
- Unity streams (`OnityUnityObservable`): `EveryUpdate`, `EveryFixedUpdate`,
  `EveryLateUpdate`, `Timer`, `Interval`.
  - Unity stream threading mode (`OnityUnityThreadMode`):
    - `SingleThread`
    - `JobMultiThread` (Unity job boundary between frame ticks; not managed operator parallelism)
    - `BurstJobMultiThread` (Burst-friendly marker job boundary)
    - `DotsEventDriven` (DOTS accumulator boundary)
- Unity lifetime helpers (`ReactiveLifetimeExtensions`) extend `IDisposable`,
  not `IOnityObservable<T>`, and take `Component`/`Behaviour`:
  `AddTo(this IDisposable, Component)` (alias for `TakeUntilDestroy`),
  `TakeUntilDestroy(this IDisposable, Component)`,
  `TakeUntilDisable(this IDisposable, Behaviour)`. There is **no**
  `AddTo(GameObject)` / `TakeUntilDestroy(GameObject)` overload - pass `this`
  from a MonoBehaviour. `AddTo(this IDisposable, CompositeDisposable)` lives in
  `OnityDisposableExtensions` for non-Unity owners.
- Keep stream setup explicit and disposal deterministic.
- Prefer `Observe<TMessage>()` bridges when messages should compose with
  reactive operators instead of callback-only handlers.
- Bridge to task-based flows through `FirstAsync` and `ToTask` (both live on
  `OnityObservableExtensions`, not on the async facade).
- Cancellation tokens are required for async flows that can outlive scene state.
- Async timeout baseline uses:
  - `CancellationTokenSource.CancelAfterSlim(...)`
  - `OnityTimeoutController` for reusable per-call timeout pipelines.
- Unity async operations should expose `AsTask()`, `WithCancellation(...)`,
  and direct `await` support when that improves gameplay readability.

## 9. Pooling and Factory Rules

- Use `BindPooledFactory(...)` for common prefab spawn use cases.
- Pool reset behavior must be explicit and testable (`OnGet` / `OnRelease`).
- Object creation decisions should be centralized in installers/factories.
- Avoid direct `Instantiate` in gameplay loops when pooled alternatives fit.

## 10. DOTS, Jobs, Burst Boundaries

- Managed DI stays on main thread.
- DOTS systems consume plain/blittable data.
- Bridges may queue events from managed to ECS, then process in Burst-safe
  systems.
- Keep DOTS feature code in dedicated modules and behind clear API boundaries.

## 11. Performance Rules

- No LINQ in hot runtime paths.
- Cache reflection metadata/delegates for injection.
- Avoid boxing and repeated dictionary churn in resolve/publish loops.
- Keep `Update` and `FixedUpdate` paths allocation-free.
- Validate with both benchmark results and profiler traces.

## 12. Tooling and Diagnostics

Primary tools:

- `Onity/Diagnostics/Monitor`
- `Onity/Diagnostics/Container Diagnostics`
- `Onity/Diagnostics/Task Tracker`
- `Onity/Diagnostics/Observable Tracker`
- `Onity/Diagnostics/Pool Monitor`
- `Onity/Diagnostics/Scene Flow Manager`
- `Onity/Validation/Validate Scene`
- `Onity/Validation/Validate All Scenes`

Engineering intent:

- Fast inspection of registrations/resolution state.
- Visibility into long-running tasks.
- Optional stack trace capture for task leak root-cause analysis.
- Scene-level validation before play/build.

## 13. Testing Strategy

Baseline:

- EditMode tests in `Assets/Onity/Tests/EditMode/Scripts`.
- Each public API addition requires tests for success and failure paths.
- New edge-case behavior requires a dedicated regression test.

Recommended layers:

1. Container parity tests (Zenject-like behavior for supported subset).
2. Reactive operator behavior and cancellation tests.
3. Messaging lifetime and disposal tests.
4. Scene flow payload transfer tests.
5. Pooling correctness and allocation-safety tests.

## 14. Documentation and Style Governance

- Source-of-truth style guide: `codex-code-style.md` (repo root).
- Keep examples aligned with real API names.
- Avoid unverified claims (for example "always faster than X") in docs.
- Update this file when runtime architecture changes materially.

## 15. Release and Split Strategy

Current intended split:

1. `onity-core` (this folder, `Assets/Onity`)
2. `onity-physics` (`Assets/Onity.Physics`)
3. `onity-skillstats` (`Assets/Onity.SkillStats`)
4. `onity-benchmarks` + third-party comparison repo

Each split should preserve:

- Independent asmdefs
- Independent README/changelog
- Versioned package metadata
- CI compile + test coverage for that package boundary
