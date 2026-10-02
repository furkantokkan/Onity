---
title: "Messaging API"
parent: "Reference"
nav_order: 3
description: "Every member of Onity's typed pub/sub surface: broker and channels, keyed and async channels, OnityEvent, OnityEventHub, the reactive and DI bridges, and the OnityTask receives."
---

# Messaging API

Use this catalog to look up the exact shape of a messaging member: the broker
and its typed channels, keyed and async channels, the `OnityEvent` facade,
`OnityEventHub`, the reactive and DI bridges, and the `OnityTask` consumers.
The [Events and Messaging guide](../guide/events-messaging.html) explains when
to use what.

The core (`Onity.Messaging`) is engine-free. `OnityEventHub`, the reactive
bridge and the DI binding helper live in `Onity.Unity.Messaging`, the static
`OnityEvent` facade in `Onity.Unity`, and the `OnityTask` consumers in
`Onity.Unity.Async`.

```csharp
using System;
using Onity.Messaging;          // MessageBroker, Publish, Subscribe

using MessageBroker broker = new MessageBroker();
IDisposable token = broker.Subscribe<PlayerDamaged>(message => { /* handle */ });
broker.Publish(new PlayerDamaged(10));
token.Dispose();
```

`MessageChannel<T>.Publish` allocates nothing in steady state, a handler may
unsubscribe from inside a publish pass, and `Publish` or `Subscribe` after
`Dispose()` throw `ObjectDisposedException`. Publish and subscribe on the Unity
main thread: the broker locks channel creation only. Handlers run in
subscription order until an unsubscribe outside a publish pass moves the last
handler into the freed slot, so the order is not a priority contract.

`MessageBroker` (with `IMessageBroker`) and `OnityEventHub` are bound by every
`OnityContext`, so a service injects either with no installer line.
`BindMessageChannel<T>()` is needed only to inject `IPublisher<T>` or
`ISubscriber<T>` directly.

---

## Broker and typed channels

| API | Signature | Namespace | Notes |
| --- | --- | --- | --- |
| `IMessageBroker.GetPublisher<TMessage>` | `GetPublisher<TMessage>() -> IPublisher<TMessage>` | `Onity.Messaging` | The channel for the message type, created on first request. |
| `IMessageBroker.GetSubscriber<TMessage>` | `GetSubscriber<TMessage>() -> ISubscriber<TMessage>` | `Onity.Messaging` | The same channel instance as the publisher. |
| `IPublisher<TMessage>.Publish` | `Publish(TMessage message) -> void` | `Onity.Messaging` | Delivers `message` to every current subscriber. A throwing handler propagates to this call; later handlers for that message do not run. |
| `ISubscriber<TMessage>.Subscribe` | `Subscribe(MessageHandler<TMessage> handler) -> IDisposable` | `Onity.Messaging` | Registers `handler`; dispose the token to unsubscribe. A null handler throws `ArgumentNullException`. |
| `MessageHandler<TMessage>` | `delegate void MessageHandler<TMessage>(TMessage message)` | `Onity.Messaging` | The handler signature. |

### Broker-level convenience

`MessageBrokerExtensions` skips the explicit `GetPublisher` / `GetSubscriber`
step.

| API | Signature | Namespace | Notes |
| --- | --- | --- | --- |
| `Publish<TMessage>` | `Publish<TMessage>(this IMessageBroker broker, TMessage message) -> void` | `Onity.Messaging` | `GetPublisher<TMessage>().Publish(message)`. A null broker throws. |
| `Subscribe<TMessage>` | `Subscribe<TMessage>(this IMessageBroker broker, MessageHandler<TMessage> handler) -> IDisposable` | `Onity.Messaging` | `GetSubscriber<TMessage>().Subscribe(handler)`. A null broker or handler throws. |

### Message broker

`sealed class MessageBroker : IMessageBroker, IDisposable`; `new MessageBroker()`.

| API | Signature | Namespace | Notes |
| --- | --- | --- | --- |
| `ChannelCount` | `int { get; }` | `Onity.Messaging` | Number of created channels. Throws `ObjectDisposedException` after dispose. |
| `GetDiagnostics` | `GetDiagnostics(List<MessageChannelDiagnostics> results) -> void` | `Onity.Messaging` | Clears `results`, then adds one entry per channel. A null list throws. |
| `Dispose` | `Dispose() -> void` | `Onity.Messaging` | Disposes every owned channel; `GetPublisher` / `GetSubscriber` throw afterwards. |

### Message channel

`sealed class MessageChannel<TMessage> : IPublisher<TMessage>, ISubscriber<TMessage>, IDisposable`.
The broker creates channels for you; construct one directly for a private
channel, a test, or through `DeclareMessage<T>()`.

