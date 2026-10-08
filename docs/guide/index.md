---
title: "Guides"
nav_order: 2
has_children: true
description: "Task-oriented guides for each Onity pillar, in reading order."
---

# Guides

Each guide covers one pillar of Onity and shows how it connects to the others. Read them in this
order the first time; later, open the one you need.

1. [Dependency Injection](dependency-injection.html): the container, bindings, lifetimes, injection sites and build.
2. [Lifecycle and Scopes](lifecycle-and-scopes.html): the one lifetime model, scope tokens, lifecycle interfaces, Unity contexts, scene transitions and the scene service.
3. [Reactive](reactive.html): observables, reactive properties, operators and the Unity and async bridges.
4. [Events and Messaging](events-messaging.html): typed messages through the broker, `OnityEvent` and `OnityEventHub`, keyed and async channels.
5. [Async with OnityTask](onitytask.html): frame waits, timers, cancellation, streams and the DI, reactive and messaging integration.
6. [Factories and Pooling](factories-and-pooling.html): runtime-argument factories, prefab pools and pool lifetime.
7. [Performance and IL2CPP](performance-and-il2cpp.html): activation paths, hot-path design and the IL2CPP checklist.
8. [Refactoring from Existing Architecture](refactoring-from-existing-architecture.html): before and after moves from managers, static events, VContainer and Zenject.

Measured results against other libraries live in [Comparisons](../comparisons/index.html), not in
the guides.
