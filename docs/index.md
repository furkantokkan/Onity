---
title: Home
nav_order: 0
permalink: /
description: "Onity documentation: dependency injection, reactive state, typed messaging, OnityTask async and factories with pooling in one Unity package with one lifetime model."
---

# Onity

Onity (`com.onity.framework`) is a Unity package that puts dependency injection, reactive state, typed
messaging, async (`OnityTask`) and factories with pooling in one package with one lifetime model: a scope
owns its services, subscriptions, tasks and pools, and disposes them together. The core is engine-free,
and the package has no non-Unity third-party runtime dependency.

Current release: [v0.8.1](https://github.com/furkantokkan/Onity/releases/tag/v0.8.1). Unity 2022.3 LTS or
newer.

## Install

In Unity, open Window / Package Manager, choose `+` and `Add package from git URL...`, and paste:

```text
https://github.com/furkantokkan/Onity.git#upm
```

To pin the current release:

```text
https://github.com/furkantokkan/Onity.git?path=/Packages/com.onity.framework#v0.8.1
```

[Getting Started](Getting-Started.html) covers the manifest form, the embedded-package form and the
assembly references.

## The shape of an Onity project

An installer binds the scene's services; a plain C# service receives them through its constructor, ties
its subscription to the scope, and runs an async loop on the scope token, which the scope cancels before
it disposes the services. This is the installer and the service of the scene that
[Getting Started](Getting-Started.html) builds step by step:

```csharp
using Onity.Composition;        // BindReactiveProperty
using Onity.DI;                 // OnityContainer
using Onity.Unity.Installers;   // MonoInstaller, BindPooledFactory
using Onity.Unity.Messaging;    // BindMessageChannel
using UnityEngine;

public sealed class GameInstaller : MonoInstaller
{
    [SerializeField] private HitMarker m_hitMarkerPrefab;
    [SerializeField] private Transform m_hitMarkerRoot;

    public override void InstallBindings(OnityContainer container)
    {
        // ReactiveProperty<int> and IReadOnlyReactiveProperty<int>; the scope disposes it.
        container.BindReactiveProperty(initialValue: 100);

        // IPublisher<PlayerDamaged> and ISubscriber<PlayerDamaged> on the scope's message broker,
        // the same channel OnityEvent and OnityEventHub use.
        container.BindMessageChannel<PlayerDamaged>();

        // IFactory<HitMarker> to spawn and IPool<HitMarker> to release; the scope disposes the pool.
        container.BindPooledFactory(m_hitMarkerPrefab, m_hitMarkerRoot, defaultCapacity: 8, maxSize: 32);

        // The container constructs it, runs Initialize() at Build() and disposes it with the scope.
        container.BindInterfacesAndSelfTo<HealthService>().AsSingle();
    }
}
```

```csharp
using System;
using System.Threading;
using Onity.DI;                 // IOnityInitializable, IOnityScopeLifetime, AddTo(scope)
using Onity.Messaging;          // ISubscriber<T>
using Onity.Reactive;           // ReactiveProperty<T>
using Onity.Unity.Async;        // OnityTask, OnityTaskVoid

public sealed class HealthService : IOnityInitializable
{
    private const int k_maxHealth = 100;
    private const float k_regenerationInterval = 1f;

    private readonly ReactiveProperty<int> m_health;
    private readonly IOnityScopeLifetime m_scope;

    public HealthService(ReactiveProperty<int> health, ISubscriber<PlayerDamaged> damage, IOnityScopeLifetime scope)
    {
        m_health = health;
        m_scope = scope;
        damage.Subscribe(OnDamaged).AddTo(scope);     // unsubscribed when the scope ends
    }

    public void Initialize()
    {
        RegenerateAsync(m_scope.Token).Forget();      // the token is canceled before the scope disposes its services
    }

    private void OnDamaged(PlayerDamaged message)
    {
        m_health.SetValue(Math.Max(0, m_health.Value - message.Amount));
    }

    private async OnityTaskVoid RegenerateAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await OnityTask.Delay(k_regenerationInterval, token);
            m_health.SetValue(Math.Min(k_maxHealth, m_health.Value + 1));
        }
    }
}
```

## Evidence

A ratio is Onity's time divided by the other library's time for the same workload in the same process;
below 1 means Onity took less time. Measured with the runners in this repository on one Windows PC with
Unity 2022.3.62f2; timing results only. The [Comparisons](comparisons/index.html) pages hold the tables,
the conditions and the known slower cases.

- Dependency injection: fastest in all seven scenarios in every process of the Windows IL2CPP release
  Player run of 2026-10-02, Onity / VContainer 0.21 to 0.59 per process
  ([DI vs VContainer and Zenject](Onity-vs-VContainer-Zenject.html)).
- Reactive: faster than R3 1.3.0 and than UniRx 7.1.0 in all nine IL2CPP rows of the 2026-10-02
  comparison (medians 0.145 to 0.805 against R3, 0.129 to 0.872 against UniRx); on Mono faster than R3 in
  seven rows and than UniRx in five, with `CombineLatest` slower than both
  ([Reactive vs R3 and UniRx](comparisons/reactive-vs-r3-unirx.html)).
- Async: faster than UniTask 2.5.11 in all 29 gated IL2CPP rows of the 2026-10-02 gate (medians 0.085 to
  0.808); on Mono faster in 25 of 29, with the four synchronous completed-result rows 1.09x to 1.92x slower
  ([OnityTask vs UniTask](guide/onitytask-comparison.html)).
- Messaging: faster than MessagePipe 1.8.1 (the Unity package, compiled against UniTask 2.5.10) in all
  nine IL2CPP rows of the 2026-10-02 comparison (medians 0.150 to 0.846) and in all nine Mono rows (0.280
  to 0.756); also faster in all nine rows on both backends against MessagePipe's .NET build
  ([Messaging vs MessagePipe](comparisons/messaging-vs-messagepipe.html)).

## Guides

In reading order:

- [Dependency Injection](guide/dependency-injection.html)
- [Lifecycle and Scopes](guide/lifecycle-and-scopes.html)
- [Reactive](guide/reactive.html)
- [Events and Messaging](guide/events-messaging.html)
- [Async with OnityTask](guide/onitytask.html)
- [Factories and Pooling](guide/factories-and-pooling.html)
- [Performance and IL2CPP](guide/performance-and-il2cpp.html)
- [Refactoring from Existing Architecture](guide/refactoring-from-existing-architecture.html)

## Reference

- [DI API](reference/di-api.html)
- [Reactive Operators](reference/reactive-operators.html)
- [Messaging API](reference/messaging-api.html)

## Migration

- [From Zenject](Migration/From-Zenject.html)
- [From VContainer](Migration/From-VContainer.html)
- [From R3 and UniRx](Migration/From-R3.html)
- [From UniTask](Migration/From-UniTask.html)

## Comparisons

- [DI vs VContainer and Zenject](Onity-vs-VContainer-Zenject.html)
- [Reactive vs R3 and UniRx](comparisons/reactive-vs-r3-unirx.html)
- [OnityTask vs UniTask](guide/onitytask-comparison.html)
- [Messaging vs MessagePipe](comparisons/messaging-vs-messagepipe.html)
- [Measurement history](archive/index.html): older measurements, kept unchanged.

## More

- [AI Usage Guide](Onity-AI-Usage-Guide.html): the rules, the canonical scene, the API index and the error
  table, written for coding assistants and dense enough for people.
- [Architecture](Architecture-Review.html): assembly boundaries, scopes, activation paths and Unity adapters.
- [Architecture Decisions](ADR/): the accepted ADRs.