| API | Signature | Namespace | Notes |
| --- | --- | --- | --- |
| `Publish` | `Publish(TMessage message) -> void` | `Onity.Messaging` | See `IPublisher<TMessage>`. |
| `Subscribe` | `Subscribe(MessageHandler<TMessage> handler) -> IDisposable` | `Onity.Messaging` | See `ISubscriber<TMessage>`. |
| `SubscriberCount` | `int { get; }` | `Onity.Messaging` | Active subscriber count. |
| `Dispose` | `Dispose() -> void` | `Onity.Messaging` | Afterwards `Publish` and `Subscribe` throw; disposing an old token is a no-op. |

### Channel diagnostics

`MessageChannelDiagnostics` (`Onity.Messaging`) is a `readonly struct` with
`Type MessageType` (the channel key) and `int SubscriberCount`.

---

## Keyed channels

`sealed class KeyedMessageChannel<TKey, TMessage> : IKeyedPublisher<TKey, TMessage>, IKeyedSubscriber<TKey, TMessage>, IDisposable`.
Each key owns an inner `MessageChannel<TMessage>`, created by the first
`Subscribe` for that key. The broker does not create keyed channels; construct
one and bind it with `BindInstance`.

| API | Signature | Namespace | Notes |
| --- | --- | --- | --- |
| `IKeyedPublisher<TKey, TMessage>.Publish` | `Publish(TKey key, TMessage message) -> void` | `Onity.Messaging` | Delivers to the subscribers of `key`; a key without subscribers is a no-op. A null key throws. |
| `IKeyedSubscriber<TKey, TMessage>.Subscribe` | `Subscribe(TKey key, MessageHandler<TMessage> handler) -> IDisposable` | `Onity.Messaging` | Subscribes to one key. A null key or handler throws. |
| `KeyCount` | `int { get; }` | `Onity.Messaging` | Keys that have a channel (a channel stays after its subscribers leave). |
| `GetSubscriberCount` | `GetSubscriberCount(TKey key) -> int` | `Onity.Messaging` | Subscribers of one key; `0` for an unknown key. A null key throws. |
| `Dispose` | `Dispose() -> void` | `Onity.Messaging` | Disposes every per-key channel; afterwards every member throws `ObjectDisposedException`. |

---

## Async channels

`sealed class AsyncMessageChannel<TMessage> : IAsyncPublisher<TMessage>, IAsyncSubscriber<TMessage>, IDisposable`.
`PublishAsync` awaits each handler before calling the next, over a pooled
snapshot, so a subscribe or unsubscribe inside a handler cannot corrupt the
pass.

| API | Signature | Namespace | Notes |
| --- | --- | --- | --- |
| `IAsyncPublisher<TMessage>.PublishAsync` | `PublishAsync(TMessage message, CancellationToken ct) -> ValueTask` | `Onity.Messaging` | Sequential, awaited delivery. `ct` is checked before each handler; cancellation surfaces as `OperationCanceledException`. A throwing handler faults the task. |
| `IAsyncSubscriber<TMessage>.Subscribe` | `Subscribe(Func<TMessage, CancellationToken, ValueTask> handler) -> IDisposable` | `Onity.Messaging` | Registers an awaitable handler. A null handler throws. |
| `SubscriberCount` | `int { get; }` | `Onity.Messaging` | Active subscriber count. |
| `Dispose` | `Dispose() -> void` | `Onity.Messaging` | Afterwards `PublishAsync` and `Subscribe` throw `ObjectDisposedException`. |
| `PublishOnityTask` | `PublishOnityTask<TMessage>(this IAsyncPublisher<TMessage> publisher, TMessage message, CancellationToken cancellationToken) -> OnityTask` | `Onity.Unity.Async` | `PublishAsync` as an `OnityTask`. |
| `SubscribeOnityTask` | `SubscribeOnityTask<TMessage>(this IAsyncSubscriber<TMessage> subscriber, Func<TMessage, OnityTask> handler) -> IDisposable` | `Onity.Unity.Async` | Subscribes a handler that returns `OnityTask`; also with a `Func<TMessage, CancellationToken, OnityTask>` handler. |

---

## Native async consumption

`OnityMessagingAsyncStreamExtensions` (`Onity.Unity.Async`). Subscribe, publish
and stop on the channel's thread; tokens may be canceled from any thread. The
calling assembly definition must reference `Onity.Messaging`, `Onity.Reactive`
and `Onity.Unity` (and `Onity.Core` when it touches `Unit`).

