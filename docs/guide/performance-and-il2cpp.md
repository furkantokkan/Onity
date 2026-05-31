---
title: "Performance & IL2CPP"
parent: "Guides"
nav_order: 6
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

The resolve machinery is **designed to avoid per-call managed allocation**: generated or compiled activators, pooled constructor-argument arrays, and cached per-type construction plans keep the steady-state resolve path off the allocator. An internal baked-graph fast path can further replace the per-resolve dictionary lookup for explicit local bindings; it is off by default (the reflection-driven map is the shipping default) and produces identical results either way.

The reactive and messaging emit paths follow the same principle: `Subject<T>.OnNext`, `MessageChannel<T>.Publish`, `EveryUpdate()`, and steady-state subscription delivery are array-backed and designed to be allocation-free in steady state, allocating only at subscribe time.

> **Allocation note.** A transient resolve still allocates the instance it returns (and a deep graph allocates one object per constructed node). The DI benchmark allocation numbers that were published earlier were **unreliable** — they reported 0 B for paths that must allocate, including for the other containers measured — so they did not capture gross allocations and are being re-measured in-editor. Do not treat any "zero-allocation resolve" or "0 B/op" statement as verified. What is accurate: the resolve *machinery* and the emit paths are built to avoid *per-call* managed allocation; the instance a transient hands back is a genuine allocation.

## Timing claims

The committed DI benchmark reports resolve **timing** (speed) numbers. Treat them as **indicative only**: they were measured on a Windows PC and are not a guaranteed result for every Unity version, scripting backend, or graph shape. They are useful for relative comparison of resolve paths within the same run, not as an absolute performance guarantee.

| Run | Result |
| --- | --- |
| Editor / Mono (`2026-05-30T19:38:06Z`) | Onity baked is faster than VContainer and Zenject on every measured timing path. |
| Windows IL2CPP Player (`2026-05-31T15:26:19Z`, 10,000 iterations) | Onity baked, with 19 generated activators registered for the benchmark graph, is faster than VContainer and Zenject on every measured timing path. |

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
