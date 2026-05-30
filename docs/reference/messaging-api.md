---
title: "Messaging API"
parent: "Reference"
nav_order: 3
---

# Messaging API Reference

A complete catalog of Onity's typed pub/sub surface. The core (`Onity.Messaging`) is engine-free; the facade and reactive/DI bridges live in `Onity.Unity.Messaging`. A `MessageChannel<T>` uses the same `SubscriptionEntry[]` design as `Subject<T>`: steady-state `Publish` is designed to avoid per-call managed allocation, unsubscribing from inside a handler is safe (swap-back removal deferred until the publish pass ends), and `Publish` / `Subscribe` after `Dispose()` throw `ObjectDisposedException`.

```csharp
using Onity.Messaging;

using MessageBroker broker = new MessageBroker();
IDisposable token = broker.Subscribe<PlayerDamaged>(d => { /* handle */ });
broker.Publish(new PlayerDamaged(10));
token.Dispose();
```

Threading: publish and subscribe on the Unity **main thread**. Channel *creation* in the broker is locked; per-channel publish is not internally locked. Handlers run in subscription order.

`MessageBroker` (and thus `IPublisher<T>` / `ISubscriber<T>` via `GetPublisher` / `GetSubscriber`) and `OnityEventHub` are auto-bound in every `OnityContext`, so a service can inject `OnityEventHub` or `IMessageBroker` with no installer line. `BindMessageChannel<T>()` is only needed to inject the typed `IPublisher<T>` / `ISubscriber<T>` directly.

> Onity depends on ZLinq for the `Onity.Unity` layer; the messaging core does not use `System.Linq`.

---

## Broker and typed channels (`Onity.Messaging`)

| API | Signature | Notes |
| --- | --- | --- |
| `IMessageBroker.GetPublisher<TMessage>` | `GetPublisher<TMessage>() -> IPublisher<TMessage>` | Returns the typed channel for the message type (created on first request). |
| `IMessageBroker.GetSubscriber<TMessage>` | `GetSubscriber<TMessage>() -> ISubscriber<TMessage>` | Same channel instance as the matching publisher. |
| `IPublisher<TMessage>.Publish` | `Publish(TMessage message) -> void` | Deliver `message` to all subscribers of the channel, in subscription order. |
| `ISubscriber<TMessage>.Subscribe` | `Subscribe(MessageHandler<TMessage> handler) -> IDisposable` | Register `handler`. Dispose the token to unsubscribe. Null handler throws `ArgumentNullException`. |
| `MessageHandler<TMessage>` | `delegate void MessageHandler<TMessage>(TMessage message)` | The subscription callback signature. |

### Broker-level convenience (`MessageBrokerExtensions`)

Skip the explicit `GetPublisher` / `GetSubscriber` step.

| API | Signature | Notes |
| --- | --- | --- |
| `Publish<TMessage>` | `Publish<TMessage>(this IMessageBroker broker, TMessage message) -> void` | Resolves the publisher and publishes in one call. Null broker throws. |
| `Subscribe<TMessage>` | `Subscribe<TMessage>(this IMessageBroker broker, MessageHandler<TMessage> handler) -> IDisposable` | Resolves the subscriber and subscribes in one call. Null broker/handler throws. |

### `MessageBroker` (default `IMessageBroker`)

`sealed class MessageBroker : IMessageBroker, IDisposable`. Constructor: `new MessageBroker()`.

| API | Signature | Notes |
| --- | --- | --- |
| `ChannelCount` | `int { get; }` | Number of created channels. Throws `ObjectDisposedException` after dispose. |
| `GetDiagnostics` | `GetDiagnostics(List<MessageChannelDiagnostics> results) -> void` | Fill a caller-supplied list (cleared first) with one entry per channel. Null list throws `ArgumentNullException`. |
| `Dispose` | `Dispose() -> void` | Dispose every owned channel and clear the map. |

### `MessageChannel<TMessage>`

`sealed class MessageChannel<TMessage> : IPublisher<TMessage>, ISubscriber<TMessage>, IDisposable`. The concrete channel type; you rarely construct it directly (the broker owns it), but it is public for tests and diagnostics.

| API | Signature | Notes |
| --- | --- | --- |
| `Publish` | `Publish(TMessage message) -> void` | See `IPublisher<TMessage>`. |
| `Subscribe` | `Subscribe(MessageHandler<TMessage> handler) -> IDisposable` | See `ISubscriber<TMessage>`. |
| `SubscriberCount` | `int { get; }` | Active subscriber count (diagnostics). |
| `Dispose` | `Dispose() -> void` | After dispose, `Publish` / `Subscribe` throw `ObjectDisposedException`. |

### `MessageChannelDiagnostics`

`readonly struct` returned in the `GetDiagnostics` list.

| Member | Type | Description |
| --- | --- | --- |
| `MessageType` | `Type` | The message payload type used as the channel key. |
| `SubscriberCount` | `int` | Active subscriber count for that channel. |

---

## Keyed channels — per-key routing (`Onity.Messaging`)

Route a published message only to subscribers registered for a matching key. Each key owns an inner `MessageChannel<TMessage>`.

| API | Signature | Notes |
| --- | --- | --- |
| `IKeyedPublisher<TKey, TMessage>.Publish` | `Publish(TKey key, TMessage message) -> void` | Deliver to subscribers of `key` only. A key with no subscribers is a no-op. |
| `IKeyedSubscriber<TKey, TMessage>.Subscribe` | `Subscribe(TKey key, MessageHandler<TMessage> handler) -> IDisposable` | Subscribe to one key. Null key/handler throws `ArgumentNullException`. |
| `KeyedMessageChannel<TKey, TMessage>` | `new KeyedMessageChannel<TKey, TMessage>()` | Concrete type implementing both interfaces and `IDisposable`. |
| `KeyedMessageChannel.KeyCount` | `int { get; }` | Number of keys that currently own a subscriber channel. |
| `KeyedMessageChannel.GetSubscriberCount` | `GetSubscriberCount(TKey key) -> int` | Subscriber count for one key (zero if unknown). Null key throws. |
| `KeyedMessageChannel.Dispose` | `Dispose() -> void` | Dispose every per-key channel. |

