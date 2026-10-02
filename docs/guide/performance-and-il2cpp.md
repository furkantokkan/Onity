---
title: "Performance & IL2CPP"
parent: "Guides"
nav_order: 7
description: "How Onity selects DI activation paths on Mono and IL2CPP, with current benchmark evidence and allocation caveats."
---

# Performance & IL2CPP

Onity is built so that one package runs on both JIT runtimes (the Unity Editor and Mono players) and ahead-of-time runtimes (IL2CPP, console AOT) without a code change. This page explains how the DI fast paths work, how the fallback works, and what the allocation and timing claims do and do not establish.

## Three activation strategies, one container

The DI layer constructs instances and injects members through the fastest safe strategy available for the selected constructor:

- **Generated** (AOT/JIT): source-generated activators register direct `new T(...)` delegates in `Onity.DI.Internal.GeneratedActivators`. When a generated activator matches the selected constructor signature, it is used first on every runtime, including IL2CPP.
- **Compiled** (JIT runtimes): if no generated activator exists, constructor activators and member setters are built with `System.Linq.Expressions.Expression.Compile`, so the resolve path avoids per-call reflection. Each constructor is compiled once and cached for the lifetime of the process, across every container `Build()`.
- **Fallback** (AOT/IL2CPP or restricted runtimes): runtime expression compilation can be unavailable, interpreter-backed, or target-dependent. The probe detects whether the compiled delegate can actually run; when it cannot and no generated activator is available, the layer falls back to reflection-based activation: slower per call, allocation-comparable, and guaranteed to run instead of crashing the container.

The probe both compiles **and invokes** a representative lambda, because some AOT runtimes let `Compile()` succeed yet throw only when the compiled delegate is first called. All strategies produce identical results — only the per-constructor delegate differs. Compilation is also resilient per-constructor: if the runtime reports compile support but one specific constructor fails to compile (for example a type the AOT linker stripped), that constructor alone falls back to reflection.

You can read which strategy is live:

```csharp
using Onity.DI;

// True when the current runtime probe accepts runtime Expression.Compile.
// Generated activators can still be used when this is false; this is for
// confirmation/diagnostics on device, not a full "fast path active" flag.
bool compiled = OnityContainer.IsCompiledActivationSupported;
```

## Hot-path design

The resolve machinery is **designed to avoid per-call managed allocation**:
generated or compiled activators, pooled constructor-argument arrays, and cached
per-type construction plans keep the steady-state resolve path off the
allocator. Standard generic `Resolve<T>()` uses a container-local dense type-id
provider slot, avoiding a `Dictionary<Type, ...>` lookup for explicit local
bindings. Dynamic `Resolve(Type)` and misses retain the general map/fallback
path. The baked graph is enabled by default and adds flat lifetime and
singleton slots for explicit local bindings; the parity suite checks that both
lanes produce identical results, including registrations made after `Build()`.

The reactive and messaging emit paths follow the same principle: `Subject<T>.OnNext`, `MessageChannel<T>.Publish`, `EveryUpdate()`, and steady-state subscription delivery are array-backed and designed to be allocation-free in steady state, allocating only at subscribe time.

> **Allocation note.** A transient resolve still allocates the instance it returns (and a deep graph allocates one object per constructed node). The DI benchmark allocation numbers that were published earlier were **unreliable** — they reported 0 B for paths that must allocate, including for the other containers measured — so they did not capture gross allocations and are being re-measured in-editor. Do not treat any "zero-allocation resolve" or "0 B/op" statement as verified. What is accurate: the resolve *machinery* and the emit paths are built to avoid *per-call* managed allocation; the instance a transient hands back is a genuine allocation.

## Timing claims

The committed DI benchmark reports resolve **timing** (speed) numbers. Treat them as **indicative only**: they were measured on a Windows PC and are not a guaranteed result for every Unity version, scripting backend, or graph shape. They are useful for relative comparison of resolve paths within the same run, not as an absolute performance guarantee.

The current Editor/Mono and Windows IL2CPP reports include keyed and scoped
singleton resolution along with the five original scenarios. The Editor's
cumulative allocation byte counters failed a 1 MiB positive control, so the
timing report marks inline bytes unavailable. Separate raw Profiler passes
validated empty, 1 MiB, and 2 MiB controls before measuring `GC.Alloc` inside
each marker. [Full run summary and raw reports](../benchmarks/advanced-di-summary-2026-09-23.md).

