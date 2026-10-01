---
title: "Dependency Injection"
parent: "Guides"
nav_order: 1
---

# Dependency Injection

Onity's DI is built around `OnityContainer` — a sealed, engine-free, parent-scoped container that implements `IResolver` and `IDisposable`. The binding vocabulary is deliberately Zenject-familiar (`Bind<T>().To<C>().AsSingle()`), so existing Unity muscle memory transfers, while the container itself has no `UnityEngine` dependency and runs in plain EditMode tests with no scene.

```csharp
using Onity.DI;

using OnityContainer container = new OnityContainer();
container.Bind<IInputService>().To<KeyboardInputService>().AsSingle();
container.Build();

IInputService input = container.Resolve<IInputService>();
```

This page covers the binding surface, keyed and conditional injection, the four injection sites, scoping via child containers and scoped bindings, factories for runtime arguments, and the documented edge behaviors. For prefab pools and pooled factories see [Factories & Pooling](factories-and-pooling.html); for the automatic per-frame/startup lifecycle see [Lifecycle & Scopes](lifecycle-and-scopes.html); for the compiled/reflection activation story see [Performance & IL2CPP](performance-and-il2cpp.html).

## Binding

A binding registers when you call `.AsSingle()`, `.AsScoped()`, `.AsTransient()`, or the terminal `.FromSubContainerResolve(...)`. `.AsScoped()` caches one instance in each resolving container; a child resolving its parent's scoped binding gets its own instance and uses its own dependencies.

```csharp
// Contract -> implementation, choose a lifetime (the lifetime call is required):
container.Bind<IInputService>().To<KeyboardInputService>().AsSingle();   // one shared instance
container.Bind<IPathfinder>().To<AStarPathfinder>().AsTransient();       // new instance per resolve
container.Bind<IRequestState>().To<RequestState>().AsScoped();           // one per resolving scope
container.Bind<IClock>().To<SystemClock>().AsSingle().NonLazy();         // built eagerly at Build()

// Self-bind shorthand (To defaults to the contract type):
container.Bind<GameState>().AsSingle();                                  // == Bind<GameState>().To<GameState>().AsSingle()
```

`NonLazy()` resolves its binding during `Build()` instead of on first use. It throws `OnityBindingException` if called before a lifetime or sub-container source is registered.

### Identified, conditional, and replaceable bindings

```csharp
container.Bind<IClock>().To<GameClock>().AsSingle();
container.Bind<IClock>().To<ReplayClock>().WithId("replay").AsSingle();
container.Bind<IClock>().To<EditorClock>()
    .WhenInjectedInto<PreviewController>().AsSingle();

IClock replay = container.Resolve<IClock>("replay");
container.Rebind<IClock>("replay").To<ReplayClock>().AsScoped();
container.Unbind<IClock>("replay"); // removes this scope's matching ID only
```

Use `[Inject(Id = "replay")]` on a field or property, or on an individual constructor or method parameter. IDs compare by value. A matching consumer-specific binding wins over an unconditional binding in the same scope; two matching conditions throw an ambiguity error. `Unbind` and `Rebind` affect the local container; ancestor registrations remain available. Previously created singleton or scoped instances are disposed with their owning scope.

### Sub-container exports

```csharp
container.Bind<IEnemyAI>().FromSubContainerResolve(scope =>
{
    scope.Bind<IEnemyAI>().To<BossAI>().AsScoped();
    scope.Bind<IBossRules>().To<BossRules>().AsSingle();
});
```

The installer runs once per requesting scope. The exported contract must be bound inside that child container; it never falls back to the parent's export. The child binding chooses the service lifetime. The requesting scope forwards lifecycle ticks and disposes its installed child scopes.

### Sharing one instance across a concrete and its interfaces

Two separate `Bind<IFoo>().To<C>()` and `Bind<IBar>().To<C>()` calls produce **two distinct** singletons. To share **one** instance across a concrete type and all of its interfaces, use `BindInterfacesAndSelfTo`:

```csharp
// One PlayerStateService instance, resolvable as IPlayerState, IFoo, ... AND PlayerStateService:
container.BindInterfacesAndSelfTo<PlayerStateService>().AsSingle();

// Interfaces only (throws OnityBindingException if the type implements none):
container.BindInterfacesTo<PlayerStateService>().AsSingle();
```

### Pre-built instances

```csharp
container.BindInstance<IConfig>(loadedConfig);   // rejects null with OnityBindingException
```

### Open generics

Bind an **open** generic definition once and resolve any **closed** form of it. On the first resolve of a closed contract the closed implementation is built and cached as a normal binding, so later resolves of the same closed type hit the fast path.

```csharp
container.Bind(typeof(IRepository<>)).To(typeof(InMemoryRepository<>)).AsTransient();

IRepository<Player> players = container.Resolve<IRepository<Player>>();   // closed form resolves
```

