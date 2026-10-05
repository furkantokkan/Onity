---
title: "DI vs VContainer and Zenject"
parent: "Comparisons"
nav_order: 1
description: "Onity's dependency injection compared with VContainer and Zenject: feature shape, measured resolve, build, allocation and pooling results with their conditions, and when to choose which."
---

# DI vs VContainer and Zenject

This page compares Onity's container with the two Unity containers it replaces, VContainer and Zenject
(Extenject), axis by axis: measured resolve and build timing, allocation per operation, pooled factory
timing, feature breadth, lifecycle, IL2CPP, and what each one does beyond dependency injection. Every
number comes from a record in this repository, was measured on one Windows machine with the runners in
this repository, and keeps its conditions next to it. It is reproducible with those runners; it is not a
guarantee for other hardware, Unity versions or object graphs.

Contents: [Read this first](#read-this-first), [Summary table](#summary-table),
[Resolve speed](#1-resolve-speed), [Build and registration speed](#2-build-and-registration-speed),
[Steady-state allocation](#3-steady-state-allocation), [DI feature breadth](#4-di-feature-breadth),
[Entry-point lifecycle](#5-entry-point-lifecycle), [IL2CPP and AOT](#6-il2cpp-and-aot),
[One package](#7-one-package), [AI assistance and the analyzer](#8-ai-assistance-and-the-analyzer),
[When to choose which](#when-to-choose-which).

## Read this first

- Onity is one package for dependency injection, reactive state, typed messaging, factories with pooling
  and `OnityTask` async. This page covers the DI and pooling axes; the reactive, async and messaging
  comparisons are [Reactive vs R3 and UniRx](comparisons/reactive-vs-r3-unirx.html),
  [OnityTask vs UniTask](guide/onitytask-comparison.html) and
  [Messaging vs MessagePipe](comparisons/messaging-vs-messagepipe.html).
- The DI timing numbers are indicative, not a guarantee. The current run (2026-10-02) used Unity
  2022.3.62f2 and the published 0.6.0 package, whose DI code is unchanged through 0.8.1; the September 2026
  runs used Unity 2022.3.62f3. Each process ran 512 warmups and 8 samples of 10,000 operations and reports
  mean ns/op. Your hardware, Unity version, scripting backend and graph shape will give different
  absolute numbers and possibly a different ordering.
- The runners are `OnityDiBenchmarkRunner` and `OnityDiBenchmarkPlayerRunner` from this repository,
  and the results have not been independently audited; the runners ship so that any reader can reproduce
  or challenge them.
- Allocation bytes come from a separate raw Profiler pass, because the Editor's
  `GC.GetAllocatedBytesForCurrentThread()` reported 0 B for a known 1 MiB allocation. The pass sums
  `GC.Alloc` sample metadata inside each measured marker and validates empty, 1 MiB and 2 MiB controls
  first. A release Player reports no allocation; its timing fields are the comparison.
- Onity is the younger project. VContainer and Zenject have years of production use, large communities
  and broad real-world hardening; Onity's DI is tested for the features below, and its production track
  record and ecosystem are still small.
- Records: [DI re-measurement at the published 0.6.0 package, 2026-10-02](benchmarks/di-remeasure-2026-10-02.md)
  (raw: `di-remeasure-il2cpp-p1..p3-2026-10-02.json` beside it);
  [re-measurement at 7266dd8, 2026-10-01](benchmarks/remeasure-2026-10-01.md);
  [September 2026 DI summary](benchmarks/advanced-di-summary-2026-09-23.md) with its
  [Editor timing](benchmarks/advanced-di-editor-mono-direct-keyed/di-benchmark-latest.json),
  [Editor allocation](benchmarks/advanced-di-editor-allocation/di-allocation-bytes-latest.json),
  [Windows IL2CPP release timing](benchmarks/di-speed-il2cpp-full-2026-09-24.json) and
  [Windows IL2CPP Development allocation](benchmarks/advanced-di-il2cpp-allocation.json) reports, and
  the [September 24 measurement note](benchmarks/di-speed-followup-2026-09-24.md);
  [own-stack pool measurements, 2026-10-01](benchmarks/pool-own-stack-2026-10-01.md) and the
  [factory and pooling verification](benchmarks/factory-pooling-2026-09-23.md). Older numbers are in
  [Measurement history](archive/index.html).

## Summary table

| Axis | Onity | VContainer | Zenject / Extenject |
| --- | --- | --- | --- |
| Resolve speed, Windows IL2CPP release Player (2026-10-02, three processes) | fastest in all seven scenarios in every process; Onity Baked / VContainer 0.21 to 0.59 | behind Onity | slowest of the three |
| Resolve speed, Editor/Mono (2026-09-23 and 2026-10-01) | fastest in all seven scenarios | behind Onity | slowest of the three |
| Build and registration speed | fastest in the complex prepare-and-register scenario on both backends | slower than Onity | slowest |
| Resolve allocation per operation (raw Profiler, Editor/Mono and IL2CPP Development) | 0 / 16 / 16 / 384 B for singleton / transient / combined / complex; keyed and scoped singleton 0 B | the same bytes | 0 / 272 / 272 / 5,780 B (Editor), 0 / 200 / 200 / 4,800 B (IL2CPP) |
| Prepare-and-register allocation | 10,364 B (Editor), 10,424 B (IL2CPP) | 15,296 B, 16,136 B | 23,166 B, 23,405 B |
| DI feature breadth | collection and open-generic binds, identified and conditional binds, `AsScoped`, sub-container exports, `Unbind` and `Rebind`, sync and async build | broad | broadest |
| Entry-point lifecycle | automatic; binding the type is the registration | manual `RegisterEntryPoint` | automatic |
| Pooling and factories | prefab and plain pools with prewarm, fixed size, parameterized reuse and hooks; typed factories, no fluent factory bodies | user-supplied pool; factory delegates | `MemoryPool`, `MonoMemoryPool`, the broadest factory surface |
| Checked managed pool timing vs Zenject `MemoryPool` | one reused item, Editor/Mono: 0.590x and 0.579x (two runs); 32-item two-parameter burst, IL2CPP Release: 0.738x and 0.729x | not measured (no native pool) | reference pool |
| IL2CPP / AOT | generated activators first, a runtime-probed compiled path on JIT, reflection fallback | mature source-generated path | mature, broadly shipped |
| Beyond DI | reactive state, typed messaging, factories with pooling and `OnityTask` in the same package and lifetime model | DI only | DI only |
| AI assistance and compile-time checks | a source-verified usage guide, shipped guides and skill, analyzers `ONITY001` to `ONITY006` with a code fix | none bundled | `ValidateAll` at run time |

## 1. Resolve speed

Windows IL2CPP release Player, 2026-10-02, the published 0.6.0 package on Unity 2022.3.62f2, 19 generated
activators, three fresh processes; the values are medians of the three process means in ns/op. Onity Baked
was fastest in every scenario in all three processes. The `Onity Standard` column is the dense-provider
lane without the baked graph (the raw reports label it `Reflection`; reflection is only its activation
fallback):

| Scenario | Onity Baked | Onity Standard | VContainer | Zenject | Baked / VContainer per process | Baked / Zenject median |
| --- | ---: | ---: | ---: | ---: | --- | ---: |
| Resolve Singleton | 20.5 | 18.7 | 77.6 | 435 | 0.25 / 0.27 / 0.21 | 0.044 |
| Resolve Transient | 151.9 | 133.2 | 518.7 | 2,478 | 0.33 / 0.27 / 0.25 | 0.057 |
| Resolve Combined | 161.1 | 150.0 | 614.4 | 2,980 | 0.27 / 0.27 / 0.26 | 0.055 |
| Resolve Complex (6-level graph) | 5,053.4 | 5,184.1 | 12,944.2 | 64,717 | 0.38 / 0.39 / 0.37 | 0.077 |
| Resolve Keyed Singleton | 31.7 | 31.1 | 83.2 | 566 | 0.37 / 0.34 / 0.41 | 0.056 |
| Resolve Scoped Singleton | 53.5 | 233.4 | 112.9 | 565 | 0.41 / 0.47 / 0.55 | 0.094 |

The standard lane's scoped-singleton resolve (233.4 ns) is slower than VContainer's (112.9 ns); the baked
graph, which is the default after `Build()`, is what the ratios use. The machine was not idle (other Unity
Editors were open), so the ratios inside one process carry the comparison, not the absolute times.
[Record](benchmarks/di-remeasure-2026-10-02.md).

The 2026-10-01 re-measurement at `7266dd8` (Unity 2022.3.62f2, three processes) found the same ordering
with Onity Baked / VContainer ratios of 0.23 to 0.57 per scenario, and an Editor/Mono comparison in which
Onity Baked was faster than both in all seven scenarios (singleton 110 vs 186 vs 2,761 ns/op; complex graph
21,446 vs 63,779 vs 540,927 ns/op). [Record](benchmarks/remeasure-2026-10-01.md).

The September 2026 Editor/Mono run (Unity 2022.3.62f3, 2026-09-23), the first with the keyed and scoped
scenarios, in ns/op:

| Scenario | Onity Standard | Onity Baked | VContainer | Zenject |
| --- | ---: | ---: | ---: | ---: |
| Resolve Singleton | ~106 | ~222 | ~210 | ~2,773 |
| Resolve Transient | ~689 | ~761 | ~1,872 | ~11,785 |
| Resolve Combined | ~547 | ~868 | ~1,886 | ~13,900 |
| Resolve Complex (6-level graph) | ~12,465 | ~12,464 | ~37,633 | ~272,418 |
| Resolve Keyed Singleton | ~140 | ~139 | ~252 | ~2,951 |
| Resolve Scoped Singleton | ~166 | ~435 | ~300 | ~2,819 |

and the Windows IL2CPP release Player run of the same day with 19 generated activators
([raw report](benchmarks/advanced-di-il2cpp-release.json)):

| Scenario | Onity Baked | Onity Standard | VContainer | Zenject |
| --- | ---: | ---: | ---: | ---: |
| Resolve Singleton | ~21 ns | ~138 ns | ~91 ns | ~463 ns |
| Resolve Transient | ~129 ns | ~257 ns | ~529 ns | ~2,333 ns |
| Resolve Combined | ~126 ns | ~395 ns | ~588 ns | ~2,901 ns |
| Resolve Complex (6-level graph) | ~3,936 ns | ~4,321 ns | ~13,078 ns | ~61,883 ns |
| Resolve Keyed Singleton | ~31 ns | ~32 ns | ~86 ns | ~574 ns |
| Resolve Scoped Singleton | ~47 ns | ~253 ns | ~109 ns | ~563 ns |

Where the speed comes from: a process-wide compiled-activator cache on JIT runtimes (`Expression.Compile`
once per `ConstructorInfo`), compiled member setters, a `[ThreadStatic]` argument-array pool, a per-plan
constructor-dependency cache, dense type-id provider slots for standard generic resolves, and generated
activators on IL2CPP, which register direct constructor delegates before construction plans are built.
Editor/Mono numbers should not be projected onto IL2CPP; the Player runs are listed separately because the
activation strategy differs.

## 2. Build and registration speed

Preparing and registering the complex graph, ns/op:

| Run | Onity Baked | Onity Standard | VContainer | Zenject |
| --- | ---: | ---: | ---: | ---: |
| Windows IL2CPP release Player, 2026-10-02 (medians of three processes) | 31,549.8 | 26,752.5 | 58,001.7 | 91,436 |
| Windows IL2CPP release Player, 2026-09-23 | ~22,575 | ~17,375 | ~40,919 | ~63,623 |
| Editor/Mono, 2026-09-23 | ~53,284 | ~38,451 | ~153,133 | ~183,996 |

In the 2026-10-02 processes the Onity Baked / VContainer ratio was 0.59, 0.54 and 0.52, the closest of the
seven scenarios. Onity avoids a separate builder object and shares activation metadata across the process;
the baked mode adds a lean dense-id map during `Build()` so explicit bindings resolve without a dictionary
lookup, reusing the standard lane's providers instead of compiling a second graph. The first build that
compiles a type pays the `Expression.Compile` cost on JIT runtimes, so a process that builds many distinct
graphs once each sees less of this advantage.

## 3. Steady-state allocation

The resolve machinery is designed to avoid per-call managed allocation beyond the objects a resolve must
create: a warm singleton resolve should allocate nothing, a transient resolve allocates the instance it
returns, and a six-level graph allocates about one object per level. The raw Profiler pass of 2026-09-23
read `GC.Alloc` metadata inside each marker after validating an empty marker (0 B) and 1 MiB and 2 MiB
controls (1,053,728 B and 2,102,304 B); each case used 3 samples of 64 operations after 512 warmups, and
every sample in a case recorded the same bytes and events.

Editor/Mono, bytes and allocation events per operation:

| Scenario | Onity Baked | VContainer | Zenject |
| --- | ---: | ---: | ---: |
| Resolve Singleton | 0 / 0 | 0 / 0 | 0 / 0 |
| Resolve Transient | 16 / 1 | 16 / 1 | 272 / 4 |
| Resolve Combined | 16 / 1 | 16 / 1 | 272 / 4 |
| Resolve Complex (6-level graph) | 384 / 24 | 384 / 24 | 5,780 / 96 |
| Resolve Keyed Singleton | 0 / 0 | 0 / 0 | 0 / 0 |
| Resolve Scoped Singleton | 0 / 0 | 0 / 0 | 0 / 0 |
| Prepare and Register Complex | 10,364 / 109 | 15,296 / 238 | 23,166 / 419 |

Windows IL2CPP Development Player, same procedure:

| Scenario | Onity Baked | VContainer | Zenject |
| --- | ---: | ---: | ---: |
| Resolve Singleton | 0 / 0 | 0 / 0 | 0 / 0 |
| Resolve Transient | 16 / 1 | 16 / 1 | 200 / 3 |
| Resolve Combined | 16 / 1 | 16 / 1 | 200 / 3 |
| Resolve Complex (6-level graph) | 384 / 24 | 384 / 24 | 4,800 / 72 |
| Resolve Keyed Singleton | 0 / 0 | 0 / 0 | 0 / 0 |
| Resolve Scoped Singleton | 0 / 0 | 0 / 0 | 0 / 0 |
| Prepare and Register Complex | 10,424 / 109 | 16,136 / 238 | 23,405 / ~420 |

The 2026-10-01 re-measurement reproduced the Editor event counts exactly (0 for singleton, keyed and
scoped in every container; 1 for Onity and VContainer and 4 for Zenject on transient and combined; 24 and
96 on the complex graph; 109, 238 and 419 on prepare and register). Onity matched VContainer's resolve
allocation and allocated less during prepare and register; it allocated less than Zenject in every
allocating case. [Editor bytes](benchmarks/advanced-di-editor-allocation/di-allocation-bytes-latest.json),
[IL2CPP bytes](benchmarks/advanced-di-il2cpp-allocation.json),
[before-tuning baseline](benchmarks/2026-09-23-editor-mono-allocation-bytes-baseline.json).

## 4. DI feature breadth

Onity's container covers the feature axes most Unity projects need: fluent binding (`AsSingle`,
`AsScoped`, `AsTransient`, `NonLazy`), the self-bind shorthand, `BindInstance`, `BindInterfacesAndSelfTo`
and `BindInterfacesTo`, typed factories with `BindFactory`, `[Inject]` on constructors, fields, properties,
methods and parameters, collection injection in seven shapes, open-generic registration, identified and
conditional bindings (`WithId`, `[Inject(Id = ...)]`, `WhenInjectedInto`), local `Unbind` and `Rebind`,
sub-container exports (`FromSubContainerResolve`), child containers, and sync and async startup with
`IOnityInitializable` and `IOnityAsyncInitializable`.

Pooling: `OnityObjectPool<T>` keeps inactive items in its own array stack with an optional duplicate-return
check that runs in players too, `PrefabComponentPool<T>` pools prefab clones with `IPoolHooks`, both offer
prewarm, fixed capacity and parameterized reuse, and `BindPooledFactory` binds `IFactory<T>` and
`IPool<T>` in one line. Zenject has the broader factory surface (`FromMethod`, `FromIFactory`,
`PlaceholderFactory`); Onity has no fluent factory bodies, no `Instantiate(args)` and no `Func<>` factory
registration. VContainer stays DI-only.

Measured pooling, both against Zenject's `MemoryPool`, paired within one process: one reused item in the
checked Editor/Mono test, Onity checked / Zenject 0.590x and 0.579x in two runs after the own-stack change
(1.483x before it on the same day); the 32-item two-parameter burst in a Windows IL2CPP Release Player,
factory / Zenject 0.738x and 0.729x in an old/new/new/old sequence, and 0.809 and 0.801 in the 2026-10-01
re-measurement at the earlier source. Development runs recorded zero warmed `GC.Alloc` events with a
positive control. These are two workloads; prefab pools and a VContainer pool adapter have no matched
timing. [Own-stack measurements](benchmarks/pool-own-stack-2026-10-01.md),
[re-measurement](benchmarks/remeasure-2026-10-01.md),
[method and raw reports](benchmarks/factory-pooling-2026-09-23.md).

## 5. Entry-point lifecycle

Implement `IOnityInitializable`, `IOnityAsyncInitializable`, `IOnityTickable`, `IOnityFixedTickable` or
`IOnityLateTickable` on a bound singleton, scoped binding or instance, and the container collects it at
`Build()`: `Initialize()` runs at the end of `Build()`, `InitializeAsync` is awaited by `BuildAsync`, and the
Unity context pumps `Tick`, `FixedTick` and `LateTick`. No entry-point registration exists. VContainer
requires `RegisterEntryPoint<T>()`; Zenject auto-collects `IInitializable` and `ITickable` as Onity does.

## 6. IL2CPP and AOT

On JIT runtimes Onity's speed comes from `Expression.Compile`. Ahead-of-time runtimes (IL2CPP, console AOT)
may not compile expressions at all, or only through an interpreter, so Onity does not assume a compiled
delegate is safe:

- A generated activator (`[OnityGenerateActivator]`, emitted by `Onity.SourceGen`) is a direct `new T(...)`
  delegate and is used first on every runtime.
- Without one, a one-time probe compiles and invokes a representative expression; when it succeeds the
  compiled path is used, once per constructor for the life of the process.
- When the probe fails, or one constructor fails to compile, that constructor uses cached reflection:
  slower per call, allocation-comparable, and it runs instead of crashing the container.

The parity test suite exercises the reflection path with an internal switch, so the AOT fallback is
covered by tests rather than discovered on device. The Windows IL2CPP Player runs above registered 19
generated activators for the benchmark graph. Open-generic registration uses `MakeGenericType`, so a closed
type must survive IL2CPP stripping (reference it statically or preserve it). See
[Performance and IL2CPP](guide/performance-and-il2cpp.html).

## 7. One package

VContainer and Zenject are DI containers. A project pairs them with a reactive library (R3 or UniRx), a
message bus (MessagePipe) and an async library (UniTask): four installs, four idioms and four disposal
rules. Onity is one package with one lifetime model:

- `MessageBroker` and `OnityEventHub` are bound by every context, so there is no `AddMessagePipe()` setup
  line, and `Observe<T>()` returns the same `IOnityObservable<T>` as `Subject<T>` and
  `ReactiveProperty<T>`, so a message flows into the operator chain without an adapter.
- Every `Subscribe` returns an `IDisposable` retained with `AddTo(this)`, `TakeUntilDisable(this)`,
  `AddTo(scope)` or `AddTo(bag)`, across reactive state and messages alike.
- `OnityTask` work takes the scope token (`IOnityScopeLifetime`, `GetScopeCancellationToken()`), which the
  scope cancels before it disposes the services the work uses; `IOnityAsyncInitializable` is awaited by
  `BuildAsync`.
- Pools created by `BindPooledFactory` are disposed with the scope.

The measured reactive, async and messaging comparisons are on their own pages:
[Reactive vs R3 and UniRx](comparisons/reactive-vs-r3-unirx.html),
[OnityTask vs UniTask](guide/onitytask-comparison.html) and
[Messaging vs MessagePipe](comparisons/messaging-vs-messagepipe.html). If you only need a DI container and already have
a reactive and event stack you like, this axis is irrelevant to you; if you want one coherent stack, it is
the main structural difference between Onity and a VContainer or Zenject plus R3, MessagePipe and UniTask
combination.

## 8. AI assistance and the analyzer

Onity ships a source-verified [AI usage guide](Onity-AI-Usage-Guide.html), the guides and the `onity-use`
skill inside the package (`Documentation~/`, `AGENTS.md`, `llms.txt`), and the analyzer pack
`Onity.Analyzers` (`ONITY001` to `ONITY006`, source under `tools/Onity.Analyzers`): a `Resolve` in an update
method, a binding after `Build()`, a dropped `Subscribe` result, multiple `[Inject]` constructors, an
`[Inject]` member that cannot be injected, and `new` on a type the same file binds or resolves. Zenject
offers `ValidateAll`, a run-time and edit-time validation pass that catches missing bindings; neither
VContainer nor Zenject bundles a usage guide for coding assistants or a compile-time analyzer with a code
fix. This matters most for AI-assisted or large-team development.

## When to choose which

- Choose Onity when you want one package for DI, reactive state, messaging, pooling and async with one
  lifetime model, value the measured resolve, build and pooling results above, want the automatic lifecycle
  and the compile-time analyzer, and accept a younger framework.
- Choose VContainer when you want a mature DI-only container with a strong community and are fine pairing
  it with a separate reactive, event and async stack.
- Choose Zenject or Extenject when you need its deeper binder and memory-pool variants or its longer
  production record and larger community, and resolve performance is not your binding constraint.

The feature claims reflect Onity 0.8.1 and the numbers the measurements named above (the DI code is
unchanged since the measured 0.6.0 package); both are revised as target-device IL2CPP coverage and
benchmark coverage expand.
