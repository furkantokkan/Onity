# Onity vs VContainer / Zenject

An honest, per-axis comparison of Onity's dependency-injection container against
the two containers it most directly competes with: **VContainer** and
**Zenject / Extenject**. The goal here is accuracy, not marketing. Where a
competitor wins, this document says so.

## Read this first — scope and caveats

- **Onity is a single Unity package** that unifies dependency injection,
  reactive programming, and events. This document compares only the **DI**
  axis against VContainer and Zenject (which are DI-only); the unified-scope
  advantage is covered as its own axis below.
- **The benchmark numbers are indicative, not guaranteed.** They were measured
  in the **Unity Editor with the Mono scripting backend, on a single Windows
  machine** (Unity 2022.3.62f3), 512 warmup iterations, 8 samples, median
  reported, allocation measured via `GC.GetTotalAllocatedBytes` deltas. Your
  hardware, Unity version, IL2CPP vs Mono backend, and graph shape will produce
  different absolute numbers and possibly different relative ordering. Treat the
  numbers as "this is what one machine measured," not "Onity is always faster."
- **Onity is younger.** VContainer and Zenject have years of production use,
  large communities, and broad real-world hardening. Onity's DI is
  feature-complete and tested, but its production track record and ecosystem are
  still small. That gap is real and is called out explicitly below.
- Full DI benchmark detail:
  [`di-benchmark-summary.md`](../Assets/Onity-Packages/Onity/Benchmarks/Results/di-benchmark-summary.md).
  The competitive roadmap and adopt/non-goal matrix:
  [`docs/Plan/07-Competitive-And-AI-Roadmap.md`](Plan/07-Competitive-And-AI-Roadmap.md).

---

## Summary table

| Axis | Onity | VContainer | Zenject / Extenject |
| --- | --- | --- | --- |
| Resolve speed (Editor-Mono, indicative) | **Fastest measured** on this machine | Fast | Slowest of the three |
| Build / registration speed (indicative) | **Fastest measured** | Slower than Onity here | Slow |
| Steady-state resolve allocation | **0 B/op** on every measured resolve path | Low (codegen mode) | Higher |
| DI feature breadth | Feature-complete for common Unity needs | Broad | Broadest |
| Entry-point lifecycle | **Automatic, no registration** | Manual `RegisterEntryPoint` | Automatic |
| Collection / open-generic binds | Yes | Yes | Yes |
| Conditional / id binds, `Unbind` | No (deliberate non-goal) | Limited | **Yes** |
| IL2CPP / AOT | Compiled fast path + automatic reflection fallback; validated on one IL2CPP build | **Mature, source-gen path** | **Mature, broadly shipped** |
| Unified DI + Reactive + Events | **Yes (one package)** | DI only | DI only |
| AI-friendliness / analyzer | **Usage guide + `ONITY001`–`ONITY006`** | None bundled | Partial (`ValidateAll`) |
| Production maturity | **Younger** | Mature | Mature |
| Ecosystem / community | **Small** | Large | Large |

---

## Axis-by-axis

### 1. Resolve speed

On the Editor-Mono benchmark machine, Onity resolved faster than both VContainer
and Zenject on every scenario:

| Scenario | Onity | VContainer | Zenject |
| --- | ---: | ---: | ---: |
| Resolve Singleton | ~152 ns | ~195 ns | ~2,326 ns |
| Resolve Transient | ~996 ns | ~1,421 ns | ~12,670 ns |
| Resolve Combined | ~1,883 ns | ~2,462 ns | ~20,392 ns |
| Resolve Complex (6-level graph) | ~37,895 ns | ~47,117 ns | ~302,383 ns |

The speed comes from a process-wide compiled-activator cache (`Expression.Compile`
runs once per `ConstructorInfo`), compiled field/property/method setters, a
`[ThreadStatic]` lock-free argument-array pool, and a per-plan per-slot
constructor-dependency cache. There is no `builder.Build()` ceremony before a
resolve, and the container has no engine coupling (`Onity.DI` is
`noEngineReferences: true`).

**Honest caveat.** These are Editor-Mono numbers on one machine. On IL2CPP the
compiled fast path is unavailable and Onity uses reflection (see axis 6), which
is slower per call — at that point VContainer's source-generated path can match
or beat Onity until Onity ships its own compile-time activator. The gap against
Zenject is large and consistent, but Zenject is the slowest of the three by
design (heavier feature set, more reflection at resolve time).

### 2. Build / registration speed

Building (preparing and registering) a complex graph was substantially faster in
Onity on the benchmark machine:

| Scenario | Onity | VContainer | Zenject |
| --- | ---: | ---: | ---: |
| Prepare & Register Complex | ~30,085 ns | ~145,953 ns | ~191,297 ns |

