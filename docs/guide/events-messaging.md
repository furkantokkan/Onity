---
title: "Events & Messaging"
parent: "Guides"
nav_order: 3
---

# Events & Messaging

Onity's messaging is typed pub/sub. `MessageChannel<T>` uses the same array-backed, re-entrancy-safe design as `Subject<T>`: allocation-free steady-state `Publish`, safe to unsubscribe from inside a handler, and it throws after `Dispose`. The core (`Onity.Messaging`) is engine-free; `OnityEventHub` and the reactive bridge live in `Onity.Unity.Messaging`.

The broker (`IMessageBroker` / `MessageBroker`) and the `OnityEventHub` facade are **auto-bound in every context**, so a service can publish or subscribe with no installer line.

```csharp
using Onity.Unity.Messaging;   // OnityEventHub

public sealed class CombatSystem
{
    private readonly OnityEventHub m_events;
    public CombatSystem(OnityEventHub events) { m_events = events; }   // auto-bound, no setup

    public void ApplyHit(int amount) => m_events.Publish(new PlayerDamaged(amount));
}
```

> Threading: publish and subscribe on the Unity **main thread**. Channels are not internally locked for publish (only broker channel *creation* is locked). Handlers run in subscription order.

## Surface

```csharp
using Onity.Messaging;

// IMessageBroker is the source of typed channels.
IPublisher<DamageEvent> pub = broker.GetPublisher<DamageEvent>();
ISubscriber<DamageEvent> sub = broker.GetSubscriber<DamageEvent>();

pub.Publish(new DamageEvent(10));
IDisposable token = sub.Subscribe(e => Debug.Log(e.Amount));

// Broker-level convenience (no manual GetPublisher / GetSubscriber):
broker.Publish(new DamageEvent(10));
IDisposable token2 = broker.Subscribe<DamageEvent>(e => Debug.Log(e.Amount));

// Diagnostics into a caller-supplied list (no allocation):
List<MessageChannelDiagnostics> diag = new List<MessageChannelDiagnostics>(8);
broker.GetDiagnostics(diag);    // each entry: MessageType + SubscriberCount
int channels = broker.ChannelCount;
```

`MessageHandler<TMessage>` is `delegate void MessageHandler<TMessage>(TMessage message)`. Define messages as small structs or classes:

```csharp
public readonly struct PlayerDamaged
{
    public readonly int Amount;
    public PlayerDamaged(int amount) { Amount = amount; }
}
```

## Recipe - inject the message broker

Use `IMessageBroker` when one service owns several message types and you do not
want separate typed publisher/subscriber constructor parameters. Every
`OnityContext` binds the broker automatically.

```csharp
using System;
using Onity.Messaging;

public readonly struct PlayerDamaged
{
    public readonly int Amount;
    public PlayerDamaged(int amount) { Amount = amount; }
}

public readonly struct PlayerHealed
{
    public readonly int Amount;
    public PlayerHealed(int amount) { Amount = amount; }
}

public sealed class CombatEvents : IDisposable
{
    private readonly IMessageBroker m_broker;
    private readonly IDisposable m_damaged;
    private readonly IDisposable m_healed;

    public CombatEvents(IMessageBroker broker)
    {
        m_broker = broker;                         // auto-bound by the context
        m_damaged = broker.Subscribe<PlayerDamaged>(OnDamaged);
        m_healed = broker.Subscribe<PlayerHealed>(OnHealed);
    }

    public void ReportDamage(int amount)
    {
        m_broker.Publish(new PlayerDamaged(amount));
    }

    public void Dispose()
    {
        m_damaged.Dispose();
        m_healed.Dispose();
    }

    private void OnDamaged(PlayerDamaged message) { /* update combat state */ }
    private void OnHealed(PlayerHealed message) { /* update combat state */ }
}
```

## The `OnityEventHub` facade

`OnityEventHub` wraps the scoped broker with a single publish/subscribe/observe surface and caches one reactive stream per message type.

```csharp
public sealed class OnityEventHub
{
    public void Publish<TMessage>(TMessage message);
    public IDisposable Subscribe<TMessage>(MessageHandler<TMessage> handler);
    public IOnityObservable<TMessage> Observe<TMessage>();   // cached per message type
}
```

## Reactive bridge — `Observe<T>()`

`broker.Observe<T>()`, `subscriber.Observe<T>()`, and `OnityEventHub.Observe<T>()` all return `IOnityObservable<T>`, so events flow into the full operator chain — the same chain you use over a `ReactiveProperty<T>` (see [Reactive](reactive.html)).

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

## Injecting only a publisher or subscriber