| Environment | Result |
| --- | --- |
| Unity Editor / Mono (`2026-09-23`) | Onity baked was faster than VContainer and Zenject in all seven measured scenarios. [Raw timing report](../benchmarks/advanced-di-editor-mono-direct-keyed/di-benchmark-latest.json). |
| Windows IL2CPP release Player (`2026-09-23`) | Onity baked was faster than VContainer and Zenject in all seven measured scenarios with `19` generated activators registered. [Raw timing report](../benchmarks/advanced-di-il2cpp-release.json). |
| Windows IL2CPP release Player re-run (`2026-10-01`, three processes) | Onity baked was fastest in every scenario in every process; Onity/VContainer per-run ratios ranged from 0.23x to 0.57x. [Re-measurement](../benchmarks/remeasure-2026-10-01.md). |
| Windows IL2CPP singleton gate (`2026-07-12T13:30:25Z`) | 1000 samples measured Onity standard at 18.80 ns/op versus VContainer at 94.39 ns/op. |

In the Editor/Mono allocation pass, Onity baked and VContainer both used
0, 16, 16, and 384 B per operation for singleton, transient, combined, and
complex resolves. Keyed and scoped singleton resolves used 0 B in all three
frameworks. Onity baked used 10,364 B for complex prepare/register versus
VContainer's 15,296 B and Zenject's 23,166 B.

A separate Windows IL2CPP Development player raw Profiler pass validated an
empty marker at 0 B and 1 MiB / 2 MiB controls at 1,053,728 B / 2,102,304 B.
Each case used three identical samples of 64 operations after warming the same
container. Onity baked and VContainer both used 0, 16, 16, and 384 B per
resolve operation; Zenject used 0, 200, 200, and 4,800 B. Keyed and scoped
singleton resolves used 0 B in all three frameworks. Complex prepare/register
used 10,424 B for Onity baked, 16,136 B for VContainer, and 23,405 B for
Zenject. [Raw IL2CPP byte report](../benchmarks/advanced-di-il2cpp-allocation.json).
Development profiling supplies allocation bytes; the release player supplies
the timing comparison. The release report's allocation fields are unavailable.

## Managed pooling and factory timing

A separate Windows IL2CPP benchmark compared Onity's checked two-parameter
pooled factory with Zenject `MemoryPool<int,int,int[]>` using 32 prewarmed
items, fixed capacity, matching initialization/reset callbacks, and rotated
bursts. In an interleaved Release confirmation, Onity/Zenject paired median
ratios were **0.803x** and **0.776x**, or **19.7%** and **22.4%** less elapsed
time in that workload. Development player runs recorded zero warmed
`GC.Alloc` events with a positive control; Release allocation was unavailable.
Both pools rejected duplicate returns and passed the final-state checks.

`OnityObjectPool<T>` keeps its own array stack instead of wrapping Unity's
`ObjectPool<T>`; the top of the stack is the checked-mode duplicate-return fast
slot. In the checked single-item Editor/Mono test, which previously favored
Zenject (**1.429x/1.566x**), two fresh runs measured Onity checked/Zenject
paired medians of **0.590x/0.579x** and Onity default/Zenject **0.674x/0.559x**,
with zero warmed allocation. In an old/new/new/old sequence of the 32-item
IL2CPP Release burst, factory/Zenject moved from **0.796x/0.856x** to
**0.738x/0.729x**. The Unity player control does not check duplicate returns and
is not an equivalent checked comparison. These results do not rank prefab
pools, VContainer pool adapters, or other workloads.
[Method, raw reports, and binary hashes](../benchmarks/factory-pooling-2026-09-23.md);
[own-stack pool measurements](../benchmarks/pool-own-stack-2026-10-01.md).

## OnityTask on IL2CPP

The [2026-10-02 Release Player gate](../assets/benchmarks/onitytask-surpass-2026-10-02.md)
measured OnityTask against UniTask 2.5.11 in non-development Windows x64 Players
(Unity 2022.3.62f2), three processes per backend and suite. On IL2CPP all 29 gated
rows were faster, with median Onity/UniTask time ratios from 0.085 to 0.808; Mono
was reported without a gate. The [comparison](onitytask-comparison.html) has the
full picture, including where Onity is slower.