Onity avoids an explicit container-build step before the first resolve, and the
activator cache is shared across the whole process, so the second container that
builds the same types pays almost nothing for compilation. **Caveat:** the very
first build that compiles a type pays the `Expression.Compile` cost; a process
that builds many distinct graphs once each will see less of this advantage, and
on IL2CPP there is no compile step at all.

### 3. Steady-state allocation

Onity is **0 B/op on every measured resolve path** (singleton, transient,
combined, complex). Allocation is measured as `GC.GetTotalAllocatedBytes` deltas
across the sampled resolves. This holds for resolve specifically; one-time
operations such as the first compile of an activator do allocate.

VContainer's codegen path is also low-allocation; Zenject allocates more on the
resolve path due to its reflection-driven model. For per-frame gameplay churn
(spawn/despawn), the 0-B/op resolve path is the meaningful number, and Onity
holds it on Mono.

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
fight the predictable single-model and zero-hot-path-allocation goals — and
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

Onity's speed lead on Mono comes from `Expression.Compile`. On a fully
ahead-of-time runtime (IL2CPP, console AOT) there is no JIT, so `Expression.Compile`
can throw or return a delegate that throws on first call. Onity handles this with
a **one-time runtime probe** (`RuntimeCompileSupport`) that both compiles *and
invokes* a representative activator-shaped lambda at startup:

- On JIT runtimes the probe succeeds and the compiled fast path is used.
- On AOT/IL2CPP the probe detects the failure and the container **falls back to
  reflection-based activation** — slower per call, but allocation-comparable and
  guaranteed to run instead of crashing. Each compiler also wraps `Compile()` in
  try/catch for per-member safety.
- `OnityContainer.ForceReflectionActivation` lets you force the reflection path
  on a JIT runtime to pre-flight a graph under the exact strategy IL2CPP uses.

This fallback was **verified all-green on an IL2CPP build**.

**Honest caveat.** Reflection only guarantees that Onity *runs* correctly on
IL2CPP; it does not preserve the *speed* lead there. VContainer ships a
source-generator that produces compile-time activators, so on IL2CPP VContainer
keeps a codegen-quality path while Onity is on reflection. A source-generated
activator for Onity (`Onity.SourceGen`) is on the roadmap to close this; until it
ships, **on IL2CPP VContainer is likely the faster DI container.** Also note that
runtime open-generic registration relies on `MakeGenericType`, so the closed type
must survive IL2CPP stripping (reference it statically or preserve it).

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
this axis is irrelevant to you. If you want one coherent stack, it is Onity's
biggest structural advantage over a VContainer/Zenject + R3 + MessagePipe
combination.

### 8. AI-friendliness and compile-time analyzer

Onity ships a verified, machine-readable
[AI usage guide](Onity-AI-Usage-Guide.md) (every snippet compiles against the
current public API) and a [Roslyn analyzer pack](../tools/Onity.Analyzers)
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
missing bindings but is not a compile-time analyzer with inline fixes. This axis
is an Onity advantage, particularly for AI-assisted or large-team development.

### 9. Production maturity

**This is the axis where the competitors clearly win.** Zenject/Extenject and
VContainer have years of shipped-game production use, extensive issue history,
and broad platform hardening across many Unity versions and devices. Onity's DI
is feature-complete and its EditMode suite runs green, with an IL2CPP build
validated, but its real-world production footprint is small and recent. If you
need a container that has already been proven across many shipped titles today,
the mature choice is VContainer or Zenject.

### 10. Ecosystem and community

Also a competitor win. Zenject and VContainer have large communities, many
tutorials, Stack Overflow answers, third-party integrations, and example
projects. Onity's documentation is solid (AI guide, getting-started, migration
guides, architecture review) but the surrounding community ecosystem is just
starting. Expect to rely on the in-repo docs rather than a large body of
third-party material.

---

## When to choose which

- **Choose Onity** if you want one coherent package for DI + Reactive + Events,
  value a zero-allocation Mono resolve path, want automatic entry-point
  lifecycle without manual registration, want a compile-time analyzer and an
  AI-readable usage guide, and are comfortable adopting a younger framework.
- **Choose VContainer** if IL2CPP resolve performance via source-generated
  activators is critical today, you want a mature DI-only container with a strong
  community, and you are fine pairing it with a separate reactive/event stack.
- **Choose Zenject / Extenject** if you depend on its conditional/contextual
  binding, sub-container resolve, memory-pool, or `Unbind`/`Rebind` features, or
  you want the most battle-tested option with the largest community, and resolve
  performance is not your binding constraint.

The numbers and feature claims here reflect the current state of Onity and are
intended to be revised as Onity matures (notably: source-generated activators for
IL2CPP speed, and growing production/ecosystem evidence).