The broker is auto-bound, but the typed `IPublisher<T>` / `ISubscriber<T>` are **not** auto-resolvable per message type. Register them with `BindMessageChannel<T>()` when a type should inject one direction only.

```csharp
using Onity.DI;
using Onity.Unity.Messaging;     // BindMessageChannel<T>

// In an installer:
container.BindMessageChannel<PlayerDamaged>();   // binds IPublisher<PlayerDamaged> + ISubscriber<PlayerDamaged>

// In a consumer:
public sealed class DamageNumbers
{
    private readonly ISubscriber<PlayerDamaged> m_damage;
    public DamageNumbers(ISubscriber<PlayerDamaged> damage) { m_damage = damage; }
    public IDisposable Listen() => m_damage.Subscribe(d => { /* spawn number */ });
}
```

## MessagePipe migration examples

MessagePipe users usually wire a broker, register message types, inject
`IPublisher<T>` / `ISubscriber<T>`, and optionally bridge into filters or async
handlers. Onity keeps the typed vocabulary but moves most setup into the scoped
context.

### Setup

```csharp
// MessagePipe + VContainer / Microsoft DI style setup:
builder.AddMessagePipe();
builder.RegisterMessageBroker<PlayerDamaged>(options);
```

```csharp
// Onity setup:
// IMessageBroker and OnityEventHub are auto-bound by ProjectContext,
// SceneContext, and GameObjectContext.

// Only add this when a service constructor injects IPublisher<T> or ISubscriber<T>
// directly. Broker and EventHub injection do not need it.
container.BindMessageChannel<PlayerDamaged>();
```

### Publisher and subscriber

```csharp
// MessagePipe shape:
public sealed class DamageSystem
{
    private readonly IPublisher<PlayerDamaged> m_publisher;
    public DamageSystem(IPublisher<PlayerDamaged> publisher) { m_publisher = publisher; }
    public void Hit(int amount) => m_publisher.Publish(new PlayerDamaged(amount));
}

public sealed class DamageHud
{
    private readonly ISubscriber<PlayerDamaged> m_subscriber;
    public DamageHud(ISubscriber<PlayerDamaged> subscriber) { m_subscriber = subscriber; }
    public IDisposable Start() => m_subscriber.Subscribe(OnDamaged);
    private void OnDamaged(PlayerDamaged message) { /* update UI */ }
}
```

```csharp
// Onity equivalent:
using Onity.DI;
using Onity.Messaging;
using Onity.Unity.Installers;
using Onity.Unity.Messaging;

public sealed class GameInstaller : MonoInstaller
{
    public override void InstallBindings(OnityContainer container)
    {
        container.BindMessageChannel<PlayerDamaged>();
        container.Bind<DamageSystem>().AsSingle();
        container.Bind<DamageHud>().AsSingle();
    }
}

public sealed class DamageSystem
{
    private readonly IPublisher<PlayerDamaged> m_publisher;
    public DamageSystem(IPublisher<PlayerDamaged> publisher) { m_publisher = publisher; }
    public void Hit(int amount) => m_publisher.Publish(new PlayerDamaged(amount));
}

public sealed class DamageHud
{
    private readonly ISubscriber<PlayerDamaged> m_subscriber;
    public DamageHud(ISubscriber<PlayerDamaged> subscriber) { m_subscriber = subscriber; }
    public IDisposable Start() => m_subscriber.Subscribe(OnDamaged);
    private void OnDamaged(PlayerDamaged message) { /* update UI */ }
}
```

When you do not need direction-only injection, use the auto-bound broker or hub:

```csharp
public sealed class DamageSystem
{
    private readonly OnityEventHub m_events;
    public DamageSystem(OnityEventHub events) { m_events = events; }
    public void Hit(int amount) => m_events.Publish(new PlayerDamaged(amount));
}
```

### Filters

MessagePipe filters map to the reactive bridge. The event channel stays lean;
the per-consumer rule lives in the observable chain.

```csharp
using Onity.Reactive;
using Onity.Unity.Messaging;
using Onity.Unity.Reactive;

m_events.Observe<PlayerDamaged>()
        .Where(message => message.Amount > 0)
        .Subscribe(OnRealDamage)
        .AddTo(m_subscriptions);
```

### Async and keyed channels

MessagePipe's async publisher/subscriber pattern maps to
`AsyncMessageChannel<T>`. Keyed MessagePipe channels map to
`KeyedMessageChannel<TKey,TMessage>`.

