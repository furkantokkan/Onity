# Changelog

All notable changes to the Onity framework are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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

- Removed the ZLinq dependency — Onity now has **zero third-party runtime
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
allocation. The core uses no `System.Linq`; ZLinq is the only third-party runtime
dependency (used by the `Onity.Unity` layer).

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
    scenarios. (The published allocation figures were unreliable and are being
    re-measured — a transient resolve still allocates the instance it returns.)
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

[0.3.4]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.3.4
[0.3.3]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.3.3
[0.3.2]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.3.2
[0.3.1]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.3.1
[0.3.0]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.3.0
[0.2.1]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.2.1
[0.1.0]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.1.0
