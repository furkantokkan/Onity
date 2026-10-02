---
title: "Events and Messaging"
parent: "Guides"
nav_order: 4
description: "Typed pub/sub in Onity: OnityEvent, OnityEventHub, the message broker, keyed and async channels, messages as reactive streams, and OnityTask receives with backpressure."
---

# Events and Messaging

Use messages to tell the rest of the game that something happened without the
sender and the listeners knowing each other: a `DamageZone` publishes
`PlayerDamaged`, a service lowers the health, a HUD flashes. Messaging is typed
pub/sub over the scope's broker; Unity code publishes through `OnityEvent`
(`Onity.Unity`), services inject `OnityEventHub` or `IMessageBroker`, and every
message type can also be read as a reactive stream. Reach for a message when
listeners only care about future occurrences; reach for a `ReactiveProperty<T>`
when a new listener must know the current value.

```csharp
using Onity.Unity;              // OnityEvent
using UnityEngine;

public readonly struct PlayerDamaged
{
    public readonly int Amount;

    public PlayerDamaged(int amount)
    {
        Amount = amount;
    }
}

public sealed class DamageZone : MonoBehaviour
{
    [SerializeField] private int m_damage = 25;

    private void OnTriggerEnter(Collider other)
    {
        OnityEvent.Publish(this, new PlayerDamaged(m_damage));   // the nearest context's channel
    }
}
```

