---
title: Home
nav_order: 0
permalink: /
description: "Onity documentation for Unity dependency injection, OnityTask async flows, reactive state, messaging, factories, and pooling."
---

# Onity

Onity is a Unity-first framework for dependency injection, async gameplay flows,
reactive state, typed messaging, factories, and pooling. Its core is engine-free,
and the package has no non-Unity third-party runtime dependencies.

**Current release:** [`v0.3.6`](https://github.com/furkantokkan/Onity/releases/tag/v0.3.6) · **Unity:** 2022.3 LTS or newer

## Install

In Unity, open **Window → Package Manager → `+` → Add package from git URL…**
and use:

```text
https://github.com/furkantokkan/Onity.git#upm
```

To pin the current release:

```text
https://github.com/furkantokkan/Onity.git?path=Packages/com.onity.framework#v0.3.6
```

## Start here

- [Getting Started](Getting-Started.html) — hands-on walkthrough wiring DI, reactive, and events in one scene.
- [Async with OnityTask](guide/onitytask.html) — frame waits, cancellation, scene loading, web requests, and the pooled-task safety rule.

## What Onity provides

| Area | Main API |
| --- | --- |
| Dependency injection | `OnityContainer`, installers, project/scene/game-object contexts |
| Async | `OnityTask`, scene/web/`AsyncOperation` bridges, cancellation |
| Reactive state | `Subject<T>`, `ReactiveProperty<T>`, synchronous and async operators |
| Messaging | `IMessageBroker`, `OnityEventHub`, `OnityEvent`, keyed and async channels |
| Creation and reuse | Typed factories, object/prefab pools, pool hooks |

## Performance snapshot

In the focused Windows IL2CPP singleton gate (Unity 2022.3.62f3, 1000
samples × 10,000 resolves), Onity standard measured `18.80 ns/op` and
VContainer measured `94.39 ns/op`: approximately **80.1% lower resolve time**
in that run. Results are indicative, not a guarantee; see
[Performance & IL2CPP](guide/performance-and-il2cpp.html) for the setup and
caveats.

## Guides

- [Dependency Injection](guide/dependency-injection.html)
- [Reactive](guide/reactive.html)
- [Events & Messaging](guide/events-messaging.html)
- [Async with OnityTask](guide/onitytask.html)
- [Lifecycle & Scopes](guide/lifecycle-and-scopes.html)
- [Factories & Pooling](guide/factories-and-pooling.html)
- [Performance & IL2CPP](guide/performance-and-il2cpp.html)
- [Refactoring from Existing Architecture](guide/refactoring-from-existing-architecture.html)

## Reference

- [DI API](reference/di-api.html)
- [Reactive Operators](reference/reactive-operators.html)
- [Messaging API](reference/messaging-api.html)

## Migration

- [From Zenject](Migration/From-Zenject.html)
- [From VContainer](Migration/From-VContainer.html)
- [From R3 / UniRx](Migration/From-R3.html)
- [From UniTask](Migration/From-UniTask.html)

## More

- [AI Usage Guide](Onity-AI-Usage-Guide.html) — machine-readable usage guide verified against the source.
- [Comparison: VContainer & Zenject](Onity-vs-VContainer-Zenject.html) — per-axis DI comparison.
- [Architecture](Architecture-Review.html) — current module boundaries, scopes, activation paths, and Unity adapters.
- [Architecture Decisions](ADR/) — accepted ADRs for performance, threading, and Unity API boundaries.
