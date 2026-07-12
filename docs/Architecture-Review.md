---
title: "Architecture"
nav_order: 8
description: "Current Onity v0.3.6 module boundaries, dependency direction, composition roots, DI activation paths, and Unity integration."
---

# Architecture

This page describes the architecture shipped in Onity `v0.3.6`. The package
source lives under `Packages/com.onity.framework`; the `upm` branch mirrors that
folder to the repository root for installation.

## Design goals

Onity is built around five constraints:

1. Keep domain-facing DI, reactive, messaging, factory, and composition APIs
   independent from `UnityEngine`.
2. Put Unity lifecycle, scene, input, async-operation, pooling, and DOTS adapters
   behind explicit Unity-facing assemblies.
3. Keep dependency direction acyclic and enforced by asmdef references.
4. Resolve dependencies at composition roots, then pass typed services into
   gameplay code instead of resolving every frame.
5. Keep runtime hot paths self-owned and free from non-Unity third-party runtime
   dependencies.

## Runtime modules

The asmdef references below are the current source of truth:

| Assembly | References | Boundary |
| --- | --- | --- |
| `Onity.Core` | none | Engine-free primitives and disposal helpers |
| `Onity.Factory` | `Onity.Core` | Engine-free factory contracts |
| `Onity.DI` | `Onity.Core`, `Onity.Factory` | Engine-free container and activation |
| `Onity.Messaging` | `Onity.Core` | Engine-free typed channels and broker |
| `Onity.Reactive` | `Onity.Core` | Engine-free observable state and operators |
| `Onity.Composition` | Core, DI, Reactive, Messaging | Engine-free one-line bindings for shared primitives |
| `Onity.Pooling` | Core, Factory, Unity pool APIs | Object and prefab pool implementations |
| `Onity.DOTS` | Core, Messaging, Burst, Collections, Entities, Mathematics | ECS/Burst event bridge and entity helpers |
| `Onity.Unity` | Core, DI, DOTS, Factory, Messaging, Reactive, Pooling, Unity packages | Contexts, lifecycle, input, async, scene flow, and editor-facing runtime adapters |

The engine-free assemblies contain no `UnityEngine` references. Plain C# domain
assemblies and EditMode tests can depend on them without loading a scene.

## Composition roots and scopes

Unity contexts own containers:

- `ProjectContext` owns session-wide services that survive scene loads.
- `SceneContext` owns one scene's services and inherits project bindings.
- `GameObjectContext` owns a local prefab or hierarchy scope.

Installers register every binding before `Build()`. A child container inherits
its parent's providers; a local binding shadows the parent inside that child.
Onity has `Singleton` and `Transient` lifetimes. A scoped singleton is expressed
as `AsSingle()` on the child container that owns it.

Plain C# services should use constructor injection. Unity-owned objects such as
`MonoBehaviour` instances use `[Inject]` fields, properties, or methods because
Unity controls their construction.

## DI activation paths

All activation strategies produce the same object graph:

1. **Generated activator:** `[OnityGenerateActivator]` lets the source generator
   emit a direct constructor delegate for AOT/IL2CPP hot graphs.
2. **Compiled activator:** JIT runtimes use a one-time `Expression.Compile`
   probe and cache successful constructor/member delegates.
3. **Reflection fallback:** unsupported or stripped cases retain a correctness
   path through cached reflection metadata.

Standard generic `Resolve<T>()` uses container-local dense type-id provider
slots for explicit local bindings. Dynamic `Resolve(Type)`, misses, and parent
fallback use the general provider map. The optional baked graph precomputes flat
lifetime and singleton slots at `Build()`.

Managed dependency resolution stays outside Burst jobs. DOTS integration passes
blittable data and entity events across the managed/ECS boundary instead of
trying to construct managed graphs inside Burst.

## Reactive and messaging model

`Subject<T>`, `ReactiveProperty<T>`, operators, and
`IMessageBroker.Observe<T>()` share `IOnityObservable<T>`. This keeps state and
events on one subscription/disposal model:

- every subscription returns `IDisposable`;
- Unity owners use `TakeUntilDisable(this)` for `OnEnable` subscriptions or
  `AddTo(this)` for a destroy lifetime;
- plain services use `CompositeDisposable`;
- callbacks that resume off-thread call `ObserveOnMainThread()` before touching
  Unity APIs.

`MessageBroker` and `OnityEventHub` are bound per context. Typed
`IPublisher<T>`/`ISubscriber<T>` injection is opt-in through
`BindMessageChannel<T>()`; keyed and async channels remain explicit types.

## Async boundary

Unity async support lives in `Onity.Unity.Async` because frame waits, scenes,
`AsyncOperation`, web requests, and main-thread continuation are engine-facing.
`OnityTask` uses pooled sources for common Unity waits. Those values are
single-consumer; share an independently materialized `Task` when multiple
consumers need the same result. See [Async with OnityTask](guide/onitytask.html).

Engine-free reactive/message APIs continue to expose `Task` or `ValueTask` where
that is the appropriate .NET contract.

## Build-time tooling

The runtime does not depend on external frameworks. Repository tooling includes:

- `Onity.SourceGen` for generated constructor activators;
- `Onity.Analyzers` for common binding, resolve, and subscription mistakes;
- benchmark assemblies gated behind `ONITY_BENCHMARKS` and comparison packages;
- Editor diagnostics for containers, tasks, pools, observables, and scene validation.

These are build, Editor, test, or benchmark inputs rather than third-party
runtime dependencies shipped to gameplay code.

## Verification snapshot

The `v0.3.6` release was verified on Unity `2022.3.62f3` with:

- 434 passing EditMode tests;
- 10 passing PlayMode tests;
- engine-free core, analyzer, and source-generator builds;
- Editor/Mono and Windows IL2CPP DI benchmarks;
- a focused 1000-sample IL2CPP singleton resolve gate.

Exact counts are release evidence, not a permanent promise; use the current CI
and Unity Test Runner results for later versions.

## Intentional limits

Onity deliberately does not provide conditional/id bindings, `Unbind`, buffered
or replay messages, handler priority, or request-response messaging. Managed DI
does not run inside Burst. See the migration and comparison guides for supported
replacement patterns rather than assuming full Zenject, VContainer, R3, or
MessagePipe API parity.

## Architecture decisions

- [ADR 0001: DOTS and DI Performance](ADR/0001-dots-and-di-performance.html)
- [ADR 0002: Reactive Thread-Pool Scheduling](ADR/0002-reactive-thread-pool-scheduling.html)
- [ADR 0003: OnityEvent Shortcuts](ADR/0003-unity-event-shortcuts.html)
- [ADR 0004: Refactoring from Existing Architecture](ADR/0004-refactoring-from-existing-architecture.html)

The resulting dependency graph is downward-only, the core remains testable
without a scene, and Unity-specific behavior is isolated at explicit adapters.
