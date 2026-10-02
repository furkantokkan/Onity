---
title: "AI Usage Guide"
nav_order: 6
description: "Source-verified Onity API rules and recipes for DI, reactive state, messaging, Unity integration, and OnityTask."
---

# Onity AI Usage Guide

Machine-readable usage guide for writing CORRECT Onity code across the three pillars:
**DI** (replaces Zenject/VContainer), **Reactive** (replaces R3/UniRx), **Events** (replaces MessagePipe and the UniRx `MessageBroker`),
plus **Async** with `OnityTask` (replaces UniTask; section 11).

This guide is verified against the real Onity source. Every code block compiles against the current
public API. When this guide and any older design doc disagree, **this guide and the source win**.

- Target: Unity. Core asmdefs (`Onity.Core`, `Onity.DI`, `Onity.Reactive`, `Onity.Messaging`,
  `Onity.Factory`, `Onity.Composition`) are **engine-free** (no `UnityEngine`). Unity glue lives in `Onity.Unity`.
- Constraints baked into the code: hot-path machinery **designed to avoid per-call managed allocation** (a transient resolve still allocates the instance it returns; the published alloc figures were unreliable and are being re-measured), **no `System.Linq`** in the core, and **no non-Unity third-party runtime dependencies**.
- Naming convention in Onity source: private instance `m_camelCase`, private static `s_camelCase`,
  constants `k_camelCase`, Allman braces. Match it when adding code to the package.

---

## 1. Use Onity, not Zenject/R3/MessagePipe — one idiom, one install

You do NOT mix three libraries. Onity is one package with one mental model:

```
DI is the spine.            Bind services in a MonoInstaller; resolve via constructor injection.
Events ride the broker.     Use OnityEvent.Publish/Subscribe in Unity code; inject OnityEventHub in services.
Reactive operators ride both. Subject<T>/ReactiveProperty<T> AND broker.Observe<T>() are the SAME
                            IOnityObservable<T>, so Where/Select/Subscribe work on events and state alike.
Lifetime is one model.      Every Subscribe returns IDisposable. Dispose it with AddTo(this) (Unity)
                            or AddTo(CompositeDisposable) (plain C#). Forgetting this leaks.
```

10-line mental model (this is the whole framework):

```csharp
// 1. Register in an installer:           container.Bind<IThing>().To<Thing>().AsSingle();
// 2. Consume by constructor injection:    public Service(IThing thing) { ... }
// 3. Send an event:                       OnityEvent.Publish(new ThingHappened());
// 4. Receive an event:                    OnityEvent.Subscribe<ThingHappened>(this, OnThing);
// 5. Receive as a filtered stream:        OnityEvent.Observe<ThingHappened>(this).Where(...).Subscribe(...).AddTo(this);
// 6. Hold reactive state:                 var hp = new ReactiveProperty<int>(100); hp.Value = 90;
// 7. Observe state (emits current first): hp.Where(v => v <= 0).Subscribe(_ => Die()).AddTo(this);
// 8. Per-frame loop:                       OnityUnityObservable.EveryUpdate().Subscribe(_ => Tick()).AddTo(this);
// 9. MessageBroker + OnityEventHub are auto-bound in every OnityContext — no manual bind needed.
// 10. Disposal is mandatory. No AddTo == leak.
```

Why Onity over the three separate libraries:

| Concern | Three libraries | Onity |
| --- | --- | --- |
| Mental models | DiContainer + Observable + filter-pipeline (3) | One `OnityContainer` spine (1) |
| Event -> stream | hand-write adapter MessagePipe -> R3 | `broker.Observe<T>()` returns `IOnityObservable<T>` |
| Disposal | 3 different idioms | one `AddTo(...)` everywhere |
| DI + events wiring | manual `AddMessagePipe()` + binds | auto-bound `MessageBroker` + `OnityEventHub` per scope |
| Engine coupling | varies | engine-free testable core |

---

## 2. DI — `OnityContainer`

`OnityContainer` is a sealed, engine-free, parent-scoped container implementing `IResolver` + `IDisposable`.
You can use it in plain EditMode tests with no Unity scene: `using OnityContainer c = new OnityContainer();`.

### 2.1 Binding surface (Zenject-familiar)

```csharp
using Onity.DI;

using OnityContainer container = new OnityContainer();

// Contract -> implementation, choose a lifetime (lifetime call is REQUIRED to actually register):
container.Bind<IInputService>().To<KeyboardInputService>().AsSingle();     // one shared instance
container.Bind<IPathfinder>().To<AStarPathfinder>().AsTransient();         // new instance per resolve
container.Bind<IClock>().To<SystemClock>().AsSingle().NonLazy();           // resolved eagerly at Build()

// Self-bind shorthand (To defaults to the contract type):
container.Bind<GameState>().AsSingle();                                    // == Bind<GameState>().To<GameState>().AsSingle()

// Share ONE instance across the concrete + ALL its interfaces (see DON'T trap in 2.7):
container.BindInterfacesAndSelfTo<PlayerStateService>().AsSingle();        // IPlayerState, IFoo, ... AND PlayerStateService
container.BindInterfacesTo<PlayerStateService>().AsSingle();               // interfaces only (throws if type has none)

// Pre-built instance (rejects null with OnityBindingException):
container.BindInstance<IConfig>(loadedConfig);

// Factories (always bound AsSingle; you author the IFactory<...> impl — see 2.5):
container.BindFactory<Enemy, EnemyFactory>();                              // IFactory<Enemy>
container.BindFactory<string, Enemy, EnemyFactory>();                      // IFactory<string, Enemy>
container.BindFactory<string, int, Enemy, EnemyFactory>();                 // IFactory<string, int, Enemy>

// Pooled factories (Unity extensions: using Onity.Unity.Installers; each binds IPool<T> + IFactory<T> — see 2.5 and 10):
container.BindPooledFactory(bulletPrefab, bulletRoot, defaultCapacity: 32, maxSize: 256);   // from a MonoInstaller
container.BindPooledFactory(existingPool);                                                  // any IPool<T> you built
```

Builder methods, exact signatures:

| Call | Returns | Then |
| --- | --- | --- |
| `Bind<TContract>()` | `TypeBindingBuilder<TContract>` | `.To<TConcrete>()` (where `TConcrete : TContract`), optional `.WithId(id)` / `.WhenInjectedInto<TConsumer>()`, then `.AsSingle()` / `.AsScoped()` / `.AsTransient()`, then optional `.NonLazy()` |
| `BindInterfacesAndSelfTo<TConcrete>()` | `MultiTypeBindingBuilder` | optional `.WithId(id)` / `.WhenInjectedInto<TConsumer>()`, then `.AsSingle()` / `.AsScoped()` / `.AsTransient()`, then optional `.NonLazy()` |
| `BindInterfacesTo<TConcrete>()` | `MultiTypeBindingBuilder` | same as above |
| `BindInstance<TContract>(instance)` | `void` | — |
| `BindFactory<TValue,TFactory>()` (+1-param, +2-param) | `void` | binds factory `AsSingle` via `BindInterfacesAndSelfTo` |
| `BindPooledFactory(prefab, parent = null, defaultCapacity = 16, maxSize = 512)` / `BindPooledFactory(IPool<T>)` (Unity extensions) | `void` | binds `IPool<T>` (instance) + `IFactory<T>` (`PooledFactory<T>` singleton); see 2.5 and 10 |

`NonLazy()` throws `OnityBindingException` if called before `AsSingle()`/`AsTransient()`.

### 2.2 Resolve / inject

```csharp
IInputService input = container.Resolve<IInputService>();                  // throws OnityResolveException if unresolvable
object svc = container.Resolve(typeof(IInputService));                      // runtime-type overload

if (container.TryResolve<IPathfinder>(out IPathfinder pathfinder)) { }      // false instead of throwing
if (container.TryResolve(typeof(IPathfinder), out object p)) { }

container.Inject(existingObject);                                           // member-injects an already-created object

bool can = container.CanResolve(typeof(IFoo));                             // check without instantiating
```

`OnityContainer` and `IResolver` always self-resolve to the active container (inject `IResolver` to do
manual resolves inside a factory).

### 2.3 `[Inject]` on constructor / field / property / method

```csharp
using Onity.DI;

public sealed class CombatService
{
    private readonly IDamageCalculator m_damage;

    // Constructor injection is PREFERRED. Selection rule: a single [Inject] ctor wins; otherwise the
    // highest-scoring public ctor (most parameters). This is "greediest", NOT Zenject's "fewest".
    public CombatService(IDamageCalculator damage)
    {
        m_damage = damage;
    }

    [Inject] private IClock m_clock;                 // field injection (private OK)
    [Inject] public ILogger Logger { get; set; }     // property injection (SETTER REQUIRED, no indexer)

    [Inject]                                          // method injection (runs after ctor + fields + properties)
    private void Initialize(IConfig config)           // CANNOT be generic
    {
        // good place for post-construction wiring
    }
}
```

Member injection order: base class -> derived class, and within a type **fields -> properties -> methods**.
Static members are NEVER injected. These throw `OnityBindingException` at resolve time:
multiple `[Inject]` constructors, `[Inject]` property without a setter, `[Inject]` indexer, generic
`[Inject]` method.

### 2.4 Child containers and `AsScoped()`

`AsScoped()` gives one instance per resolving container, including bindings a child inherits from its
parent, disposed with that scope (the `Lifetime` enum itself stays `{ Singleton, Transient }`). A child
container's own `AsSingle` is also per-scope. Children inherit parent bindings; a child bind shadows the
parent only inside the child.

```csharp
using OnityContainer parent = new OnityContainer();
parent.Bind<IDependency>().To<Dependency>().AsSingle();

using OnityContainer child = new OnityContainer(parent);
child.Bind<IDependency>().To<AlternateDependency>().AsSingle();   // shadows in child only

// child.Resolve<IDependency>()  -> AlternateDependency
// parent.Resolve<IDependency>() -> Dependency (unchanged)
```

Map VContainer `Lifetime.Scoped` -> Onity `AsScoped()` (or a child-container `AsSingle`).

### 2.5 Factories (runtime arguments)

There is no `container.Instantiate<T>(args)` and no fluent factory body (`.FromMethod` etc. do not exist).
To pass a runtime value into an injected object, author an `IFactory<...>` (from `Onity.Factory`) and bind
it with `BindFactory`:

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

`BindFactory` facts (verified in `OnityContainer.BindFactory` and `OnityZenjectFactoryParityTests`):

- `where TFactory : class, IFactory<...>`. It registers `BindInterfacesAndSelfTo<TFactory>().AsSingle()`: the factory resolves as `IFactory<...>` **and** as its concrete type, as ONE shared instance per container, created on first resolve. It returns `void`, so there is no `AsTransient()` / `NonLazy()` form.
- The container constructs the factory: inject services or `IResolver` into its constructor and take only the runtime values through `Create(...)`. Two parameters is the largest arity; wrap more values in a struct.
- A container-built class cannot receive a per-call runtime value (an id, a level, a position) through its constructor: the container only supplies bound types, so resolving it throws `OnityResolveException` (8.4). Inject the factory instead and call `Create(value)`.

Which factory or pool API?

| You need | Use | Registers | Notes |
| --- | --- | --- | --- |
| A new plain object per call from 0, 1 or 2 runtime arguments | your `IFactory<...>` class + `BindFactory<...>()` | `IFactory<...>` and the factory type (singleton) | No pooling; you write `Create` |
| Reusable prefab instances, no arguments | `BindPooledFactory(prefab, parent, defaultCapacity, maxSize)` in a `MonoInstaller` | `IPool<TComponent>` (instance) and `IFactory<TComponent>` (`PooledFactory<TComponent>` singleton) | Defaults `16` and `512`; no `initialSize`, `fixedSize` or name option; the scope disposes the pool it creates (10.4) |
| A prefab pool with prewarm, fixed capacity or a diagnostics name | `new PrefabComponentPool<T>(...)`, then `BindPooledFactory(pool)` | the same two bindings | Caller-owned: call `Dispose()` or `pool.AddTo(container)` (10.4) |
| A pool you already built (any item type) | `BindPooledFactory(IPool<T>)` | the same two bindings | Bound as an instance and caller-owned: the container disposes it only if you `pool.AddTo(container)` (10.4) |
| Pooled spawn with 1 or 2 runtime arguments | `new PooledFactory<TParam, T>(pool, initialize)` (or the 2-parameter form) + `BindInstance<IFactory<TParam, T>>(...)` | what you bind | Needs an `IParameterizedPool<T>`; `initialize` runs before get hooks and activation (10.2) |
| Reusable plain C# objects (no `GameObject`) | `new OnityObjectPool<T>(...)` (`T : class`), optionally `BindPooledFactory(pool)` | what you bind | Reset in `actionOnGet` / `actionOnRelease`; `IPoolHooks` is not called (10.3) |

`BindPooledFactory` lives in `Onity.Unity` and the `Onity.Pooling` assembly references UnityEngine (for `PrefabComponentPool<T>`); the engine-free `Onity.DI` and `Onity.Factory` do not. Recipes, call order, documented behaviors and lifetime rules: section 10.

### 2.6 Build / async startup

```csharp
container.RegisterBuildCallback(r => r.Resolve<IGameLoopRunner>().Start());            // sync, runs in Build()
container.RegisterBuildCallbackAsync(async (r, ct) => await r.Resolve<ISaveLoader>().PrimeAsync(ct));

container.Build();                          // runs sync callbacks once; idempotent
await container.BuildAsync(cancellationToken);   // runs Build(), async callbacks, then IOnityAsyncInitializable; result cached, re-armed on cancel/failure
```

Callbacks cannot be registered after Build is finalized (throws `OnityBindingException`).
`Dispose()` cancels the container's `LifetimeToken` first, then disposes owned singletons in reverse
registration order. Async startup and scope tokens: section 11.4.

### 2.7 Documented behaviors (test-locked) — DO / DON'T

