# Changelog

All notable changes to the Onity framework are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
- Timing benchmarks captured on 2022.3.62f3 (Editor-Mono, one machine — indicative,
  not guaranteed). The resolve/publish/`OnNext`/`EveryUpdate` paths are designed to
  avoid per-call managed allocation, but the published allocation figures were
  unreliable and need a corrected in-editor re-measure; a transient resolve still
  allocates the instance it returns.

[0.2.1]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.2.1
[0.1.0]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.1.0
