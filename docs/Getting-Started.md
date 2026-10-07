---
title: "Getting Started"
nav_order: 1
description: "Install Onity and build one scene that uses dependency injection, reactive state, a typed message, an async loop and a pooled prefab together."
---

# Getting Started

This page installs Onity and walks you through one small scene, step by step. Onity
(`com.onity.framework`) puts dependency injection, reactive state, typed messaging, async (`OnityTask`)
and factories with pooling in one package with one lifetime model: a scope owns its services,
subscriptions, tasks and pools, and disposes them together. The core is engine-free, so the service you
write in step 4 is tested without a scene in step 7.

Contents:

- [What you will build](#what-you-will-build)
- [Step 1: install](#step-1-install)
- [Step 2: add a SceneContext](#step-2-add-a-scenecontext)
- [Step 3: write the installer](#step-3-write-the-installer)
- [Step 4: a constructor-injected service](#step-4-a-constructor-injected-service)
- [Step 5: react in a MonoBehaviour](#step-5-react-in-a-monobehaviour)
- [Step 6: publish an event and spawn from a pool](#step-6-publish-an-event-and-spawn-from-a-pool)
- [Step 7: test the service without a scene](#step-7-test-the-service-without-a-scene)
- [Common mistakes](#common-mistakes)
- [Where to go next](#where-to-go-next)

## What you will build

A scene scope owns the player's health. A `DamageZone` publishes a `PlayerDamaged` message when
something enters it. `HealthService`, a constructor-injected plain C# class, lowers the health and
regenerates it on an async loop that ends with the scope. `HealthHud` shows the health and spawns a
pooled `HitMarker` for each large hit, then releases it after half a second. One installer binds all of
it.

| File | Type | Role |
| --- | --- | --- |
| `PlayerDamaged.cs` | readonly struct | The message |
| `GameInstaller.cs` | `MonoInstaller` | The scene's bindings |
| `HealthService.cs` | plain C# class | Health rules and the regeneration loop |
| `HealthHud.cs` | `MonoBehaviour` | Shows health, spawns and releases hit markers |
| `HitMarker.cs` | `MonoBehaviour` | The pooled prefab |
| `DamageZone.cs` | `MonoBehaviour` | Publishes the message |
| `HealthServiceTests.cs` | NUnit test | Runs `HealthService` without a scene |

## Step 1: install

Onity supports Unity 2022.3 LTS or newer. Pick one install method.

Package Manager, git URL (`Window/Package Manager`, `+`, `Add package from git URL...`):

```text
https://github.com/furkantokkan/Onity.git#upm
```

The `upm` branch is the package at its repository root and tracks the latest release. To pin a release,
use the explicit form:

```text
https://github.com/furkantokkan/Onity.git?path=/Packages/com.onity.framework#v0.8.3
```

Or add the dependency to `Packages/manifest.json` yourself:

```json
{
  "dependencies": {
    "com.onity.framework": "https://github.com/furkantokkan/Onity.git?path=/Packages/com.onity.framework#v0.8.3"
  }
}
```

Both forms need Git on the machine. Without Git, copy the package folder into your project as an
embedded package at `<YourProject>/Packages/com.onity.framework/`.

No assembly definition edits are needed: every Onity runtime assembly is auto-referenced, so scripts in
Unity's predefined assemblies compile against Onity as soon as the package is installed. Code that lives
in your own `.asmdef` lists the Onity assemblies it uses (`Onity.Core`, `Onity.DI`, `Onity.Reactive`,
`Onity.Messaging`, `Onity.Factory`, `Onity.Pooling`, `Onity.Composition`, `Onity.Unity`) in that asmdef's
references, as it would for any package. References are not transitive: code that touches
`IOnityObservable<Unit>`, such as the frame streams from `OnityUnityObservable.EveryUpdate()`, needs
`Onity.Core`, where `Unit` lives, and code that calls the `Onity.Unity.Async` extensions needs
`Onity.Reactive`; without them the compiler reports CS0012.

Onity has no non-Unity third-party runtime dependencies. The Unity packages it needs (Input System,
Entities, Burst, Collections, Mathematics) are declared in its `package.json` and resolved by the Package
Manager.

To confirm the install, add this file anywhere under `Assets/`. If it compiles, the package is
referenced:

```csharp
using Onity.DI;                 // OnityContainer, IResolver

public static class OnityInstallCheck
{
    public static bool Works()
    {
        using OnityContainer container = new OnityContainer();
        container.Build();
        return container.Resolve<IResolver>() == container;   // a container resolves itself as IResolver
    }
}
```

Delete the file afterwards; it has no other purpose.

## Step 2: add a SceneContext

A context (`Onity.Unity.Contexts`) is a `MonoBehaviour` that owns one container, called a scope. In
`Awake` a `SceneContext` creates the container (as a child of the `ProjectContext` when one exists),
binds the defaults (the container, `IResolver`, `IOnityScopeLifetime`, the context itself,
`MessageBroker` and `OnityEventHub`), runs the installers in its Installers list, calls `Build()`, and
member-injects every `MonoBehaviour` under its own GameObject. In `Start` it runs `BuildAsync` for the
async initializers. `Update`, `FixedUpdate` and `LateUpdate` pump the lifecycle ticks. `OnDestroy`
disposes the container, which cancels the scope token first and then disposes the services.

In the Editor:

1. Create an empty GameObject and name it `SceneContext`.
2. Click Add Component, search for `Scene Context` and add it. The component has no Onity submenu;
   search for it by name.
3. Leave the Installers list empty for now; step 3 fills it.

Services that must outlive scene loads (settings, saves, audio) go on a `ProjectContext` instead. Create
its prefab once with the menu `Onity/Contexts/Create ProjectContext Prefab`; it is saved at
`Assets/Resources/Onity/ProjectContext.prefab` and loaded before the first scene. A `SceneContext` finds
`ProjectContext.Instance` on its own and becomes its child. This walkthrough needs only the
`SceneContext`.

## Step 3: write the installer

First the message. A message is a small struct or class; it needs nothing from Onity:

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

Then the installer. An installer is a `MonoInstaller` (`Onity.Unity.Installers`) with one method,
`InstallBindings`, which receives the scope's `OnityContainer` (`Onity.DI`):

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

Line by line:

- `BindReactiveProperty(initialValue: 100)` (`Onity.Composition`) creates one `ReactiveProperty<int>`
  and binds it as both `ReactiveProperty<int>` and `IReadOnlyReactiveProperty<int>`. A service that
  changes the value injects the first; a view that only reads it injects the second. The helper created
  the property, so the scope disposes it.
- `BindMessageChannel<PlayerDamaged>()` (`Onity.Unity.Messaging`) binds `IPublisher<PlayerDamaged>` and
  `ISubscriber<PlayerDamaged>` from the scope's `MessageBroker`, which every context binds for you. A
  message published through `OnityEvent` or `OnityEventHub` reaches these subscribers, because they are
  the same channel.
- `BindPooledFactory(prefab, parent, defaultCapacity, maxSize)` (`Onity.Unity.Installers`) creates a
  `PrefabComponentPool<HitMarker>` and binds `IPool<HitMarker>` and `IFactory<HitMarker>`. The scope
  disposes the pool, which destroys its inactive clones.
- `BindInterfacesAndSelfTo<HealthService>().AsSingle()` binds one `HealthService` under its own type
  and every interface it implements. The lifetime call is what registers the binding; `Bind` or
  `BindInterfacesAndSelfTo` alone registers nothing.

Add the `GameInstaller` component to the `SceneContext` object, then drag it into the context's
Installers list. The prefab and root fields are assigned in step 6.

## Step 4: a constructor-injected service

A plain C# class states what it needs in its constructor, and the container supplies it. Start without
the regeneration loop:

```csharp
using System;
using Onity.DI;                 // IOnityScopeLifetime, AddTo(scope)
using Onity.Messaging;          // ISubscriber<T>
using Onity.Reactive;           // ReactiveProperty<T>

public sealed class HealthService
{
    private readonly ReactiveProperty<int> m_health;

    public HealthService(ReactiveProperty<int> health, ISubscriber<PlayerDamaged> damage, IOnityScopeLifetime scope)
    {
        m_health = health;
        damage.Subscribe(OnDamaged).AddTo(scope);     // unsubscribed when the scope ends
    }

    private void OnDamaged(PlayerDamaged message)
    {
        m_health.SetValue(Math.Max(0, m_health.Value - message.Amount));
    }
}
```

Three things to notice:

- `ReactiveProperty<int>` and `ISubscriber<PlayerDamaged>` come from the installer. `IOnityScopeLifetime`
  (`Onity.DI`) is bound by every context: it is the scope that owns this service, with a `Token` that is
  canceled when the scope ends.
- `Subscribe` returns an `IDisposable`. `AddTo(scope)` disposes it when the scope ends, so the service
  never holds a subscription past its owner. Every `Subscribe` result in Onity is retained like this.
- `SetValue` changes the value and notifies subscribers only when the value changed.

Now add the regeneration loop. `IOnityInitializable` (`Onity.DI`) gives the service an `Initialize()`
that the container calls at the end of `Build()`, and `async OnityTaskVoid` (`Onity.Unity.Async`) is the
fire-and-forget method shape. The loop takes the scope token, so it stops before the scope disposes the
property it writes to:

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

`BindInterfacesAndSelfTo<HealthService>()` in the installer is what makes the container see
`IOnityInitializable`: binding the type is all the wiring needed. When the context is destroyed,
`m_scope.Token` is canceled, `OnityTask.Delay` ends canceled, the loop exits, and only then does the
scope dispose the property. An `OperationCanceledException` that ends an `async OnityTaskVoid` is dropped
by the scheduler by default, so the exit is silent.

## Step 5: react in a MonoBehaviour

Unity constructs MonoBehaviours, so they cannot take constructor arguments. Mark fields with `[Inject]`
(`Onity.DI`) and the context fills them in `Awake`, before the component's own `Start`. Begin with the
health display:

```csharp
using Onity.DI;                 // Inject
using Onity.Reactive;           // IReadOnlyReactiveProperty<T>
using Onity.Unity.Reactive;     // TakeUntilDisable
using UnityEngine;

public sealed class HealthHud : MonoBehaviour
{
    // Unity constructs MonoBehaviours, so the context injects members instead of a constructor.
    [Inject] private IReadOnlyReactiveProperty<int> m_health;

    private void OnEnable()
    {
        m_health.Subscribe(value => Debug.Log($"Health: {value}"))   // emits the current value first
                .TakeUntilDisable(this);
    }
}
```

`IReadOnlyReactiveProperty<int>` is the read-only contract bound by `BindReactiveProperty`; the HUD can
read and subscribe but not write. Subscribing emits the current value at once, so a HUD enabled late
still shows the right number. The subscription is made in `OnEnable`, so it is tied to
`TakeUntilDisable(this)` (`Onity.Unity.Reactive`) and disposed in `OnDisable`; the next `OnEnable`
subscribes again. Use `AddTo(this)` instead when a subscription is made once (for example in `Start`)
and should live until the component is destroyed. Both go after `Subscribe`, on the `IDisposable` it
returns.

## Step 6: publish an event and spawn from a pool

The publisher is a trigger volume. `OnityEvent.Publish(this, message)` (`Onity.Unity`) publishes through
the nearest context above the component, which is the `SceneContext` here:

```csharp
using Onity.Unity;              // OnityEvent
using UnityEngine;

public sealed class DamageZone : MonoBehaviour
{
    [SerializeField] private int m_damage = 25;

    private void OnTriggerEnter(Collider other)
    {
        OnityEvent.Publish(this, new PlayerDamaged(m_damage));   // the nearest context's channel
    }
}
```

The pooled prefab implements `IPoolHooks` (`Onity.Pooling`) so it can reset itself when the pool hands
it out and takes it back. `PrefabComponentPool<T>` activates the GameObject on get and deactivates it on
release; the hooks run inside those calls:

```csharp
using Onity.Pooling;            // IPoolHooks
using UnityEngine;

public sealed class HitMarker : MonoBehaviour, IPoolHooks
{
    public void Show(int amount)
    {
        // Position the marker and display the amount.
    }

    public void OnPoolGet()
    {
        // Reset per-use state when the pool hands this instance out.
    }

    public void OnPoolRelease()
    {
        // Clear references when the instance returns to the pool.
    }
}
```

Now the complete `HealthHud`. It observes the same `PlayerDamaged` channel as a stream, filters it with
`Where`, spawns a marker through `IFactory<HitMarker>` and releases it through `IPool<HitMarker>` after
a delay:

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

`OnityEvent.Observe<PlayerDamaged>(this)` returns an `IOnityObservable<PlayerDamaged>`, so the `Where`
operator from `Onity.Reactive` applies to it exactly as it applies to a reactive property.
`GetScopeCancellationToken()` (`Onity.Unity.Async`) returns the token of the nearest context above the
component, so the delay is canceled when the scene scope ends and a marker is never released into a
disposed pool. Each marker is released exactly once; a second release of the same instance is an error.

Wire the scene: create an empty GameObject `SceneContext`, add the `SceneContext` component and the
`GameInstaller` component, drag `GameInstaller` into the context's Installers list, assign the
`HitMarker` prefab and a root transform, and place `HealthHud` and `DamageZone` under the `SceneContext`
object so the context injects them in `Awake`. The context builds the container in `Awake` (which runs
`HealthService.Initialize`), injects the hierarchy, runs the async build in `Start`, pumps the lifecycle
ticks, and disposes the container in `OnDestroy`, which cancels the scope token first.

Press Play, move a collider into the `DamageZone`, and watch the health drop by 25, a `HitMarker` appear
for half a second, and the health climb back one point per second.

## Step 7: test the service without a scene

`HealthService` depends only on engine-free types, so an EditMode test builds it in a bare container.
`DeclareMessage<T>` (`Onity.Composition`) creates a standalone `MessageChannel<T>` and binds it as
`IPublisher<T>` and `ISubscriber<T>`; it stands in for the broker channel that a context would bind:

```csharp
using NUnit.Framework;
using Onity.Composition;        // BindReactiveProperty, DeclareMessage
using Onity.DI;
using Onity.Messaging;
using Onity.Reactive;

public sealed class HealthServiceTests
{
    [Test]
    public void Damage_lowers_health()
    {
        using OnityContainer container = new OnityContainer();
        ReactiveProperty<int> health = container.BindReactiveProperty(initialValue: 100);
        MessageChannel<PlayerDamaged> damage = container.DeclareMessage<PlayerDamaged>();  // standalone channel; a context would bind the broker's
        container.BindInstance<IOnityScopeLifetime>(container);                              // what a context binds for you
        container.BindInterfacesAndSelfTo<HealthService>().AsSingle();
        container.Build();                                                                    // constructs HealthService and runs Initialize()

        damage.Publish(new PlayerDamaged(30));

        Assert.AreEqual(70, health.Value);
    }   // Dispose cancels the scope token, which ends the regeneration loop
}
```

`OnityContainer` implements `IOnityScopeLifetime`, so binding the container itself under that contract
gives the service the same scope it gets from a context. The `using` declaration disposes the container
at the end of the test, which cancels the token and ends the regeneration loop before the property is
disposed.

## Common mistakes

- Retain every `Subscribe` result. Use `TakeUntilDisable(this)` for subscriptions made in `OnEnable`,
  `AddTo(this)` for subscriptions that live until destroy, `AddTo(scope)` in services, or a
  `CompositeDisposable` you dispose yourself. An unretained subscription outlives its owner.
- Put `AddTo`, `TakeUntilDisable` and `TakeUntilDestroy` after `Subscribe`. They extend the `IDisposable`
  that `Subscribe` returns, and they take a `Component` or `Behaviour`, so pass `this`.
- Finish every binding with a lifetime. `container.Bind<IFoo>().To<Foo>();` registers nothing; add
  `AsSingle()`, `AsScoped()` or `AsTransient()`.
- Bind before `Build()`. A context builds in `Awake`, so installers are the place for bindings. Build
  callbacks registered after the build throw `OnityBindingException`.
- Read injected members in `Start` or later. Field initializers run before injection, and a
  component's own `Awake` sees injected members only when it runs after the context's `Awake`, which
  the context execution orders arrange for default-ordered components under it.
- Resolve once and keep the reference. `Resolve` in `Update` is a lookup per frame for a value that
  injection already handed you.
- One class, one instance: `Bind<IFoo>().To<C>()` plus `Bind<IBar>().To<C>()` creates two `C` objects.
  Use `BindInterfacesAndSelfTo<C>().AsSingle()` to share one across its contracts.
- `DeclareMessage` channels are not the broker. A channel from `DeclareMessage<T>` is standalone;
  `OnityEvent` and `OnityEventHub` publish through the scope's broker and never reach it. In a scene use
  `BindMessageChannel<T>`, which binds the broker's channel.
- Await a pooled `OnityTask` once. The task value returns to a pool after its single consumer; store an
  `OnityTask` in a field only to await it once later. See
  [Single-consumer rule for pooled tasks](guide/onitytask.html#single-consumer-rule-for-pooled-tasks).
- Never write `async void`. Use `async OnityTaskVoid` for fire-and-forget methods, pass a token, and
  name who cancels it.
- Do not add a second DI container, reactive library or message bus next to Onity to fill a gap. Model
  current state as a `ReactiveProperty<T>`, past-tense notifications as messages, and single-owner
  commands as direct calls on an injected service.

## Where to go next

- Guides, in reading order: [Dependency Injection](guide/dependency-injection.html),
  [Lifecycle and Scopes](guide/lifecycle-and-scopes.html), [Reactive](guide/reactive.html),
  [Events and Messaging](guide/events-messaging.html), [Async with OnityTask](guide/onitytask.html),
  [Factories and Pooling](guide/factories-and-pooling.html),
  [Performance and IL2CPP](guide/performance-and-il2cpp.html) and
  [Refactoring from Existing Architecture](guide/refactoring-from-existing-architecture.html).
- [Reference](reference/index.html): the DI, reactive operator and messaging catalogs.
- [Migration](Migration/index.html): mapping tables from Zenject, VContainer, R3, UniRx and UniTask.
- [Comparisons](comparisons/index.html): what was measured against other libraries, with conditions.
- [AI Usage Guide](Onity-AI-Usage-Guide.html): the rules, recipes and error table written for coding
  assistants, which also serve as a dense lookup for people.