| Behavior | DO / DON'T |
| --- | --- |
| Implicit transients | Unbound **concrete** classes auto-resolve as transients. DON'T rely on it for shared state — it is NOT a singleton. |
| Unbound abstractions | Unbound **interfaces/abstracts/open-generics** throw `OnityResolveException`. DO bind them. |
| Last-binding-wins | Re-binding the same contract REPLACES the previous binding (no duplicate-binding exception). DO use this to override; DON'T expect a conflict error. |
| Shared instance across contracts | DON'T expect two `Bind<IFoo>().To<C>()` + `Bind<IBar>().To<C>()` to share one instance — they produce **distinct** singletons. DO use `BindInterfacesAndSelfTo<C>().AsSingle()`. |
| Circular dependency | Constructor AND member cycles throw `OnityResolveException` at **resolve time** (not build time). DO break the cycle (e.g. inject a factory or `IResolver`). |
| Constructor selection | Greediest **public** ctor wins (or the single `[Inject]` ctor). DON'T add a second `[Inject]` ctor — throws. |
| Open generics | DO bind an open contract to an open implementation with `Bind(typeof(IRepository<>)).To(typeof(Repository<>)).AsSingle()`. Resolve closed forms such as `IRepository<Player>`; preserve those closed types on IL2CPP. |
| Collection injection | Multiple explicit bindings can be injected as `IEnumerable<T>`, `IReadOnlyList<T>`, `IReadOnlyCollection<T>`, `IList<T>`, `ICollection<T>`, `List<T>`, or `T[]`. Registration order is the initial collection order. |
| Statics | `[Inject]` on a static member is silently ignored. DON'T use it. |
| Conditional / keyed binds | DO configure `WithId(id)` / `WhenInjectedInto<TConsumer>()` before the lifetime call, and consume an ID with `[Inject(Id = ...)]` or `Resolve<T>(id)`. A missing non-null ID does not implicitly construct a service; ambiguous conditions throw. |

### 2.8 Copy-paste installer recipe

```csharp
using Onity.DI;
using Onity.Unity.Installers;          // MonoInstaller, BindScriptableObject, BindPooledFactory
using Onity.Unity.Messaging;           // BindMessageChannel<T>
using UnityEngine;

public sealed class GameInstaller : MonoInstaller
{
    [SerializeField] private GameConfig m_config;

    public override void InstallBindings(OnityContainer container)
    {
        container.BindScriptableObject(m_config);                                  // inject + bind a ScriptableObject
        container.Bind<IScoreService>().To<ScoreService>().AsSingle();
        container.BindInterfacesAndSelfTo<EnemySpawner>().AsSingle().NonLazy();     // eager, multi-contract
        container.BindMessageChannel<ScoreChanged>();                              // IPublisher/ISubscriber<ScoreChanged>
    }
}
```

> Note: `MessageBroker` (and thus `IPublisher<T>`/`ISubscriber<T>` via `GetPublisher`/`GetSubscriber`) and
> `OnityEventHub` are auto-bound in every `OnityContext`. A service can inject `OnityEventHub` or
> `IMessageBroker` with **no** installer line. `BindMessageChannel<T>()` is only needed to inject the typed
> `IPublisher<T>`/`ISubscriber<T>` directly (those are not auto-resolvable per message type).

---

## 3. Reactive — `Onity.Reactive` (+ `Onity.Unity.Reactive` bridges)

Push-based, hot-by-default. The everyday contract is `IOnityObservable<T>`. `Subject<T>`,
`ReactiveProperty<T>`, every operator, and `broker.Observe<T>()` all use it.

> `Observer<T>` is `public delegate void Observer<T>(T value)`. The `Subscribe(Action<T>)` you normally
> write is an extension that wraps it. There is also an advanced `OnityObserver<T>` lifecycle class
> (`OnNext`/`OnError`/`OnCompleted`/`Dispose`); gameplay code rarely needs it.

### 3.1 Primitives

```csharp
using Onity.Reactive;

// Subject<T>: multicast event source. OnNext is designed allocation-free in steady state.
Subject<int> damage = new Subject<int>();
IDisposable sub = damage.Subscribe(v => Debug.Log(v));
damage.OnNext(10);
sub.Dispose();
damage.Dispose();                       // OnNext/Subscribe AFTER Dispose throw ObjectDisposedException

// ReactiveProperty<T>: value + change notification. DistinctUntilChanged is BUILT IN (default comparer).
ReactiveProperty<int> hp = new ReactiveProperty<int>(100);
int now = hp.Value;                     // read
hp.Value = 90;                          // set (notifies if changed)
bool changed = hp.SetValue(90);         // set + return whether it actually changed (false here, already 90)
hp.Subscribe(v => Debug.Log(v));        // emits CURRENT value (90) immediately, then on each real change
hp.Subscribe(v => Debug.Log(v), emitCurrentValue: false);   // skip the initial emit
IReadOnlyReactiveProperty<int> readOnly = hp;               // expose read-only to consumers

// CompositeDisposable: lifetime bag for plain C# owners.
CompositeDisposable bag = new CompositeDisposable();
hp.Subscribe(v => { }).AddTo(bag);
bag.Clear();                            // dispose all, keep reusable
bag.Dispose();                          // dispose all, final
```

### 3.2 Synchronous operators (`OnityObservableExtensions`)

All return `IOnityObservable<T>` and allocate only at subscribe time (0 alloc per emitted value):

`Where(Predicate<T>)`, `Select(Func<TSource,TResult>)`, `DistinctUntilChanged(IEqualityComparer = null)`,
`Skip(int)`, `SkipWhile(Predicate<T>)`, `Take(int)`, `TakeWhile(Predicate<T>)`, `StartWith(T)`,
`Scan<TState>(seed, Func<TState,T,TState>)`, `Pairwise() -> IOnityObservable<OnityPair<T>>`,
`Merge(params IOnityObservable<T>[])`, `CombineLatest<T1,T2,TResult>(other, selector)`, `Sample<TSignal>(signalSource)`,
`Subscribe(Action<T>)`, `Subscribe(Action<T>, Action<Exception>, Action<OnityResult>)`,
`TakeUntilCancellation(CancellationToken)`, `FirstAsync(CancellationToken) -> Task<T>`,
`ToTask(this IOnityObservable<Unit>) -> Task`.

Factories (`OnityObservable` static): `FromEvent<T>(addHandler, removeHandler)`, `Return<T>(value)`,
`Empty<T>()`. (There is NO `Never`/`Create`/`Defer`.)

```csharp
hp.Where(v => v <= 0)
  .Select(_ => "dead")
  .Subscribe(msg => Debug.Log(msg))
  .AddTo(this);
```

### 3.3 Async / time operators (`OnityObservableAsyncExtensions`)

Callable directly on `IOnityObservable<T>`. Each takes an optional `OnityTimeProvider` (deterministic in
tests; pass a Unity time provider in gameplay — see 3.5):

- `Debounce(TimeSpan dueTime, OnityTimeProvider = null)` — emit the LAST value after a quiet window.
- `Throttle(TimeSpan interval, OnityTimeProvider = null)` — emit the first value immediately, then drop values during the cool-down window.
- `ThrottleLast(TimeSpan interval, OnityTimeProvider = null)` — emit the latest value once per interval.
- `Buffer(int count)` / `Buffer(TimeSpan, OnityTimeProvider = null)` — emit count- or time-windowed lists.
- `TakeUntil(CancellationToken)` / `TakeUntil(Task)` — stop on a signal.
- `SelectAwait(Func<T,CancellationToken,ValueTask<TResult>>)` / `WhereAwait(Func<T,CancellationToken,ValueTask<bool>>)`
  — sequential async projection/filter. These can resume away from the Unity main thread. Call
  `ObserveOnMainThread()` (or `ObserveOn(OnityFrameProviders.Update)`) before a downstream observer
  touches `UnityEngine` APIs.

### 3.4 Unity bridges — frame loops, timers, lifetime (`Onity.Unity.Reactive`)

```csharp
using Onity.Unity.Reactive;     // OnityUnityObservable, AddTo(Component), TakeUntilDestroy, TakeUntilDisable

OnityUnityObservable.EveryUpdate()        // IOnityObservable<Unit>, shared singleton, pumped by hidden DontDestroyOnLoad object
OnityUnityObservable.EveryFixedUpdate()
OnityUnityObservable.EveryLateUpdate()
OnityUnityObservable.Timer(2f)            // emits one Unit after 2s (overload: unscaled)
OnityUnityObservable.Interval(1f)         // IOnityObservable<int> tick index every 1s (overload: unscaled)

// Lifetime helpers (all return the same IDisposable for chaining):
someDisposable.AddTo(this);               // dispose on Component destroy (== TakeUntilDestroy)
someDisposable.TakeUntilDestroy(this);    // dispose on Component destroy
someDisposable.TakeUntilDisable(this);    // dispose on Behaviour disable
someDisposable.AddTo(compositeDisposable);// add to a CompositeDisposable (from Onity.Reactive)
```

> Lifetime overloads take `Component`/`Behaviour`. There is **no** `AddTo(GameObject)` /
> `TakeUntilDestroy(GameObject)` overload. Pass `this` from a MonoBehaviour.

### 3.5 Gameplay recipes

```csharp
// Recipe A: health <= 0 -> die. ReactiveProperty emits current value on subscribe.
using Onity.Reactive;
using Onity.Unity.Reactive;
using UnityEngine;

public sealed class Health : MonoBehaviour
{
    private readonly ReactiveProperty<int> m_hp = new ReactiveProperty<int>(100);
    public IReadOnlyReactiveProperty<int> Hp => m_hp;

    private void Start()
    {
        m_hp.Where(v => v <= 0)
            .Subscribe(_ => Debug.Log("dead"))
            .AddTo(this);                 // disposed on Destroy
    }

    public void TakeDamage(int amount) => m_hp.SetValue(m_hp.Value - amount);
}
```

```csharp
// Recipe B: tick AI every frame until this Behaviour is disabled.
using Onity.Reactive;
using Onity.Unity.Reactive;
using UnityEngine;

public sealed class AiTicker : MonoBehaviour
{
    private void OnEnable()
    {
        // Lifetime helpers (AddTo / TakeUntilDisable / TakeUntilDestroy) extend IDisposable,
        // so they go AFTER Subscribe (which returns the IDisposable), not on the observable.
        OnityUnityObservable.EveryUpdate()
            .Subscribe(_ => TickAi())
            .TakeUntilDisable(this);      // disposed on disable (and on destroy)
    }

    private void TickAi() { }
}
```

```csharp
// Recipe C: debounce a search box; pass a Unity time provider so it honors Time.timeScale.
using System;
using Onity.Reactive;
using Onity.Unity.Reactive;             // OnityTimeProviders

public sealed class SearchBox
{
    private readonly Subject<string> m_query = new Subject<string>();

    public IDisposable Wire(Action<string> onSearch)
    {
        return m_query
            .Debounce(TimeSpan.FromMilliseconds(250), OnityTimeProviders.UpdateUnscaled)
            .Subscribe(onSearch);
    }

    public void OnType(string text) => m_query.OnNext(text);
}
```

```csharp
// Recipe D: await the first matching value (reactive -> async).
using System.Threading;
using System.Threading.Tasks;
using Onity.Reactive;

public sealed class WaveGate
{
    private readonly Subject<int> m_enemiesAlive = new Subject<int>();
    public void Report(int count) => m_enemiesAlive.OnNext(count);

    // Completes when the stream first reports 0; throws OperationCanceledException on cancel.
    public Task WaitForClearAsync(CancellationToken ct) =>
        m_enemiesAlive.Where(c => c == 0).FirstAsync(ct);
}
```

```csharp
// Recipe E: Input System reactive bridge (requires ENABLE_INPUT_SYSTEM).
using UnityEngine;
using UnityEngine.InputSystem;
using Onity.Reactive;
using Onity.Unity.Input;                // PerformedAsObservable / StartedAsObservable / CanceledAsObservable
using Onity.Unity.Reactive;             // AddTo

public sealed class FireControl : MonoBehaviour
{
    [SerializeField] private InputActionReference m_fire;

    private void OnEnable()
    {
        m_fire.action.PerformedAsObservable()
            .Subscribe(_ => Fire())
            .TakeUntilDisable(this);
    }

    private void Fire() { }
}
```

---

## 4. Events — `Onity.Messaging` (+ `Onity.Unity.Messaging`)

Typed pub/sub. `MessageChannel<T>` is the same `SubscriptionEntry[]` design as `Subject<T>`: steady-state
`Publish` designed allocation-free, re-entrancy-safe (unsubscribe inside a handler is OK), throws after `Dispose`.

> Threading: publish/subscribe on the Unity **main thread**. Channels are not internally locked for
> publish (broker channel CREATION is locked). Initial delivery follows subscription order, but
> unsubscribe uses swap-back removal, so no stable priority/order contract exists afterward.

### 4.1 Surface

```csharp
using Onity.Messaging;

// IMessageBroker: source of typed channels.
IPublisher<DamageEvent> pub = broker.GetPublisher<DamageEvent>();
ISubscriber<DamageEvent> sub = broker.GetSubscriber<DamageEvent>();

// IPublisher<T>.Publish(msg) ; ISubscriber<T>.Subscribe(handler) -> IDisposable
pub.Publish(new DamageEvent(10));
IDisposable token = sub.Subscribe(e => Debug.Log(e.Amount));

// Broker-level convenience (no manual GetPublisher/GetSubscriber):
broker.Publish(new DamageEvent(10));
IDisposable token2 = broker.Subscribe<DamageEvent>(e => Debug.Log(e.Amount));

// Diagnostics into a caller-supplied list (no allocation):
List<MessageChannelDiagnostics> diag = new List<MessageChannelDiagnostics>(8);
broker.GetDiagnostics(diag);            // each entry: MessageType + SubscriberCount
int channels = broker.ChannelCount;
```

`MessageHandler<TMessage>` is `public delegate void MessageHandler<TMessage>(TMessage message)`.
`MessageChannel<T>` is keyed by message **Type**. Two extras now ship: **per-key** routing via
`KeyedMessageChannel<TKey,TMessage>` (`IKeyedPublisher`/`IKeyedSubscriber`), and **async** handlers via
`AsyncMessageChannel<T>` (`IAsyncPublisher.PublishAsync(msg, ct)` / `IAsyncSubscriber`, sequential by default).
Still intentionally NOT shipped (non-goals): buffered/replay events, handler priority, request-response. Model
"current state new listeners need" as a `ReactiveProperty<T>`; model transient notifications as messages.

### 4.2 Reactive bridge — `Observe<T>()`

