---
title: "Comparison: VContainer & Zenject"
nav_order: 7
description: "A caveated, reproducible comparison of Onity, VContainer, and Zenject features and DI benchmark results."
---

# Onity vs VContainer / Zenject

A per-axis comparison of three Unity dependency-injection containers — **Onity**,
**VContainer**, and **Zenject / Extenject** — written to read as an external
evaluation rather than marketing copy. It is maintained in the Onity repository,
and every benchmark number can be reproduced with the runner included in this
repository.

## Read this first — scope and caveats

- **Onity is a single Unity package** that unifies dependency injection,
  reactive programming, and events. The main timing comparison is for **DI**;
  two separately measured managed pooling/factory workloads appear below.
  The unified-scope advantage is covered as its own axis.
- **The DI benchmark timing numbers are indicative, not guaranteed.** They were
  measured on a single Windows machine with Unity 2022.3.62f3: a September 2026
  Unity Editor/Mono run and a September 2026 Windows IL2CPP release player run. Each used
  512 warmup iterations, 8 samples, and arithmetic mean reporting. Your hardware,
  Unity version, IL2CPP vs Mono backend, and graph shape will produce different
  absolute numbers and possibly different relative ordering.
  Treat the numbers as "this is what one machine measured," not "Onity is always
  faster." They were produced by the Onity project's own `OnityDiBenchmarkRunner`
  and have **not been independently audited** — the runner ships in this
  repository specifically so any reader can reproduce, or challenge, them.
- The current seven-scenario run includes keyed and child-scope singleton
  resolution after the advanced DI changes. Conditional injection, rebinding,
  sub-container exports, and pool operations have correctness tests. Since
  `OnityObjectPool<T>` moved to its own stack, both the checked single-item
  Editor/Mono pool test and the checked two-parameter, 32-item IL2CPP factory
  test favor Onity over Zenject `MemoryPool`. Neither result ranks all pool use
  cases. See the [DI run summary](benchmarks/advanced-di-summary-2026-09-23.md),
  [pool experiment](benchmarks/factory-pooling-2026-09-23.md), and
  [own-stack pool measurements](benchmarks/pool-own-stack-2026-10-01.md).
- **Allocation bytes use a separate raw Profiler pass.** The Editor's
  `GC.GetAllocatedBytesForCurrentThread()` reported 0 B for a known 1 MiB
  allocation, and `GC.GetTotalAllocatedBytes(bool)` is unavailable. The
  timing runner therefore marks its inline byte fields N/A. A separate
  `RawFrameDataView` pass sums `GC.Alloc` sample metadata inside measured
  markers and validates empty, 1 MiB, and 2 MiB controls before reporting
  bytes. The earlier zero-allocation figures from the unvalidated counter
  remain withdrawn.
- **Onity is younger.** VContainer and Zenject have years of production use,
  large communities, and broad real-world hardening. Onity's DI is
  tested for common DI features, but its production track record and ecosystem are
  still small. That gap is real and is called out explicitly below.
