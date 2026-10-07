# Onity

Onity (`com.onity.framework`) is a Unity package that puts dependency injection, reactive state, typed
messaging, async (`OnityTask`) and factories with pooling in one package with one lifetime model: a scope
owns its services, subscriptions, tasks and pools, and disposes them together. The core is engine-free
and has no non-Unity third-party runtime dependency.

[![Onity CI](https://github.com/furkantokkan/Onity/actions/workflows/onity-ci.yml/badge.svg)](https://github.com/furkantokkan/Onity/actions/workflows/onity-ci.yml)
![Unity 2022.3+](https://img.shields.io/badge/Unity-2022.3%2B-black?logo=unity)
![License MIT](https://img.shields.io/badge/license-MIT-green)

Unity 2022.3 LTS or newer. The repository's Unity project pins 2022.3.62f2.

[Documentation site](https://furkantokkan.github.io/Onity/) · [Getting Started](docs/Getting-Started.md) ·
[AI usage guide](docs/Onity-AI-Usage-Guide.md) · [Comparisons](docs/comparisons/index.md) ·
[Changelog](CHANGELOG.md)

## Why one package

A Unity project usually assembles a container (Zenject or VContainer), a reactive library (R3 or UniRx),
a message bus (MessagePipe) and an async library (UniTask), each with its own idiom and its own disposal
rule. Onity's pillars are designed to work with each other:

- A scope (`ProjectContext`, `SceneContext`, `GameObjectContext`, or a plain `OnityContainer`) owns the
  services it created, the subscriptions retained with `AddTo(scope)`, the async work started with its
  token and the pools it created, and disposes them in a fixed order: the token is canceled first, so
  async work stops before the services it uses are torn down.
- Every context binds the message broker and `OnityEventHub`, and `Observe<T>()` returns the same
  `IOnityObservable<T>` as `Subject<T>` and `ReactiveProperty<T>`, so a message flows into the operator
  chain without an adapter.
- Every `Subscribe` returns an `IDisposable` retained the same way across state and messages:
  `TakeUntilDisable(this)`, `AddTo(this)`, `AddTo(scope)` or `AddTo(bag)`.
- `OnityTask` connects to the other pillars in ways a standalone async library cannot: the scope token
  (`IOnityScopeLifetime`, `GetScopeCancellationToken()`), awaited startup (`IOnityAsyncInitializable`,
  `OnityContext.WaitReadyAsync`), waits on a `ReactiveProperty<T>` (`WaitAsync`, `WaitUntilAsync`), and
  message receives with backpressure (`ReceiveAsync`, `ReceiveAllAsync`, `SubscribeQueued`).
- `BindPooledFactory` binds `IFactory<T>` to spawn and `IPool<T>` to release, and the scope disposes the
  pool.

## What is in the package

| Pillar | Assemblies | Main types | Guide |
| --- | --- | --- | --- |
| Dependency injection | `Onity.DI`, `Onity.Factory` (engine-free) | `OnityContainer`, `MonoInstaller`, `[Inject]`, `IOnityScopeLifetime`, the lifecycle interfaces, `IFactory<...>` | [Dependency Injection](docs/guide/dependency-injection.md), [Lifecycle and Scopes](docs/guide/lifecycle-and-scopes.md) |
| Reactive state | `Onity.Reactive` (engine-free), `Onity.Unity.Reactive` bridges | `IOnityObservable<T>`, `Subject<T>`, `ReactiveProperty<T>`, operators, `OnityUnityObservable` | [Reactive](docs/guide/reactive.md) |
| Messaging | `Onity.Messaging` (engine-free), `Onity.Unity.Messaging` | `IMessageBroker`, `MessageChannel<T>`, `OnityEventHub`, `OnityEvent`, keyed and async channels | [Events and Messaging](docs/guide/events-messaging.md) |
| Async | `Onity.Unity.Async` | `OnityTask`, `OnityTask<T>`, `OnityTaskVoid`, PlayerLoop timings, triggers, streams, channels | [Async with OnityTask](docs/guide/onitytask.md) |
| Factories and pooling | `Onity.Pooling`, `Onity.Unity.Installers` | `OnityObjectPool<T>`, `PrefabComponentPool<T>`, `IPool<T>`, `IPoolHooks`, `BindPooledFactory` | [Factories and Pooling](docs/guide/factories-and-pooling.md) |
| Composition | `Onity.Composition` (engine-free) | `BindReactiveProperty`, `BindSubject`, `DeclareMessage`, `DeclareAsyncMessage` | [Dependency Injection](docs/guide/dependency-injection.md#shared-reactive-and-messaging-primitives) |
| DOTS | `Onity.DOTS` (with `com.unity.entities` 1.0+) | Burst `ISystem` groups that drain the broker into Entities, entity pooling helpers | [Architecture](docs/Architecture-Review.md) |

The engine-free assemblies carry no `UnityEngine` reference, so domain code and EditMode tests use them
without a scene. Every runtime assembly is auto-referenced.

## Install

In Unity, open Window / Package Manager, choose `+` and `Add package from git URL...`, and paste:

```text
https://github.com/furkantokkan/Onity.git#upm
```

The `upm` branch is the package at its repository root and tracks the latest release. To pin a release:

```text
https://github.com/furkantokkan/Onity.git?path=/Packages/com.onity.framework#v0.8.3
```

Or in `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.onity.framework": "https://github.com/furkantokkan/Onity.git?path=/Packages/com.onity.framework#v0.8.3"
  }
}
```

Both forms need Git on the machine. Without Git, copy `Packages/com.onity.framework` into your project's
`Packages/` folder as an embedded package. The Unity packages Onity needs (Input System, Entities, Burst,
Collections, Mathematics) are declared in its `package.json` and resolved by the Package Manager.

## Quick start

One scene scope owns the player's health. A `DamageZone` publishes `PlayerDamaged`; `HealthService`, a
constructor-injected plain C# class, lowers the health and regenerates it on an async loop that ends with
the scope; `HealthHud` shows the health and spawns a pooled `HitMarker` for each large hit. One installer
binds all of it. [Getting Started](docs/Getting-Started.md) builds the same scene step by step, including
`HitMarker`, `DamageZone` and a test that runs `HealthService` without a scene.

`PlayerDamaged.cs` is the message: plain C#, nothing from Onity.

```csharp
public readonly struct PlayerDamaged
{
    public readonly int Amount;

    public PlayerDamaged(int amount)
    {
        Amount = amount;
    }
}
```

`GameInstaller.cs` holds the scene's bindings.

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

`HealthService.cs` is a plain C# service. The container builds it; the scope owns its subscription and
its loop.

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

`HealthHud.cs` is a thin MonoBehaviour. Unity constructs it, so the context injects members.

```csharp
using System.Threading;
using Onity.DI;                 // Inject
using Onity.Factory;            // IFactory<T>
using Onity.Pooling;            // IPool<T>
using Onity.Reactive;           // IReadOnlyReactiveProperty<T>, Where
using Onity.Unity;              // OnityEvent
using Onity.Unity.Async;        // OnityTask, OnityTaskVoid, GetScopeCancellationToken
using Onity.Unity.Reactive;     // TakeUntilDisable
using UnityEngine;

public sealed class HealthHud : MonoBehaviour
{
    private const float k_hitMarkerSeconds = 0.5f;

    // Unity constructs MonoBehaviours, so the context injects members instead of a constructor.
    [Inject] private IReadOnlyReactiveProperty<int> m_health;
    [Inject] private IFactory<HitMarker> m_hitMarkers;
    [Inject] private IPool<HitMarker> m_hitMarkerPool;

    private void OnEnable()
    {
        m_health.Subscribe(value => Debug.Log($"Health: {value}"))   // emits the current value first
                .TakeUntilDisable(this);

        OnityEvent.Observe<PlayerDamaged>(this)                       // the channel HealthService subscribes to
                  .Where(message => message.Amount >= 10)
                  .Subscribe(ShowHit)
                  .TakeUntilDisable(this);
    }

    private void ShowHit(PlayerDamaged message)
    {
        HitMarker marker = m_hitMarkers.Create();                     // taken from the pool
        marker.Show(message.Amount);
        ReleaseLaterAsync(marker, this.GetScopeCancellationToken()).Forget();
    }

    private async OnityTaskVoid ReleaseLaterAsync(HitMarker marker, CancellationToken token)
    {
        await OnityTask.Delay(k_hitMarkerSeconds, token);             // ends with the scope
        m_hitMarkerPool.Release(marker);                              // back to the pool, exactly once
    }
}
```

Wire the scene: create an empty GameObject `SceneContext`, add the `SceneContext` component and the
`GameInstaller` component, drag `GameInstaller` into the context's Installers list, assign the `HitMarker`
prefab (a `MonoBehaviour` that implements `IPoolHooks`) and a root transform, and place `HealthHud` and the
`DamageZone` (which calls `OnityEvent.Publish(this, new PlayerDamaged(25))`) under the `SceneContext` object.
The context builds the container in `Awake`, which runs `HealthService.Initialize`, injects the hierarchy,
pumps the lifecycle ticks, and disposes the container in `OnDestroy`, which cancels the scope token first.

## Evidence

Every number below is Onity's time divided by the other library's time for the same workload in the same
process (below 1 means Onity took less time), measured with the runners in this repository on one Windows
PC (AMD Ryzen 9 5900X) with Unity 2022.3.62f2. The [Comparisons](docs/comparisons/index.md) pages hold the
full tables, the conditions and the known slower cases; these are timing results, not allocation or
cross-platform claims.

- Dependency injection. In the Windows IL2CPP release Player run of 2026-10-02 (the published 0.6.0
  package, whose DI code is unchanged through 0.8.2; three processes; 512 warmups and 8 samples of 10,000
  operations), Onity Baked was fastest in all seven scenarios in every process, with Onity / VContainer
  per-process ratios of 0.21 to 0.59 ([record](docs/benchmarks/di-remeasure-2026-10-02.md)). On
  Editor/Mono it was faster than VContainer and Zenject in all seven scenarios, with the same
  per-operation allocation event counts as VContainer ([record](docs/benchmarks/remeasure-2026-10-01.md)).
- Reactive. In the Release Player comparison of 2026-10-02 (R3 1.3.0 and UniRx 7.1.0, three processes per
  backend), Onity.Reactive 0.7.0 took less time than R3 in all nine IL2CPP rows, median ratios 0.145 to
  0.805 with no process above 0.811, and less time than UniRx in all nine rows, 0.129 to 0.872 with no
  process above 0.874. Mono is reported, not gated: faster than R3 in seven rows, on par in one and slower
  in `CombineLatest` (1.378); faster than UniRx in five rows, on par in two and slower in single-subscriber
  publish (1.098) and `CombineLatest` (1.527) ([record](docs/assets/benchmarks/reactive-surpass-r3-2026-10-02.md)).
- Async. In the Release Player gate of 2026-10-02 (UniTask 2.5.11, three processes per backend and suite,
  context flow off, pool retention matched for the 1,024- and 4,096-operation bursts), OnityTask took less
  time than UniTask in all 29 gated IL2CPP rows, median ratios 0.085 to 0.808, worst process 0.906. On
  Mono it is faster in 25 of 29 rows; the four synchronous completed-result rows are 1.09x to 1.92x slower
  ([record](docs/assets/benchmarks/onitytask-surpass-2026-10-02.md)).
- Messaging. In the Release Player comparison of 2026-10-02 (MessagePipe 1.8.1 as the Unity package
  compiled against UniTask 2.5.10, three processes per backend), Onity.Messaging 0.8.0 took less time than
  MessagePipe in all nine IL2CPP rows, median ratios 0.150 to 0.846 with no process above 0.895, and in
  all nine Mono rows, 0.280 to 0.756 with no process above 0.862. Against MessagePipe's .NET build (the
  NuGet dll) it took less time in all nine rows on both backends as well
  ([record](docs/assets/benchmarks/messaging-surpass-messagepipe-2026-10-02.md)).
- Pooling. Against Zenject's `MemoryPool`, paired within one process: one reused item in the checked
  Editor/Mono test, 0.590x and 0.579x; the 32-item two-parameter burst in a Windows IL2CPP Release Player,
  0.738x and 0.729x ([record](docs/benchmarks/pool-own-stack-2026-10-01.md)).

Known slower cases, from the same records: OnityTask with opt-in `AsyncLocal` flow is 1.5x to 1.6x slower
than UniTask on IL2CPP in the four-suspension lifecycle, and 4,096-call bursts at the default pool
retention are up to 2.6x slower (raise `OnityTask.RunnerPoolCapacity` and `SourcePoolCapacity`); on Mono,
Onity.Reactive's `CombineLatest` is 1.378x slower than R3 and 1.527x slower than UniRx, and its
single-subscriber publish 1.098x slower than UniRx.

## Documentation

- [Getting Started](docs/Getting-Started.md): install Onity and build the scene above step by step.
- Guides: [Dependency Injection](docs/guide/dependency-injection.md),
  [Lifecycle and Scopes](docs/guide/lifecycle-and-scopes.md), [Reactive](docs/guide/reactive.md),
  [Events and Messaging](docs/guide/events-messaging.md), [Async with OnityTask](docs/guide/onitytask.md),
  [Factories and Pooling](docs/guide/factories-and-pooling.md),
  [Performance and IL2CPP](docs/guide/performance-and-il2cpp.md),
  [Refactoring from Existing Architecture](docs/guide/refactoring-from-existing-architecture.md).
- Reference: [DI API](docs/reference/di-api.md), [Reactive Operators](docs/reference/reactive-operators.md),
  [Messaging API](docs/reference/messaging-api.md).
- Migration: [From Zenject](docs/Migration/From-Zenject.md), [From VContainer](docs/Migration/From-VContainer.md),
  [From R3 and UniRx](docs/Migration/From-R3.md), [From UniTask](docs/Migration/From-UniTask.md).
- Comparisons: [DI vs VContainer and Zenject](docs/Onity-vs-VContainer-Zenject.md),
  [Reactive vs R3 and UniRx](docs/comparisons/reactive-vs-r3-unirx.md),
  [OnityTask vs UniTask](docs/guide/onitytask-comparison.md),
  [Messaging vs MessagePipe](docs/comparisons/messaging-vs-messagepipe.md); older measurements in
  [Measurement history](docs/archive/index.md).
- [AI usage guide](docs/Onity-AI-Usage-Guide.md): the rules, the canonical scene, the API index and the
  error table for coding assistants. The package ships it with the guides under `Documentation~/`, with
  [`AGENTS.md`](Packages/com.onity.framework/AGENTS.md), `CLAUDE.md` and `llms.txt`, and the Editor command
  `Onity/AI/Install Assistant Guidance...` copies the `onity-use` skill into your project.
- [Architecture](docs/Architecture-Review.md), the [architecture decisions](docs/ADR/index.md), the
  package [engineering notes](Packages/com.onity.framework/ENGINEERING.md) and [CONTRIBUTING.md](CONTRIBUTING.md).

The package root also holds a [README](Packages/com.onity.framework/README.md) for the installed package
and the [CHANGELOG](CHANGELOG.md).

## Requirements

- Unity 2022.3 LTS or newer. Unity-only: standalone .NET, Godot and other engines are out of scope.
- No non-Unity third-party runtime dependency; the runtime uses no `System.Linq`.
- The Input System reactive bridge compiles with `ENABLE_INPUT_SYSTEM`; the DOTS bridge with
  `com.unity.entities` 1.0 or newer; the uGUI async events with `com.unity.ugui`.

## Credits

Onity is inspired by the libraries it sets out to unify:

- [Zenject / Extenject](https://github.com/modesttree/Zenject): the binding vocabulary and the automatic
  entry-point lifecycle.
- [VContainer](https://github.com/hadashiA/VContainer): the performance focus and the container
  diagnostics window.
- [R3](https://github.com/Cysharp/R3) and [UniRx](https://github.com/neuecc/UniRx): the reactive model and
  UniRx's `MessageBroker`.
- [MessagePipe](https://github.com/Cysharp/MessagePipe): the typed, DI-native pub/sub broker.
- [UniTask](https://github.com/Cysharp/UniTask): the PlayerLoop-driven async patterns.

## Author

Furkan Tokkan ([@furkantokkan](https://github.com/furkantokkan))

## License

MIT; see [LICENSE](LICENSE).