`broker.Observe<T>()`, `subscriber.Observe<T>()`, and `OnityEventHub.Observe<T>()` all return
`IOnityObservable<T>`, so events flow into the full operator chain. `OnityEventHub.Observe<T>()` caches
one stream per message type.

```csharp
using Onity.Reactive;            // Where, Select, Subscribe
using Onity.Unity.Messaging;     // Observe<T> on IMessageBroker
using Onity.Unity.Reactive;      // AddTo

broker.Observe<DamageEvent>()
      .Where(e => e.Amount > 0)
      .Select(e => e.Amount)
      .Subscribe(amount => Debug.Log($"Took {amount}"))
      .AddTo(this);
```

### 4.3 Unity shortcut + `OnityEventHub` facade

```csharp
// Unity shorthand: use this from MonoBehaviours and simple scene code.
using System;
using Onity.Reactive;
using Onity.Unity;

OnityEvent.Publish(new PlayerDamaged(10));
IDisposable token = OnityEvent.Subscribe<PlayerDamaged>(OnDamaged);
IOnityObservable<PlayerDamaged> stream = OnityEvent.Observe<PlayerDamaged>();
```

```csharp
// Plain services: inject the scoped facade explicitly.
public sealed class OnityEventHub
{
    public void Publish<TMessage>(TMessage message);
    public IDisposable Subscribe<TMessage>(MessageHandler<TMessage> handler);
    public IOnityObservable<TMessage> Observe<TMessage>();      // cached per message type
}
```

### 4.4 Event recipes

```csharp
// Recipe A: publish a typed message from Unity code. Define messages as small structs/classes.
using Onity.Unity;                      // OnityEvent.Publish
using UnityEngine;

public readonly struct PlayerDamaged
{
    public readonly int Amount;
    public PlayerDamaged(int amount) { Amount = amount; }
}

public sealed class DamageButton : MonoBehaviour
{
    public void Click() => OnityEvent.Publish(new PlayerDamaged(10));
}
```

```csharp
// Recipe B: subscribe with the disposable-token model; own lifetime via OnEnable/OnDisable.
using System;
using Onity.Unity;                       // OnityEvent.Subscribe
using UnityEngine;

public sealed class HealthBar : MonoBehaviour
{
    private IDisposable m_subscription;

    private void OnEnable()  => m_subscription = OnityEvent.Subscribe<PlayerDamaged>(this, OnDamaged);
    private void OnDisable() => m_subscription?.Dispose();
    private void OnDamaged(PlayerDamaged message) { /* update bar */ }
}
```

```csharp
// Recipe C: fine-grained injection of only the publisher or subscriber (needs BindMessageChannel<T>()).
using Onity.Messaging;                   // ISubscriber<T>

public sealed class DamageNumbers
{
    private readonly ISubscriber<PlayerDamaged> m_damage;
    public DamageNumbers(ISubscriber<PlayerDamaged> damage) { m_damage = damage; }
    public IDisposable Listen() => m_damage.Subscribe(d => { /* spawn number */ });
}
```

```csharp
// Recipe D: broker-direct, engine-free (lowest overhead; great for tests).
using System.Collections.Generic;
using Onity.Messaging;

using MessageBroker broker = new MessageBroker();
IDisposable token = broker.Subscribe<PlayerDamaged>(d => { /* handle */ });
broker.Publish(new PlayerDamaged(10));
token.Dispose();
```

---

## 5. End-to-end — ONE MonoInstaller wiring DI + a message channel + a reactive service

```csharp
// ---- messages ----
public readonly struct PlayerDamaged
{
    public readonly int Amount;
    public readonly bool IsCritical;
    public PlayerDamaged(int amount, bool isCritical) { Amount = amount; IsCritical = isCritical; }
}

// ---- installer: DI + Events + Reactive state in one block ----
using Onity.DI;
using Onity.Reactive;
using Onity.Unity.Installers;
using Onity.Unity.Messaging;            // BindMessageChannel<T>

public sealed class CombatInstaller : MonoInstaller
{
    public override void InstallBindings(OnityContainer container)
    {
        container.BindMessageChannel<PlayerDamaged>();                  // IPublisher/ISubscriber<PlayerDamaged>
        container.BindInstance(new ReactiveProperty<int>(100));        // shared player-health state
        container.BindInterfacesAndSelfTo<ScoreService>().AsSingle().NonLazy();
    }
}

// ---- pure-C# service: events -> reactive operators -> shared state, disposed via AddTo(bag) ----
using System;
using Onity.Messaging;                   // ISubscriber<T>
using Onity.Reactive;                    // ReactiveProperty, CompositeDisposable, Where, Select, Subscribe, AddTo
using Onity.Unity.Messaging;             // Observe<T>()

public sealed class ScoreService : IDisposable
{
    private readonly ReactiveProperty<int> m_health;
    private readonly CompositeDisposable m_subscriptions = new CompositeDisposable();

    // ISubscriber<PlayerDamaged> comes from BindMessageChannel; the ReactiveProperty from BindInstance.
    public ScoreService(ISubscriber<PlayerDamaged> damage, ReactiveProperty<int> health)
    {
        m_health = health;

        damage.Observe()
              .Where(evt => evt.Amount > 0)
              .Select(evt => evt.Amount)
              .Subscribe(amount => m_health.Value -= amount)
              .AddTo(m_subscriptions);
    }

    public void Dispose() => m_subscriptions.Dispose();
}

// ---- thin MonoBehaviour: inject the hub + state, react to events AND the frame loop, scope to the object ----
using Onity.DI;                          // Inject
using Onity.Reactive;                    // Where, Subscribe
using Onity.Unity.Messaging;             // OnityEventHub
using Onity.Unity.Reactive;              // OnityUnityObservable, TakeUntilDisable
using UnityEngine;

public sealed class HealthHud : MonoBehaviour
{
    [Inject] private OnityEventHub m_events;                  // auto-bound facade
    [Inject] private ReactiveProperty<int> m_health;          // shared state from the installer

    private void OnEnable()
    {
        m_health.Subscribe(value => Debug.Log($"Health: {value}"))
                .TakeUntilDisable(this);   // emits current value first; no duplicate after re-enable

        m_events.Observe<PlayerDamaged>()
                .Where(evt => evt.IsCritical)
                .Subscribe(_ => Debug.Log("Critical hit!"))
                .TakeUntilDisable(this);

        OnityUnityObservable.EveryUpdate()
                .Subscribe(_ => { /* per-frame HUD tween */ })
                .TakeUntilDisable(this);
    }
}
```

Wire-up in the scene: add a context component (`ProjectContext` / `SceneContext` / `GameObjectContext`
from `Onity.Unity.Contexts`), assign `CombatInstaller` to its installer list, and put `HealthHud` under the
context root. The context creates the container, registers default bindings (container, `IResolver`,
`IOnityScopeLifetime`, itself, `MessageBroker`, `OnityEventHub`), runs installers, builds, and auto-injects the
hierarchy.

### 5.1 Context scoping (project vs scene) — pick the right context for each installer

> **RULE: put project-scope services on the `ProjectContext` prefab, not on a `SceneContext`.** Anything that
> must live for the whole session and survive scene loads — card/item catalogs, save/currency/inventory,
> settings, RNG/seed, the `MessageBroker`, audio, scene-flow — belongs in an installer on the auto-loaded
> `ProjectContext`. Per-scene collaborators (a match's board/turn machine/combat, presentation/spawn
> factories, per-screen controllers) belong in installers on that scene's `SceneContext`. **Never put a
> project-scope installer on a `SceneContext`** — a `SceneContext` is created on every scene load, so its
> singletons are rebuilt per scene and do not persist. Scene contexts resolve project bindings through the
> parent chain automatically, so a scene installer can depend on project services without rebinding them.

The three contexts (all `Onity.Unity.Contexts`, all extend `OnityContext`):

| Context | Lifetime | Parent it resolves | Use for |
| --- | --- | --- | --- |
| `ProjectContext` | One persistent instance (`ProjectContext.Instance`, `DontDestroyOnLoad`); survives scene loads | none (root) | session-wide services that outlive scenes |
| `SceneContext` | Rebuilt per scene load | explicit `m_projectContext` field else `ProjectContext.Instance` | per-scene services; inherits all project bindings |
| `GameObjectContext` | Lives with its GameObject subtree | nearest parent `OnityContext` in the hierarchy, else `ProjectContext.Instance` | a sub-scope for one object subtree under a scene |

`ProjectContext` is auto-loaded **before any scene** by `ProjectContextBootstrap`
(`[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`) from `Resources/Onity/ProjectContext`
(`ProjectContextBootstrap.ResourcePath`), i.e. the prefab at `Assets/Resources/Onity/ProjectContext.prefab`.
It only loads if no `ProjectContext` already exists, so a scene may also hold one. Create the prefab via the
menu **`Onity → Contexts → Create ProjectContext Prefab`** (writes that exact path), then add your
project-scope installer(s) to its **Installers** list. A `SceneContext` then needs no parent wiring — it
discovers `ProjectContext.Instance` and becomes its child automatically.

```
// DON'T: a session-wide service on a SceneContext — rebuilt every scene load, never persists.
SceneContext  -> Installers: [SaveInstaller, CurrencyInstaller, MatchInstaller]   // wrong scope for Save/Currency

// DO: split by lifetime.
ProjectContext (Assets/Resources/Onity/ProjectContext.prefab)
              -> Installers: [SaveInstaller, CurrencyInstaller, AudioInstaller]   // persist across scenes
SceneContext  -> Installers: [MatchInstaller, PresentationInstaller]              // per match; resolves Save/Currency from the parent
```

---

## 6. DO / DON'T

