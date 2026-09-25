# Changelog

All notable changes to the Onity framework are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Added `OnityTask.SwitchToMainThread(cancellationToken)`, returning the
  `OnityTaskThreadSwitch` awaitable. Awaiting it on Unity's main thread
  completes synchronously without a frame delay (the 0 B/op design target for
  that path is not yet measured; see the comparison guide); awaiting it on
  another thread queues the continuation and resumes it during the Update phase
  through the task runner. Cancellation is observed at `GetResult` on the
  destination thread, including tokens that were already canceled. Outside
  Play Mode the Editor drains the queue from `EditorApplication.update` while
  it is not compiling or importing assets. Entering or exiting Play Mode and
  quitting the player start a new session, and continuations queued in an
  earlier session are discarded. A worker-thread switch requested before the
  runner exists, or after the runner was destroyed, recreates the runner
  through Unity's synchronization context. Thread-pool switching and timing
  selection are not included.
- Added focused EditMode, PlayMode, and Editor lifecycle tests for the thread
  switch, plus the `Onity/Benchmarks/Run OnityTask Thread Switch Benchmarks
  (Play Mode)` comparison harness. The tests were compile-checked outside
  Unity; the 2026-09-25 benchmark report cites 614 EditMode, 37 PlayMode, and
  26 analyzer passes whose counts match the suites at `3260c40`, and the
  harness ran in two Editor/Mono processes. The drain rewrite below and the
  tests added after `3260c40` have not run in Unity.

### Improved

- Rewrote the main-thread switch drain to array-backed double buffers with one
  session read per batch and an inline exception guard. Two Editor/Mono runs of
  the earlier `List<T>` drain measured main-thread dispatch about 25 to 36
  percent (24.7 to 36.1) slower than pinned UniTask while worker enqueue of
  4,096 continuations was 10.5 to 22 percent faster; the rewrite is not yet
  measured, and allocation counters were unavailable in those runs. A runner
  destroyed while continuations are queued now requests its replacement at
  once, and a fresh player domain keeps its first session so awaits requested
  before Onity's load hook still resume.

## [0.3.13] - 2026-09-24

### Added

- Added typed homogeneous two-input `OnityTask.WhenAny<T>(first, second)`,
  returning the winner index and value. It consumes both inputs, observes the
  loser, and rejects duplicate single-consumer native inputs.
- Added typed and untyped `OnityTask.Preserve()` for sharing one pooled native
  operation with multiple pending or late consumers. The original task is
  claimed once; completed, Task-backed, and completion-source tasks are returned
  without a new retained source.
- Added a two-input untyped `OnityTask.WhenAll(first, second)` overload. Two
  already successful inputs are consumed immediately. Eligible pending
  completion-source inputs use a pooled coordinator; other states retain the
  existing `Task.WhenAll` behavior.

### Fixed

- Kept the task tracker dictionary and display order consistent when a fault's
  custom `Exception.Message` clears or clears and retracks during completion.
  Error text is read outside the tracker lock; an already completed replacement
  entry retains its own completion time.
- Marked an input `AsTask()` fault bridge observed when the pending two-input
  coordinator consumes its fault, including bridges created during or after
  completion. Pending calls with an existing input bridge use the prior
  `Task.WhenAll` path.

### Improved

- Deferred typed `WhenAny` callbacks until inputs are pending, reducing
  completed-input allocation from 424 to 152 B/op in Unity 2022 Editor/Mono.
- Read completed multi-consumer results without taking the completion-source
  lock, while preserving the bridge/status publication order for concurrent
  completion.
- Removed the separate lock-object allocation from internal `Preserve()`
  sources. The measured pending native conversion fell from 264 to 248 B/task
  in the Unity 2022 Editor/Mono benchmark.
- Stored the `Preserve()` adapter directly in the native source's task-bridge
  slot, removing its bound callback allocation. Pending conversion fell from
  248 to 120 B/task; ordinary native awaiting stayed at 656 B/task in the
  calibrated lifecycle benchmark. The original task cannot consume the result
  while the adapter owns it.
- Returned the pending completion-source status without taking its gate when
  no .NET task bridge exists. A published bridge still uses the gate to keep
  terminal status ordered after bridge completion; all measured status reads
  remained at 0 B/op.
