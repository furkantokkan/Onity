---
title: "AI Usage Guide"
nav_order: 6
---

# Onity AI Usage Guide

Machine-readable usage guide for writing CORRECT Onity code across the three pillars:
**DI** (replaces Zenject/VContainer), **Reactive** (replaces R3/UniRx), **Events** (replaces MessagePipe and the UniRx `MessageBroker`).

This guide is verified against the real Onity source. Every code block compiles against the current
public API. When this guide and any older design doc disagree, **this guide and the source win**.

- Target: Unity. Core asmdefs (`Onity.Core`, `Onity.DI`, `Onity.Reactive`, `Onity.Messaging`,
  `Onity.Factory`) are **engine-free** (no `UnityEngine`). Unity glue lives in `Onity.Unity`.
- Constraints baked into the code: hot-path machinery **designed to avoid per-call managed allocation** (a transient resolve still allocates the instance it returns; the published alloc figures were unreliable and are being re-measured), **no `System.Linq`** in the core, and **no non-Unity third-party runtime dependencies**.
- Naming convention in Onity source: private instance `m_camelCase`, private static `s_camelCase`,
  constants `k_camelCase`, Allman braces. Match it when adding code to the package.

---

## 1. Use Onity, not Zenject/R3/MessagePipe — one idiom, one install

You do NOT mix three libraries. Onity is one package with one mental model:

```
DI is the spine.            Bind services in a MonoInstaller; resolve via constructor injection.
Events ride the broker.     Inject IPublisher<T>/ISubscriber<T> or OnityEventHub; Publish / Subscribe.
Reactive operators ride both. Subject<T>/ReactiveProperty<T> AND broker.Observe<T>() are the SAME
                            IOnityObservable<T>, so Where/Select/Subscribe work on events and state alike.
Lifetime is one model.      Every Subscribe returns IDisposable. Dispose it with AddTo(this) (Unity)
                            or AddTo(CompositeDisposable) (plain C#). Forgetting this leaks.
```

10-line mental model (this is the whole framework):

```csharp
// 1. Register in an installer:           container.Bind<IThing>().To<Thing>().AsSingle();
// 2. Consume by constructor injection:    public Service(IThing thing) { ... }
// 3. Send an event:                       eventHub.Publish(new ThingHappened());
// 4. Receive an event:                    eventHub.Subscribe<ThingHappened>(OnThing).AddTo(this);
// 5. Receive as a filtered stream:        eventHub.Observe<ThingHappened>().Where(...).Subscribe(...).AddTo(this);
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
```

Builder methods, exact signatures:

| Call | Returns | Then |
| --- | --- | --- |
| `Bind<TContract>()` | `TypeBindingBuilder<TContract>` | `.To<TConcrete>()` (where `TConcrete : TContract`), then `.AsSingle()` / `.AsTransient()`, then optional `.NonLazy()` |
| `BindInterfacesAndSelfTo<TConcrete>()` | `MultiTypeBindingBuilder` | `.AsSingle()` / `.AsTransient()`, then optional `.NonLazy()` |
| `BindInterfacesTo<TConcrete>()` | `MultiTypeBindingBuilder` | same as above |
| `BindInstance<TContract>(instance)` | `void` | — |
| `BindFactory<TValue,TFactory>()` (+1-param, +2-param) | `void` | binds factory `AsSingle` via `BindInterfacesAndSelfTo` |

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

### 2.4 Child containers = Onity's "Scoped"

There is no `Scoped` lifetime keyword. Lifetime enum is exactly `{ Singleton, Transient }`. A per-scope
instance is a child-container `AsSingle`. Children inherit parent bindings; a child bind shadows the
parent only inside the child.

```csharp
using OnityContainer parent = new OnityContainer();
parent.Bind<IDependency>().To<Dependency>().AsSingle();

using OnityContainer child = new OnityContainer(parent);
child.Bind<IDependency>().To<AlternateDependency>().AsSingle();   // shadows in child only

// child.Resolve<IDependency>()  -> AlternateDependency
// parent.Resolve<IDependency>() -> Dependency (unchanged)
```

Map VContainer `Lifetime.Scoped` -> Onity child-container `AsSingle`.

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

### 2.6 Build / async startup

```csharp
container.RegisterBuildCallback(r => r.Resolve<IGameLoopRunner>().Start());            // sync, runs in Build()
container.RegisterBuildCallbackAsync(async (r, ct) => await r.Resolve<ISaveLoader>().PrimeAsync(ct));

container.Build();                          // runs sync callbacks once; idempotent
await container.BuildAsync(cancellationToken);   // runs Build() then async callbacks; result cached, re-armed on cancel/failure
```

