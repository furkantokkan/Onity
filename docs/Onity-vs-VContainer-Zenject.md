---
title: "Comparison: VContainer & Zenject"
nav_order: 7
---

# Onity vs VContainer / Zenject

A per-axis comparison of three Unity dependency-injection containers — **Onity**,
**VContainer**, and **Zenject / Extenject** — written to read as an external
evaluation rather than marketing copy. It is maintained in the Onity repository,
and every benchmark number can be reproduced with the runner included in this
repository.

## Read this first — scope and caveats

- **Onity is a single Unity package** that unifies dependency injection,
  reactive programming, and events. This document compares only the **DI**
  axis against VContainer and Zenject (which are DI-only); the unified-scope
  advantage is covered as its own axis below.
- **The benchmark timing numbers are indicative, not guaranteed.** They were
  measured in the **Unity Editor with the Mono scripting backend** and in a
  **Windows IL2CPP player**, on a single Windows machine (Unity 2022.3.62f3).
  The Editor and IL2CPP player runs both used 512 warmup iterations / 8 samples /
  10,000 measured iterations per sample. Different Unity versions, scripting
  backends, and graph shapes can produce different
  absolute numbers and possibly different relative ordering. Treat the numbers
  as "this is what this Windows PC measured," not "Onity is always
  faster." They were produced by the Onity project's own `OnityDiBenchmarkRunner`
  and have **not been independently audited** — the runner ships in this
  repository specifically so any reader can reproduce, or challenge, them.
- **The published allocation figures were unreliable.** The committed run
  reported 0 B for VContainer and Zenject as well, which cannot be correct (a
  transient resolve allocates the instance it returns, and Zenject is
  allocation-heavy), so the `GC.GetTotalAllocatedBytes` delta was not capturing
  gross allocations. Those alloc numbers are withdrawn pending a corrected
  in-editor re-measure. The **timing** numbers are unaffected by this.
