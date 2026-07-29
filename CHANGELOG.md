# Changelog

All notable changes to the Onity framework are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.3.7] - 2026-07-29

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

- Replaced editor-only `UnityEngine.Object.GetInstanceID()` usage with the
  compatible object hash identity path so Onity compiles on Unity `6000.5` while
  preserving Unity `2022.3` support.
- Bootstrap scene flow now waits for synchronous and asynchronous
  `ProjectContext` build callbacks before transitioning exactly once.
- Scene Flow Manager Apply now adds or repairs Bootstrap and Loading support
  idempotently and keeps Menu, Hub, and Gameplay groups distinct.

### Tested

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