- Latest raw reports: [Editor timing](benchmarks/advanced-di-editor-mono-direct-keyed/di-benchmark-latest.json),
  [Editor allocation](benchmarks/advanced-di-editor-allocation/di-allocation-bytes-latest.json),
  [Windows IL2CPP release timing](benchmarks/di-speed-il2cpp-full-2026-09-24.json),
  and [Windows IL2CPP Development allocation](benchmarks/advanced-di-il2cpp-allocation.json).
  The [September 24 measurement note](benchmarks/di-speed-followup-2026-09-24.md)
  records the focused repeat runs and their stability limits.
  The [earlier May chart summary](https://github.com/furkantokkan/Onity/blob/main/Packages/com.onity.framework/Benchmarks/Results/di-benchmark-summary.md)
  is historical.
  The competitive roadmap and adopt/non-goal matrix:
  [`docs/Plan/07-Competitive-And-AI-Roadmap.md`](https://github.com/furkantokkan/Onity/blob/main/docs/Plan/07-Competitive-And-AI-Roadmap.md).

---

## Summary table

| Axis | Onity | VContainer | Zenject / Extenject |
| --- | --- | --- | --- |
| Resolve speed (Editor-Mono, indicative) | Fastest in this run with baked resolve | Behind Onity baked | Slowest of the three |
| Resolve speed (current Windows IL2CPP release player run, indicative) | **Fastest in that run with generated activators** | Behind Onity baked | Slowest of the three |
| Build / registration speed (indicative) | Fastest in both current Editor/Mono and IL2CPP player prepare/register runs | Slower than Onity on prepare/register | Slow |
| Resolve allocation (Editor/Mono) | 0 / 16 / 16 / 384 B per op in singleton / transient / combined / complex cases; keyed and scoped singleton 0 B | Same measured bytes | 0 / 272 / 272 / 5,780 B; keyed and scoped singleton 0 B |
| Prepare/register allocation (Editor/Mono) | 10,364 B per op | 15,296 B per op | 23,166 B per op |
| Resolve allocation (IL2CPP Development) | 0 / 16 / 16 / 384 B per op in the same cases; keyed and scoped singleton 0 B | Same measured bytes | 0 / 200 / 200 / 4,800 B; keyed and scoped singleton 0 B |
| Prepare/register allocation (IL2CPP Development) | 10,424 B per op | 16,136 B per op | 23,405 B per op |
| DI feature breadth | Feature-complete for common Unity needs | Broad | Broadest |
| Entry-point lifecycle | **Automatic, no registration** | Manual `RegisterEntryPoint` | Automatic |
| Collection / open-generic binds | Yes | Yes | Yes |
| Conditional / id binds, `Unbind` / `Rebind` | **Yes; keyed speed and allocation measured** | Identified binds | **Yes** |
| Pool prewarm / fixed size / parameterized reuse | **Yes; correctness tests pass** | User-supplied pool | **Yes** |
| Checked managed pool/factory timing | Faster than Zenject in the one-item Editor test (0.58x–0.59x) and the 32-item, two-parameter IL2CPP Release factory test (0.73x–0.74x) | Not measured | Reference memory pool for both workloads |
| IL2CPP / AOT | Runtime-probed activation with generated activators and reflection fallback; current **DI** player timings beat VContainer on all measured paths | **Mature, source-gen path** | **Mature, broadly shipped** |
| Unified DI + Reactive + Events | **Yes (one package)** | DI only | DI only |
| AI-friendliness / analyzer | **Usage guide + `ONITY001`–`ONITY006`** | None bundled | Partial (`ValidateAll`) |

---

## Axis-by-axis

### 1. Resolve speed

On the Editor-Mono benchmark machine, Onity baked resolved faster than both
comparison containers in all seven measured scenarios. Values are ns/op:

| Scenario | Onity Standard | Onity Baked | VContainer | Zenject |
| --- | ---: | ---: | ---: | ---: |
| Resolve Singleton | ~106 | ~222 | ~210 | ~2,773 |
| Resolve Transient | ~689 | ~761 | ~1,872 | ~11,785 |
| Resolve Combined | ~547 | ~868 | ~1,886 | ~13,900 |
| Resolve Complex (6-level graph) | ~12,465 | ~12,464 | ~37,633 | ~272,418 |
| Resolve Keyed Singleton | ~140 | ~139 | ~252 | ~2,951 |
| Resolve Scoped Singleton | ~166 | ~435 | ~300 | ~2,819 |

The speed comes from a process-wide compiled-activator cache (`Expression.Compile`
runs once per `ConstructorInfo`), compiled field/property/method setters, a
`[ThreadStatic]` lock-free argument-array pool, a per-plan per-slot
constructor-dependency cache, and dense type-id provider slots for standard
generic resolves. The baked path builds its lookup graph at
`Build()`; the reflection path can resolve before that call. The container
has no engine coupling (`Onity.DI` is `noEngineReferences: true`).

The Windows IL2CPP release player run with 19 generated activators registered
also put Onity baked ahead in all seven scenarios. [Raw report](benchmarks/advanced-di-il2cpp-release.json).

| Scenario | Onity (Baked) | Onity (Reflection) | VContainer | Zenject | Result |
| --- | ---: | ---: | ---: | ---: | --- |
| Resolve Singleton | ~21 ns | ~138 ns | ~91 ns | ~463 ns | Onity baked fastest |
| Resolve Transient | ~129 ns | ~257 ns | ~529 ns | ~2,333 ns | Onity baked fastest |
| Resolve Combined | ~126 ns | ~395 ns | ~588 ns | ~2,901 ns | Onity baked fastest |
| Resolve Complex (6-level graph) | ~3,936 ns | ~4,321 ns | ~13,078 ns | ~61,883 ns | Onity baked fastest |
| Resolve Keyed Singleton | ~31 ns | ~32 ns | ~86 ns | ~574 ns | Onity baked fastest |
| Resolve Scoped Singleton | ~47 ns | ~253 ns | ~109 ns | ~563 ns | Onity baked fastest |

Editor-Mono numbers should not be projected onto IL2CPP. The player benchmark is
listed separately because AOT activation strategy matters; the current run used
`19` generated activators for the benchmark graph.

### 2. Build / registration speed

Building (preparing and registering) a complex graph was substantially faster in
Onity on the benchmark machine:

| Scenario | Onity (Baked) | Onity (Reflection) | VContainer | Zenject |
| --- | ---: | ---: | ---: | ---: |
| Prepare & Register Complex (Editor/Mono) | ~53,284 ns | ~38,451 ns | ~153,133 ns | ~183,996 ns |
| Prepare & Register Complex (IL2CPP release Player) | ~22,575 ns | ~17,375 ns | ~40,919 ns | ~63,623 ns |

Onity avoids VContainer's separate builder object and shares activation metadata
across the whole process. The baked mode adds a lean dense-id map during `Build()`
so explicit bindings can resolve without a dictionary lookup; it reuses the same
providers as the reflection path instead of compiling a second dependency graph.
The container starts with storage sized for a small scope and leaves unused
implicit-provider, plan, and callback backing arrays unallocated. Its maps
grow for larger scopes. The very first build that compiles a type pays the `Expression.Compile` cost on
JIT runtimes; a process that builds many distinct graphs once each will see less
of this advantage. On IL2CPP, generated activators remove the reflection
construction cost for generated constructor-injected types.

### 3. Steady-state allocation

Onity's resolve machinery is **designed to avoid per-call managed allocation**
beyond the constructed instances themselves: generated/compiled activators, a
`[ThreadStatic]` pooled argument array, and cached construction plans mean a
singleton resolve from a warm container should not allocate, while a transient
resolve still allocates the instance it returns and a 6-level graph allocates
roughly one object per level. One-time operations such as the first compile of
an activator also allocate.

The separate raw Profiler pass read `GC.Alloc` allocation-size metadata inside
each measured marker. An empty marker recorded 0 B; 128 arrays with 1 MiB
total payload recorded 1,053,728 B, and doubling their payload recorded
2,102,304 B. Each case used 3 samples of 64 operations after 512 warmups;
every sample in a case had the same bytes and events:

| Scenario | Onity Baked (B / events per op) | VContainer | Zenject |
| --- | ---: | ---: | ---: |
| Resolve Singleton | 0 / 0 | 0 / 0 | 0 / 0 |
| Resolve Transient | 16 / 1 | 16 / 1 | 272 / 4 |
| Resolve Combined | 16 / 1 | 16 / 1 | 272 / 4 |
| Resolve Complex (6-level graph) | 384 / 24 | 384 / 24 | 5,780 / 96 |
| Resolve Keyed Singleton | 0 / 0 | 0 / 0 | 0 / 0 |
| Resolve Scoped Singleton | 0 / 0 | 0 / 0 | 0 / 0 |
| Prepare & Register Complex | 10,364 / 109 | 15,296 / 238 | 23,166 / 419 |

Onity matched VContainer's resolve allocation and allocated less during
prepare/register. It allocated less than Zenject in each allocating case.
The [raw byte report](benchmarks/advanced-di-editor-allocation/di-allocation-bytes-latest.json)
includes every sample. The
[before-tuning report](benchmarks/2026-09-23-editor-mono-allocation-bytes-baseline.json)
shows baked prepare/register at 25,212 B per operation before container
backing-array sizing was reduced.

A separate Windows IL2CPP Development player captured `GC.Alloc` byte metadata
inside the same benchmark markers. Its empty marker recorded 0 B; the 1 MiB and
2 MiB controls recorded 1,053,728 B and 2,102,304 B. Each case had three
identical samples of 64 operations on its warmed container:

| Scenario | Onity Baked (B / events per op) | VContainer | Zenject |
| --- | ---: | ---: | ---: |
| Resolve Singleton | 0 / 0 | 0 / 0 | 0 / 0 |
| Resolve Transient | 16 / 1 | 16 / 1 | 200 / 3 |
| Resolve Combined | 16 / 1 | 16 / 1 | 200 / 3 |
| Resolve Complex (6-level graph) | 384 / 24 | 384 / 24 | 4,800 / 72 |
| Resolve Keyed Singleton | 0 / 0 | 0 / 0 | 0 / 0 |
| Resolve Scoped Singleton | 0 / 0 | 0 / 0 | 0 / 0 |
| Prepare & Register Complex | 10,424 / 109 | 16,136 / 238 | 23,405 / ~420 |

The [IL2CPP byte report](benchmarks/advanced-di-il2cpp-allocation.json)
includes all samples and controls. It measures managed allocations in a
profiler-enabled Development player. The separate release player run supplies
the timing numbers above; its allocation fields are unavailable.

### 4. DI feature breadth

Onity's DI now covers the feature axes most Unity projects need:

- Fluent binding (`Bind<T>().To<C>().AsSingle()/.AsTransient()/.NonLazy()`),
  self-bind shorthand, `BindInstance`, `BindInterfacesAndSelfTo` /
  `BindInterfacesTo`, and `BindFactory<...>` (0/1/2-parameter `IFactory<...>`).
- `[Inject]` on constructor, field, property, or method.
- **Collection injection** — `IEnumerable<T>`, `IReadOnlyList<T>`,
  `IReadOnlyCollection<T>`, `IList<T>`, `ICollection<T>`, `List<T>`, `T[]`.
- **Open-generic registration** — `Bind(typeof(IRepo<>)).To(typeof(Repo<>))`,
  closing the type on first resolve of `IRepo<Foo>`.
- **Identified and conditional binding** — `WithId`, `[Inject(Id = ...)]`,
  `WhenInjectedInto<TConsumer>()`, identified resolve, and local `Unbind` /
  `Rebind`. These preserve the ordinary unkeyed resolve path.
- Native `AsScoped()` caches per resolving container; `FromSubContainerResolve`
  exports a binding installed locally in a child container.
- Child containers; sync `Build()` and async
  `BuildAsync(ct)` startup.

**Remaining feature gaps:** Zenject has a deeper memory-pool / factory system,
including more binder variants. Onity's keyed and scoped resolve paths have
timing/allocation measurements; sub-container exports, conditional injection,
and pool operations have correctness coverage. The current checked one-item
Editor/Mono pool comparison favors Zenject: interleaved Onity/Zenject paired
medians were **1.429x/1.566x** after Onity's fast-slot change. A separate
Windows IL2CPP Release comparison of 32-item, two-parameter managed pooled
factories favors Onity: confirmatory paired medians were **0.803x/0.776x**
(**19.7%/22.4%** less elapsed time). Development runs recorded zero warmed
allocation events with a positive control. These are different workloads;
prefab pools and a VContainer pool adapter have no matched timing here.
[Method, raw samples, and historical-label correction](benchmarks/factory-pooling-2026-09-23.md).

### 5. Entry-point lifecycle

This is an axis where **Onity is ahead of VContainer** and on par with Zenject.
Implement `IOnityInitializable`, `IOnityTickable`, `IOnityFixedTickable`, or
`IOnityLateTickable` on a bound singleton and the container wires it up
automatically — `Initialize()` runs at the end of `Build()`, and the Unity
context pumps `Tick` / `FixedTick` / `LateTick` from `Update` / `FixedUpdate` /
`LateUpdate`. **No manual entry-point registration is required.**

VContainer requires you to register entry points explicitly
(`RegisterEntryPoint<T>()`); Zenject auto-collects `IInitializable` / `ITickable`
much like Onity does. So Onity matches Zenject's ergonomics here and improves on
VContainer's manual wiring.

### 6. IL2CPP / AOT

Onity's speed lead on Mono comes from `Expression.Compile`. On ahead-of-time
runtimes (IL2CPP, console AOT), runtime expression compilation can be unavailable,
interpreter-backed, or target-dependent, so Onity does not assume that a compiled
delegate is safe. It now has a generated AOT activator registry for hot DI types
and keeps the runtime probe (`RuntimeCompileSupport`) as the fallback gate:

- If a generated activator is registered for the selected constructor, Onity
  uses that direct `new T(...)` delegate first on every runtime.
- On JIT runtimes without a generated activator, the probe succeeds and the
  compiled fast path is used.
- On AOT/IL2CPP or restricted runtimes, if the probe detects a failed compiled
  delegate, the container **falls back to reflection-based activation** — slower
  per call, but allocation-comparable and guaranteed to run instead of crashing.
  Each compiler also wraps `Compile()` in try/catch for per-member safety.
- `OnityContainer.ForceReflectionActivation` lets you force the reflection path
  on a JIT runtime to pre-flight a graph under the exact strategy IL2CPP uses.

The fallback is covered by AOT fallback tests. The Windows IL2CPP player
benchmark now also proves that the benchmark graph runs in a player build,
registers 19 generated activators, records timings, and beats the local
VContainer/Zenject baselines in every measured scenario. Also note that runtime
open-generic registration relies on `MakeGenericType`, so the closed type must
survive IL2CPP stripping (reference it statically or preserve it).

### 7. Unified scope — DI + Reactive + Events

VContainer and Zenject are **DI containers only**. A typical project pairs them
with a separate reactive library (R3 / UniRx) and a separate message bus
(MessagePipe), giving four installs, four mental models, and four disposal
idioms.

Onity is **one package** spanning all three:

- `IMessageBroker` and `OnityEventHub` are **auto-bound in every scope** — no
  `AddMessagePipe()`-style setup line.
- `broker.Observe<T>()` returns the same `IOnityObservable<T>` as `Subject<T>`
  and `ReactiveProperty<T>`, so any event flows directly into the reactive
  operator chain with no hand-written adapter.
- Everything disposes the same way: `Subscribe` returns `IDisposable`, scoped
  with `AddTo(this)` (Unity) or `AddTo(CompositeDisposable)` (plain C#).

If you only need a DI container and already have a reactive/event stack you like,
this axis is irrelevant to you. If you want one coherent stack, it is the main
structural difference between Onity and a VContainer/Zenject + R3 + MessagePipe
combination — and the clearest reason a project would pick Onity over assembling
the three libraries separately.

### 8. AI-friendliness and compile-time analyzer

Onity ships a verified, machine-readable
[AI usage guide](Onity-AI-Usage-Guide.html) (every snippet compiles against the
current public API) and a [Roslyn analyzer pack](https://github.com/furkantokkan/Onity/blob/main/tools/Onity.Analyzers)
(`ONITY001`–`ONITY006`) with code fixes. The rules catch:

- `ONITY001` — `Resolve` inside `Update` / `FixedUpdate` / `LateUpdate`.
- `ONITY002` — binding/resolving after `Build()`.
- `ONITY003` — a `Subscribe` result dropped without `AddTo(...)`.
- `ONITY004` — multiple `[Inject]` constructors.
- `ONITY005` — an `[Inject]` member that cannot be injected (get-only property,
  indexer, generic method, static member).
- `ONITY006` — manual `new` on a type the same file binds/resolves through Onity.

Neither VContainer nor Zenject bundles a usage-guide-plus-analyzer pair like
this. Zenject offers a `ValidateAll` runtime/edit validation pass, which catches
missing bindings but is not a compile-time analyzer with inline fixes. Onity is
the only one of the three with this pairing; it matters most for AI-assisted or
large-team development, and is largely irrelevant to a solo developer who uses
neither AI assistance nor frequent onboarding.

## When to choose which

- **Choose Onity** if you want one coherent package for DI + Reactive + Events,
  value its fastest measured DI resolve paths in this repository's Editor-Mono
  and Windows IL2CPP comparisons,
  want automatic entry-point lifecycle without manual registration, want a
  compile-time analyzer and an AI-readable usage guide, and are comfortable
  adopting a younger framework.
- **Choose VContainer** if you want a mature DI-only container with a strong
  community and are fine pairing it with a separate reactive/event stack.
- **Choose Zenject / Extenject** if you need its deeper binder/memory-pool
  variants or its longer production record and larger community, and resolve
  performance is not your binding constraint.

The numbers and feature claims here reflect the current state of Onity and are
intended to be revised as target-device IL2CPP coverage and benchmark coverage
expand.