```csharp
using System.Threading;
using System.Threading.Tasks;
using Onity.Messaging;

AsyncMessageChannel<LevelLoaded> levelLoaded = new AsyncMessageChannel<LevelLoaded>();

levelLoaded.Subscribe(async (message, ct) =>
{
    await WarmupLevelAsync(message, ct);
});

await levelLoaded.PublishAsync(new LevelLoaded(/* ... */), CancellationToken.None);
```

Onity does not provide a global static `GlobalMessagePipe` equivalent. Scope the
broker through `ProjectContext`, `SceneContext`, or `GameObjectContext`, then
inject `OnityEventHub` where manager-style ergonomics are useful.

## Keyed channels

`KeyedMessageChannel<TKey,TMessage>` (`IKeyedPublisher` / `IKeyedSubscriber`) routes a published message to only the subscribers registered for its key. Each key owns an inner `MessageChannel<TMessage>`, reusing the allocation-free steady-state publish.

```csharp
using Onity.Messaging;

using KeyedMessageChannel<int, UnitSpawned> spawns = new KeyedMessageChannel<int, UnitSpawned>();

IDisposable token = spawns.Subscribe(teamId: 1, msg => Debug.Log(msg));   // only team 1
spawns.Publish(teamId: 1, new UnitSpawned(/* ... */));                    // delivered
spawns.Publish(teamId: 2, new UnitSpawned(/* ... */));                    // ignored by the team-1 subscriber

int keys = spawns.KeyCount;
int teamOneSubs = spawns.GetSubscriberCount(1);
```

## Async channels

`AsyncMessageChannel<TMessage>` (`IAsyncPublisher` / `IAsyncSubscriber`) awaits each handler before invoking the next. Delivery iterates over a pooled snapshot, so a subscribe or unsubscribe from inside a handler cannot corrupt the in-flight pass.

```csharp
using System.Threading;
using System.Threading.Tasks;
using Onity.Messaging;

using AsyncMessageChannel<LevelLoaded> levelLoaded = new AsyncMessageChannel<LevelLoaded>();

IDisposable token = levelLoaded.Subscribe(async (msg, ct) =>
{
    await PreloadAsync(msg, ct);
});

await levelLoaded.PublishAsync(new LevelLoaded(/* ... */), CancellationToken.None);   // awaits every handler in turn
```

A cancellation surfaces as `OperationCanceledException` and is checked before each handler.

## Message vs ReactiveProperty vs direct call

Three ways for one part of the game to tell another that something happened. Pick by the **shape of the information**, not by habit.

| Use | When | Why |
| --- | --- | --- |
| **Message** (`OnityEventHub` / `IPublisher<T>` + `ISubscriber<T>`) | A transient, fire-and-forget notification with 0..N decoupled listeners that only care about future occurrences (`PlayerDamaged`, `EnemyKilled`, `LevelLoaded`). | Sender and receivers never reference each other. A late subscriber misses past messages **by design** — there is no replay or buffer. |
| **`ReactiveProperty<T>`** (shared via DI) | Current state a new listener must immediately know (health, score, current wave, connection status). | Subscribing emits the **current value first**, then every real change. Built-in `DistinctUntilChanged`. This is the only "replay current value" primitive. |
| **Direct service call** (constructor-injected interface) | A command or query with exactly one owner where you need a return value, ordering, or a synchronous result (`damage.Calculate(...)`, `save.Write(...)`). | A message cannot return a value or guarantee a single handler. One caller, one callee, one result — call the method. |

Decision flow:

1. Need a return value, or must exactly one thing handle this? -> **Direct service call**. Stop.
2. Is this the current value of some state a fresh subscriber must see right now? -> **`ReactiveProperty<T>`**. Stop.
3. Otherwise (a past-tense notification, fan-out to unknown listeners, late subscribers may miss it) -> **Message**.

> Intentionally not shipped: buffered/replay events, handler priority, and request-response. If you want "the last message for a late subscriber", that is a `ReactiveProperty<T>`, not a buffered channel.

## Recipe — own a subscription's lifetime

Subscribe in `OnEnable`, clear the bag in `OnDisable`:

```csharp
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

## Error handling

`OnityMessagingException` is the dedicated messaging exception type. `ObjectDisposedException` indicates publish/subscribe after `Dispose()` (tie subscriptions to lifetime with `AddTo`); `ArgumentNullException` indicates a null handler or key.

## See also

- [Reactive](reactive.html) — the operator chain `Observe<T>()` feeds, and `ReactiveProperty<T>` for current state.
- [Dependency Injection](dependency-injection.html) — `BindMessageChannel<T>` and constructor-injected services.
- [Lifecycle & Scopes](lifecycle-and-scopes.html) — what each context auto-binds.
