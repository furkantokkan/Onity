---
title: Home
nav_order: 0
permalink: /
---

# Onity

Onity is one Unity package that unifies dependency injection, reactive programming, and events behind a single coherent API — replacing the usual mix of Zenject/VContainer, R3/UniRx, and MessagePipe. The core is engine-free; Onity has no non-Unity third-party runtime dependencies.

## Start here

- [Getting Started](Getting-Started.html) — hands-on walkthrough wiring DI, reactive, and events in one scene.

## Guides

- [Dependency Injection](guide/dependency-injection.html)
- [Reactive](guide/reactive.html)
- [Events & Messaging](guide/events-messaging.html)
- [Lifecycle & Scopes](guide/lifecycle-and-scopes.html)
- [Factories & Pooling](guide/factories-and-pooling.html)
- [Performance & IL2CPP](guide/performance-and-il2cpp.html)

## Reference

- [DI API](reference/di-api.html)
- [Reactive Operators](reference/reactive-operators.html)
- [Messaging API](reference/messaging-api.html)

## Migration

- [From Zenject](Migration/From-Zenject.html)
- [From VContainer](Migration/From-VContainer.html)
- [From R3 / UniRx](Migration/From-R3.html)

## More

- [AI Usage Guide](Onity-AI-Usage-Guide.html) — machine-readable usage guide verified against the source.
- [Comparison: VContainer & Zenject](Onity-vs-VContainer-Zenject.html) — per-axis DI comparison.
- [Architecture](Architecture-Review.html) — clean-OOP / SOLID review of the framework.
- [ADR 0001: DOTS and DI Performance](ADR/0001-dots-and-di-performance.html) — decision record for DOTS boundaries and DI optimization.
- [ADR 0003: Unity Event Shortcuts](ADR/0003-unity-event-shortcuts.html) — decision record for scoped `Onity.Publish` / `Subscribe` shortcuts.