Contents: [Messages in Onity](#messages-in-onity),
[Publish and subscribe from Unity code](#publish-and-subscribe-from-unity-code),
[Publish and subscribe from services](#publish-and-subscribe-from-services),
[Inject only a publisher or subscriber](#inject-only-a-publisher-or-subscriber),
[Filter an event like a stream](#filter-an-event-like-a-stream), [Keyed channels](#keyed-channels),
[Async channels](#async-channels),
[Native async consumption (OnityTask)](#native-async-consumption-onitytask),
[Migrating from MessagePipe](#migrating-from-messagepipe), [Threading and ordering](#threading-and-ordering),
[Error handling](#error-handling).

## Messages in Onity

A `MessageChannel<T>` (`Onity.Messaging`) delivers one message type to its
subscribers: `Publish` allocates nothing in steady state, a handler may
unsubscribe from inside a publish pass, and the channel throws after
`Dispose()`. The broker (`IMessageBroker`, implemented by `MessageBroker`) owns
one channel per message type, and `OnityEventHub` (`Onity.Unity.Messaging`)
wraps the broker with `Publish`, `Subscribe` and `Observe`. Every
`ProjectContext`, `SceneContext` and `GameObjectContext` binds the broker and
the hub, so publishing needs no installer line. The core is engine-free; the
hub, the reactive and DI bridges live in `Onity.Unity.Messaging`, and the
`OnityTask` receives in `Onity.Unity.Async`.

A message is a small `readonly struct` or a class. Prefer one type per meaning
(`PlayerDamaged`, `PlayerDied`, `WaveStarted`) over a shared payload with a
kind field; the message type is the first level of filtering.

Three ways for one part of the game to tell another something. Pick by the
shape of the information:

| Use | When | Why |
| --- | --- | --- |
| **Message** (`OnityEvent`, `OnityEventHub`, `IPublisher<T>` + `ISubscriber<T>`) | A past-tense notification with zero or more decoupled listeners that only care about future occurrences: `PlayerDamaged`, `PlayerDied`, `WaveStarted`. | Sender and receivers never reference each other. A late subscriber misses earlier messages by design; there is no replay or buffer. |
| **`ReactiveProperty<T>`** (shared through DI) | Current state a new listener must know at once: health, score, current wave, connection status. | Subscribing emits the current value first, then every real change. It is the only "replay the current value" primitive. |
| **Direct service call** (a constructor-injected interface) | A command or query with exactly one owner, a return value, or a synchronous result. | A message cannot return a value or guarantee a single handler. |

1. Need a return value, or must exactly one thing handle it? Call the service.
2. Is it the current value of some state a fresh subscriber must see now? Use a `ReactiveProperty<T>`.
3. Otherwise publish a message.

Buffered or replayed messages, handler priority and request-response are not
shipped. "The last message for a late subscriber" is a `ReactiveProperty<T>`.

## Publish and subscribe from Unity code

`OnityEvent` (`Onity.Unity`) is the static shorthand for `MonoBehaviour` code:

| Member | Context it uses |
| --- | --- |
| `OnityEvent.Publish(message)`, `Subscribe<T>(handler)`, `Observe<T>()` | The default context: the most recently activated `SceneContext`, then `ProjectContext.Instance`, then any other active context. |
| `OnityEvent.Publish(this, message)`, `Subscribe<T>(this, handler)`, `Observe<T>(this)` | The context on the owner component or its nearest parent, with the default context as fallback. Use these inside a `GameObjectContext`. |

Without any active context the members throw `InvalidOperationException`. A
`ScriptableObject` or any plain object can call `OnityEvent.Publish(message)`
too; it has no transform, so it always uses the default context. `DamageZone`
above publishes through its nearest context.

`OnityEvent.Subscribe(this, handler)` also disposes the subscription when the
component is destroyed. Subscribe in `OnEnable` and dispose in `OnDisable` so a
re-enabled component does not subscribe twice. Fragment, in a `MonoBehaviour`:

```csharp
private IDisposable m_subscription;

private void OnEnable()
{
    m_subscription = OnityEvent.Subscribe<PlayerDamaged>(this, OnDamaged);   // also disposed on destroy
}

private void OnDisable()
{
    m_subscription?.Dispose();
    m_subscription = null;
}
```

With several subscriptions, collect them in a `CompositeDisposable`
(`Onity.Reactive`) and `Clear()` it in `OnDisable`, or observe the message as a
stream and end each subscription with `TakeUntilDisable(this)`, as `HealthHud`
does in [Filter an event like a stream](#filter-an-event-like-a-stream).

## Publish and subscribe from services

A service injects `OnityEventHub` (or `IMessageBroker`) and ties each
subscription to the scope that owns the service with `AddTo(scope)`
(`Onity.DI`), where `scope` is an injected `IOnityScopeLifetime`:

```csharp
using Onity.DI;                 // IOnityScopeLifetime, AddTo(scope)
using Onity.Unity.Messaging;    // OnityEventHub

public readonly struct PlayerDied { }

public readonly struct WaveStarted
{
    public readonly int Index;

    public WaveStarted(int index)
    {
        Index = index;
    }
}

public sealed class WaveDirector
{
    private readonly OnityEventHub m_events;
    private int m_wave;
    private bool m_isStopped;

    public WaveDirector(OnityEventHub events, IOnityScopeLifetime scope)
    {
        m_events = events;
        events.Subscribe<PlayerDied>(OnPlayerDied).AddTo(scope);   // unsubscribed when the scope ends
    }

    public void StartNextWave()
    {
        if (m_isStopped)
        {
            return;
        }

        m_events.Publish(new WaveStarted(++m_wave));
    }

    private void OnPlayerDied(PlayerDied message)
    {
        m_isStopped = true;
    }
}
```

`IMessageBroker` offers the same `Publish<T>` and `Subscribe<T>` as extension
methods (`MessageBrokerExtensions`, `Onity.Messaging`) over `GetPublisher<T>()`
and `GetSubscriber<T>()`; inject it when one service handles several message
types and does not want a parameter per type. The concrete `MessageBroker`
also reports `ChannelCount` and fills a caller-supplied list in
`GetDiagnostics(List<MessageChannelDiagnostics>)` without allocating beyond
that list.

The `ONITY003` analyzer flags a `Subscribe` whose `IDisposable` is dropped.

## Inject only a publisher or subscriber

The broker is bound automatically, but the typed `IPublisher<T>` and
`ISubscriber<T>` are not resolvable per message type until you register them.
`BindMessageChannel<T>()` (`Onity.Unity.Messaging`) binds the scope broker's
channel for `T` under both interfaces, so a constructor can take one direction
only and `OnityEvent.Publish` still reaches it. Fragment of the canonical
`GameInstaller`:

```csharp
// IPublisher<PlayerDamaged> and ISubscriber<PlayerDamaged> on the scope's message broker,
// the same channel OnityEvent and OnityEventHub use.
container.BindMessageChannel<PlayerDamaged>();

// The container constructs it, runs Initialize() at Build() and disposes it with the scope.
container.BindInterfacesAndSelfTo<HealthService>().AsSingle();
```

`HealthService` takes the subscriber side. This is the canonical service with
the regeneration loop left out:

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

A service that only publishes takes `IPublisher<PlayerDamaged> publisher` and
calls `publisher.Publish(new PlayerDamaged(amount))`.

`container.DeclareMessage<T>()` (`Onity.Composition`) also binds
`IPublisher<T>`, `ISubscriber<T>` and `MessageChannel<T>`, but it creates its
own channel that the broker does not know about: `OnityEvent` and
`OnityEventHub` do not reach its subscribers. Use it for a channel that stays
private to the services that inject it, or as the engine-free stand-in for the
broker in a test, and `BindMessageChannel<T>()` for a channel shared with the
rest of the scope.

## Filter an event like a stream

`OnityEvent.Observe<T>()`, `OnityEventHub.Observe<T>()`, `broker.Observe<T>()`
and `subscriber.Observe<T>()` return `IOnityObservable<T>`, the same contract a
`ReactiveProperty<T>` implements, so a message flows straight into the operator
chain without an adapter. The hub caches one stream per message type. This is
the canonical `HealthHud`'s subscription part:

```csharp
using Onity.DI;                 // Inject
using Onity.Reactive;           // IReadOnlyReactiveProperty<T>, Where
using Onity.Unity;              // OnityEvent
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

        OnityEvent.Observe<PlayerDamaged>(this)                       // the channel HealthService subscribes to
                  .Where(message => message.Amount >= 10)
                  .Subscribe(ShowHit)
                  .TakeUntilDisable(this);                            // disposed on disable
    }

    private void ShowHit(PlayerDamaged message)
    {
        // Spawn a pooled HitMarker; Getting Started step 6 shows that part.
    }
}
```

Filtering has three levels, and the channel itself runs no filter pipeline on
publish:

| Level | Mechanism | Example |
| --- | --- | --- |
| Message type | Separate types for separate meanings. | `Subscribe<PlayerDamaged>` never receives `PlayerDied`. |
| Operator | A per-listener rule in the stream. | `Observe<PlayerDamaged>().Where(m => m.Amount >= 10)`. |
| Key | The same type routed by key. | `KeyedMessageChannel<int, WaveStarted>` keyed by arena. |

The standard flow for UI state is: gameplay publishes `PlayerDamaged`,
`HealthService` folds the stream into a `ReactiveProperty<int>`, and views
subscribe to the read-only property and get the current value first; the
service is written out in
[Reactive](reactive.html#event-stream-updates-reactive-state). For CPU-heavy
handlers, `SelectOnThreadPool` and `ObserveOnMainThread` apply to a message
stream exactly as described in [Reactive](reactive.html#thread-pool-work).

## Keyed channels

`KeyedMessageChannel<TKey, TMessage>` (`Onity.Messaging`; `IKeyedPublisher<TKey, TMessage>`,
`IKeyedSubscriber<TKey, TMessage>`) delivers a message only to the subscribers
of its key. Each key owns an inner `MessageChannel<TMessage>`, created on the
first subscription for that key. The broker does not create keyed channels;
construct one and bind it with `BindInstance`.

```csharp
using System;
using Onity.Messaging;          // KeyedMessageChannel<TKey, TMessage>
using UnityEngine;

using KeyedMessageChannel<int, WaveStarted> waves = new KeyedMessageChannel<int, WaveStarted>();

IDisposable token = waves.Subscribe(1, message => Debug.Log(message.Index));   // arena 1 only
waves.Publish(1, new WaveStarted(1));                                          // delivered
waves.Publish(2, new WaveStarted(1));                                          // no subscriber: nothing happens

int keys = waves.KeyCount;
int arenaOneSubscribers = waves.GetSubscriberCount(1);
```

## Async channels

`AsyncMessageChannel<TMessage>` (`Onity.Messaging`; `IAsyncPublisher<TMessage>`,
`IAsyncSubscriber<TMessage>`) delivers sequentially: `PublishAsync` awaits each
handler before calling the next and checks the token before each one. A pass
delivers to the handlers registered when it started, so a handler disposed
during the pass still receives that message and one added during the pass
first receives the next. `PublishAsync` never throws synchronously: a disposed
channel, a canceled token or a throwing handler completes the returned
`ValueTask` as an `async` method would, canceled for
`OperationCanceledException` and faulted otherwise.

Handlers run inline on the caller's stack until the first pending one; from
there the rest of the pass runs in an awaiting continuation. A handler that is
not itself an `async` method therefore runs its synchronous work in the
caller's context, as a handler of the synchronous `Publish` does: an
`AsyncLocal<T>` value or a `SynchronizationContext` it sets stays visible to
the caller after `PublishAsync` returns, whether it returned a completed
`ValueTask` or was the first to return a pending one. An `async` handler keeps
its own scope, so the caller does not see its change, and the caller does not
see the changes of the handlers that run inside the continuation either.

```csharp
using System;
using System.Threading;
using Onity.Messaging;          // AsyncMessageChannel<T>

using AsyncMessageChannel<PlayerDied> died = new AsyncMessageChannel<PlayerDied>();

IDisposable token = died.Subscribe(async (message, ct) =>
{
    await SaveRunAsync(ct);                                   // ValueTask; the publisher waits for it
});

await died.PublishAsync(new PlayerDied(), CancellationToken.None);   // returns after every handler
```

`container.DeclareAsyncMessage<T>()` (`Onity.Composition`) binds one channel
as `AsyncMessageChannel<T>`, `IAsyncPublisher<T>` and `IAsyncSubscriber<T>`
and disposes it with the scope. `PublishOnityTask(message, ct)` and
`SubscribeOnityTask(handler)` (`Onity.Unity.Async`) are the `OnityTask`-shaped
forms of `PublishAsync` and `Subscribe`.

## Native async consumption (OnityTask)

`Onity.Unity.Async` adds `OnityTask` consumers on top of the channels. A plain
async subscription is sequential and awaited, so one slow handler stalls every
publisher; these give you a one-shot receive, a buffered stream, and a queue
with real backpressure:

| Member | Use |
| --- | --- |
| `subscriber.ReceiveAsync(ct)` / `ReceiveAsync(predicate, ct)` | `OnityTask<T>`: the next (matching) message. Pooled, one shared subscription per subscriber; replaces `Observe().Where().FirstOnityTask()`. A canceled receive gets no later message. |
| `subscriber.ReceiveAllAsync(capacity, overflow)` | `IOnityAsyncEnumerable<T>` over a synchronous `ISubscriber<T>`. A synchronous publisher cannot wait, so `OnityBufferOverflow` decides what happens when the buffer is full: `Fault` (default: yield the accepted messages, then fault), `DropOldest`, or `DropNewest`. |
| `asyncSubscriber.SubscribeQueued(handler, capacity, lifetimeToken)` | A bounded queue between an `IAsyncSubscriber<T>` and your `OnityTask` handler. `PublishAsync` only waits while the queue is full; one consumer runs the handler for each message in order until `lifetimeToken` is canceled or the returned subscription is disposed. |

An assembly definition that uses these extensions needs references to
`Onity.Messaging` and `Onity.Reactive` as well as `Onity.Unity` (and `Onity.Core`
when it touches `Unit`); Unity assembly references are not transitive, and
without them the compiler reports CS0012 for the `Onity.Unity.Async` overloads.

```csharp
using System.Threading;
using Onity.DI;                 // IOnityInitializable, IOnityScopeLifetime
using Onity.Messaging;          // IAsyncSubscriber<T>
using Onity.Unity.Async;        // SubscribeQueued, OnityTask

public readonly struct SaveRequested { }

public sealed class SaveQueue : IOnityInitializable
{
    private readonly IAsyncSubscriber<SaveRequested> m_requests;
    private readonly IOnityScopeLifetime m_scope;

    public SaveQueue(IAsyncSubscriber<SaveRequested> requests, IOnityScopeLifetime scope)
    {
        m_requests = requests;
        m_scope = scope;
    }

    public void Initialize()
    {
        // Publishers return as soon as the request is queued and wait only while 8 are pending.
        // The scope token ends the subscription; keep the returned IDisposable to end it earlier.
        m_requests.SubscribeQueued(WriteAsync, 8, m_scope.Token);
    }

    private async OnityTask WriteAsync(SaveRequested request, CancellationToken ct)
    {
        await OnityTask.SwitchToThreadPool(ct);
        // Write the save file.
    }
}
```

- **Receive loops:** a receive that starts while the channel is publishing (for
  example in a continuation that the publication resumed) waits for the
  following message, so `while (...) await subscriber.ReceiveAsync(ct)` sees
  each message once.
- **Stopping a queued subscription:** disposing it, or canceling
  `lifetimeToken`, unsubscribes, cancels the running handler, discards queued
  messages, and releases publishers waiting for space without an exception
  (their messages are dropped). A publisher whose own token is canceled while it
  waits gets `OperationCanceledException`, and its message is not queued.
- **Handler faults:** a queued handler's exception is logged with
  `Debug.LogException`, and the next message is still handled.
- **Threading:** subscribe, publish and stop on the channel's thread (the main
  thread); tokens may be canceled from any thread. A handler that publishes to
  its own channel while the queue is full waits for itself.

## Migrating from MessagePipe

MessagePipe users register a broker and message types, inject `IPublisher<T>`
and `ISubscriber<T>`, and add filters or async handlers. Onity keeps the typed
vocabulary and moves the setup into the context.

### Setup

```csharp
// MessagePipe with a DI container:
builder.AddMessagePipe();
builder.RegisterMessageBroker<PlayerDamaged>(options);
```

```csharp
// Onity: IMessageBroker and OnityEventHub are bound by every context. Add this line only
// when a constructor injects IPublisher<T> or ISubscriber<T> directly.
container.BindMessageChannel<PlayerDamaged>();
```

### Publisher and subscriber

The interfaces have the same names and shapes (`Onity.Messaging`), so a
constructor that takes `IPublisher<PlayerDamaged>` or `ISubscriber<PlayerDamaged>`
compiles unchanged once `BindMessageChannel<PlayerDamaged>()` is in the
installer; `HealthService` above is that shape. When a class does not need
one direction only, inject the auto-bound `OnityEventHub` or `IMessageBroker`
instead and drop the registration line.

### Filters

MessagePipe filters map to the reactive bridge: the channel stays lean and the
per-consumer rule lives in the stream, as in `HealthHud` above
(`Observe<PlayerDamaged>(this).Where(message => message.Amount >= 10)`).
There is no filter pipeline on the publish path.

### Async and keyed channels

| MessagePipe | Onity |
| --- | --- |
| `IAsyncPublisher<T>` / `IAsyncSubscriber<T>` | `AsyncMessageChannel<T>` through `container.DeclareAsyncMessage<T>()`; `SubscribeQueued` when a slow handler must not stall publishers. Onity's `PublishAsync` is sequential only, like `AsyncPublishStrategy.Sequential`; there is no parallel strategy. It returns `ValueTask`, never throws synchronously, and runs handlers that complete synchronously inline, as MessagePipe's Unity build does. |
| Keyed `IPublisher<TKey, T>` / `ISubscriber<TKey, T>` | `KeyedMessageChannel<TKey, T>`, bound with `BindInstance`. |
| `GlobalMessagePipe` | `OnityEvent.Publish` / `Subscribe` / `Observe` on the active context. |
| Buffered or request-response brokers | Not shipped; current state is a `ReactiveProperty<T>`, a query is a service call. |

## Threading and ordering

Publish and subscribe on the Unity main thread. The broker locks its channel
table, never the publish path; a channel's `Publish` is not locked. Handlers run in
subscription order until a subscriber leaves outside a publish pass, when the
last subscriber moves into the freed slot, so do not treat the order as a
priority contract. Unsubscribing from inside a handler is safe: the handler is
skipped for the rest of the pass and removed afterwards. An
`AsyncMessageChannel<T>` pass instead keeps its start set (see
[Async channels](#async-channels)).

## Error handling

- A handler that throws propagates the exception to the `Publish` call (or
  faults the `PublishAsync` task), and the remaining handlers for that message
  do not run. This also applies to a stream from `Observe<T>()`, whose
  observers run inside the channel's handler. Catch inside the handler when one
  listener must not break the others.
- `ObjectDisposedException`: `Publish` or `Subscribe` after `Dispose()`, or a
  broker member after the broker was disposed; `PublishAsync` after `Dispose()`
  returns a task faulted with it instead of throwing. Tie subscriptions to a
  lifetime with `AddTo` or `TakeUntilDisable`.
- `ArgumentNullException`: a null handler, key, broker or owner component.
- `InvalidOperationException` from `OnityEvent`: no active context could be
  resolved.
- `OperationCanceledException` from `PublishAsync`: the token was canceled
  before a handler, and the returned task is canceled; normal cancellation,
  not a failure.
- `OnityMessagingException` is declared for messaging-core failures; the
  shipped channels throw the standard exceptions above.

## See also

- [Messaging API](../reference/messaging-api.html): every signature.
- [Reactive](reactive.html): the operator chain `Observe<T>()` feeds, and `ReactiveProperty<T>` for current state.
- [Dependency Injection](dependency-injection.html#shared-reactive-and-messaging-primitives): `DeclareMessage` and `DeclareAsyncMessage`.
- [Lifecycle and Scopes](lifecycle-and-scopes.html): what each context binds, and the scope token.
- [Messaging vs MessagePipe](../comparisons/messaging-vs-messagepipe.html): the feature comparison and the measured results with their conditions.
- [Async with OnityTask](onitytask.html#reactive-and-messaging-bridges): the task-side bridges.
- [Reactive vs R3 and UniRx](../comparisons/reactive-vs-r3-unirx.html): the measured comparison, and the [Comparisons](../comparisons/index.html) hub.