### Collection injection

Register the same contract more than once and resolve all implementations as a collection. Supported shapes are `IEnumerable<T>`, `IReadOnlyList<T>`, `T[]`, and `List<T>`:

```csharp
container.Bind<IHandler>().To<SaveHandler>().AsSingle();
container.Bind<IHandler>().To<LoadHandler>().AsSingle();

IReadOnlyList<IHandler> handlers = container.Resolve<IReadOnlyList<IHandler>>();
```

A single-type `Resolve<IHandler>()` still returns the **last** registered binding (last-binding-wins), so collection resolution is opt-in by the collection type you ask for.

### Binding-surface summary

| Call | Returns | Then |
| --- | --- | --- |
| `Bind<TContract>()` | `TypeBindingBuilder<TContract>` | `.To<TConcrete>()`, then `.AsSingle()` / `.AsScoped()` / `.AsTransient()`, or `.FromSubContainerResolve(...)`; optional `.NonLazy()` |
| `BindInterfacesAndSelfTo<TConcrete>()` | `MultiTypeBindingBuilder` | `.AsSingle()` / `.AsScoped()` / `.AsTransient()`, then optional `.NonLazy()` |
| `BindInterfacesTo<TConcrete>()` | `MultiTypeBindingBuilder` | same as above |
| `BindInstance<TContract>(instance)` | `void` | optional ID overload |
| `BindFactory<TValue,TFactory>()` (+1-param, +2-param) | `void` | binds the factory `AsSingle` via `BindInterfacesAndSelfTo` |

## Resolving

```csharp
IInputService input = container.Resolve<IInputService>();         // throws OnityResolveException if unresolvable
object svc = container.Resolve(typeof(IInputService));            // runtime-type overload

if (container.TryResolve<IPathfinder>(out IPathfinder pathfinder)) { }   // false instead of throwing
if (container.TryResolve(typeof(IPathfinder), out object p)) { }

bool can = container.CanResolve(typeof(IFoo));                    // check without instantiating
container.Inject(existingObject);                                // member-inject an already-created object
```

`OnityContainer` and `IResolver` always self-resolve to the active container. Inject `IResolver` when a type needs to perform manual resolves (for example inside a factory).

## Injection sites

Onity injects through four sites. **Constructor injection is preferred**; use the `[Inject]` attribute on fields, properties, or methods only when a constructor cannot do the work.

```csharp
using Onity.DI;

public sealed class CombatService
{
    private readonly IDamageCalculator m_damage;

    // Constructor injection. Selection rule: a single [Inject] ctor wins; otherwise the
    // greediest public constructor (most parameters) is chosen. This is "greediest",
    // not Zenject's "fewest".
    public CombatService(IDamageCalculator damage)
    {
        m_damage = damage;
    }

    [Inject] private IClock m_clock;                 // field injection (private is fine)
    [Inject] public ILogger Logger { get; set; }     // property injection (a setter is required)

    [Inject]                                          // method injection (runs last)
    private void Initialize(IConfig config)           // cannot be generic
    {
        // post-construction wiring
    }
}
```

Member injection order is base class -> derived class, and within a type **fields -> properties -> methods**. Static members are never injected. The following each throw `OnityBindingException` at resolve time: more than one `[Inject]` constructor, an `[Inject]` property without a setter, an `[Inject]` indexer, and a generic `[Inject]` method.

## Factories (runtime arguments)

There is no `container.Instantiate<T>(args)` and no fluent factory body. To pass a runtime value into a constructed object, author an `IFactory<...>` (from `Onity.Factory`) and register it with `BindFactory`. Factories are always bound `AsSingle`.

```csharp
using Onity.DI;
using Onity.Factory;

public sealed class EnemyFactory : IFactory<string, Enemy>
{
    private readonly IResolver m_resolver;            // IResolver self-injects
    public EnemyFactory(IResolver resolver) { m_resolver = resolver; }

    public Enemy Create(string id) => new Enemy(id, m_resolver.Resolve<IClock>());
}

// Registration + use:
container.BindFactory<string, Enemy, EnemyFactory>();
Enemy goblin = container.Resolve<IFactory<string, Enemy>>().Create("goblin");
```

`BindFactory` has zero-, one-, and two-parameter overloads matching `IFactory<TValue>`, `IFactory<TParam,TValue>`, and `IFactory<TParam1,TParam2,TValue>`. See [Factories & Pooling](factories-and-pooling.html) for prefab pooled factories, `IPool<T>`, and `IPoolHooks` examples.

### Pooled factories and capacity