- Full DI benchmark detail:
  [`di-benchmark-summary.md`](https://github.com/furkantokkan/Onity/blob/main/Packages/com.onity.framework/Benchmarks/Results/di-benchmark-summary.md).
  The competitive roadmap and adopt/non-goal matrix:
  [`docs/Plan/07-Competitive-And-AI-Roadmap.md`](https://github.com/furkantokkan/Onity/blob/main/docs/Plan/07-Competitive-And-AI-Roadmap.md).

---

## Summary table

| Axis | Onity | VContainer | Zenject / Extenject |
| --- | --- | --- | --- |
| Resolve speed (Editor-Mono, indicative) | Fastest in this run with baked resolve | Behind Onity baked | Slowest of the three |
| Resolve speed (Windows IL2CPP player, indicative) | Fastest in this run with generated AOT activators | Behind Onity baked in this run | Slowest of the three |
| Build / registration speed (indicative) | Fastest in both current Editor/Mono and IL2CPP player prepare/register runs | Slower than Onity on prepare/register | Slow |
| Steady-state resolve allocation | resolve machinery designed allocation-free (a transient allocates the returned instance; alloc figures pending a corrected re-measure) | Low (codegen mode) | Higher |
| DI feature breadth | Feature-complete for common Unity needs | Broad | Broadest |
| Entry-point lifecycle | **Automatic, no registration** | Manual `RegisterEntryPoint` | Automatic |
| Collection / open-generic binds | Yes | Yes | Yes |
| Conditional / id binds, `Unbind` | No (deliberate non-goal) | Limited | **Yes** |
| IL2CPP / AOT | Generated AOT activators plus runtime-probed reflection fallback; current Windows player benchmark beats VContainer on the measured scenarios | Source-gen path | Broad AOT support |
| Unified DI + Reactive + Events | **Yes (one package)** | DI only | DI only |
| AI-friendliness / analyzer | **Usage guide + `ONITY001`–`ONITY006`** | None bundled | Partial (`ValidateAll`) |

---

## Axis-by-axis

### 1. Resolve speed

On the Editor-Mono benchmark machine, Onity resolved faster than both VContainer
and Zenject on every scenario:

| Scenario | Onity (Baked) | Onity (Reflection) | VContainer | Zenject |
| --- | ---: | ---: | ---: | ---: |
| Resolve Singleton | ~63 ns | ~164 ns | ~214 ns | ~2,866 ns |
| Resolve Transient | ~1,083 ns | ~943 ns | ~1,879 ns | ~12,356 ns |
| Resolve Combined | ~972 ns | ~1,233 ns | ~2,079 ns | ~17,248 ns |
| Resolve Complex (6-level graph) | ~22,905 ns | ~25,940 ns | ~42,158 ns | ~289,823 ns |

The Windows IL2CPP player run uses Onity's generated AOT activator registry for
the benchmark graph and keeps the same relative ordering:

| Scenario | Onity (Baked) | Onity (Reflection) | VContainer | Zenject | Onity Baked vs VContainer |
| --- | ---: | ---: | ---: | ---: | ---: |
| Resolve Singleton | ~17 ns | ~157 ns | ~79 ns | ~547 ns | ~+79% |
| Resolve Transient | ~191 ns | ~348 ns | ~576 ns | ~2,742 ns | ~+67% |
| Resolve Combined | ~232 ns | ~634 ns | ~794 ns | ~3,531 ns | ~+71% |
| Resolve Complex (6-level graph) | ~5,399 ns | ~6,095 ns | ~12,740 ns | ~61,072 ns | ~+58% |

On Mono/JIT, the speed comes from a process-wide compiled-activator cache
(`Expression.Compile` runs once per `ConstructorInfo`), compiled
field/property/method setters, a `[ThreadStatic]` lock-free argument-array pool,
and a per-plan per-slot constructor-dependency cache. On IL2CPP, generated
activators register direct `new T(...)` delegates before construction plans are
built, so the resolve path avoids `ConstructorInfo.Invoke` on AOT builds too.
There is no `builder.Build()` ceremony before a resolve, and the container has
no engine coupling (`Onity.DI` is `noEngineReferences: true`).

**Backend note.** Editor-Mono numbers should not be projected onto IL2CPP. The
current IL2CPP player benchmark proves the AOT activator path runs and beats
VContainer and Zenject on this benchmark graph, but it is still one Windows
machine and one graph shape. Re-run the player benchmark for your target device
before treating the ordering as a target-platform result.

### 2. Build / registration speed

Building (preparing and registering) a complex graph was substantially faster in
Onity on the benchmark machine:

| Scenario | Onity (Baked) | Onity (Reflection) | VContainer | Zenject |
| --- | ---: | ---: | ---: | ---: |
| Prepare & Register Complex (Editor/Mono) | ~61,044 ns | ~42,929 ns | ~150,730 ns | ~215,537 ns |
| Prepare & Register Complex (IL2CPP Player) | ~31,084 ns | ~24,958 ns | ~42,446 ns | ~66,386 ns |

Onity avoids VContainer's separate builder object and shares activation metadata
across the whole process. The baked mode adds a lean dense-id map during `Build()`
so explicit bindings can resolve without a dictionary lookup; it reuses the same
providers as the reflection path instead of compiling a second dependency graph.
**Caveat:** the very first build that compiles a type pays the
`Expression.Compile` cost; a process that builds many distinct graphs once each
will see less of this advantage, and on IL2CPP the generated AOT activator must
exist for each hot implementation type to avoid the reflection fallback.

### 3. Steady-state allocation

Onity's resolve machinery is **designed to avoid per-call managed allocation**
beyond the constructed instances themselves: generated/compiled activators, a
`[ThreadStatic]` pooled argument array, and cached construction plans mean a
singleton resolve from a warm container should not allocate, while a transient
resolve still allocates the instance it returns and a 6-level graph allocates
roughly one object per level. One-time operations such as the first compile of
an activator also allocate.

**The published allocation numbers for this axis are not trustworthy.** The
committed benchmark reported 0 B for VContainer and Zenject as well, which is
impossible for a transient resolve (it must allocate the returned instance) and
for Zenject's reflection-heavy model — so the `GC.GetTotalAllocatedBytes` delta
was not capturing gross allocations. Those figures are withdrawn pending a
corrected in-editor re-measure. VContainer's codegen path is genuinely
low-allocation and Zenject allocates more on the resolve path; the precise
per-op deltas for all three will be restated once re-measured.

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
- Child containers as the scoped lifetime; sync `Build()` and async
  `BuildAsync(ct)` startup.

**Where the competitors are still ahead on raw feature count:** Zenject in
particular offers conditional / contextual binds (`WhenInjectedInto`, `WithId`,
`FromSubContainerResolve`), `Unbind`/`Rebind`, and a deep memory-pool / factory
system. Onity **deliberately omits** conditional/id binds and `Unbind` — they
fight the predictable single-model and allocation-conscious hot-path goals — and
answers "two implementations of one interface" with collection injection or a
typed factory instead. VContainer offers a first-class `Lifetime.Scoped`
keyword; Onity models scope as a child container. If your project depends on
Zenject's conditional binding or sub-container resolve features, Onity does not
have a drop-in equivalent.

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
  value a resolve path designed to avoid per-call managed allocation on Mono,
  want automatic entry-point lifecycle without manual registration, want a
  compile-time analyzer and an AI-readable usage guide.
- **Choose VContainer** if you want a DI-only container and are fine pairing it
  with a separate reactive/event stack.
- **Choose Zenject / Extenject** if you depend on its conditional/contextual
  binding, sub-container resolve, memory-pool, or `Unbind`/`Rebind` features,
  and resolve performance is not your binding constraint.

The numbers and feature claims here reflect the current state of Onity and are
intended to be revised as target-device IL2CPP coverage and benchmark coverage
expand.