| API | Signature | Namespace | Notes |
| --- | --- | --- | --- |
| `ReceiveAsync` | `ReceiveAsync<T>(this ISubscriber<T> subscriber, CancellationToken cancellationToken = default) -> OnityTask<T>` | `Onity.Unity.Async` | The next message. Receives on one subscriber share one subscription and are pooled; a canceled receive gets no later message; a receive started on a disposed channel is canceled. |
| `ReceiveAsync` (predicate) | `ReceiveAsync<T>(this ISubscriber<T> subscriber, Func<T, bool> predicate, CancellationToken cancellationToken = default) -> OnityTask<T>` | `Onity.Unity.Async` | The next matching message; a throwing predicate faults the task. |
| `ReceiveAllAsync` | `ReceiveAllAsync<T>(this ISubscriber<T> subscriber, int capacity, OnityBufferOverflow overflow = OnityBufferOverflow.Fault) -> IOnityAsyncEnumerable<T>` | `Onity.Unity.Async` | Buffers up to `capacity` messages per enumeration in publish order. `Fault` stops accepting at the first overflow, yields the buffered messages, then faults with `InvalidOperationException`; `DropOldest` and `DropNewest` keep going. Cancellation and disposal discard the buffer; the stream never ends on its own. |
| `SubscribeQueued` | `SubscribeQueued<T>(this IAsyncSubscriber<T> subscriber, Func<T, CancellationToken, OnityTask> handler, int capacity, CancellationToken lifetimeToken) -> IDisposable` | `Onity.Unity.Async` | A bounded queue between the channel and one consumer that runs `handler` per message in order. `PublishAsync` waits only while the queue is full. Stopping (dispose or `lifetimeToken`) unsubscribes, cancels the running handler, discards queued messages and releases waiting publishers without an exception. A handler exception is logged and the next message is handled. |
| `OnityBufferOverflow` | `enum { Fault, DropOldest, DropNewest }` | `Onity.Unity.Async` | The full-buffer policy for `ReceiveAllAsync`. |

---

## Unity facade and bridges

### OnityEvent

Static shorthand for Unity code (`Onity.Unity`). The no-owner members use the
default context: the most recently activated `SceneContext`, then
`ProjectContext.Instance`, then any other active context. The owner members
use the context on `owner` or its nearest parent (inactive parents included)
and fall back to the default context. When no context resolves, `Publish`,
`Subscribe`, `Observe` and `GetEventHub` throw `InvalidOperationException`; a
null owner throws `ArgumentNullException`.

| API | Signature | Namespace | Notes |
| --- | --- | --- | --- |
| `Publish<TMessage>` | `Publish<TMessage>(TMessage message) -> void` | `Onity.Unity` | Through the default context. |
| `Publish<TMessage>` (owner) | `Publish<TMessage>(Component owner, TMessage message) -> void` | `Onity.Unity` | Through the nearest context. |
| `Subscribe<TMessage>` | `Subscribe<TMessage>(MessageHandler<TMessage> handler) -> IDisposable` | `Onity.Unity` | Through the default context; the caller owns the token. |
| `Subscribe<TMessage>` (owner) | `Subscribe<TMessage>(Component owner, MessageHandler<TMessage> handler) -> IDisposable` | `Onity.Unity` | Through the nearest context; the token is also disposed when `owner` is destroyed. |
| `Observe<TMessage>` | `Observe<TMessage>() -> IOnityObservable<TMessage>` | `Onity.Unity` | The hub's cached stream for the type. |
| `Observe<TMessage>` (owner) | `Observe<TMessage>(Component owner) -> IOnityObservable<TMessage>` | `Onity.Unity` | From the nearest context. |
| `GetEventHub` | `GetEventHub() -> OnityEventHub`; `GetEventHub(Component owner) -> OnityEventHub` | `Onity.Unity` | The resolved hub. |
| `TryGetEventHub` | `TryGetEventHub(out OnityEventHub eventHub) -> bool`; `TryGetEventHub(Component owner, out OnityEventHub eventHub) -> bool` | `Onity.Unity` | `false` instead of throwing (also for a null owner). |

### OnityEventHub

`sealed class OnityEventHub`; `new OnityEventHub(IMessageBroker broker)`. Bound
by every context over the scoped broker.

| API | Signature | Namespace | Notes |
| --- | --- | --- | --- |
| `Publish<TMessage>` | `Publish<TMessage>(TMessage message) -> void` | `Onity.Unity.Messaging` | Delegates to the broker. |
| `Subscribe<TMessage>` | `Subscribe<TMessage>(MessageHandler<TMessage> handler) -> IDisposable` | `Onity.Unity.Messaging` | Delegates to the broker. |
| `Observe<TMessage>` | `Observe<TMessage>() -> IOnityObservable<TMessage>` | `Onity.Unity.Messaging` | The message type as a stream; one cached instance per type. |

### Reactive bridge

`OnityMessageReactiveExtensions` turns a channel into the operator chain; see
[Reactive Operators](reactive-operators.html).