- Removed Task bridges from the already successful two-input `WhenAll` path.
  Calibrated Unity 2022 Editor/Mono scheduling allocation fell from 276 to
  0 B/op; the pinned UniTask comparison used 128 B/op. Pending calls saved
  64 B/op by avoiding the `params` input array. Already-completed calls do not
  add a tracker entry.
- Collected eligible already successful typed `WhenAll<T>` results directly in
  input order, without a .NET Task bridge or tracker entry. Duplicate
  single-consumer native inputs retain the previous conversion behavior, and
  native duplicate scans are bounded to 16 inputs before falling back. In the
  calibrated Unity 2022 Editor/Mono scheduling slice, completed two-input
  allocation fell from 392 to 40 B/op and four-input allocation from 592 to
  48 B/op; the pinned UniTask controls were 112 and 120 B/op respectively.
  Pending two-input Onity allocation remained at 1,408 B/op.
- Reused a static task tracker completion callback instead of allocating a
  capture and delegate per pending task. In the two-input `WhenAll` scheduling
  comparison with tracking enabled, allocation fell from 1,556 to 1,408 B/op;
  tracking-disabled allocation stayed at 544 B/op. ExecutionContext flow and
  the tracker result behavior were preserved.
- Reused a bounded internal coordinator for eligible pending untyped two-input
  completion-source `WhenAll` calls. The output remains Task-backed and
  shareable; faults retain argument order and dominate cancellation. In two
  calibrated Unity 2022 Editor/Mono passes, main-thread full-lifecycle
  allocation fell from 624.56 to 176 B/op for tracker-off success, 1,288.56
  to 880 B/op for fault, and 1,040.56 to 592 B/op for cancellation. Tracker-on
  allocation also fell in all three cases, but remains above pinned UniTask in
  the measured pass. The first observed pending schedule increased by 434 B;
  completed two-input scheduling stayed at 0 B/op. The
  [full report](https://github.com/furkantokkan/Onity/blob/b6233dd84533c517890ecb3705b7f285269b42ca/docs/assets/benchmarks/onity-pending-whenall-v6-bridge-fallback-2026-09-24.md)
  records first-call, bridge-present, saturation, timing, and worker limits.

### Tested

- At the previous `6f32e15` product revision, Unity `2022.3.62f3` EditMode
  `549/549` and PlayMode `23/23` passed, including
  native sharing, fault/cancellation propagation, source reuse, and main-thread
  continuation tests. The final bridge-publication adjustment passed `36/36`
  focused completion-source EditMode tests; the two-input `WhenAll` subset
  passed `7/7`. Both reentrant tracker regressions failed before their fixes
  and passed after them. Typed `WhenAll` covered completed and pending inputs,
  duplicate native sources, source consumption, faults, cancellation, and large
  result sets. The Release `Onity.Unity` build passed with no errors.
- At the integrated pending-pair revision `556602a`, independent Unity
  `2022.3.62f3` verification passed EditMode `579/579` and PlayMode `25/25`.
  `Onity.Unity` Release built with zero errors and 16 preexisting Unity
  reference warnings. The new coverage includes pending success, ordered
  faults, cancellation, preexisting and concurrent `AsTask()` bridges, worker
  completion, tracker context, and coordinator reuse.
- At the accepted typed `WhenAny` revision `8fa63ac`, local Unity
  `2022.3.62f3` verification passed EditMode `602/602` and PlayMode `28/28`.
  The Release `Onity.Unity` build had zero errors and 16 preexisting MSB3277
  reference warnings. The engine-free core Release build had zero warnings and
  errors, and analyzer/source-generator tests passed `26/26`. GitHub docs and
  core checks passed on PR #14; Unity CI was skipped because `UNITY_LICENSE`
  is not configured.
- A test-only Unity `2022.3.62f3` StandaloneWindows64 IL2CPP Player build
  succeeded with the accepted runtime (`OnityAsync` blob `1a7fdd8`). The
  `OnityTaskSafetyPlayModeTests` local XML recorded `20/20` passed with no
  failures, skips, or inconclusive results; the Player callback recorded 20
  starts and 20 finishes, and the Editor exited with code 0. This focused
  Player result is not a Player performance benchmark.

## [0.3.12] - 2026-09-23

### Added

- Added typed and untyped `OnityTaskCompletionSource` for completing retained,
  multi-consumer tasks from callbacks. Multiple pending or late awaiters can
  observe the same result, fault, or cancellation without consuming a pool token.
- Allowed the same completion-source task in both inputs of the two-task
  `WhenAny` overload while retaining the single-consumer guard for pooled tasks.

### Fixed

- Kept completion-source status reads coherent with an already completed .NET
  task bridge during concurrent completion.

### Improved

- Avoided Unity synchronization-context capture while creating a completion
  source's .NET task bridge, preserving asynchronous bridge continuations.
  Pending typed and untyped `AsTask()` conversion fell from 848 to 176 B/op
  in the calibrated Unity 2022 Editor/Mono comparison; pinned UniTask used
  120 B/op in the same workloads. The other 30 allocation metrics did not change.

### Tested

- Unity `2022.3.62f3`: EditMode `515/515` and PlayMode `21/21` passed,
  including concurrent completion, synchronization-context, and `AsTask()`
  bridge tests.
- Compared 16 completion-source scenarios with pinned UniTask `2.5.11` using
  eight timing and allocation samples per scenario. Results vary by workload;
  see the [comparison](https://furkantokkan.github.io/Onity/guide/onitytask-comparison.html)
  and raw reports.
- A separate calibrated `IsCompleted` probe allocated zero bytes for both
  libraries; Onity's pending read remained slower in this Editor/Mono run.

## [0.3.11] - 2026-09-23

### Fixed

- Completed the `AsTask()` bridge before a pooled OnityTask source can be
  released during concurrent completion and a second bridge request. This
  preserves typed results, faults, and cancellation tokens.

### Tested

- Unity `2022.3.62f3`: EditMode `480/480` and PlayMode `21/21` passed,
  including concurrent typed and untyped `AsTask()` regressions.

## [0.3.10] - 2026-09-23

### Added

- Added two-input `OnityTask.WhenAny` with a native result source. It consumes
  both single-consumer inputs and preserves the winner's fault or cancellation.
- Expanded the optional UniTask benchmark harness with matched typed and
  untyped async-method workloads at 128 and 4,096 concurrent operations.

### Fixed

- Kept typed `AsTask()` conversion from invoking user-defined result equality.
- Forwarded task-backed `UnsafeOnCompleted` registrations to the underlying
  unsafe awaiter in both typed and untyped tasks.
- Kept deferred scene loads reachable after cancellation or a progress callback
  failure, and activated prepared loads during loading-scene cancellation so
  Unity's async operation queue cannot remain stalled.
- Kept exceptions from native await continuations from stopping the task runner
  or faulting a newly rented pooled wait; unhandled callback errors reach the
  Unity log.
- Made positive scaled and unscaled delays wait until a later rendered frame
  when scheduled before the task runner's `Update`.

### Performance

- Removed Unity object equality from the cached OnityTask runner lookup on every
  scheduled frame wait.
- Stored synchronous successful `async OnityTask<T>` results inline. Suspended
  and exceptional methods continue to use .NET Task internals.

### Measurement

- Measured 16 matched OnityTask and pinned UniTask 2.5.11 workloads in Unity
  `2022.3.62f3` Windows Editor/Mono. Async-method scheduling remains slower for
  OnityTask in the measured cohorts; the faster `GetResult` slices exclude the
  suspended frame and continuation work. Allocation bytes are unavailable
  because the 64 KiB positive control failed calibration. No overall
  OnityTask superiority claim is made.

### Tested

- Unity `2022.3.62f3`: integrated EditMode `476/476` and PlayMode `21/21` passed.
- Engine-free core, analyzer, and source-generator Release builds passed with
  zero warnings; package metadata covered all `234/234` files.
- The 16-scenario Editor/Mono comparison completed in a separate host with
  official UniTask 2.5.11 pinned by Git commit. Raw reports accompany the
  GitHub release; IL2CPP player performance was not measured for this version.

## [0.3.9] - 2026-09-23

### Added

- Added `OnityTask.DelayFrames` and ordered typed `OnityTask.WhenAll<T>` results
  for common Unity async flows.

### Fixed

- Made `OnityTask.NextFrame` wait until a later rendered frame even when called
  from an `Update` callback before the Onity task runner.

### Tested

- Unity `2022.3.62f3`: EditMode `452/452` and PlayMode `15/15` passed.
- Roslyn analyzer and source-generator tests: `26/26` passed; engine-free core,
  analyzer, and source-generator Release builds passed; package metadata covered
  all `228/228` files.
- An independent code review found no actionable correctness or regression issues.
- No OnityTask versus UniTask allocation or throughput benchmark was run for
  this release; typed `WhenAll<T>` uses .NET Task interop and allocates.

## [0.3.8] - 2026-09-23

### Fixed

- Rebuilt the shipped Roslyn analyzer and source generator with compiler APIs
  compatible with Unity 2022.3, removing newer-Roslyn load warnings.
- Made generated activators compile on Unity 2022.3 by emitting an internal
  `ModuleInitializerAttribute` only when the target framework lacks it.
- Kept nested message and reactive dispatch stable when subscriptions change
  during callbacks, and restored throttle state after a callback throws.
- Allowed an async container build to retry after cancellation or failure,
  kept lifecycle registrations distinct by object identity, and completed
  disposal of remaining services when one disposal throws.
- Kept child context injection within its nearest owning context.

### Tested

- Unity `2022.3.62f3`: EditMode `445/445` and PlayMode `13/13` passed in the
  release candidate worktree.
- Roslyn analyzer/source-generator tests: `26/26` passed.
- Engine-free core, analyzer, and source-generator Release builds passed;
  package metadata coverage remained `228/228` files.

### Documentation

- Updated the release install pins and analyzer/source-generator build guidance.

## [0.3.7] - 2026-07-30

### Added

- Added a distinct `Hub` scene-flow state while preserving all existing enum
  values, plus a four-stage `Loading -> Menu -> Hub -> Game` ready profile.
- Added Bootstrap readiness and default UI Toolkit Loading scene initiators,
  including progress text and a non-interactive normalized slider.
- Added a Blank Template action that creates only an empty scene-flow profile.

### Changed

- Updated the Unity 2022.3-compatible package baselines to Input System `1.19.0`,
  Entities `1.4.8`, Burst `1.8.29`, Collections `2.6.8`, and Mathematics `1.3.3`.
- Updated first-party GitHub Actions to Node 24-backed major versions, removing
  the Node.js 20 runner deprecation warning.

### Fixed

- Added stable Unity metadata for the retained benchmark JSON so immutable UPM
  installs import it without repeated missing-meta errors.
- Replaced editor-only `UnityEngine.Object.GetInstanceID()` usage with the
  compatible object hash identity path so Onity compiles on Unity `6000.5` while
  preserving Unity `2022.3` support.
- Bootstrap scene flow now waits for synchronous and asynchronous
  `ProjectContext` build callbacks before transitioning exactly once.
- Scene Flow Manager Apply now adds or repairs Bootstrap and Loading support
  idempotently and keeps Menu, Hub, and Gameplay groups distinct.

### Tested

- Package metadata completeness validation passes for every non-hidden file in
  `Packages/com.onity.framework`.
- Unity `2022.3.62f3`: EditMode `439/439` and PlayMode `12/12` passed.
- Unity `6000.5.2f1`: EditMode `434/434` and PlayMode `10/10` passed.
- Unity `2022.3.62f3` focused scene-flow EditMode tests: `11/11` passed.
- Unity `2022.3.62f3` transition-store EditMode tests: `2/2` passed.
- Unity `2022.3.62f3` context-readiness/loading-gate PlayMode tests: `2/2` passed.

### Documentation

- Added a first-class OnityTask guide, current architecture overview, custom
  404 page, canonical metadata, and sitemap-backed GitHub Pages navigation.
- Corrected stale DI/reactive API claims, subscription lifetime examples,
  messaging-order wording, benchmark delta labels, and package setup examples.
- Added reproducible Jekyll dependencies plus strict build and internal-link
  checks for documentation pull requests and Pages deployments.

## [0.3.6] - 2026-07-12

### Added

- Added `OnityRaycastCommandBatch.Complete()` as an explicit synchronization
  point for scheduled raycast jobs.
- Added `-onityBenchmarkScenario <name>` to the IL2CPP benchmark runner for
  focused high-sample release gates.

### Changed

- Added a dense type-id provider slot to the standard generic DI resolve path,
  removing the steady-state `Dictionary<Type, ...>` lookup while preserving
  provider lifetime, diagnostics, rebind, parent fallback, and self-resolve
  behavior.
- Bumped the package and release pin examples to `0.3.6`.

### Fixed

- Added generation validation and single-consumer guards to pooled `OnityTask`
  sources so stale task copies cannot observe a later pooled operation.
- Routed `OnityTask` cancellation completion through the Unity runner so
  continuations resume on the main thread, including fixed-frame waits while
  `Time.timeScale` is zero.
- Returned sources materialized through `AsTask()` / `Forget()` to their pools
  after the independent `Task` completes.
- Made `OnityRaycastCommandBatch` own and complete pending jobs before reading,
  reusing, resizing, clearing, or disposing native buffers, and schedule only
  the active command range.
- Made the IL2CPP benchmark runner preserve menu scene/build-target state and
  delete only its generated temporary scene.
- Marked uncalibrated Editor allocation metrics unavailable, published them as
  `n/a`, and removed the stale zero-allocation comparison chart.

### Performance

- Windows IL2CPP focused singleton gate (`1000` samples, `10,000` resolves per
  sample): Onity standard/reflection `18.80 ns/op`, Onity baked `17.40 ns/op`,
  VContainer `94.39 ns/op`, and Zenject `435.24 ns/op`.
- The final full Windows IL2CPP suite keeps both Onity resolve lanes ahead of
  VContainer on every measured resolve and prepare/register scenario.

### Tested

- Unity EditMode: `434/434` passed.
- Unity PlayMode: `10/10` passed after adding OnityTask safety coverage.
- Editor/Mono DI, Windows IL2CPP DI, and OnityTask vs UniTask benchmarks rerun
  on Unity `2022.3.62f3`.

## [0.3.5] - 2026-06-21

### Added

- Added `OnityTask` / `OnityTask<T>` as the Onity-owned Unity async awaitable
  surface for frame waits, delays, scene loads, `AsyncOperation`,
  `UnityWebRequest`, reactive awaits, and async messaging bridges.
- Added OnityTask vs UniTask benchmark tooling behind `ONITY_BENCHMARKS`.
- Added a UniTask migration guide covering common async mappings and web/scene
  request examples.

### Changed

- Bumped package and release pin examples to `0.3.5`.

### Tested

- `dotnet build Onity.Unity.csproj -nologo`
- `dotnet build Onity.Tests.EditMode.csproj -nologo`
- `dotnet build onity-core-ci.csproj -c Release -nologo`

## [0.3.4] - 2026-05-31

### Changed

- Renamed the Unity event shortcut facade from `Onity` to `OnityEvent` so event
  publishing, subscribing, observing, and event hub access are explicit at call
  sites.
- Gated benchmark comparison assemblies behind `ONITY_BENCHMARKS` so package
  users do not need VContainer or Zenject installed unless they run benchmarks.
- Updated README and documentation examples to use `OnityEvent.Publish`,
  `OnityEvent.Subscribe`, and `OnityEvent.Observe`.

### Tested

- `dotnet build onity-core-ci.csproj -c Release -nologo`

## [0.3.3] - 2026-05-31

### Added

- Added Scene Flow ready profile presets that create missing loading, menu, and
  gameplay scenes under `Assets/Scenes/OnitySceneFlow`.
- Added one-click 2-scene and 3-scene Scene Flow setup paths for quick project
  bootstrapping.
- Added a Single Scene Setup preset that creates one gameplay scene with a
  `SceneContext`, prepares the runtime-loaded `ProjectContext` prefab, and
  applies Build Settings immediately.
- Added Unity event shortcuts over the auto-bound event hub, plus
  component-scoped overloads for `GameObjectContext` usage.

### Changed

- Removed bundled package samples and the `samples` manifest entry so the UPM
  package stays focused on the runtime, editor tooling, tests, and benchmarks.
- Consolidated editor entries under the `Onity/...` menu and removed duplicate
  `Tools/Onity` / `Window/Onity` aliases.
- Polished diagnostics, pool, observable, and task tracker toolbars so the search
  clear button is only drawn when needed and toolbar labels do not overlap.
- Updated docs to describe the package as sample-free and to use the consolidated
  `Onity/...` menu paths.

### Tested

- `dotnet build onity-core-ci.csproj -c Release -nologo`

## [0.3.2] - 2026-05-30

### Added

- Added an AOT-safe generated constructor activator registry for `Onity.DI`.
- Added `[OnityGenerateActivator]` and shipped `Onity.SourceGen.dll` as a
  Roslyn analyzer asset so marked DI types can register direct `new T(...)`
  activators before construction plans are built.
- Added IL2CPP player benchmark bootstrap support and configurable player
  benchmark iterations, samples, and warmup arguments.
- Added generated-activator EditMode coverage.

### Changed

- `ActivatorCompiler` now prefers registered generated activators before the
  runtime `Expression.Compile` / reflection paths.
- Updated README, comparison docs, IL2CPP guide, and performance plans with the
  latest Windows IL2CPP player benchmark results.
- Bumped package version and release pin examples to `0.3.2`.

### Tested

- `dotnet build tools/Onity.SourceGen/Onity.SourceGen.csproj -c Release`
- `dotnet build onity-core-ci.csproj -c Release`
- Windows IL2CPP player DI benchmark with 19 generated activators registered:
  Onity baked was faster than VContainer and Zenject on all five measured
  timing scenarios.

## [0.3.1] - 2026-05-31

### Changed

- Removed the external LINQ replacement dependency — Onity now has **zero third-party runtime
  dependencies**. `OnityUiPresenterFactory` uses a hand-rolled loop instead of
  `AsValueEnumerable`; install no longer needs NuGetForUnity.

## [0.3.0] - 2026-05-30

### Added

- Added engine-free reactive thread-pool scheduling:
  - `ObserveOnThreadPool()` re-posts values to a .NET thread-pool worker while
    preserving source order.
  - `SelectOnThreadPool(...)` runs CPU-bound selectors on the .NET thread pool
    with configurable max concurrency.
- Added EditMode coverage for thread-pool scheduling, configured parallelism,
  source-order preservation when concurrency is one, and invalid argument
  validation.
- Added ADR 0002 documenting the managed thread-pool boundary and why Unity
  Job/Burst modes remain separate from managed reactive operator execution.

### Changed

- Updated reactive architecture and migration docs to distinguish real managed
  thread-pool operators from the experimental Unity job/Burst frame boundary.
- Bumped package version to `0.3.0`.
- Updated UPM install documentation and release pin examples to `v0.3.0`.

### Tested

- `dotnet build onity-core-ci.csproj -c Release`
- Focused reactive thread-pool smoke coverage for ordered thread hops and
  configured selector parallelism.

## [0.2.1] - 2026-05-30

### Changed

- Optimized the baked DI build path by reusing the existing provider records in
  the baked graph instead of collecting unused dependency and activator data.
- Updated DI benchmark reports and charts. `Onity (Baked)` now beats VContainer
  in every measured Editor/Mono resolve and prepare/register scenario, including
  `Prepare & Register (Complex)`.
- Documented the DOTS/Burst boundary for DI performance: managed DI stays in the
  C# container; DOTS remains a bridge for blittable, batchable workloads.
- Refreshed the public VContainer/Zenject comparison with the latest 0.2.1
  benchmark numbers.
- Added message broker and MessagePipe migration examples to the events
  documentation.

### Tested

- `dotnet build Onity.DI.csproj`
- `dotnet build Onity.Tests.EditMode.csproj`
- Unity EditMode filtered baked parity suite: 15/15 passed.
- Unity batchmode DI benchmark generated
  `Benchmarks/Results/di-benchmark-latest.json`.

## [0.1.0] - 2026-05-30

First public preview. Three feature-complete pillars on a shared engine-free core
with one disposal model and hot-path machinery designed to avoid per-call managed
allocation. The core uses no `System.Linq`.

### Added

- **Dependency Injection (`Onity.DI`)**
  - Zenject-familiar fluent binding: `Bind<T>().To<C>().AsSingle()` /
    `.AsTransient()` / `.NonLazy()`, self-bind shorthand, `BindInstance<T>`,
    `BindInterfacesAndSelfTo<T>` / `BindInterfacesTo<T>`, and `BindFactory<...>`
    (0/1/2-parameter `IFactory<...>`).
  - `[Inject]` on constructor, field, property, or method; `Resolve<T>` /
    `TryResolve<T>` / `Inject(existing)` / `CanResolve(type)`; child containers as
    the scoped lifetime; sync `Build()` and async `BuildAsync(ct)` startup.
  - Optimized resolve path: process-wide compiled-activator cache
    (`Expression.Compile` once per `ConstructorInfo`), a `[ThreadStatic]`
    lock-free argument-array pool, and a per-plan per-slot dependency cache.
    Designed to avoid per-call managed allocation beyond the constructed
    instances themselves; beats VContainer and Zenject on all measured timing
    scenarios, while transient resolves still allocate the instance they return.
  - Member injection setters (field/property) wired into the activation pipeline.
  - Opt-in **BakedGraph** resolve path behind a feature flag for pre-resolved
    construction plans.
  - Actionable, fix-oriented errors: `OnityResolveException`,
    `OnityBindingException`, with opt-in binding-source attribution.

- **Reactive (`Onity.Reactive`)**
  - Primitives: `Subject<T>` (steady-state `OnNext` designed allocation-free) and
    `ReactiveProperty<T>` (built-in `DistinctUntilChanged`, emits current value on
    subscribe, `SetValue(T) -> bool`).
  - Synchronous operators: `Where`, `Select`, `DistinctUntilChanged`,
    `Skip`/`SkipWhile`, `Take`/`TakeWhile`, `StartWith`, `Scan`, `Pairwise`,
    `Merge`, `CombineLatest`, `Sample` - emit paths designed allocation-free.
  - Async / time operators: `Debounce`, `ThrottleLast`,
    `TakeUntil(CancellationToken)` / `TakeUntil(Task)`, `SelectAwait`,
    `WhereAwait`, plus `FirstAsync` / `ToTask` reactive-to-async bridges.
  - Exception isolation so one faulting observer cannot break sibling observers
    or the source stream; deterministic time testing via pluggable
    `OnityTimeProvider`.

- **Messaging (`Onity.Messaging`)**
  - Typed pub/sub broker: `IPublisher<T>` / `ISubscriber<T>` from
    `IMessageBroker`; steady-state `Publish` designed allocation-free, re-entrancy-safe
    (unsubscribe inside a handler is allowed).
  - `OnityEventHub` facade (`Publish<T>` / `Subscribe<T>` / `Observe<T>()`) and
    `broker.Observe<T>()` returning `IOnityObservable<T>`, so any event flows into
    the full reactive operator chain.
  - Keyed message channels and async publish/subscribe support.
  - Allocation-free diagnostics: `GetDiagnostics(List<...>)` and `ChannelCount`.

- **Unity integration (`Onity.Unity`)**
  - `MonoInstaller`, `ProjectContext` / `SceneContext` / `GameObjectContext`,
    auto-bound `IMessageBroker` + `OnityEventHub` per scope,
    `BindMessageChannel<T>()`.
  - Reactive Unity bridges: `EveryUpdate` / `EveryFixedUpdate` / `EveryLateUpdate`,
    `Timer`, `Interval`; lifetime helpers `AddTo(Component)` /
    `TakeUntilDestroy(Component)` / `TakeUntilDisable(Behaviour)`.
  - Input System reactive bridge: `PerformedAsObservable()` /
    `StartedAsObservable()` / `CanceledAsObservable()` and long-press observables.

- **Roslyn analyzer pack (ONITY001-ONITY006)** - six diagnostics that catch
  common misuse at compile time and steer code toward the correct Onity idiom.

- **Sample - "Coin Rush"** (`Samples/OnityShowcase`) - a runnable mini-game
  wiring all three pillars through a single installer with thin MonoBehaviours.

- **Documentation** - machine-readable AI Usage Guide (verified against source),
  Getting Started, architecture review, and migration guides from Zenject,
  VContainer, and R3 / UniRx.

### Tested

- Full EditMode suite green: **203/203** in Unity 6.4.
- Timing benchmarks captured on 2022.3.62f3 (Editor-Mono, Windows PC — indicative,
  not guaranteed). The resolve/publish/`OnNext`/`EveryUpdate` paths are designed to
  avoid per-call managed allocation, but the published allocation figures were
  unreliable and need a corrected in-editor re-measure; a transient resolve still
  allocates the instance it returns.

[0.3.13]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.3.13
[0.3.12]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.3.12
[0.3.11]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.3.11
[0.3.10]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.3.10
[0.3.9]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.3.9
[0.3.8]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.3.8
[0.3.7]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.3.7
[0.3.6]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.3.6
[0.3.5]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.3.5
[0.3.4]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.3.4
[0.3.3]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.3.3
[0.3.2]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.3.2
[0.3.1]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.3.1
[0.3.0]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.3.0
[0.2.1]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.2.1
[0.1.0]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.1.0