Callbacks cannot be registered after Build is finalized (throws `OnityBindingException`).
`Dispose()` disposes owned singletons in reverse registration order.

### 2.7 Documented behaviors (test-locked) — DO / DON'T

| Behavior | DO / DON'T |
| --- | --- |
| Implicit transients | Unbound **concrete** classes auto-resolve as transients. DON'T rely on it for shared state — it is NOT a singleton. |
| Unbound abstractions | Unbound **interfaces/abstracts/open-generics** throw `OnityResolveException`. DO bind them. |
| Last-binding-wins | Re-binding the same contract REPLACES the previous binding (no duplicate-binding exception). DO use this to override; DON'T expect a conflict error. |
| Shared instance across contracts | DON'T expect two `Bind<IFoo>().To<C>()` + `Bind<IBar>().To<C>()` to share one instance — they produce **distinct** singletons. DO use `BindInterfacesAndSelfTo<C>().AsSingle()`. |
| Circular dependency | Constructor AND member cycles throw `OnityResolveException` at **resolve time** (not build time). DO break the cycle (e.g. inject a factory or `IResolver`). |
| Constructor selection | Greediest **public** ctor wins (or the single `[Inject]` ctor). DON'T add a second `[Inject]` ctor — throws. |
| Open generics | DON'T bind `typeof(Repository<>)`. Only **closed** generics resolve. Bind `Repository<int>` explicitly. |
| Collection injection | There is NO `IEnumerable<T>` multi-injection. DON'T inject `IEnumerable<IHandler>`. DO inject a single registry/factory, or resolve a known set. |
| Statics | `[Inject]` on a static member is silently ignored. DON'T use it. |
| Conditional / keyed binds | No `WhenInjectedInto`, no `WithId`. DON'T attempt them; use a typed factory or distinct contracts. |

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
- `ThrottleLast(TimeSpan interval, OnityTimeProvider = null)` — emit the latest value once per interval.
  **The operator is named `ThrottleLast`, NOT `Throttle`.** There is no leading-edge `Throttle`.
- `TakeUntil(CancellationToken)` / `TakeUntil(Task)` — stop on a signal.
- `SelectAwait(Func<T,CancellationToken,ValueTask<TResult>>)` / `WhereAwait(Func<T,CancellationToken,ValueTask<bool>>)`
  — sequential async projection/filter. **These resume on a threadpool thread** (`Task.Run` internally).
  DON'T touch `UnityEngine` APIs directly in a `Subscribe` after them (there is no main-thread post-back
  operator yet — keep async results pure, or marshal back yourself).

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
            .AddTo(this);
    }

    private void Fire() { }
}
```

---

## 4. Events — `Onity.Messaging` (+ `Onity.Unity.Messaging`)

Typed pub/sub. `MessageChannel<T>` is the same `SubscriptionEntry[]` design as `Subject<T>`: steady-state
`Publish` designed allocation-free, re-entrancy-safe (unsubscribe inside a handler is OK), throws after `Dispose`.

> Threading: publish/subscribe on the Unity **main thread**. Channels are not internally locked for
> publish (broker channel CREATION is locked). Messages run in subscription order.

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

### 4.3 `OnityEventHub` facade (auto-bound in every context)

```csharp
public sealed class OnityEventHub
{
    public void Publish<TMessage>(TMessage message);
    public IDisposable Subscribe<TMessage>(MessageHandler<TMessage> handler);
    public IOnityObservable<TMessage> Observe<TMessage>();      // cached per message type
}
```

### 4.4 Event recipes

```csharp
// Recipe A: inject the hub, publish a typed message. Define messages as small structs/classes.
using Onity.Unity.Messaging;            // OnityEventHub

public readonly struct PlayerDamaged
{
    public readonly int Amount;
    public PlayerDamaged(int amount) { Amount = amount; }
}

public sealed class CombatSystem
{
    private readonly OnityEventHub m_events;
    public CombatSystem(OnityEventHub events) { m_events = events; }   // auto-bound, no installer line needed
    public void ApplyHit(int amount) => m_events.Publish(new PlayerDamaged(amount));
}
```

```csharp
// Recipe B: subscribe with the disposable-token model; own lifetime via OnEnable/OnDisable.
using Onity.DI;                          // Inject
using Onity.Reactive;                    // CompositeDisposable
using Onity.Unity.Messaging;             // OnityEventHub
using Onity.Unity.Reactive;              // AddTo(CompositeDisposable)
using UnityEngine;

