---
name: onity-use
description: Use Onity in a Unity game or migrate game code from Zenject, VContainer, R3, UniRx, MessagePipe, or UniTask. Covers the shipped DI, reactive, messaging, OnityTask async, and Unity context APIs; use onity-develop for changes to the Onity package itself.
---

# Use Onity

Work against the Onity version installed in the target project. Find its package source or `Packages/manifest.json` entry first. If the project also contains Onity documentation, read only the guide for the feature being used. The installed public API and tests win when a guide or example disagrees. Do not invent an API from a migration analogy.

In an Onity source checkout, start with `docs/Getting-Started.md`, then the relevant file under `docs/guide/` or `docs/reference/`. Use `docs/Onity-AI-Usage-Guide.md` as an index and verify feature signatures in the installed source and focused tests. When this skill was installed by `Onity/AI/Install Assistant Guidance...`, the same usage guide is `references/Onity-AI-Usage-Guide.md` next to this file, and the installed package ships every guide under `Documentation~/`.

## Choose the right surface

- DI: register services with `OnityContainer` in a `MonoInstaller` owned by `ProjectContext`, `SceneContext`, or `GameObjectContext`. A fluent `Bind` registers only after `AsSingle()`, `AsScoped()` or `AsTransient()`. Use `AsScoped()` for a per-scope instance. Prefer constructor injection for plain C# services. Use `BindInterfacesAndSelfTo<T>()` when the same singleton must be shared across its interfaces and concrete type.
- Factories and pooling: pass runtime arguments through an `IFactory<...>` registered with `BindFactory`, and reuse prefab instances with `BindPooledFactory` by injecting `IFactory<T>` to spawn and `IPool<T>` to release (`OnityObjectPool<T>` for plain C# objects). Release each item exactly once and never touch it afterwards; reset state in `IPoolHooks` or the pool actions. The scope disposes the pool that `BindPooledFactory(prefab, ...)` creates; a pool you build and pass in stays yours, so dispose it or call `pool.AddTo(container)`. See sections 2.5 and 10 of the usage guide.
- Async: use `OnityTask` (`Onity.Unity.Async`), not UniTask. Give every async loop a token from its owner (`GetCancellationTokenOnDestroy()`, an injected `IOnityScopeLifetime.Token`), await pooled tasks once, and use `async OnityTaskVoid` or `Forget(handler)` instead of `async void`. Assemblies that call its extension methods must reference `Onity.Reactive`. See section 11 of the usage guide and `docs/Migration/From-UniTask.md` for UniTask names.
- Async sharing and bursts: in Play, token-less `NextFrame()`, `DelayFrames(n)` and `Yield()` waits are re-awaitable, but every other pooled task is single-consumer: await it once or `Preserve()` it before sharing. `OnityTask.RunnerPoolCapacity` (default 128 per async method) and `OnityTask.SourcePoolCapacity` (default 256 per source type) cap what is pooled; raise them once at startup for bursts of thousands of operations, because each extra one allocates.
- Messaging: use the context-bound `IMessageBroker` or `OnityEventHub` for typed events. Contexts bind these automatically. Use `BindMessageChannel<T>()` when directly injecting `IPublisher<T>` or `ISubscriber<T>` is needed. A message is a future notification; use `ReactiveProperty<T>` for current state.
- Reactive: compose `IOnityObservable<T>` streams and retain every `Subscribe` result. Use `AddTo(this)` for a Unity `Component`, or `AddTo(CompositeDisposable)` for plain C#; there is no `AddTo(GameObject)` overload. Check threading when an async operator feeds Unity API calls.
- Unity/DOTS: keep the engine-free core free of `UnityEngine` references. Resolve managed dependencies outside Burst jobs and pass data into jobs.

## Project and scene scopes

- `ProjectContextBootstrap` runs before the first scene and, when no `ProjectContext` exists, instantiates the prefab at `Resources/Onity/ProjectContext` (the menu `Onity/Contexts/Create ProjectContext Prefab` creates `Assets/Resources/Onity/ProjectContext.prefab`). A missing prefab is silent and leaves `ProjectContext.Instance` null, so scenes get no project parent. `ProjectContext` persists across scene loads and owns the app-wide container.
- `ProjectScopeInstaller` is not an Onity type. Register app-wide services in a game-defined `MonoInstaller` listed in the `ProjectContext` prefab's Installers (attaching the component alone registers nothing); an empty project scope needs none. Session-wide services belong there, and scene services on a `SceneContext`, whose singletons do not outlive its scene.
- A `SceneContext` owns a child container. Its parent is the explicit `Parent Context`, else its `Project Context` field, else `ProjectContext.Instance`, so blank parent fields are normal. Add one to each runtime scene that needs scene-scoped services, including a scene opened directly in Play Mode.
- A `GameObjectContext` takes the nearest `OnityContext` up its hierarchy, else `ProjectContext`. It does not search the scene for a `SceneContext`, so nest it under that context or set its `Parent Context`.
- A context's GameObject name (for example, `SceneScope`) is only a hierarchy label, not a container key; each context component creates its own container.

Before changing serialized Unity assets, packages, or project settings, follow the target repository's approval and ownership rules. For code changes, add focused tests for changed behavior and verify with the target project's Unity tooling. Treat hot-path allocation and performance claims as measurements, not assumptions.
