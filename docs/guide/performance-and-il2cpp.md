---
title: "Performance & IL2CPP"
parent: "Guides"
nav_order: 5
---

# Performance & IL2CPP

Onity is built so that one package runs on both JIT runtimes (the Unity Editor and Mono players) and fully ahead-of-time runtimes (IL2CPP, console AOT) without a code change. This page explains how the DI fast path works, how it degrades on AOT, and — honestly — what the allocation and timing claims do and do not establish.

## Two activation strategies, one container

The DI layer constructs instances and injects members through one of two strategies, chosen **once per process** by a runtime probe:

- **Compiled** (JIT runtimes): constructor activators and member setters are built with `System.Linq.Expressions.Expression.Compile`, so the resolve path avoids per-call reflection. Each constructor is compiled once and cached for the lifetime of the process, across every container `Build()`.
- **Reflection** (AOT/IL2CPP): there is no JIT, so `Expression.Compile` can throw or return a delegate that throws on first call. The probe detects this and the layer falls back to reflection-based activation: slower per call, allocation-comparable, and guaranteed to run instead of crashing the container.

The probe both compiles **and invokes** a representative lambda, because some AOT runtimes let `Compile()` succeed yet throw only when the compiled delegate is first called. The two strategies produce identical results — only the per-constructor delegate differs. Compilation is also resilient per-constructor: if the runtime reports compile support but one specific constructor fails to compile (for example a type the AOT linker stripped), that constructor alone falls back to reflection.

You can read which strategy is live:

```csharp
using Onity.DI;

// True on JIT (Editor/Mono); false on AOT/IL2CPP. The container constructs and
// injects correctly either way — this is for confirmation/diagnostics on device.
bool compiled = OnityContainer.IsCompiledActivationSupported;
```

## Hot-path design

The resolve machinery is **designed to avoid per-call managed allocation**: compiled activators, pooled constructor-argument arrays, and cached per-type construction plans keep the steady-state resolve path off the allocator. An internal baked-graph fast path can further replace the per-resolve dictionary lookup for explicit local bindings; it is off by default (the reflection-driven map is the shipping default) and produces identical results either way.

The reactive and messaging emit paths follow the same principle: `Subject<T>.OnNext`, `MessageChannel<T>.Publish`, `EveryUpdate()`, and steady-state subscription delivery are array-backed and designed to be allocation-free in steady state, allocating only at subscribe time.

> **Honest caveat — this is a design property, not a verified "0 B/op" figure.** A transient resolve still allocates the instance it returns (and a deep graph allocates one object per constructed node). The DI benchmark allocation numbers that were published earlier were **unreliable** — they reported 0 B for paths that must allocate, including for the other containers measured — so they did not capture gross allocations and are being re-measured in-editor. Do not treat any "zero-allocation resolve" or "0 B/op" statement as verified. What is accurate: the resolve *machinery* and the emit paths are built to avoid *per-call* managed allocation; the instance a transient hands back is a genuine allocation.

## Timing claims

The committed DI benchmark reports resolve **timing** (speed) numbers. Treat them as **indicative only**: they were measured in the Editor on Mono, on a single machine, and are not a guaranteed result on your hardware, your build target, or under IL2CPP. They are useful for relative comparison of resolve paths within the same run, not as an absolute performance guarantee.

## IL2CPP checklist

- **No setup required for the fallback.** The reflection path engages automatically on AOT/IL2CPP. The container runs the same bindings; only activation speed changes. This fallback was verified all-green on an IL2CPP build.
- **Keep the core engine-free.** `Onity.Core`, `Onity.DI`, `Onity.Reactive`, `Onity.Messaging`, and `Onity.Factory` have no `UnityEngine` dependency, which keeps them simple to strip and test. `ZLinq` is the only third-party runtime dependency, used by the `Onity.Unity` layer.
- **Closed generics only.** Open generic *definitions* are bound (`Bind(typeof(IRepo<>))`), but each **closed** form (`IRepo<Foo>`) is what actually resolves and is built on first use. Make sure the closed types you resolve are reachable so the AOT linker preserves them.
- **No reflection-only members the linker can drop silently.** Constructors and injected members the container needs must survive stripping; reference them so they are not removed.
- **Pre-flight under the AOT strategy in the Editor.** The activation strategy is auto-detected, and the internal force-reflection switch the parity tests use lets the suite exercise the exact reflection path an IL2CPP build takes — so AOT behavior is covered by tests rather than discovered on device.

## What is not here

There is no source generator or IL post-processor. The "compiled" fast path is `Expression.Compile` at runtime on JIT targets, not generated C#; on AOT it is reflection. If you need source-generated activation, that is a non-goal of the current design.

## See also

- [Dependency Injection](dependency-injection.html) — the binding and resolve surface the fast path serves.
- [Reactive](reactive.html) and [Events & Messaging](events-messaging.html) — the emit paths designed to avoid per-call allocation.
- [Comparison: Onity vs VContainer / Zenject](../Onity-vs-VContainer-Zenject.html) — where Onity sits against the libraries it replaces.
- [Architecture Review](../Architecture-Review.html) — the engine-free layering in depth.