public sealed class HealthBar : MonoBehaviour
{
    [Inject] private OnityEventHub m_events;
    private readonly CompositeDisposable m_subscriptions = new CompositeDisposable();

    private void OnEnable()  => m_events.Subscribe<PlayerDamaged>(OnDamaged).AddTo(m_subscriptions);
    private void OnDisable() => m_subscriptions.Clear();
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
using Onity.Unity.Reactive;              // OnityUnityObservable, AddTo(Component)
using UnityEngine;

public sealed class HealthHud : MonoBehaviour
{
    [Inject] private OnityEventHub m_events;                  // auto-bound facade
    [Inject] private ReactiveProperty<int> m_health;          // shared state from the installer

    private void OnEnable()
    {
        m_health.Subscribe(value => Debug.Log($"Health: {value}")).AddTo(this);   // emits current value first

        m_events.Observe<PlayerDamaged>()
                .Where(evt => evt.IsCritical)
                .Subscribe(_ => Debug.Log("Critical hit!"))
                .AddTo(this);

        OnityUnityObservable.EveryUpdate()
                .Subscribe(_ => { /* per-frame HUD tween */ })
                .AddTo(this);
    }
}
```

Wire-up in the scene: add a context component (`ProjectContext` / `SceneContext` / `GameObjectContext`
from `Onity.Unity.Contexts`), assign `CombatInstaller` to its installer list, and put `HealthHud` under the
context root. The context creates the container, registers default bindings (container, `IResolver`, itself,
`MessageBroker`, `OnityEventHub`), runs installers, builds, and auto-injects the hierarchy.

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
- DO use a child container (`new OnityContainer(parent)`) for a "scoped" instance.
- DO pass an `OnityTimeProvider` (e.g. `OnityTimeProviders.UpdateUnscaled`) to `Debounce`/`ThrottleLast` in gameplay.
- DO write the operator name `ThrottleLast` (not `Throttle`).

DON'T:
- DON'T resolve before `Build()` is reasonable, but NEVER add bindings AFTER `Build()` (throws).
- DON'T `Resolve<T>()` inside `Update`/`FixedUpdate`/`LateUpdate` — resolve once in ctor/`Awake` and cache.
- DON'T `new` up services that have dependencies — bind them and let DI construct them.
- DON'T expect two separate `Bind<I>().To<C>()` calls to share one instance (they don't).
- DON'T inject `IEnumerable<T>` / `T[]` collections — collection injection is not supported.
- DON'T bind open generics `typeof(Foo<>)` — only closed generics resolve.
- DON'T add a second `[Inject]` constructor, a setterless `[Inject]` property, an `[Inject]` indexer, or a generic `[Inject]` method — each throws `OnityBindingException`.
- DON'T use `System.Linq` in Onity package code (write plain loops; Onity has no non-Unity third-party runtime dependencies); avoid LINQ/allocations in hot paths.
- DON'T touch `UnityEngine` members in a `Subscribe` directly after `SelectAwait`/`WhereAwait` (they resume off the main thread).
- DON'T publish/subscribe to a broker or `Subject<T>` from a background thread.
- DON'T call APIs that aren't in this guide assuming Zenject/R3/MessagePipe parity: no `Instantiate(args)`,
  no `WhenInjectedInto`/`WithId`. Reactive: `Merge`/`CombineLatest`/`Scan`/`Pairwise`/`Sample`/`Buffer`,
  leading-edge `Throttle` (+ `ThrottleLast`), and `ObserveOn`/`ObserveOnMainThread` ARE shipped; still missing —
  no `Window`/`Zip`/`Switch`/`Concat`, no `Publish`/`Share`/`RefCount`. Messaging: keyed + async channels ARE
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
  `Build()`, `BuildAsync(CancellationToken = default)`, `Dispose()`,
  `PushBindingSource(string)`, `TryGetBindingSource(...)`, `TryGetLocalBindingSource(...)`,
  `GetDiagnostics()`, `GetBindingDiagnostics(List<OnityBindingDiagnostics>)`,
  static `DiagnosticsCollectionEnabled`. Ctor: `new OnityContainer(OnityContainer parent = null)`.
- `IResolver` (`Resolve<T>`, `Resolve(Type)`, `TryResolve<T>`, `TryResolve(Type,...)`, `Inject`)
- `TypeBindingBuilder<TContract>` (`To<TConcrete>()`, `AsSingle()`, `AsTransient()`, `NonLazy()`)
- `MultiTypeBindingBuilder` (`AsSingle()`, `AsTransient()`, `NonLazy()`)
- `InjectAttribute` (`[Inject]`; targets Constructor | Field | Property | Method)
- `OnityResolveException`, `OnityBindingException`
- diagnostics structs: `OnityContainerDiagnostics`, `OnityBindingDiagnostics`, `OnityBindingSourceInfo`

### `Onity.Factory` (engine-free)
- `IFactory<TValue>` (`Create()`)
- `IFactory<TParam,TValue>` (`Create(param)`)
- `IFactory<TParam1,TParam2,TValue>` (`Create(p1,p2)`)

### `Onity.Reactive` (engine-free)
- `IOnityObservable<T>` (`Subscribe(Observer<T>)`, `Subscribe(OnityObserver<T>)`)
- `Observer<T>` (delegate `void(T)`), `OnityObserver<T>` (abstract lifecycle), `OnityResult` (struct)
- `Subject<T>` (`Subscribe`, `OnNext`, `Dispose`)
- `ReactiveProperty<T>` (`Value`, `SetValue(T)->bool`, `Subscribe(..., emitCurrentValue = true)`, `Dispose`)
- `IReadOnlyReactiveProperty<T>` (`Value`, `Subscribe(Observer<T>, emitCurrentValue = true)`)
- `CompositeDisposable` (`Add`, `Remove`, `Clear`, `Count`, `Dispose`)
- `OnityObservable<T>` (delegate-backed) + static `OnityObservable`: `FromEvent<T>`, `Return<T>`, `Empty<T>`
- `OnityObservableExtensions`: `Where`, `Select`, `DistinctUntilChanged`, `Skip`, `SkipWhile`, `Take`,
  `TakeWhile`, `StartWith`, `Subscribe(Action<T>)`, `Subscribe(onNext,onError,onCompleted)`,
  `TakeUntilCancellation`, `FirstAsync`, `ToTask`
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
- `MessageBrokerExtensions`: `Publish<T>(this IMessageBroker, T)`, `Subscribe<T>(this IMessageBroker, MessageHandler<T>)`
- `MessageChannelDiagnostics` (struct: `MessageType`, `SubscriberCount`)

### `Onity.Unity` (UnityEngine)
- Contexts (`Onity.Unity.Contexts`): `OnityContext` (abstract base), `ProjectContext`, `SceneContext`, `GameObjectContext`
- Installers (`Onity.Unity.Installers`): `MonoInstaller` (abstract; `InstallBindings(OnityContainer)`);
  extensions `BindScriptableObject<T>(asset)` / `BindScriptableObject<TContract,TAsset>(asset)`,
  `BindPooledFactory<TComponent>(prefab, ...)` / `BindPooledFactory<TValue>(IPool<TValue>)`, `BindUiResolverBridge()`
- Messaging (`Onity.Unity.Messaging`): `OnityEventHub` (`Publish<T>`, `Subscribe<T>`, `Observe<T>()`);
  `OnityMessageReactiveExtensions.Observe<T>()` (on `IMessageBroker` and `ISubscriber<T>`);
  `OnityMessageBindingExtensions.BindMessageChannel<T>(this OnityContainer)`
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

---

## 8. Error -> fix

What the runtime throws and how to fix it. Four dedicated exception types now ship,
each `sealed : Exception`: **`OnityResolveException`** (resolve/inject failures) and
**`OnityBindingException`** (binding/config failures) in `Onity.DI`;
**`OnityReactiveException`** in `Onity.Reactive`; and **`OnityMessagingException`** in
`Onity.Messaging`. Reactive also exposes a settable `OnityObservableExceptionHandler`
hook: `Subject<T>.OnNext` catches a throwing subscriber, routes it to the handler, and
keeps notifying the rest (one bad observer never breaks a frame).

### 8.1 `OnityResolveException` (DI resolve / inject)

| Message contains | Cause | Fix |
| --- | --- | --- |
| `No binding registered for '<T>' and it cannot be auto-resolved...` (built by `BuildUnresolvableMessage`) | Resolving an unbound interface/abstract/open-generic, or a concrete with an unresolvable dependency. | `Bind<T>().To<Impl>().AsSingle()` (or `.AsTransient()`). Abstractions never auto-resolve; only unbound **concrete** classes auto-resolve as transients. |
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
call). There is no keyed/buffered/request-response messaging — if you find
yourself wanting "the last message for a late subscriber", that is a
`ReactiveProperty<T>`.