What the IL2CPP build relies on:

- **Code generation options.** Onity carries internal copies of the
  `Unity.IL2CPP.CompilerServices` attributes, which IL2CPP recognizes by name.
  `[Il2CppSetOption(Option.NullChecks, false)]` removes null checks from the task
  awaiters, method builders, pooled source bases, async-method runners and their
  pool, the PlayerLoop owner and the yield awaiter, where every receiver is non-null
  by construction and public entry points validate their arguments.
  `[Il2CppEagerStaticClassConstruction]` on the settings holder and the PlayerLoop
  owner runs their static constructors at startup, so reads of their static fields
  carry no class-initialization check. `OnityTask` itself has no static
  constructor, so its inlined members have none either; the generated C++ of the
  measured Player confirms both.
- **Immediate runner return.** A suspended async method's runner goes back to its
  pool as soon as its task is consumed. This is safe because the runner calls the
  state machine's `MoveNext` by address, with no copy back, which the generated C++
  of the measured Player also confirms.
- **Context flow.** Performance claims for `async OnityTask` are measured at the
  default `FlowExecutionContext = false`, which is UniTask-equivalent; enabling flow
  adds one execution-context capture and restore per suspension, and the complete
  lifecycle with four suspensions then measured 1.5x to 1.6x slower than UniTask.
- **Pool retention.** Onity keeps at most 128 runners per async method
  (`OnityTask.RunnerPoolCapacity`) and 256 sources per source type
  (`OnityTask.SourcePoolCapacity`); UniTask keeps everything. Above the cap each
  extra operation allocates, which made 4,096-call bursts of one async method up to
  2.6x slower than UniTask at the defaults. Raise both caps at startup for methods
  that run in large bursts when the retained memory is acceptable; the gate's
  4,096-operation rows were measured that way.

## IL2CPP checklist

- **Use generated activators for hot IL2CPP graphs.** Mark hot DI-managed implementation types with `[OnityGenerateActivator]` and ship the `Onity.SourceGen` Roslyn analyzer DLL so IL2CPP can use direct `new T(...)` delegates instead of `ConstructorInfo.Invoke`.
- **No setup required for correctness.** If no generated activator exists, the runtime probe selects the compiled path only when it can compile and invoke safely; otherwise the reflection path engages automatically. The same bindings run in Editor, Mono player, and IL2CPP player builds.
- **Keep the core engine-free.** `Onity.Core`, `Onity.DI`, `Onity.Reactive`, `Onity.Messaging`, and `Onity.Factory` have no `UnityEngine` dependency, which keeps them simple to strip and test. Onity has no non-Unity third-party runtime dependencies.
- **Closed generics only.** Open generic *definitions* are bound (`Bind(typeof(IRepo<>))`), but each **closed** form (`IRepo<Foo>`) is what actually resolves and is built on first use. Make sure the closed types you resolve are reachable so the AOT linker preserves them.
- **No reflection-only members the linker can drop silently.** Constructors and injected members the container needs must survive stripping; reference them so they are not removed.
- **Pre-flight the fallback in the Editor.** The activation strategy is auto-detected, and the internal force-reflection switch the parity tests use lets the suite exercise the reflection path IL2CPP takes when no generated activator is available — so AOT fallback behavior is covered by tests rather than discovered on device.

## What remains

The current generator is explicit: it emits activators for types marked with `[OnityGenerateActivator]`. Future work can improve discovery, generate member setters, and add more platform/device benchmark coverage. For now, use the generated path for hot implementation types, keep the reflection fallback for correctness, and re-run the player benchmark for your target platform before treating the published Windows numbers as target-platform results.

## See also

- [Dependency Injection](dependency-injection.html) — the binding and resolve surface the fast path serves.
- [Reactive](reactive.html) and [Events & Messaging](events-messaging.html) — the emit paths designed to avoid per-call allocation.
- [Comparison: Onity vs VContainer / Zenject](../Onity-vs-VContainer-Zenject.html) — where Onity sits against the libraries it replaces.
- [Architecture Review](../Architecture-Review.html) — the engine-free layering in depth.
