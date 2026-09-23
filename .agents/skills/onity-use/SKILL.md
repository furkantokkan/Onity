---
name: onity-use
description: Use Onity in a Unity game or migrate game code from Zenject, VContainer, R3, UniRx, or MessagePipe. Covers the shipped DI, reactive, messaging, and Unity context APIs; use onity-develop for changes to the Onity package itself.
---

# Use Onity

Work against the Onity version installed in the target project. Find its package source or `Packages/manifest.json` entry first. If the project also contains Onity documentation, read only the guide for the feature being used. The installed public API and tests win when a guide or example disagrees. Do not invent an API from a migration analogy.

In an Onity source checkout, start with `docs/Getting-Started.md`, then the relevant file under `docs/guide/` or `docs/reference/`. Use `docs/Onity-AI-Usage-Guide.md` as an index and verify feature signatures in the installed source and focused tests.

## Choose the right surface

- DI: register services with `OnityContainer` in a `MonoInstaller` owned by `ProjectContext`, `SceneContext`, or `GameObjectContext`. A fluent `Bind` registers only after `AsSingle()` or `AsTransient()`. Use a child container's `AsSingle()` for a per-scope instance. Prefer constructor injection for plain C# services. Use `BindInterfacesAndSelfTo<T>()` when the same singleton must be shared across its interfaces and concrete type.
- Messaging: use the context-bound `IMessageBroker` or `OnityEventHub` for typed events. Contexts bind these automatically. Use `BindMessageChannel<T>()` when directly injecting `IPublisher<T>` or `ISubscriber<T>` is needed. A message is a future notification; use `ReactiveProperty<T>` for current state.
- Reactive: compose `IOnityObservable<T>` streams and retain every `Subscribe` result. Use `AddTo(this)` for a Unity `Component`, or `AddTo(CompositeDisposable)` for plain C#; there is no `AddTo(GameObject)` overload. Check threading when an async operator feeds Unity API calls.
- Unity/DOTS: keep the engine-free core free of `UnityEngine` references. Resolve managed dependencies outside Burst jobs and pass data into jobs.

Before changing serialized Unity assets, packages, or project settings, follow the target repository's approval and ownership rules. For code changes, add focused tests for changed behavior and verify with the target project's Unity tooling. Treat hot-path allocation and performance claims as measurements, not assumptions.