`OnityObjectPool<T>` and `PrefabComponentPool<TComponent>` accept `initialSize` to create distinct items up front. `maxSize` limits retained inactive items by default; add `fixedSize: true` to cap total created items and throw when all are checked out. `Prewarm(count)` can raise the target total later without calling pool get/release hooks. Prefab clones are created under an inactive parent, so an active prefab's clone does not receive `OnEnable` before runtime parameters are applied. Do not depend on runtime parameters in `Awake`. Return checked-out items before disposing the pool; `Get`, `Release`, `Prewarm`, and `Clear` throw `ObjectDisposedException` afterward.

```csharp
using Onity.Factory;
using Onity.Pooling;

var pool = new OnityObjectPool<Enemy>(
    () => new Enemy(), initialSize: 32, maxSize: 32, fixedSize: true);
IFactory<int, Enemy> factory = new PooledFactory<int, Enemy>(
    pool, (enemy, level) => enemy.SetLevel(level));

Enemy enemy = factory.Create(5);
pool.Release(enemy);
```

One- and two-parameter `PooledFactory` adapters apply the new values before pool get hooks (and before the prefab pool's activation step) on every reuse. Register the pool and factory with `BindInstance` when injecting them; bound instances remain caller-owned and should be disposed by their owner.

For the no-parameter prefab case, `container.BindPooledFactory(enemyPrefab)` registers both `IFactory<EnemyView>` and `IPool<EnemyView>` in one call (`enemyPrefab` is an `EnemyView` component). Use the explicit pool and adapter construction above when you need prewarming, fixed capacity, or runtime parameters.

## Build and async startup

```csharp
container.RegisterBuildCallback(r => r.Resolve<IGameLoopRunner>().Start());
container.RegisterBuildCallbackAsync(async (r, ct) => await r.Resolve<ISaveLoader>().PrimeAsync(ct));

container.Build();                              // runs sync callbacks once; idempotent
await container.BuildAsync(cancellationToken); // runs Build() then async callbacks; result cached
```

Callbacks cannot be registered after build is finalized (throws `OnityBindingException`). `Dispose()` disposes owned singletons in reverse registration order.

## Documented behaviors (test-locked)

These behaviors are locked by tests; rely on them, and avoid the listed traps.

| Behavior | Notes |
| --- | --- |
| Implicit transients | Unbound **concrete** classes auto-resolve as transients. Do not rely on this for shared state — it is not a singleton. |
| Unbound abstractions | Unbound interfaces, abstract classes, and open generics throw `OnityResolveException`. Bind them. |
| Last-binding-wins | Re-binding the same contract replaces the previous binding (no duplicate-binding error). Use it to override; do not expect a conflict exception. |
| Distinct singletons | Two separate `Bind<I>().To<C>()` calls do **not** share an instance. Use `BindInterfacesAndSelfTo<C>().AsSingle()` to share one. |
| Circular dependency | Constructor and member cycles throw `OnityResolveException` at **resolve time** (not build time). Break the cycle by injecting a factory or `IResolver`. |
| Constructor selection | The greediest **public** constructor wins (or the single `[Inject]` constructor). Do not add a second `[Inject]` constructor. |
| Conditional / keyed binds | `WhenInjectedInto` and `WithId` are configured before the lifetime. Missing non-null IDs do not implicitly construct services. |

## Unity wiring

In a scene, bindings live in a `MonoInstaller`. A context component (`ProjectContext` / `SceneContext` / `GameObjectContext`) creates the container, registers the default bindings (the container, `IResolver`, itself, `MessageBroker`, `OnityEventHub`), runs your installers, builds, and injects the hierarchy. See [Lifecycle & Scopes](lifecycle-and-scopes.html) for the full context model.

```csharp
using Onity.DI;
using Onity.Unity.Installers;          // MonoInstaller, BindScriptableObject
using Onity.Unity.Messaging;           // BindMessageChannel<T>
using UnityEngine;

public sealed class GameInstaller : MonoInstaller
{
    [SerializeField] private GameConfig m_config;

    public override void InstallBindings(OnityContainer container)
    {
        container.BindScriptableObject(m_config);                              // inject + bind a ScriptableObject
        container.Bind<IScoreService>().To<ScoreService>().AsSingle();
        container.BindInterfacesAndSelfTo<EnemySpawner>().AsSingle().NonLazy(); // eager, multi-contract
        container.BindMessageChannel<ScoreChanged>();                          // see Events & Messaging
    }
}
```

## See also

- [Events & Messaging](events-messaging.html) — the auto-bound broker and `OnityEventHub`.
- [Reactive](reactive.html) — `ReactiveProperty<T>` as shared, DI-bound state.
- [Lifecycle & Scopes](lifecycle-and-scopes.html) — child scopes, contexts, and the automatic lifecycle.
- [Factories & Pooling](factories-and-pooling.html) — runtime-argument factories and prefab pool wiring.
- [Performance & IL2CPP](performance-and-il2cpp.html) — compiled activators and the AOT fallback.
- [Migration: From Zenject](../Migration/From-Zenject.html) and [From VContainer](../Migration/From-VContainer.html).