---

## Async channels — awaitable delivery (`Onity.Messaging`)

Sequential awaitable handlers: `PublishAsync` awaits each handler before invoking the next, iterating over a pooled snapshot so subscribe/unsubscribe from inside a handler cannot corrupt the in-flight pass.

| API | Signature | Notes |
| --- | --- | --- |
| `IAsyncPublisher<TMessage>.PublishAsync` | `PublishAsync(TMessage message, CancellationToken ct) -> ValueTask` | Deliver to all async subscribers sequentially; awaits delivery. Honors `ct` between handlers. |
| `IAsyncSubscriber<TMessage>.Subscribe` | `Subscribe(Func<TMessage, CancellationToken, ValueTask> handler) -> IDisposable` | Register an awaitable handler. Null handler throws. |
| `AsyncMessageChannel<TMessage>` | `new AsyncMessageChannel<TMessage>()` | Concrete type implementing both interfaces and `IDisposable`. |
| `AsyncMessageChannel.SubscriberCount` | `int { get; }` | Active subscriber count (diagnostics). |
| `AsyncMessageChannel.Dispose` | `Dispose() -> void` | After dispose, `PublishAsync` / `Subscribe` throw `ObjectDisposedException`. |

---

## Facade and bridges (`Onity.Unity.Messaging`)

### `OnityEventHub`

`sealed class OnityEventHub`. Auto-bound in every `OnityContext` over the scoped broker; inject it with no installer line. Constructor: `new OnityEventHub(IMessageBroker broker)`.

| API | Signature | Notes |
| --- | --- | --- |
| `Publish<TMessage>` | `Publish<TMessage>(TMessage message) -> void` | Publish into the current scope (delegates to the broker). |
| `Subscribe<TMessage>` | `Subscribe<TMessage>(MessageHandler<TMessage> handler) -> IDisposable` | Subscribe in the current scope (delegates to the broker). |
| `Observe<TMessage>` | `Observe<TMessage>() -> IOnityObservable<TMessage>` | Observe the message type as a reactive stream. **Caches one stream instance per message type.** |

### Reactive bridge (`OnityMessageReactiveExtensions`)

Turn messages into the full reactive operator chain. The returned `IOnityObservable<T>` flows into `Where` / `Select` / `Subscribe` etc. (see [Reactive Operators](reactive-operators.md)).

| API | Signature | Notes |
| --- | --- | --- |
| `Observe<TMessage>` (broker) | `Observe<TMessage>(this IMessageBroker broker) -> IOnityObservable<TMessage>` | Observe a broker channel as a stream. Null broker throws. |
| `Observe<TMessage>` (subscriber) | `Observe<TMessage>(this ISubscriber<TMessage> subscriber) -> IOnityObservable<TMessage>` | Observe a typed subscriber as a stream. Null subscriber throws. |

### DI binding helper (`OnityMessageBindingExtensions`)

| API | Signature | Notes |
| --- | --- | --- |
| `BindMessageChannel<TMessage>` | `BindMessageChannel<TMessage>(this OnityContainer container) -> void` | Bind `IPublisher<TMessage>` and `ISubscriber<TMessage>` instances (sourced from the scope's `IMessageBroker`) so services can constructor-inject them directly. |

```csharp
using Onity.Unity.Messaging;     // Observe<T>, OnityEventHub
using Onity.Reactive;            // Where, Select, Subscribe
using Onity.Unity.Reactive;      // AddTo

broker.Observe<DamageEvent>()
      .Where(e => e.Amount > 0)
      .Select(e => e.Amount)
      .Subscribe(amount => Debug.Log($"Took {amount}"))
      .AddTo(this);
```

---

## Exceptions

| Type | Notes |
| --- | --- |
| `OnityMessagingException` | `sealed : Exception`, in `Onity.Messaging`, for messaging-layer failures. |
| `ObjectDisposedException` | `Publish` / `Subscribe` / `PublishAsync` after `Dispose()`. Tie subscriptions to lifetime with `AddTo(this)` / `AddTo(bag)`. |
| `ArgumentNullException` | Null broker, handler, or key passed to a broker/channel API. |
| `OperationCanceledException` | The `CancellationToken` passed to `PublishAsync` cancelled delivery. Normal cancellation, not a bug. |

---

## Choosing a channel type

| Use | When |
| --- | --- |
| `IPublisher<T>` / `ISubscriber<T>` (or `OnityEventHub`) | Standard transient, fire-and-forget notification with 0..N decoupled listeners. Late subscribers miss past messages by design (no replay). |
| `IKeyedPublisher` / `IKeyedSubscriber` | The same message type but only listeners registered for a specific key should receive it. |
| `IAsyncPublisher` / `IAsyncSubscriber` | The publisher must await delivery (sequential async handlers). |
| `ReactiveProperty<T>` (in DI) | **Current state** a new listener must immediately know (health, score, connection status). Subscribing emits the current value first. This is the only "replay current value" primitive. |
| Direct service call (injected interface) | A command/query with exactly one owner where you need a return value, ordering, or a synchronous result. |

> Intentionally **not shipped** (non-goals): buffered/replay events, handler priority, request-response. Model "the last message a late subscriber needs" as a `ReactiveProperty<T>`. See the [Events & Messaging guide](../guide/events-messaging.md) for the full decision rule and recipes.
