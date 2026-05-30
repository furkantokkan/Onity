---
title: "Comparison: VContainer & Zenject"
nav_order: 7
---

# Onity vs VContainer / Zenject

A per-axis comparison of three Unity dependency-injection containers — **Onity**,
**VContainer**, and **Zenject / Extenject** — written to read as an external
evaluation rather than marketing copy. It is maintained in the Onity repository,
so treat it as a self-assessment that discloses its sources and invites
verification: where VContainer or Zenject is the stronger choice, that is stated
plainly, and every benchmark number can be reproduced with the runner included in
this repository.

## Read this first — scope and caveats

- **Onity is a single Unity package** that unifies dependency injection,
  reactive programming, and events. This document compares only the **DI**
  axis against VContainer and Zenject (which are DI-only); the unified-scope
  advantage is covered as its own axis below.
- **The benchmark timing numbers are indicative, not guaranteed.** They were
  measured in the **Unity Editor with the Mono scripting backend, on a single
  Windows machine** (Unity 2022.3.62f3), 512 warmup iterations, 8 samples, mean
  reported. Your hardware, Unity version, IL2CPP vs Mono backend, and graph shape
  will produce different absolute numbers and possibly different relative ordering.
  Treat the numbers as "this is what one machine measured," not "Onity is always
  faster." They were produced by the Onity project's own `OnityDiBenchmarkRunner`
  and have **not been independently audited** — the runner ships in this
  repository specifically so any reader can reproduce, or challenge, them.
- **The published allocation figures were unreliable.** The committed run
  reported 0 B for VContainer and Zenject as well, which cannot be correct (a
  transient resolve allocates the instance it returns, and Zenject is
  allocation-heavy), so the `GC.GetTotalAllocatedBytes` delta was not capturing
  gross allocations. Those alloc numbers are withdrawn pending a corrected
  in-editor re-measure. The **timing** numbers are unaffected by this.
- **Onity is younger.** VContainer and Zenject have years of production use,
  large communities, and broad real-world hardening. Onity's DI is
  feature-complete and tested, but its production track record and ecosystem are
  still small. That gap is real and is called out explicitly below.
- Full DI benchmark detail:
  [`di-benchmark-summary.md`](https://github.com/furkantokkan/Onity/blob/main/Packages/com.onity.framework/Benchmarks/Results/di-benchmark-summary.md).
  The competitive roadmap and adopt/non-goal matrix:
  [`docs/Plan/07-Competitive-And-AI-Roadmap.md`](https://github.com/furkantokkan/Onity/blob/main/docs/Plan/07-Competitive-And-AI-Roadmap.md).

---

## Summary table

| Axis | Onity | VContainer | Zenject / Extenject |
| --- | --- | --- | --- |
| Resolve speed (Editor-Mono, indicative) | Fastest in this run with baked resolve (~1.9–2.5× VContainer, ~12–33× Zenject) | Behind Onity baked; close to Onity reflection on simple resolves | Slowest of the three |
| Build / registration speed (indicative) | Fastest in this run (baked ~2.9× VContainer, reflection ~3.8× VContainer) | Slower than Onity | Slow |
| Steady-state resolve allocation | resolve machinery designed allocation-free (a transient allocates the returned instance; alloc figures pending a corrected re-measure) | Low (codegen mode) | Higher |
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

| Scenario | Onity (Baked) | Onity (Reflection) | VContainer | Zenject |
| --- | ---: | ---: | ---: | ---: |
| Resolve Singleton | ~94 ns | ~215 ns | ~202 ns | ~3,137 ns |
| Resolve Transient | ~775 ns | ~1,725 ns | ~1,697 ns | ~11,681 ns |
| Resolve Combined | ~896 ns | ~1,059 ns | ~1,712 ns | ~15,400 ns |
| Resolve Complex (6-level graph) | ~22,787 ns | ~26,502 ns | ~57,995 ns | ~285,394 ns |

The speed comes from a process-wide compiled-activator cache (`Expression.Compile`
runs once per `ConstructorInfo`), compiled field/property/method setters, a
`[ThreadStatic]` lock-free argument-array pool, and a per-plan per-slot
constructor-dependency cache. There is no `builder.Build()` ceremony before a
resolve, and the container has no engine coupling (`Onity.DI` is
`noEngineReferences: true`).

**Honest caveat.** These are Editor-Mono numbers on one machine. On IL2CPP the
compiled activation path is unavailable and Onity uses reflection (see axis 6);
that mode was slightly behind VContainer on simple singleton/transient resolves
in this run, while still ahead on combined and complex graph resolves. At that
point VContainer's source-generated path can match or beat Onity until Onity ships
its own compile-time activator. The gap against Zenject is large and consistent,
but Zenject is the slowest of the three by design (heavier feature set, more
reflection at resolve time).

### 2. Build / registration speed

Building (preparing and registering) a complex graph was substantially faster in
Onity on the benchmark machine:

| Scenario | Onity (Baked) | Onity (Reflection) | VContainer | Zenject |
| --- | ---: | ---: | ---: | ---: |
| Prepare & Register Complex | ~47,243 ns | ~35,837 ns | ~135,140 ns | ~197,132 ns |

Onity avoids VContainer's separate builder object and shares activation metadata
across the whole process. The baked mode adds a lean dense-id map during `Build()`
so explicit bindings can resolve without a dictionary lookup; it reuses the same
providers as the reflection path instead of compiling a second dependency graph.
**Caveat:** the very first build that compiles a type pays the
`Expression.Compile` cost; a process that builds many distinct graphs once each
will see less of this advantage, and on IL2CPP there is no compile step at all.

### 3. Steady-state allocation

Onity's resolve machinery is **designed to avoid per-call managed allocation**
beyond the constructed instances themselves: compiled activators, a
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
  value a resolve path designed to avoid per-call managed allocation on Mono,
  want automatic entry-point lifecycle without manual registration, want a
  compile-time analyzer and an AI-readable usage guide, and are comfortable
  adopting a younger framework.
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