DO:
- DO put domain logic in plain testable C#; keep MonoBehaviours thin.
- DO prefer **constructor injection**; use `[Inject]` fields/properties/methods only when a ctor cannot.
- DO call `.AsSingle()` or `.AsTransient()` — a `Bind<>()`/`To<>()` without a lifetime registers nothing.
- DO dispose every subscription: `.AddTo(this)` in a MonoBehaviour, `.AddTo(compositeDisposable)` in plain C#.
- DO subscribe in `OnEnable` (and `Clear()` the bag in `OnDisable`) or in `Start`/ctor with `AddTo(this)`.
- DO use `BindInterfacesAndSelfTo<C>().AsSingle()` to share one instance across a concrete + its interfaces.
- DO model shared current-state as `ReactiveProperty<T>`; model transient notifications as messages.
- DO use `AsScoped()` (or a child container's `AsSingle()`) for a per-scope instance.
- DO pass an `OnityTimeProvider` (e.g. `OnityTimeProviders.UpdateUnscaled`) to `Debounce`/`ThrottleLast` in gameplay.
- DO use `Throttle` for leading-edge cool-down and `ThrottleLast` for trailing/latest-value sampling.
- DO spawn reusable objects through a pool (`BindPooledFactory`: inject `IFactory<T>` to spawn and `IPool<T>` to release; `OnityObjectPool<T>` for plain C#) instead of `Instantiate`/`Destroy` in gameplay loops (section 10).

DON'T:
- DON'T add bindings after `Build()`. Post-build registration is unsupported because baked lookup and
  lifecycle collections are already finalized; only late build-callback registration is explicitly rejected.
- DON'T `Resolve<T>()` inside `Update`/`FixedUpdate`/`LateUpdate` — resolve once in ctor/`Awake` and cache.
- DON'T `new` up services that have dependencies — bind them and let DI construct them.
- DON'T expect two separate `Bind<I>().To<C>()` calls to share one instance (they don't).
- DON'T hand-build registries when collection injection fits; bind each implementation and inject a supported collection shape.
- DON'T resolve an open generic definition. Bind open definitions, then resolve a preserved closed form such as `IRepository<Player>`.
- DON'T add a second `[Inject]` constructor, a setterless `[Inject]` property, an `[Inject]` indexer, or a generic `[Inject]` method — each throws `OnityBindingException`.
- DON'T use `System.Linq` in Onity package code (write plain loops; Onity has no non-Unity third-party runtime dependencies); avoid LINQ/allocations in hot paths.
- DON'T touch `UnityEngine` members directly after `SelectAwait`/`WhereAwait`; call `ObserveOnMainThread()` first.
- DON'T publish/subscribe to a broker or `Subject<T>` from a background thread.
- DON'T release a pooled item twice, use it after `Release`, or `Destroy` it: release it exactly once (section 10.5).
- DON'T assume the container disposes a pool you built. `BindPooledFactory(prefab, ...)` creates its pool, so the scope disposes it; a pool you pass to `BindPooledFactory(pool)` or `BindInstance` is caller-owned and outlives the context unless you dispose it or call `pool.AddTo(container)` (section 10.4).
- DON'T call APIs that aren't in this guide assuming Zenject/R3/MessagePipe/UniTask parity: no
  `Instantiate(args)` (`WithId`, `WhenInjectedInto` and `AsScoped()` DO exist; 2.1). Reactive: `Merge`/`CombineLatest`/`Scan`/`Pairwise`/`Sample`/`Buffer`,
  leading-edge `Throttle` (+ `ThrottleLast`), and `ObserveOn`/`ObserveOnMainThread` ARE shipped; still missing —
  no `Window`/`Zip`/`Switch`/`Concat`, no `Publish`/`Share`/`RefCount` on `IOnityObservable<T>` (the async-stream
  forms on `IOnityAsyncEnumerable<T>` exist; section 11). Messaging: keyed + async channels ARE
  shipped; no buffered/replay, no priority, no request-response.

---

## 7. Public-API index (per module)

### `Onity.Core` (engine-free)
- `Unit` (readonly struct; `Unit.Default`)
- `Lifetime` (enum: `Singleton`, `Transient`)
- `DisposableAction` (IDisposable wrapper; `DisposableAction.Empty`)

### `Onity.DI` (engine-free)
- `OnityContainer` (`IResolver`, `IDisposable`):
  `Bind<T>()`, `BindInterfacesAndSelfTo<T>()`, `BindInterfacesTo<T>()`, `BindInstance<T>(instance)`,
  `BindFactory<TValue,TFactory>()` (+1-param, +2-param),
  `Resolve<T>()`, `Resolve(Type)`, `TryResolve<T>(out T)`, `TryResolve(Type,out object)`, `Inject(object)`,
  `CanResolve(Type)`, `RegisterBuildCallback(Action<IResolver>)`,
  `RegisterBuildCallbackAsync(Func<IResolver,Task>)` / `(Func<IResolver,CancellationToken,Task>)`,
  `Build()`, `BuildAsync(CancellationToken = default)`, `Dispose()`, `Unbind<T>(id = null)`, `Rebind<T>(id = null)`,
  `LifetimeToken`, `IsDisposed`,
  `PushBindingSource(string)`, `TryGetBindingSource(...)`, `TryGetLocalBindingSource(...)`,
  `GetDiagnostics()`, `GetBindingDiagnostics(List<OnityBindingDiagnostics>)`,
  static `DiagnosticsCollectionEnabled`. Ctor: `new OnityContainer(OnityContainer parent = null)`.
- `IResolver` (`Resolve<T>`, `Resolve<T>(object id)`, `Resolve(Type)`, `TryResolve<T>`, `TryResolve<T>(object id, out T)`, `TryResolve(Type,...)`, `Inject`)
- `TypeBindingBuilder<TContract>` (`To<TConcrete>()`, `WithId(id)`, `WhenInjectedInto<TConsumer>()`, `AsSingle()`, `AsScoped()`,
  `AsTransient()`, `FromSubContainerResolve(install)`, `NonLazy()`)
- `RuntimeTypeBindingBuilder` from `Bind(Type)` (`To(Type)`, `WithId`, `WhenInjectedInto`, `AsSingle()`, `AsScoped()`, `AsTransient()`,
  `FromSubContainerResolve`, `NonLazy()`)
- `MultiTypeBindingBuilder` (`WithId`, `WhenInjectedInto`, `AsSingle()`, `AsScoped()`, `AsTransient()`, `NonLazy()`)
- `InjectAttribute` (`[Inject]`, `[Inject(Id = ...)]`; targets Constructor | Field | Property | Method | Parameter)
- `IOnityScopeLifetime` (`Token`, `IsDisposed`; `OnityContainer` implements it), `OnityScopeLifetimeExtensions.AddTo(disposable, scope)`
- `IOnityInitializable`, `IOnityAsyncInitializable` (`ValueTask InitializeAsync(CancellationToken)`), `IOnityTickable`,
  `IOnityFixedTickable`, `IOnityLateTickable`
- `OnityResolveException`, `OnityBindingException`
- diagnostics structs: `OnityContainerDiagnostics`, `OnityBindingDiagnostics`, `OnityBindingSourceInfo`

### `Onity.Factory` (engine-free)
- `IFactory<out TValue>` (`TValue Create()`)
- `IFactory<in TParam, out TValue>` (`TValue Create(TParam param)`)
- `IFactory<in TParam1, in TParam2, out TValue>` (`TValue Create(TParam1 param1, TParam2 param2)`)
- Bound with `OnityContainer.BindFactory<...>()` (`Onity.DI`); the only implementations that ship are the `PooledFactory<...>` adapters in `Onity.Pooling`.

### `Onity.Reactive` (engine-free)
- `IOnityObservable<T>` (`Subscribe(Observer<T>)`, `Subscribe(OnityObserver<T>)`)
- `Observer<T>` (delegate `void(T)`), `OnityObserver<T>` (abstract lifecycle), `OnityResult` (struct)
- `Subject<T>` (`Subscribe`, `OnNext`, `Dispose`)
- `ReactiveProperty<T>` (`Value`, `SetValue(T)->bool`, `Subscribe(..., emitCurrentValue = true)`, `Dispose`)
- `IReadOnlyReactiveProperty<T>` (`Value`, `Subscribe(Observer<T>, emitCurrentValue = true)`)
- `CompositeDisposable` (`Add`, `Remove`, `Clear`, `Count`, `Dispose`)
- `OnityObservable<T>` (delegate-backed) + static `OnityObservable`: `FromEvent<T>`, `Return<T>`, `Empty<T>`
- `OnityObservableExtensions`: `Where`, `Select`, `DistinctUntilChanged`, `Skip`, `SkipWhile`, `Take`,
  `TakeWhile`, `StartWith`, `Scan`, `Pairwise`, `Merge`, `CombineLatest`, `Sample`, `Throttle`,
  `Buffer(count)`, `Buffer(timeSpan)`, `ObserveOn`, `Subscribe(Action<T>)`,
  `Subscribe(onNext,onError,onCompleted)`, `TakeUntilCancellation`, `FirstAsync`, `ToTask`,
  `ObserveOnThreadPool`, `SelectOnThreadPool`
- `OnityObservableAsyncExtensions`: `Debounce`, `ThrottleLast`, `TakeUntil(CancellationToken)`,
  `TakeUntil(Task)`, `SelectAwait`, `WhereAwait`
- `OnityDisposableExtensions`: `AddTo(this IDisposable, CompositeDisposable)`
- `OnityTimeProvider` (abstract; `OnityTimeProvider.System`), `OnityFrameProvider` (abstract),
  `OnityObservableTracker` (opt-in diagnostics)

### `Onity.Messaging` (engine-free)
- `IMessageBroker` (`GetPublisher<T>()`, `GetSubscriber<T>()`)
- `IPublisher<T>` (`Publish(T)`), `ISubscriber<T>` (`Subscribe(MessageHandler<T>) -> IDisposable`)
- `MessageHandler<T>` (delegate `void(T)`)
- `MessageBroker` (`IMessageBroker`, `IDisposable`; `ChannelCount`, `GetDiagnostics(List<...>)`)
- `MessageChannel<T>` (`IPublisher<T>` + `ISubscriber<T>` + diagnostics + `IDisposable`)
- keyed channels: `IKeyedPublisher<TKey,TMessage>`, `IKeyedSubscriber<TKey,TMessage>`,
  `KeyedMessageChannel<TKey,TMessage>`
- async channels: `IAsyncPublisher<TMessage>`, `IAsyncSubscriber<TMessage>`,
  `AsyncMessageChannel<TMessage>`
- `MessageBrokerExtensions`: `Publish<T>(this IMessageBroker, T)`, `Subscribe<T>(this IMessageBroker, MessageHandler<T>)`
- `MessageChannelDiagnostics` (struct: `MessageType`, `SubscriberCount`)

### `Onity.Composition` (engine-free)
- `BindReactiveProperty<T>(initialValue)`, `BindSubject<T>()`, `DeclareMessage<T>()` and `DeclareAsyncMessage<T>()`
  register one shared primitive against its useful contracts; the container owns and disposes it.
  `BindAsyncReactiveProperty<T>(initialValue)` is the `Onity.Unity.Async` counterpart (section 11.4).

### `Onity.Pooling` (assembly references UnityEngine; only `PrefabComponentPool<T>` uses engine types)
- `IPool<T>` (`T Get()`, `void Release(T item)`, `void Clear()`)
- `IParameterizedPool<T> : IPool<T>` (`T Get<TParam>(TParam param, Action<T, TParam> initialize)`,
  `T Get<TParam1, TParam2>(TParam1 param1, TParam2 param2, Action<T, TParam1, TParam2> initialize)`;
  the initializer runs before get hooks and activation)
- `IPoolHooks` (`void OnPoolGet()`, `void OnPoolRelease()`; invoked only by `PrefabComponentPool<T>`)
- `OnityObjectPool<T>` (`where T : class`; `IParameterizedPool<T>`, `IDisposable`, `IOnityPoolDiagnosticsSource`):
  ctor `(Func<T> createFunc, Action<T> actionOnGet = null, Action<T> actionOnRelease = null,
  Action<T> actionOnDestroy = null, bool collectionCheck = false, int defaultCapacity = 16, int maxSize = 1024,
  string diagnosticsName = null, int initialSize = 0, bool fixedSize = false)`;
  `Get()`, `Get<TParam>(...)`, `Get<TParam1,TParam2>(...)`, `Release(T)`, `Prewarm(int count)`, `Clear()`,
  `Dispose()`, `GetDiagnosticsSnapshot()`
- `PrefabComponentPool<TComponent>` (`where TComponent : Component`; same interfaces and members):
  ctor `(TComponent prefab, Transform parent = null, int defaultCapacity = 16, int maxSize = 512,
  string diagnosticsName = null, int initialSize = 0, bool fixedSize = false)`
- `PooledFactory<TValue>` (`IFactory<TValue>`; ctor `(IPool<TValue> pool)`),
  `PooledFactory<TParam, TValue>` (ctor `(IParameterizedPool<TValue> pool, Action<TValue, TParam> initialize)`),
  `PooledFactory<TParam1, TParam2, TValue>` (ctor `(IParameterizedPool<TValue> pool,
  Action<TValue, TParam1, TParam2> initialize)`); a `null` argument throws `ArgumentNullException`
- `OnityPoolDiagnosticsRegistry` (static: `Register(IOnityPoolDiagnosticsSource)`, `Unregister(...)`,
  `GetSnapshots(List<OnityPoolDiagnosticsSnapshot>)`), `IOnityPoolDiagnosticsSource` (`GetDiagnosticsSnapshot()`),
  `OnityPoolDiagnosticsSnapshot` (readonly struct: `PoolName`, `PoolType`, `ItemType`, `CountAll`, `CountActive`,
  `CountInactive`, `GetCount`, `ReleaseCount`, `IsDisposed`)

### `Onity.Unity` (UnityEngine)
- Static shortcut (`Onity.Unity.OnityEvent`):
  `OnityEvent.Publish<T>(message)`, `OnityEvent.Publish<T>(owner, message)`,
  `OnityEvent.Subscribe<T>(handler)`, `OnityEvent.Subscribe<T>(owner, handler)`,
  `OnityEvent.Observe<T>()`, `OnityEvent.Observe<T>(owner)`,
  `OnityEvent.GetEventHub(...)`, `OnityEvent.TryGetEventHub(...)`
- Contexts (`Onity.Unity.Contexts`): `OnityContext` (abstract base), `ProjectContext`, `SceneContext`, `GameObjectContext`
- Installers (`Onity.Unity.Installers`): `MonoInstaller` (abstract; `InstallBindings(OnityContainer)`);
  extensions `BindScriptableObject<T>(asset)` / `BindScriptableObject<TContract,TAsset>(asset)`,
  `BindPooledFactory<TComponent>(this OnityContainer, TComponent prefab, Transform parent = null, int defaultCapacity = 16,
  int maxSize = 512)` (`where TComponent : Component`) / `BindPooledFactory<TValue>(this OnityContainer, IPool<TValue> pool)`
  (class `OnityFactoryBindingExtensions`; each binds `IPool<T>` as an instance plus `IFactory<T>` as a `PooledFactory<T>`
  singleton, and a null prefab or pool throws `OnityBindingException`; the scope disposes the pool the prefab overload
  creates, while a pool passed to the `IPool<T>` overload stays caller-owned; see 2.5 and 10), `BindUiResolverBridge()`
- Messaging (`Onity.Unity.Messaging`): `OnityEventHub` (`Publish<T>`, `Subscribe<T>`, `Observe<T>()`);
  `OnityMessageReactiveExtensions.Observe<T>()` (on `IMessageBroker` and `ISubscriber<T>`);
  `OnityMessageBindingExtensions.BindMessageChannel<T>(this OnityContainer)`;
  `OnityEventComponentExtensions` (`Publish`, `Subscribe`, `Observe` on `Component`)
- Reactive (`Onity.Unity.Reactive`):
  `OnityUnityObservable.EveryUpdate/EveryFixedUpdate/EveryLateUpdate` (+ `OnityUnityThreadMode` and
  `CancellationToken` overloads), `Timer(float, useUnscaledTime = false)`, `Interval(float, useUnscaledTime = false)`;
  `OnityUnityObservableExtensions.Delay<T>(seconds, useUnscaledTime = false)`;
  `ReactiveLifetimeExtensions`: `AddTo(this IDisposable, Component)`, `TakeUntilDestroy(this IDisposable, Component)`,
  `TakeUntilDisable(this IDisposable, Behaviour)`;
  `OnityFrameProviders` (`Update`, `FixedUpdate`, `LateUpdate`),
  `OnityTimeProviders` (`UpdateScaled`/`UpdateUnscaled`/`UpdateRealtime`, `FixedScaled`/`FixedUnscaled`/`FixedRealtime`,
  `LateScaled`/`LateUnscaled`/`LateRealtime`)
- Input (`Onity.Unity.Input`, requires `ENABLE_INPUT_SYSTEM`):
  `InputAction.StartedAsObservable()/PerformedAsObservable()/CanceledAsObservable()`;
  `OnityReactiveInputPlayer` (`GetButtonObservable`/`GetVector2Observable`/`GetFloatObservable`/
  `GetLongPressObservable`/`GetLongPressProgressObservable`, `PushContext`/`PopContext`/`SetContext`/`ClearContexts`)
- Async (`Onity.Unity.Async`; full index in section 11.7): `OnityTask`, `OnityTask<T>`, `OnityTaskVoid`,
  `OnityPlayerLoopTiming`, `OnityTaskPlayerLoop`, `OnityYieldAwaitable`, `OnityDelayType`, `OnityPlayerLoopTimer`,
  `OnityTimeoutController`, `OnityTaskCompletionSource(<T>)`, `OnityAutoResetTaskCompletionSource(<T>)`,
  `OnityAsyncLazy(<T>)`, `OnityTaskScheduler`, `OnityTaskTracker`, `IOnityAsyncEnumerable<T>`,
  `OnityAsyncEnumerable`, `OnityAsyncEnumerableLinq`, `OnityChannel`, `OnityAsyncReactiveProperty<T>`, triggers
  (`Onity.Unity.Async.Triggers`), UnityEvent / UI Toolkit / uGUI (optional `Onity.Unity.UGUI`) events.
  Pooled task values are single-consumer; see [Async with OnityTask](https://furkantokkan.github.io/Onity/guide/onitytask.html).

---

## 8. Error -> fix

What the runtime throws and how to fix it. DI uses the dedicated
**`OnityResolveException`** and **`OnityBindingException`** types. Reactive and
messaging also define `OnityReactiveException` / `OnityMessagingException`, but
the current shipped guard paths documented below use standard .NET exceptions
such as `ObjectDisposedException` and `ArgumentNullException`; do not catch only
the Onity-specific types. Reactive's settable `OnityObservableExceptionHandler`
receives subscriber/operator callback failures so one bad observer does not stop
delivery to the remaining observers.

### 8.1 `OnityResolveException` (DI resolve / inject)

| Message contains | Cause | Fix |
| --- | --- | --- |
| `Could not resolve service type '<T>'. It is an unbound interface.` (or `...an unbound abstract type.`, `...an open generic type definition and cannot be resolved directly.`; built by `BuildUnresolvableMessage`) | Resolving an unbound interface/abstract/open-generic, or a concrete with an unresolvable dependency. | `Bind<T>().To<Impl>().AsSingle()` (or `.AsTransient()`). Abstractions never auto-resolve; only unbound **concrete** classes auto-resolve as transients. |
| `Circular dependency detected while creating '<T>'. Resolution chain: ...` | Constructor **or** member-injection cycle (A needs B needs A). Detected at **resolve time**, not build time. | Break the cycle: inject `IFactory<...>` or `IResolver` on one side and resolve lazily, or split the type. |
| `Failed to instantiate '<T>' using constructor '<ctor>'. Error: <inner>` | The selected constructor threw, or a parameter was unresolvable. | Read the inner `Error:`; fix the throwing ctor body or bind the missing parameter type. |
| `Cannot resolve a null service type.` | `Resolve(null)` / `CanResolve(null)`. | Pass a real `Type`. |
| `Cannot inject into a null target.` | `Inject(null)`. | Pass the already-created instance to inject into. |
| `Container has already been disposed.` | Resolve/inject after `Dispose()`. | Resolve before disposing; do not reuse a disposed container (and never re-resolve from a disposed child scope). |

### 8.2 `OnityBindingException` (DI binding / config)

| Message contains | Cause | Fix |
| --- | --- | --- |
| `Cannot bind a null instance.` | `BindInstance<T>(null)`. | Pass a non-null instance. |
| `Implementation '<Impl>' does not satisfy contract '<C>'.` | `.To<Impl>()` where `Impl` is not assignable to the contract. | Use an implementation that derives from / implements the contract. |
| `Implementation type '<Impl>' must be a concrete class.` | `.To<>` target is abstract / interface. | Point `To<>` at a concrete class. |
| `Type '<Impl>' does not implement any interfaces.` | `BindInterfacesTo<Impl>()` on a type with no interfaces. | Use `BindInterfacesAndSelfTo<Impl>()`, or give the type an interface. |
| `Contract type list cannot be empty.` / `Contract type cannot be null.` | Internal binding built with no/`null` contracts. | Use the public `Bind*` entry points; do not hand-build empty contract lists. |
| `Type '<T>' has no accessible constructor...` | No public or non-public ctor the container can call. | Add a constructor the container can invoke. |
| `Type '<T>' contains multiple [Inject] constructors.` | More than one `[Inject]` ctor. | Mark exactly **one** ctor `[Inject]`, or remove all and let greediest-public-ctor selection apply. |
| `[Inject] property '<P>' on '<T>' must have a setter.` | `[Inject]` on a get-only property. | Add a (private) `set`, or move `[Inject]` to a backing field. |
| `[Inject] property '<P>' on '<T>' cannot be an indexer.` | `[Inject]` on an indexer. | Inject into a non-indexed property, a field, or a method parameter. |
| `[Inject] method '<M>' on '<T>' cannot be generic.` | Generic `[Inject]` method. | Make the `[Inject]` method non-generic with concrete, resolvable parameter types. |
| `Build callbacks cannot be registered after container build has been finalized.` | `RegisterBuildCallback[Async]` after `Build()`/`BuildAsync()`. | Register all callbacks (and all bindings) before `Build()`. |

### 8.3 Reactive / Messaging (standard .NET exceptions)

| Exception | Cause | Fix |
| --- | --- | --- |
| `ObjectDisposedException` (`Subject`/`MessageChannel`/`MessageBroker`) | `OnNext`/`Subscribe`/`Publish` after `Dispose()`. | Tie subscriptions to lifetime with `AddTo(this)` / `AddTo(bag)`; stop publishing to a disposed source. |
| `ArgumentNullException` (operators, factories, broker ext.) | A `null` source, handler, predicate, or selector passed to an operator / `FromEvent` / `Subscribe`. | Pass non-null delegates and sources. |
| `ArgumentOutOfRangeException` (`Skip`/`Take` etc.) | Negative count passed to a count-based operator. | Pass a count `>= 0`. |
| `OperationCanceledException` (`FirstAsync`/`ToTask`/`TakeUntil`/await helpers) | The `CancellationToken` cancelled before the awaited value arrived. **This is normal cancellation, not a bug.** | Catch it where you start the async flow, or guard with `if (ct.IsCancellationRequested)`; do not treat as failure. |

> All four DI failure paths are locked by `OnityErrorMessageTests.cs`. Reactive/
> messaging disposal-after-dispose behavior is locked by `ReactiveTests.cs` and
> `MessageChannelTests.cs`. If you change a message string, update the test.

### 8.4 Factories and pooling

Factory and pool resolution failures reuse the `OnityResolveException` / `OnityBindingException` types above; the pool classes themselves throw standard .NET exceptions.

| Exception and message contains | Cause | Fix |
| --- | --- | --- |
| `OnityResolveException`: `Could not resolve service type '<IFactory type>'. It is an unbound interface.` | An `IFactory<...>` was injected or resolved, but no `BindFactory<...>()` / `BindPooledFactory(...)` ran in this container or a parent before the resolve (or it was bound in a sibling or child scope). | Bind it in an installer of the same or a parent context: `BindFactory<...>()` for your own factory class, `BindPooledFactory(...)` for a pool. Prefer these over the message's generic `Bind<...>().To<Impl>()` hint. |
| `OnityResolveException`: `Could not resolve service type '<IPool type>'. It is an unbound interface.` (same for `IParameterizedPool<T>`) | `IPool<T>` was injected but nothing bound it, or only a custom factory was bound. `BindPooledFactory` binds `IPool<T>` only, never `IParameterizedPool<T>`. | Call `BindPooledFactory(prefab, ...)` or `BindPooledFactory(pool)`. For `IParameterizedPool<T>` also add `BindInstance<IParameterizedPool<T>>(pool)`. |
| `OnityResolveException`: `Failed to instantiate '<YourType>' using constructor '<YourType>(System.Int32)'. Error: Type 'System.Int32' has no accessible constructor...` (a `string` parameter reports `Failed to instantiate 'System.String'...` instead; BCL constructor text varies by runtime) | A container-built class takes a runtime value (id, level, position) as a constructor parameter, so the container tries to build the primitive or struct itself. | Move the value into an `IFactory<TParam, TValue>` (`BindFactory`), inject the factory, and call `Create(value)`. |
| `OnityBindingException`: `Cannot bind pooled factory with a null prefab.` / `Cannot bind pooled factory with a null pool.` | `BindPooledFactory` received an unassigned or destroyed prefab (an empty `[SerializeField]`) or a null pool. | Assign the prefab on the installer, or pass a constructed pool. |
| `InvalidOperationException`: `Item has already been returned to the pool.` | A second `Release` of an item that is already inactive in an `OnityObjectPool<T>` created with `collectionCheck: true`. `PrefabComponentPool<T>` never throws this. | Release each item exactly once. Clear the item's stored release callback in `OnPoolRelease` / `actionOnRelease` so a repeat call is a no-op. |
| `InvalidOperationException`: `Fixed-size pool has no available items.` / `Fixed-size prefab pool has no available items.` | A `fixedSize: true` pool has all `maxSize` items checked out. | Release items, raise `maxSize`, or drop `fixedSize` (the pool then creates on demand and retains at most `maxSize` inactive items). |
| `ObjectDisposedException` (object name = the pool's diagnostics name) from `Get`, `Release`, `Clear`, `Prewarm` or a `PooledFactory.Create` | The pool was already disposed: by your code, by its scope ending (the pool `BindPooledFactory(prefab, ...)` creates is disposed with its scope), or you hold a pool from a finished scope. | Stop using a pool after its owner disposes it, return checked-out items before `Dispose()`, and resolve from a live scope. |
| `ArgumentOutOfRangeException` (`initialSize`): `Pool capacity must be positive and initialSize must be within maxSize.` / (`count`) from `Prewarm` | `maxSize <= 0`, an `initialSize` below 0 or above `maxSize`, or a `Prewarm` count outside `0..maxSize`. | Fix the capacity arguments. `Prewarm(count)` is a target total, not an increment. |
| `ArgumentNullException` (`createFunc`, `prefab`, `pool`, `initialize`) | A null create delegate, prefab, pool or initializer reached a pool or `PooledFactory` constructor (or a null `initialize` reached `Get`). | Pass non-null arguments. |

`OnityErrorMessageTests.cs` locks the unbound-interface wording; `PoolingTests.cs` locks the pool exception types but not their strings, which come from `OnityObjectPool.cs`, `PrefabComponentPool.cs` and `MonoInstaller.cs`. If you change one of those strings, update this table.

---

## 9. Events decision rule — message vs ReactiveProperty vs direct call

Three ways for one part of the game to tell another that something happened.
Pick by **the shape of the information**, not by habit:

| Use | When | API | Why |
| --- | --- | --- | --- |
| **Message** (`OnityEventHub` / `IPublisher<T>` + `ISubscriber<T>`) | A **transient, fire-and-forget notification** with 0..N decoupled listeners that only care about *future* occurrences (`PlayerDamaged`, `EnemyKilled`, `LevelLoaded`). | `events.Publish(new PlayerDamaged(10));` / `events.Subscribe<PlayerDamaged>(OnDamaged).AddTo(this);` | Sender and receivers never reference each other. A late subscriber misses past messages **by design** — there is no replay/buffer. |
| **`ReactiveProperty<T>`** (in DI, shared via `BindInstance` / `BindInterfacesAndSelfTo`) | **Current state that new listeners must immediately know** (health, score, current wave, connection status). | `var hp = new ReactiveProperty<int>(100); hp.Value = 90;` / `hp.Where(v => v <= 0).Subscribe(_ => Die()).AddTo(this);` | Subscribing **emits the current value first**, then every real change. Built-in `DistinctUntilChanged`. This is the only shipped "replay current value" primitive. |
| **Direct service call** (constructor-injected interface) | A **command/query with exactly one owner** where you need a **return value, ordering, or a synchronous result** (`damage.Calculate(...)`, `save.Write(...)`, `inventory.TryAdd(...)`). | `public Combat(IDamageCalculator d) { m_d = d; } ... m_d.Calculate(hit);` | A message/observable cannot return a value or guarantee a single handler. One caller, one callee, one result — just call the method. |

Decision flow:

1. **Do you need a return value, or must exactly one thing handle this?** -> **Direct service call** (inject the interface). Stop.
2. **Is this the *current value* of some state a fresh subscriber must see right now?** -> **`ReactiveProperty<T>`** (subscribe emits current value first). Stop.
3. **Otherwise** (a past-tense notification, fan-out to unknown listeners, late subscribers may miss it) -> **Message** via `OnityEventHub` / channel.

Composition: any message stream can become reactive with `events.Observe<T>()`
(returns `IOnityObservable<T>`), so you can `Where`/`Select` over events exactly
like over a `ReactiveProperty<T>`. Do **not** reach for messaging to model
current state (new listeners would miss it) and do **not** reach for a
`ReactiveProperty<T>` to model a one-shot command with a result (use a direct
call). Keyed messaging ships through `KeyedMessageChannel<TKey, TMessage>`;
buffered/replay and request-response messaging do not. If you want "the last
message for a late subscriber", use a `ReactiveProperty<T>`.

---

## 10. Factories and pooling — `Onity.Factory`, `Onity.Pooling`

Contracts: `IFactory<...>` (`Onity.Factory`, engine-free), `IPool<T>`, `IParameterizedPool<T>` and `IPoolHooks` (`Onity.Pooling`). Implementations: `OnityObjectPool<T>` (plain C# items, `T : class`) and `PrefabComponentPool<T>` (prefab clones), both `IParameterizedPool<T>` and `IDisposable`; `PooledFactory<...>` adapts a pool to `IFactory<...>`. Choose the API with the table in 2.5. There is no `MemoryPool<T>` / `PlaceholderFactory` equivalent beyond these types, and ECS entity pooling (`Onity.DOTS`: `OnityPoolableTag`, `OnityDotsPoolEntityUtils`) is a separate system, not an `IPool<T>`.

### 10.1 Prefab pool — `BindPooledFactory`

`container.BindPooledFactory(prefab, parent, defaultCapacity, maxSize)` (a `MonoInstaller` extension from `Onity.Unity.Installers`) creates a `PrefabComponentPool<TComponent>` and registers two bindings: `IPool<TComponent>` (the pool, as an instance) and `IFactory<TComponent>` (a `PooledFactory<TComponent>` singleton whose `Create()` is `pool.Get()`). Inject the factory to spawn and the pool to release.

```csharp
// Recipe A: installer. Defaults are defaultCapacity 16 and maxSize 512.
using Onity.DI;
using Onity.Unity.Installers;           // BindPooledFactory
using UnityEngine;

public sealed class ProjectileInstaller : MonoInstaller
{
    [SerializeField] private Projectile m_projectilePrefab;
    [SerializeField] private Transform m_projectileRoot;           // optional parent; keep it as long-lived as the pool

    public override void InstallBindings(OnityContainer container)
    {
        container.BindPooledFactory(m_projectilePrefab, m_projectileRoot, defaultCapacity: 32, maxSize: 256);   // the scope disposes this pool (10.4)
        container.Bind<ProjectileSpawner>().AsSingle();
    }
}
```

```csharp
// Recipe B: spawner. IFactory<T> spawns, IPool<T> releases.
using System;
using Onity.Factory;
using Onity.Pooling;
using UnityEngine;

public sealed class ProjectileSpawner
{
    private readonly IFactory<Projectile> m_factory;
    private readonly IPool<Projectile> m_pool;
    private readonly Action<Projectile> m_despawn;                 // cached once; a method-group conversion allocates each time

    public ProjectileSpawner(IFactory<Projectile> factory, IPool<Projectile> pool)
    {
        m_factory = factory;
        m_pool = pool;
        m_despawn = Despawn;
    }

    public Projectile Spawn(Vector3 position, Vector3 velocity)
    {
        Projectile projectile = m_factory.Create();                // active; OnPoolGet has already run
        projectile.transform.position = position;
        projectile.Launch(velocity, m_despawn);
        return projectile;
    }

    public void Despawn(Projectile projectile)
    {
        m_pool.Release(projectile);                                // exactly once per Spawn
    }
}
```

```csharp
// Recipe C: pooled component. IPoolHooks carries the reset logic.
using System;
using Onity.Pooling;
using UnityEngine;

public sealed class Projectile : MonoBehaviour, IPoolHooks
{
    private Action<Projectile> m_despawn;
    private Vector3 m_velocity;

    public void Launch(Vector3 velocity, Action<Projectile> despawn)
    {
        m_velocity = velocity;
        m_despawn = despawn;
    }

    public void OnHit()
    {
        m_despawn?.Invoke(this);                                   // no-op once released: guards a double release
    }

    public void OnPoolGet()
    {
        // Runs after SetActive(true). Setup that needs the live object belongs here.
    }

    public void OnPoolRelease()
    {
        // Runs before SetActive(false). Reset ALL per-use state here.
        m_velocity = Vector3.zero;
        m_despawn = null;
    }
}
```

Call order (verified in `OnityObjectPool.cs` and `PrefabComponentPool.cs`):

| Operation | `PrefabComponentPool<T>` | `OnityObjectPool<T>` |
| --- | --- | --- |
| `Get()` / `factory.Create()` | take an inactive clone, else `Instantiate` one (inactive) -> `SetActive(true)` -> `IPoolHooks.OnPoolGet()` | take an inactive item, else `createFunc()` -> `actionOnGet` |
| `Get(param, initialize)` / `PooledFactory<TParam,...>.Create(param)` | take or instantiate -> `initialize(item, param)` -> `SetActive(true)` -> `OnPoolGet()` | take or create -> `initialize(item, param)` -> `actionOnGet` |
| `Release(item)` | `OnPoolRelease()` -> `SetActive(false)` -> keep it, or destroy it when `maxSize` inactive items are already held | `actionOnRelease` -> keep it, or `actionOnDestroy` when `maxSize` inactive items are already held |

### 10.2 Parameterized pooled spawn

`BindPooledFactory(prefab)` binds only the zero-argument `IFactory<T>`. For `Create(position)`, choose by when the component must see the argument:

- **Wrapper `IFactory<TParam, T>` over `IPool<T>`** (works with `BindPooledFactory(prefab)`): you set state after `Get()` returns, so activation, `OnEnable` and `OnPoolGet` have already run without it.
- **`PooledFactory<TParam, T>`** (and the 2-parameter form) over an `IParameterizedPool<T>`: `initialize(item, param)` runs before activation and get hooks on every use, including a clone's first `OnEnable`. `BindPooledFactory` binds only `IPool<T>`, so build the pool yourself and bind the factory with `BindInstance`.

```csharp
// Recipe D: wrapper factory (state is applied after activation).
using Onity.Factory;
using Onity.Pooling;
using UnityEngine;

public sealed class ProjectileAtPositionFactory : IFactory<Vector3, Projectile>
{
    private readonly IPool<Projectile> m_pool;

    public ProjectileAtPositionFactory(IPool<Projectile> pool)
    {
        m_pool = pool;
    }

    public Projectile Create(Vector3 position)
    {
        Projectile projectile = m_pool.Get();                      // already active: OnEnable and OnPoolGet have run
        projectile.transform.position = position;
        return projectile;
    }
}

// In the installer (Recipe A), next to the BindPooledFactory call:
container.BindFactory<Vector3, Projectile, ProjectileAtPositionFactory>();     // IFactory<Vector3, Projectile>
```

```csharp
// Recipe E: PooledFactory adapter (state is applied before activation and OnPoolGet). The pool is built here, so tie it to the scope.
using Onity.DI;                                                         // AddTo
using Onity.Factory;
using Onity.Pooling;
using Onity.Unity.Installers;
using UnityEngine;

public sealed class ProjectileInstaller : MonoInstaller
{
    [SerializeField] private Projectile m_projectilePrefab;
    [SerializeField] private Transform m_projectileRoot;

    public override void InstallBindings(OnityContainer container)
    {
        PrefabComponentPool<Projectile> pool = new PrefabComponentPool<Projectile>(
            m_projectilePrefab, m_projectileRoot, defaultCapacity: 32, maxSize: 256);
        pool.AddTo(container);                                          // caller-built pool: disposed with the scope only because of this (10.4)
        container.BindPooledFactory(pool);                              // IPool<Projectile> + IFactory<Projectile>
        container.BindInstance<IFactory<Vector3, Projectile>>(
            new PooledFactory<Vector3, Projectile>(
                pool,
                (projectile, position) => projectile.transform.position = position));   // runs before activation and OnPoolGet
    }
}

// Consumer: inject IFactory<Vector3, Projectile> and call Create(position).
```

With the adapter, `OnPoolGet` already sees the state `initialize` set, so do not clear those fields in `OnPoolGet`: reset them in `OnPoolRelease` (10.5).

### 10.3 Plain C# pool — `OnityObjectPool<T>`

Use `new OnityObjectPool<T>(...)` for items that are not `GameObject`s (`T : class`). Parameters, with the real names and defaults:

| Parameter | Default | Meaning |
| --- | --- | --- |
| `createFunc` | required | creates an item when none is inactive (`null` throws `ArgumentNullException`) |
| `actionOnGet` | `null` | runs on every `Get` after the item is taken (never during prewarm) |
| `actionOnRelease` | `null` | runs on every `Release`, before the item is retained or destroyed; if it throws, the pool is unchanged and the release can be retried |
| `actionOnDestroy` | `null` | runs for items the pool drops: overflow above `maxSize`, `Clear()`, `Dispose()` |
| `collectionCheck` | `false` | `true` makes a duplicate `Release` of an inactive item throw (by reference, in the Editor and in players) |
| `defaultCapacity` | `16` | initial capacity of the inactive stack (it doubles up to `maxSize`) |
| `maxSize` | `1024` | most INACTIVE items retained; the total cap when `fixedSize` is `true`; must be above 0 |
| `diagnosticsName` | `null` | label in snapshots and the `ObjectDisposedException` object name; `null` gives `OnityObjectPool<ItemTypeName>` |
| `initialSize` | `0` | items created in the constructor without running hooks; `0..maxSize` |
| `fixedSize` | `false` | `true` makes `Get()` throw `InvalidOperationException` when all `maxSize` items are checked out |

`PrefabComponentPool<T>` takes `(prefab, parent = null, defaultCapacity = 16, maxSize = 512, diagnosticsName = null, initialSize = 0, fixedSize = false)`; its default `diagnosticsName` is `PrefabComponentPool<T>:<prefab name>`. `Prewarm(count)` on either pool raises the created total to `count` (a target, not an increment) without running hooks.

```csharp
// Recipe F: a service that owns a plain pool, using every constructor option.
using System;
using Onity.Pooling;
using Onity.Unity.Installers;           // BindPooledFactory (binding choices below)

public sealed class PathNodePool : IDisposable
{
    private readonly OnityObjectPool<PathNode> m_pool;

    public PathNodePool()
    {
        m_pool = new OnityObjectPool<PathNode>(
            createFunc: () => new PathNode(),
            actionOnGet: node => node.Reset(),                          // every Get, after the item is taken
            actionOnRelease: node => node.Clear(),                      // every Release, before it is retained
            actionOnDestroy: node => node.Free(),                       // overflow above maxSize, Clear(), Dispose()
            collectionCheck: true,                                      // default false; true makes a duplicate Release throw
            defaultCapacity: 64,
            maxSize: 1024,
            diagnosticsName: "PathNodePool");
    }

    public PathNode Rent()
    {
        return m_pool.Get();
    }

    public void Return(PathNode node)
    {
        m_pool.Release(node);                                           // exactly once per Rent; never touch the node afterwards
    }

    public void Dispose()
    {
        m_pool.Dispose();
    }
}

// Binding choices:
container.Bind<PathNodePool>().AsSingle();                      // the service owns the pool; the container disposes the IDisposable singleton
// or inject IPool<PathNode> / IFactory<PathNode> directly; the pool is then caller-owned (10.4):
IPool<PathNode> shared = new OnityObjectPool<PathNode>(() => new PathNode(), collectionCheck: true);
container.BindPooledFactory(shared);                            // IPool<PathNode> + IFactory<PathNode>
```

### 10.4 Lifetime and disposal — what the code does

- **Creation.** `BindPooledFactory(prefab, ...)` constructs the pool while the installer runs. No clone exists yet: the first `Get` or `Prewarm` instantiates clones under a hidden inactive root, then parents each one to `parent`.
- **Visibility follows the binding's scope.** `IPool<T>` is bound as an instance and `PooledFactory<T>` as a singleton in the container that ran the installer. Child scopes resolve the same pool and factory; a child that calls `BindPooledFactory` for the same `T` shadows them only inside that child. Two binds for one `T` in the same container: the last wins and the first pool is orphaned (one that `BindPooledFactory(prefab, ...)` created is still disposed with the scope). So a `ProjectContext` pool is shared by every scene scope and a `SceneContext` pool only by its scene.
- **Disposal follows who created the pool.** `BindPooledFactory(prefab, ...)` creates its pool, so the scope owns it: `OnityContainer.Dispose()` (a context calls it from `OnDestroy`) cancels the container's `LifetimeToken` first and that disposes the pool, so it leaves the static `OnityPoolDiagnosticsRegistry` and its inactive clones and hidden `... Inactive Clone Root` are destroyed. A pool you build and pass in, with `BindPooledFactory(pool)` or `BindInstance`, stays caller-owned: the container never disposes bound instances (`InstanceProvider.Dispose()` is empty), so it outlives the scope and stays registered until you dispose it. Singleton services that implement `IDisposable` ARE disposed by the container (2.6), after the pool.
- **Tie your own pool to the scope.** Build it yourself, bind it with `BindPooledFactory(pool)`, and call `pool.AddTo(container)` (`Onity.DI`; an `OnityContainer` is an `IOnityScopeLifetime`; Recipe E) so the container disposes it, or call `pool.Dispose()` when the scope ends, or hold it inside an `IDisposable` service bound with `AsSingle()` (Recipe F). To end a pool that `BindPooledFactory(prefab, ...)` created before its scope ends, cast the resolved `IPool<T>` to `IDisposable`; the scope's later disposal then does nothing.
- **`Dispose()`** is idempotent. It destroys inactive items only (`actionOnDestroy`; for prefab clones `Destroy` in play mode and `DestroyImmediate` otherwise, plus the hidden root), unregisters the pool from the registry, and leaves checked-out items untouched, so return them first (a scope-owned pool is disposed before the scope's singletons, so a service's own `Dispose()` can no longer release into it). Afterwards `Get`, `Release`, `Clear`, `Prewarm` and every `PooledFactory.Create` throw `ObjectDisposedException`, and snapshots report zero counts with `IsDisposed == true`.
- **Parent lifetime.** Keep `parent` (and the scene it lives in) at least as long-lived as the pool. Unity destroys clones together with their parent, and `PrefabComponentPool<T>` does not check for destroyed items (10.5).

### 10.5 Documented behaviors — DO / DON'T

Evidence names tests (`PoolingTests` EditMode unless marked PlayMode; `*` is a name stem) or `source` when only the code supports the row.

| Behavior | DO / DON'T | Evidence |
| --- | --- | --- |
| Release exactly once | DO release every rented item once. With `collectionCheck: true`, a second `Release` of an inactive item throws `InvalidOperationException` (`Item has already been returned to the pool.`) in the Editor **and** in players. It compares references, and rejects the call before the release hook runs or any counter changes. | `OnityObjectPool_CheckedReturns_*` |
| Duplicate release is unchecked by default | DON'T count on detection: `collectionCheck` defaults to `false`, and `PrefabComponentPool<T>` has no duplicate check at all. A repeated release then retains the same instance twice, so two later `Get()` calls return it. DO guard in the item (clear the stored release callback in `OnPoolRelease`, call it with `?.Invoke`). | source |
| Use after release | DON'T read, write or keep a reference to an item after `Release`: the next `Get` may hand it to someone else, the prefab pool has deactivated it, and an item above `maxSize` (or still inactive at `Clear()` / `Dispose()`) is destroyed. | `OnityObjectPool_ReusesReleasedInstances`, `PrefabComponentPool_GetAndRelease_*` |
| Release only what the pool issued | DON'T release `null` or an item from another pool. `Release` does not validate origin: a released `null` can come back from a later `Get()`, and `CountActive` can go negative. | source |
| Reset in hooks | DO reset per-use state in `OnPoolRelease` / `actionOnRelease` (once per use, before deactivation), not in spawn code. Use `OnPoolGet` / `actionOnGet` only for setup that needs the live item and must not overwrite state a parameterized `initialize` already set. Prewarm (`initialSize`, `Prewarm`) runs no hooks. | `OnityObjectPool_PrewarmAndFixedCapacity_*`, `PrefabComponentPool_PrewarmAndParameterizedGet_*` |
| `IPoolHooks` scope | DON'T put `IPoolHooks` on a plain item and expect `OnityObjectPool<T>` to call it: that pool only runs `actionOnGet` / `actionOnRelease`. `PrefabComponentPool<T>` calls `IPoolHooks` on the pooled component. | source |
| Failing hooks | A throwing get hook or parameterized `initialize` returns the item to the pool, skips the release hook, does not count the get, and rethrows. A throwing release hook leaves the item checked out: retry the same `Release`. | `*_GetHookFailure_*`, `TwoParameterPooledFactory_Recovers*`, `OnityObjectPool_InitializerFailure_*`, `OnityObjectPool_CheckedReturns_TrackRetriesOverflowAndClear` |
| Never destroy a pooled item | DON'T `Destroy` / `DestroyImmediate` a pooled instance, or destroy its `parent` or scene while the pool lives: release it. The pool keeps its reference, `PrefabComponentPool<T>.Get` has no destroyed-item check, and a clone destroyed while checked out never leaves `CountActive`. | source |
| Capacity above `maxSize` | `maxSize` caps retained **inactive** items. A release above it runs `actionOnRelease`, then `actionOnDestroy`, and `CountAll` drops (`OnityObjectPool<T>`; the prefab pool destroys the clone through Unity's `ObjectPool<T>`). With `fixedSize: true`, `maxSize` is the total cap and `Get()` throws `InvalidOperationException` (`Fixed-size pool has no available items.` / `Fixed-size prefab pool has no available items.`) when every item is checked out. | `OnityObjectPool_CheckedReturns_TrackRetriesOverflowAndClear`, `OnityObjectPool_PrewarmAndFixedCapacity_*`; the `CountAll` decrement is source-only |
| `Clear()` | `Clear()` destroys inactive items only. Checked-out items stay active and tracked; `OnityObjectPool<T>` subtracts only the destroyed ones from `CountAll`. | `OnityObjectPool_Clear_*` |
| Prefab activation | `PrefabComponentPool<T>` runs `SetActive(true)` then `OnPoolGet` on `Get`, and `OnPoolRelease` then `SetActive(false)` on `Release`, so `OnEnable` / `OnDisable` run on every use. Clones start inactive: `OnEnable` first fires on the first `Get`, after any `initialize`. DON'T read runtime parameters in `Awake`. | `PrefabComponentPool_GetAndRelease_*`; PlayMode `ActivePrefab_FirstParameterizedGet_InitializesBeforeOnEnable` |
| No container injection | DON'T expect `[Inject]` members on a pooled clone to be filled: the prefab pool only `Instantiate`s it. Pass dependencies in on spawn (as `Launch(...)` does in Recipe C) or call `IResolver.Inject(clone)` yourself. | source |
| Threading | DON'T share a pool across threads. `Get` / `Release` take no lock (the diagnostic counters use `Interlocked` only so the editor monitor can read them), and the prefab pool touches Unity objects. | source |
| Pool ownership | The scope disposes the pool `BindPooledFactory(prefab, ...)` creates. DO `pool.AddTo(container)` (or `Dispose()` it) for a pool you build and bind with `BindPooledFactory(pool)` or `BindInstance`: the container never disposes it. DON'T `Release` into a pool whose scope has ended: it throws `ObjectDisposedException`. | `BindPooledFactory_ContainerDispose_*`, `BindPooledFactory_CallerSuppliedPoolAddedToScope_*`; PlayMode `Destroying*Context_DisposesPoolCreatedByBindPooledFactory` |

### 10.6 Allocation and diagnostics

- Editor tests (`PoolingTests`) count zero `GC.Alloc` profiler events over 64 warmed rent/return pairs for `OnityObjectPool<T>` (default and `collectionCheck: true`), all three `PooledFactory` forms and a hook-less `PrefabComponentPool<T>`. That covers the warmed path only: a `Get` that finds no inactive item runs `createFunc` or `Instantiate`, `Prewarm` creates items, and the inactive stack grows (doubling, up to `maxSize`) once `defaultCapacity` is exceeded. Cache the delegates you pass in; converting a method group allocates each time. Measured reports: `docs/benchmarks/factory-pooling-2026-09-23.md` and `docs/benchmarks/pool-own-stack-2026-10-01.md`.
- `pool.GetDiagnosticsSnapshot()` returns `PoolName`, `PoolType`, `ItemType`, `CountAll`, `CountActive`, `CountInactive`, `GetCount`, `ReleaseCount` and `IsDisposed`; `OnityPoolDiagnosticsRegistry.GetSnapshots(list)` lists every registered pool, and the editor Pool Monitor window (`Onity/Tools/Pool Monitor`) reads that registry.

---

## 11. Async — `OnityTask` (`Onity.Unity.Async`)

`OnityTask` / `OnityTask<T>` replace UniTask. Use them for every Unity-facing async flow; keep `Task` only where a
plain .NET API already returns it. Namespaces: `Onity.Unity.Async` (tasks, timings, composition, streams, channels,
UI events) and `Onity.Unity.Async.Triggers` (lifecycle and MonoBehaviour message triggers).

> **Assembly references (CS0012).** An asmdef that calls `Onity.Unity.Async` extension methods MUST reference
> `Onity.Reactive` as well as `Onity.Unity` (and `Onity.Messaging` for the messaging bridges). `AsOnityAsyncEnumerable`,
> `BindTo`, `ToOnityTask` and `WaitAsync` have overloads that use `Onity.Reactive` types, so without the reference the
> compiler reports CS0012 even when you call a different overload. uGUI extensions also need `Onity.Unity.UGUI` and
> `UnityEngine.UI`.

### 11.1 Rules — DO / DON'T

| Behavior | DO / DON'T |
| --- | --- |
| Single consumer | Pooled tasks (cancelable waits, timed waits, delays, `WaitUntil`, operations, suspended `async OnityTask` methods) are single-consumer. DO await once; DO `Preserve()` once before sharing. DON'T read `IsCompleted` / `Status` after consuming: it throws `InvalidOperationException`. |
| Stateless waits | In Play, `NextFrame()`, `DelayFrames(n)`, `NextFixedFrame()`, `NextLateFrame()` and `Yield()` without a cancelable token rent nothing and may be awaited by any number of consumers. With a token they are pooled and single-consumer. |
| `Yield()` | Returns `OnityYieldAwaitable` (converts implicitly to `OnityTask`). DO call `.ToOnityTask()` before an `OnityTask` extension (`Yield().ToOnityTask().Timeout(1f)`). DON'T write `Yield(default)`: it is ambiguous; use `Yield(cancellationToken: ct)`. |
| Ownership | DO give every loop and long wait a token from its owner: `this.GetCancellationTokenOnDestroy()` in a component, `IOnityScopeLifetime.Token` / `this.GetScopeCancellationToken()` for scope services. DON'T start an unowned `while (true)` loop. |
| Fire and forget | DO `task.Forget(handler)` or an `async OnityTaskVoid` method. DON'T write `async void`. Unobserved faults go to `OnityTaskScheduler`, which drops `OperationCanceledException` by default. |
| Threads | Continuations run on the thread that completes the awaited operation. DO `await OnityTask.SwitchToMainThread(ct)` after `RunOnThreadPool(..., returnToMainThread: false)`, `SwitchToThreadPool` or a worker producer before touching Unity APIs. |
| Timings | `OnityPlayerLoopTiming.Update` / `FixedUpdate` / `LateUpdate` run AFTER the script callbacks. UniTask's `PlayerLoopTiming.Update` / `FixedUpdate` are `UpdateBegin` / `FixedUpdateBegin`. |
| Seconds | `OnityTask.Delay(2)` waits two SECONDS. There is no integer-milliseconds overload; use `Delay(TimeSpan.FromMilliseconds(ms))`. |
| Composition | `WhenAll(a, b)` with typed arguments returns a TUPLE (even for the same type); pass an array for `T[]`. `WhenAny(a, b)` typed returns `(winArgumentIndex, result1, result2)`. `WhenAny` never cancels losers. |
| Context flow | Off by default (UniTask semantics). Set `OnityTask.FlowExecutionContext = true` once at startup only if code needs `AsyncLocal<T>` across awaits. |
| Bursts | Defaults keep 128 runners per async method and 256 sources per source type. For methods that run in bursts of thousands, raise `OnityTask.RunnerPoolCapacity` / `SourcePoolCapacity` at startup. |
| Null literals | `Select(null)`, `Where(null)`, `SelectAwait(null)`, `WhereAwait(null)`, `ForEachAsync(null)` on streams, `AsOnityTask(null, ct)` and `new OnityChannelClosedException(null)` are ambiguous: cast to the intended type. |
| Edit Mode | Timing waits, `Yield()` and explicit timings require an active Play/player session on the main thread. Only the float-seconds `Delay`, token-only `WaitUntil` / `WaitWhile` and default frame waits also run in Edit Mode. |

### 11.2 Which async primitive

| You need | Use |
| --- | --- |
| Wait frames, time or a condition | `OnityTask.NextFrame(ct)`, `DelayFrames(n, ct)`, `Delay(seconds, ct)`, `WaitUntil(predicate, ct)`; timed forms take an `OnityPlayerLoopTiming` and `OnityDelayType` |
| A detached method | `async OnityTaskVoid` or `task.Forget(handler)` |
| Complete from a callback, many awaiters | `OnityTaskCompletionSource<T>` |
| Pooled one-shot handoff, one awaiter | `OnityAutoResetTaskCompletionSource<T>` |
| A repeating callback on a timing | `OnityPlayerLoopTimer.StartNew(...)` |
| A sequence of values pulled with `await foreach` | `IOnityAsyncEnumerable<T>`: `OnityAsyncEnumerable.Create`, timing streams, LINQ operators |
| Producer/consumer with backpressure | `OnityChannel.CreateBounded<T>(capacity)` |
| Current value plus async change notification | `OnityAsyncReactiveProperty<T>`; for an existing `ReactiveProperty<T>`, `WaitAsync` / `WaitUntilAsync` |
| The next message from the bus | `subscriber.ReceiveAsync(ct)`; a buffered stream: `ReceiveAllAsync(capacity, overflow)`; an async handler with backpressure: `asyncSubscriber.SubscribeQueued(handler, capacity, token)` |
| Work that ends with its DI scope | `IOnityScopeLifetime.Token`, `disposable.AddTo(scope)`; awaited startup: `IOnityAsyncInitializable` |
| MonoBehaviour messages | triggers: `GetAsync<Name>Trigger().<Message>Async(ct)` |
| UI input | UI Toolkit `button.OnClickAsync(ct)`, `field.OnValueChangedAsync(ct)`; uGUI the same names (`Onity.Unity.UGUI`) |
| Show a stream in UI | `stream.BindTo(textElement)`, `stream.BindTo(target, (target, value) => ..., ct)` |
| Synchronous fan-out to observers | not async: `Subject<T>` / `ReactiveProperty<T>` (section 3) or a message (section 4) |

### 11.3 Recipes

```csharp
// Recipe A: a component flow owned by its destroy token.
using System;
using System.Threading;
using Onity.Unity.Async;
using UnityEngine;

public sealed class DoorOpener : MonoBehaviour
{
    [SerializeField] private Transform m_door;

    private void Start()
    {
        OpenAsync(destroyCancellationToken).Forget(Debug.LogException);
    }

    private async OnityTask OpenAsync(CancellationToken ct)
    {
        await OnityTask.Delay(TimeSpan.FromSeconds(1), OnityDelayType.DeltaTime, OnityPlayerLoopTiming.Update, ct);
        while (m_door.localPosition.y < 2f)
        {
            m_door.localPosition += Vector3.up * Time.deltaTime;
            await OnityTask.NextFrame(ct);
        }
    }
}
```

```csharp
// Recipe B: a scope service that waits on state and messages; ends with its context.
using System.Threading;
using Onity.DI;
using Onity.Messaging;
using Onity.Reactive;
using Onity.Unity.Async;

public sealed class WaveDirector : IOnityInitializable
{
    private readonly IReadOnlyReactiveProperty<int> m_enemiesAlive;
    private readonly ISubscriber<BossDefeated> m_bossDefeated;
    private readonly IOnityScopeLifetime m_scope;

    public WaveDirector(IReadOnlyReactiveProperty<int> enemiesAlive, ISubscriber<BossDefeated> bossDefeated,
        IOnityScopeLifetime scope)
    {
        m_enemiesAlive = enemiesAlive;
        m_bossDefeated = bossDefeated;
        m_scope = scope;
    }

    public void Initialize() => RunAsync(m_scope.Token).Forget();

    private async OnityTaskVoid RunAsync(CancellationToken token)
    {
        await m_enemiesAlive.WaitUntilAsync(count => count == 0, token);   // completes at once if already 0
        BossDefeated message = await m_bossDefeated.ReceiveAsync(token);   // next message; no subscription to dispose
        await OnityTask.Delay(2f, token);
    }
}
```

```csharp
// Recipe C: a save queue: publishers wait only while 8 requests are pending.
using System.Threading;
using Onity.DI;
using Onity.Messaging;
using Onity.Unity.Async;

public sealed class SaveQueue : IOnityInitializable
{
    private readonly IAsyncSubscriber<SaveRequested> m_requests;
    private readonly IOnityScopeLifetime m_scope;

    public SaveQueue(IAsyncSubscriber<SaveRequested> requests, IOnityScopeLifetime scope)
    {
        m_requests = requests;
        m_scope = scope;
    }

    public void Initialize() => m_requests.SubscribeQueued(WriteAsync, 8, m_scope.Token);

    private async OnityTask WriteAsync(SaveRequested request, CancellationToken ct)
    {
        await OnityTask.SwitchToThreadPool(ct);
        // ... write the file ...
    }
}
```

```csharp
// Recipe D: triggers, UI Toolkit events and binding.
using System.Threading;
using Onity.Reactive;
using Onity.Unity.Async;
using Onity.Unity.Async.Triggers;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class Pickup : MonoBehaviour
{
    [SerializeField] private UIDocument m_hud;
    private readonly ReactiveProperty<int> m_coins = new ReactiveProperty<int>(0);

    private void Start()
    {
        RunAsync(destroyCancellationToken).Forget(Debug.LogException);
    }

    private async OnityTask RunAsync(CancellationToken ct)
    {
        Label coinsLabel = m_hud.rootVisualElement.Q<Label>("coins");
        m_coins.AsLatestAsyncEnumerable().BindTo(coinsLabel, ct);           // TextElement text, latest value

        Button collect = m_hud.rootVisualElement.Q<Button>("collect");
        await collect.OnClickAsync(ct);

        Collider other = await this.GetAsyncTriggerEnterTrigger().OnTriggerEnterAsync(ct);
        m_coins.Value += 1;
    }
}
```

```csharp
// Recipe E: composition.
(Texture2D icon, string title) = await OnityTask.WhenAll(LoadIconAsync(ct), LoadTitleAsync(ct));   // tuple
int[] scores = await OnityTask.WhenAll(scoreTasks);                                               // array in, array out
(bool hasResultLeft, int score) = await OnityTask.WhenAny(scoreTask, OnityTask.Delay(5f, ct));      // typed vs untyped

await foreach (OnityWhenEachResult<int> each in OnityTask.WhenEach(scoreTasks).WithCancellation(ct))
{
    if (each.IsCompletedSuccessfully)
    {
        Debug.Log(each.Result);
    }
}
```

```csharp
// Recipe F: timings and timers.
await OnityTask.Yield(OnityPlayerLoopTiming.PreLateUpdate);                       // next PreLateUpdate drain
await OnityTask.WaitForFixedUpdate();                                             // after the physics step
await OnityTask.WaitUntil(this, self => self.m_isReady, OnityPlayerLoopTiming.LateUpdate, ct);   // no closure

OnityPlayerLoopTimer timer = OnityPlayerLoopTimer.StartNew(
    TimeSpan.FromSeconds(1), periodic: true, OnityDelayType.UnscaledDeltaTime,
    OnityPlayerLoopTiming.Update, ct, state => ((Spawner)state).Spawn(), this);
timer.Dispose();                                                                  // stops it
```

### 11.4 DI, reactive and messaging integration

| API | What it does |
| --- | --- |
| `IOnityScopeLifetime` (`Token`, `IsDisposed`) | Injectable scope lifetime; every context binds it. `container.LifetimeToken` / `OnityContext.LifetimeToken` are the same token. `Dispose()` cancels it BEFORE disposing services. |
| `disposable.AddTo(scope)` (`Onity.DI`) | Disposes when the scope ends (now if it already ended). |
| `component.GetScopeCancellationToken()` | Nearest context's `LifetimeToken`, else the component's destroy token. |
| `IOnityAsyncInitializable.InitializeAsync(CancellationToken)` | Awaited by `BuildAsync` after the async build callbacks, one at a time in registration order, on the build's context; the token is the scope token. |
| `context.IsReady`, `ReadyTask`, `WaitReadyAsync(ct)` | Readiness including the async initializers; `WaitReadyAsync` resumes on the main thread. |
| `container.DeclareAsyncMessage<T>()`, `container.BindAsyncReactiveProperty(initial)` | One shared `AsyncMessageChannel<T>` / `OnityAsyncReactiveProperty<T>` bound to its contracts; the container disposes it. |
| `property.WaitAsync(ct)`, `WaitUntilAsync(predicate, ct)` | Next accepted change / first match of an `IReadOnlyReactiveProperty<T>`; pooled, one shared subscription per property. |
| `property.AsLatestAsyncEnumerable()`, `ToAsyncReactiveProperty(ct)`, `asyncProperty.BindTo(reactiveProperty, ct)` | Conflating stream; async property view; write back into a `ReactiveProperty<T>`. |
| `subscriber.ReceiveAsync(ct)`, `ReceiveAsync(predicate, ct)` | Next (matching) message from an `ISubscriber<T>`. |
| `subscriber.ReceiveAllAsync(capacity, OnityBufferOverflow)` | Buffered stream; `Fault` (default), `DropOldest`, `DropNewest`. |
| `asyncSubscriber.SubscribeQueued(handler, capacity, lifetimeToken)` | Bounded queue to one ordered handler; `PublishAsync` waits only while full. |

`ReactiveProperty<T>.Dispose()` does NOT complete pending waits; pass the scope token. A `BindPooledFactory(prefab, ...)`
pool is disposed with its scope too (10.4).

### 11.5 UniTask names that change

| UniTask | Onity |
| --- | --- |
| `UniTaskVoid`, `UniTaskCompletionSource`, `AutoResetUniTaskCompletionSource` | `OnityTaskVoid`, `OnityTaskCompletionSource`, `OnityAutoResetTaskCompletionSource` |
| `PlayerLoopTiming.Update` / `FixedUpdate` | `OnityPlayerLoopTiming.UpdateBegin` / `FixedUpdateBegin` |
| `DelayType`, `PlayerLoopTimer`, `TimeoutController` | `OnityDelayType`, `OnityPlayerLoopTimer`, `OnityTimeoutController` |
| `UniTask.Delay(int ms)` | `OnityTask.Delay(TimeSpan.FromMilliseconds(ms))` |
| `UniTaskAsyncEnumerable`, `IUniTaskAsyncEnumerable<T>` | `OnityAsyncEnumerable`, `IOnityAsyncEnumerable<T>` |
| `ToUniTask(...)` on an `AsyncOperation` | `AsOnityTask(...)` |
| `ToUniTaskAsyncEnumerable()` | `ToOnityAsyncEnumerable()`; from an `IOnityObservable<T>`: `AsOnityAsyncEnumerable(capacity)` |
| `ct.ToUniTask()` | `ct.ToOnityTask()` |
| `AsyncReactiveProperty<T>` | `OnityAsyncReactiveProperty<T>` |
| `Channel.CreateSingleConsumerUnbounded<T>()` | `OnityChannel.CreateUnbounded<T>()` |
| `UniTaskScheduler`, `TaskPool.SetMaxPoolSize` | `OnityTaskScheduler`, `OnityTask.SourcePoolCapacity` |

Everything else keeps UniTask's member name on the Onity type. Full map: `Documentation~/Migration/From-UniTask.md`
in the package, or the [migration guide](https://furkantokkan.github.io/Onity/Migration/From-UniTask.html).

### 11.6 Error -> fix (async)

| Exception or diagnostic | Cause | Fix |
| --- | --- | --- |
| `InvalidOperationException`: `The OnityTask source is no longer valid. Pooled OnityTask instances can be awaited only once.` | A pooled task was awaited, read or bridged after it was consumed. | Await once and keep the result, or `Preserve()` before sharing. |
| `InvalidOperationException`: `OnityTask supports only one native awaiter.` | Two consumers awaited the same pooled task. | `Preserve()` once and share the returned task. |
| `InvalidOperationException`: `Onity timing waits require Unity's main thread.` / `... require an active Play/player session.` | An explicit-timing or timed wait created off the main thread, or one of those waits or `Yield()` used outside Play. | Create them on the main thread in Play (`SwitchToMainThread` first); Edit Mode tools use the float-seconds `Delay`, `WaitUntil` and the default frame waits. |
| `ArgumentException` from `WhenAll` / `WhenAny` | The same single-consumer task was passed twice. | Pass each task once; `Preserve()` to use one result twice. |
| `InvalidOperationException`: `The property is already publishing a value...` | An `OnityAsyncReactiveProperty<T>` was set from a continuation of its own publication. | Set it after the publication returns (for example after `Yield()`). |
| `OnityChannelClosedException` | Read from a completed, drained channel, or write / `Complete` after completion. | Use `ReadAllAsync` / `Completion` to end consumers; complete once. |
| `TimeoutException` | `Timeout(...)` won before the producer. | Use `TimeoutWithoutException` to get a flag; the producer keeps running unless you pass a source to cancel. |
| `OperationCanceledException` | The token was canceled. **Normal**, not a failure. | Catch it where the flow starts (`catch (OperationCanceledException) when (ct.IsCancellationRequested)`). |
| CS0012 (type defined in an assembly that is not referenced: `Onity.Reactive`) | The asmdef calls an `Onity.Unity.Async` extension without referencing `Onity.Reactive`. | Add `Onity.Reactive` (and `Onity.Messaging` for messaging bridges) to the asmdef references. |
| CS0121 (ambiguous call) on `Yield(default)` or a `null` delegate | Overloads accept both. | Use a named argument or cast `null` to the delegate type. |

### 11.7 Public-API index (`Onity.Unity.Async`)

- `OnityTask` statics: `CompletedTask`, `FromResult`, `FromException(<T>)`, `FromCanceled(<T>)`, `FromTask`, `Create`, `Defer`,
  `Never`, `Lazy`, `Void`, `Action`, `UnityAction`, `Yield`, `NextFrame`, `DelayFrame`, `DelayFrames`, `NextFixedFrame`,
  `NextLateFrame`, `WaitForFixedUpdate`, `WaitForEndOfFrame`, `Post`, `Delay`, `DelayUnscaled`, `WaitForSeconds`,
  `WaitUntil`, `WaitWhile`, `WaitUntilCanceled`, `WaitUntilValueChanged`, `WhenAll`, `WhenAny`, `WhenEach`,
  `SwitchToMainThread`, `ReturnToMainThread`, `SwitchToThreadPool`, `SwitchToTaskPool`, `SwitchToSynchronizationContext`,
  `ReturnToSynchronizationContext`, `ReturnToCurrentSynchronizationContext`, `RunOnThreadPool`, `ToCoroutine`,
  `LoadScene`, `LoadSceneAdditive`, `LoadSceneAsync`, `ActivateScene`, `UnloadScene`, `Send`, `GetJson`, `PostJson`;
  settings `FlowExecutionContext`, `RunnerPoolCapacity`, `SourcePoolCapacity`.
- Task members: `Status`, `IsCompleted`, `IsCompletedSuccessfully`, `IsCanceled`, `IsFaulted`, `AsTask()`, `Preserve()`,
  `Forget(handler)`, `GetAwaiter()`, `AsUnitTask()`, `AsOnityTask()` (typed), implicit `OnityTask<T>` to `OnityTask` and to
  `ValueTask`.
- Extensions: `Timeout`, `TimeoutWithoutException`, `AttachExternalCancellation`, `SuppressCancellationThrow`,
  `ContinueWith`, `Unwrap`, `ToAsyncLazy`, `Forget(handler, handleExceptionOnMainThread)`, `AsValueTask`, `AsOnityTask`
  (on `Task`, `ValueTask`, `AsyncOperation`, `JobHandle`, `AsyncGPUReadbackRequest`, `Awaitable`), `AsAssetOnityTask`,
  `AwaitForAllAssets`, `AsAssetBundleOnityTask`, `AsWebRequestOnityTask`, `AsInstancesOnityTask`, `WaitAsync` (`JobHandle`),
  `ToCancellationToken`, `ToOnityTask` (token, observable, `IEnumerator`), `WaitUntilCanceled` (token), `AddTo(ct)`,
  `RegisterWithoutCaptureExecutionContext`, `IsOperationCanceledException`, `GetCancellationTokenOnDestroy`,
  `RegisterRaiseCancelOnDestroy`, `GetScopeCancellationToken`, `ToObservable`, `FirstOnityTask`, `PublishOnityTask`,
  `SubscribeOnityTask`, `ReceiveAsync`, `ReceiveAllAsync`, `SubscribeQueued`, `WaitAsync` / `WaitUntilAsync` /
  `AsLatestAsyncEnumerable` / `ToAsyncReactiveProperty` (reactive properties), `StartAsyncCoroutine`, `WithCancellation`.
- Types: `OnityTaskVoid`, `OnityTaskStatus`, `OnityPlayerLoopTiming`, `OnityTaskPlayerLoop` (`Initialize`, `InitializeAll`,
  `IsInjected`, `DumpCurrentPlayerLoop`, `AddAction`, `AddContinuation`, `IsMainThread`, `MainThreadId`,
  `UnitySynchronizationContext`), `IOnityPlayerLoopItem`, `OnityYieldAwaitable`, `OnityDelayType`, `OnityPlayerLoopTimer`,
  `OnityTimeoutController`, `OnityTaskCompletionSource(<T>)`, `OnityAutoResetTaskCompletionSource(<T>)`, `OnityAsyncLazy(<T>)`,
  `OnityWhenEachResult<T>`, `OnityTaskScheduler`, `OnityTaskTracker`, `OnityProgress`, `OnityUnityWebRequestException`,
  `OnityCancellationTokenEqualityComparer`.
- Streams: `IOnityAsyncEnumerable<T>`, `IOnityAsyncEnumerator<T>`, `IOnityAsyncWriter<T>`, `OnityAsyncEnumerable`
  (`Create`, `Empty`, `Return`, `Range`, `Repeat`, `Never`, `Throw`, `Merge`, `EveryUpdate`, `Timer`, `Interval`, `TimerFrame`,
  `IntervalFrame`, `EveryValueChanged`), `OnityAsyncEnumerableLinq` (UniTask's LINQ operators), `OnityAsyncEnumerableExtensions`
  (`Select`, `Where`, `Take`, `SelectAwait`, `WhereAwait`, `ForEachAsync`, `FirstAsync`, `ToArrayAsync`, `WithCancellation`,
  `AsOnityAsyncEnumerable`, `AsAsyncEnumerable`, `AsObservable`, `ToOnityAsyncEnumerable`, `Subscribe`, `SubscribeAwait`),
  `IOnityConnectableAsyncEnumerable<T>`, `IOnityOrderedAsyncEnumerable<T>`, `IOnityLookup<TKey, T>`, `IOnityGrouping<TKey, T>`,
  `OnityBufferOverflow`, `BindTo`.
- Channels: `OnityChannel` (`CreateBounded`, `CreateUnbounded`), `OnityChannel<T>` (`Reader`, `Writer`),
  `OnityChannelReader<T>` (`TryRead`, `ReadAsync`, `WaitToReadAsync`, `Completion`, `ReadAllAsync`),
  `OnityChannelWriter<T>` (`TryWrite`, `WriteAsync`, `TryComplete`, `Complete`), `OnityChannelClosedException`.
- Async reactive properties: `OnityAsyncReactiveProperty<T>`, `OnityReadOnlyAsyncReactiveProperty<T>`,
  `IOnityAsyncReactiveProperty<T>`, `IOnityReadOnlyAsyncReactiveProperty<T>`, `ToReadOnlyAsyncReactiveProperty`,
  `BindAsyncReactiveProperty` (container).
- Triggers (`Onity.Unity.Async.Triggers`): `GetAsync<Name>Trigger()` for Awake, Start, Enable, Disable, Destroy and 55
  MonoBehaviour messages, `AwakeAsync`, `StartAsync`, `OnDestroyAsync`, `OnityAsyncTriggerBase<T>`, `OnityTriggerEvent<T>`,
  `IOnityTriggerHandler<T>`, `IOnityAsyncOneShotTrigger`.
- UI events: `UnityEvent` `OnInvokeAsync` / `OnInvokeAsAsyncEnumerable` / `GetAsyncEventHandler`; UI Toolkit `OnClickAsync`,
  `OnEventAsync<TEvent>`, `OnValueChangedAsync` and their `...AsAsyncEnumerable` forms, `BindTo(TextElement)`; uGUI
  (`Onity.Unity.UGUI`, only with `com.unity.ugui`) `OnClickAsync`, `OnValueChangedAsync`, `OnEndEditAsync`, EventSystems
  triggers, `BindTo(Text)`, `BindTo(Selectable)`.