| API | Signature | Namespace | Notes |
| --- | --- | --- | --- |
| `Observe<TMessage>` (broker) | `Observe<TMessage>(this IMessageBroker broker) -> IOnityObservable<TMessage>` | `Onity.Unity.Messaging` | `GetSubscriber<TMessage>().Observe()`. |
| `Observe<TMessage>` (subscriber) | `Observe<TMessage>(this ISubscriber<TMessage> subscriber) -> IOnityObservable<TMessage>` | `Onity.Unity.Messaging` | Each subscription to the stream is one channel subscription; observers run inside the channel's handler. |

```csharp
using Onity.Reactive;           // Where, Select, Subscribe
using Onity.Unity.Messaging;    // Observe<T>
using Onity.Unity.Reactive;     // AddTo
using UnityEngine;              // Debug

broker.Observe<PlayerDamaged>()
      .Where(message => message.Amount > 0)
      .Select(message => message.Amount)
      .Subscribe(amount => Debug.Log($"Took {amount}"))
      .AddTo(this);
```

### DI bindings

| API | Signature | Namespace | Notes |
| --- | --- | --- | --- |
| `BindMessageChannel<TMessage>` | `BindMessageChannel<TMessage>(this OnityContainer container) -> void` | `Onity.Unity.Messaging` | Binds `IPublisher<TMessage>` and `ISubscriber<TMessage>` to the scope broker's channel, so `OnityEvent` and the hub reach the same subscribers. Resolves `IMessageBroker` at bind time: a context binds it before installers run; a bare container needs `BindInterfacesAndSelfTo<MessageBroker>().AsSingle()` first. |
| `DeclareMessage<T>` | `DeclareMessage<T>(this OnityContainer container) -> MessageChannel<T>` | `Onity.Composition` | Creates a separate `MessageChannel<T>` and binds it as `MessageChannel<T>`, `IPublisher<T>` and `ISubscriber<T>`; the container disposes it. The broker, `OnityEvent` and `OnityEventHub` do not see this channel. |
| `DeclareAsyncMessage<T>` | `DeclareAsyncMessage<T>(this OnityContainer container) -> AsyncMessageChannel<T>` | `Onity.Composition` | Creates an `AsyncMessageChannel<T>` and binds it as `AsyncMessageChannel<T>`, `IAsyncPublisher<T>` and `IAsyncSubscriber<T>`; the container disposes it. |

### Component extensions

`OnityEventComponentExtensions` (`Onity.Unity.Messaging`) aliases the owner
overloads on any `Component`: `owner.Publish(message)`,
`owner.Subscribe<TMessage>(handler)`, `owner.Observe<TMessage>()` and
`owner.GetEventHub()`.

---

## Exceptions

| Type | Raised when |
| --- | --- |
| A handler's own exception | Propagates out of `Publish` (or faults `PublishAsync`); the remaining handlers for that message are skipped. Catch inside the handler when one listener must not break the others. |
| `ObjectDisposedException` | `Publish`, `Subscribe` or `PublishAsync` after `Dispose()`; any `MessageBroker` member after the broker was disposed. |
| `ArgumentNullException` | A null handler, key, broker, list or owner component. |
| `InvalidOperationException` | `OnityEvent` found no active context; a `ReceiveAllAsync` buffer overflowed with `OnityBufferOverflow.Fault`. |
| `OperationCanceledException` | The token passed to `PublishAsync`, `ReceiveAsync` or `ReceiveAllAsync` was canceled. Normal cancellation. |
| `OnityMessagingException` | Declared (`sealed`, in `Onity.Messaging`) for messaging-core failures; the shipped channels throw the standard exceptions above. |

---

## Choosing a channel type

| Use | When |
| --- | --- |
| `OnityEvent`, `OnityEventHub`, `IPublisher<T>` / `ISubscriber<T>` | A transient notification with zero or more decoupled listeners. Late subscribers miss earlier messages; there is no replay. |
| `KeyedMessageChannel<TKey, T>` | The same message type, delivered only to the listeners of one key. |
| `AsyncMessageChannel<T>` | The publisher must await delivery. Add `SubscribeQueued` when a slow handler must not stall publishers. |
| `ReceiveAsync` / `ReceiveAllAsync` | An `OnityTask` flow waits for one message, or consumes messages as a stream. |
| `ReactiveProperty<T>` (through DI) | Current state a new listener must know at once; subscribing emits the current value first. |
| A direct service call | A command or query with one owner, a return value, or a synchronous result. |

Buffered or replayed messages, handler priority and request-response are not
shipped. See the [Events and Messaging guide](../guide/events-messaging.html)
for the decision rule and recipes, and
[Reactive Operators](reactive-operators.html) for the chain `Observe<T>()`
feeds.
