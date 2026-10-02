---
title: "Reactive"
parent: "Guides"
nav_order: 3
description: "Model changing state and streams of values with IOnityObservable<T>: subjects, reactive properties, operators, Unity frame loops, subscription lifetime and the OnityTask bridges."
---

# Reactive

Use the reactive layer to follow state that changes over time and to work with
streams of values through one contract, `IOnityObservable<T>` (`Onity.Reactive`):
a `Subject<T>` for a local stream, a `ReactiveProperty<T>` for current state,
operators to filter, combine and schedule, and `Observe<T>()` to read a message
channel as a stream. Reach for it when a view must follow state, when a message
needs per-listener filtering, or when async work must come back to the Unity
loop. Every `Subscribe` returns an `IDisposable` that you tie to an owner.

```csharp
using Onity.DI;                 // Inject
using Onity.Reactive;           // IReadOnlyReactiveProperty<T>, Where
using Onity.Unity;              // OnityEvent
using Onity.Unity.Reactive;     // TakeUntilDisable
using UnityEngine;

public readonly struct PlayerDamaged
{
    public readonly int Amount;

    public PlayerDamaged(int amount)
    {
        Amount = amount;
    }
}

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

Contents: [Streams and state](#streams-and-state), [Primitives](#primitives),
[Choose the shape](#choose-the-shape), [Synchronous operators](#synchronous-operators),
[Time and async operators](#time-and-async-operators), [Back to the Unity loop](#back-to-the-unity-loop),
[Thread-pool work](#thread-pool-work), [Disposal](#disposal),
[Unity frame loops and timers](#unity-frame-loops-and-timers), [Recipes](#recipes),
[Await a ReactiveProperty with OnityTask](#await-a-reactiveproperty-with-onitytask),
[What is not shipped](#what-is-not-shipped), [Error handling](#error-handling).

## Streams and state

`Subject<T>`, `ReactiveProperty<T>`, every operator and the message bridge
`Observe<T>()` return `IOnityObservable<T>`, so one operator chain works over
local streams, shared state and messages alike. The core (`Onity.Reactive`) has
no `UnityEngine` reference. The Unity bridges (frame loops, timers, component
lifetime) live in `Onity.Unity.Reactive`, and the `OnityTask` bridges in
`Onity.Unity.Async`.

`IReadOnlyReactiveProperty<T>` is the read-only face of a property: `Value`
plus `Subscribe(observer, emitCurrentValue = true)`. It does not implement
`IOnityObservable<T>`, so the operators do not apply to it. A consumer that
only follows the value subscribes directly, as `HealthHud` does above. Where a
consumer needs `Where` or another operator, inject the `ReactiveProperty<T>`
itself (it implements `IOnityObservable<T>`), or wrap the read-only property
once. Fragment, in a class that holds `IReadOnlyReactiveProperty<int> m_health`:

```csharp
IOnityObservable<int> healthStream = new OnityObservable<int>(observer => m_health.Subscribe(observer));
```

A `ReactiveProperty<T>` is the only primitive that replays its current value to
a new subscriber. Model current state as a `ReactiveProperty<T>` and past-tense
notifications as messages; the decision rule is in
[Events and Messaging](events-messaging.html#messages-in-onity).

## Primitives

| Primitive | Namespace | Use it for | Notes |
| --- | --- | --- | --- |
| `Subject<T>` | `Onity.Reactive` | A hot stream owned by one class: input samples, internal model events. | `OnNext` allocates nothing in steady state. `OnNext` and `Subscribe` throw `ObjectDisposedException` after `Dispose()`. |
| `ReactiveProperty<T>` | `Onity.Reactive` | Current state that a late subscriber must see at once: health, score, current wave. | Subscribing emits the current value first, then each change. Equal values are skipped (`DistinctUntilChanged` is built in; an `IEqualityComparer<T>` is optional). `SetValue(value)` returns whether the value changed. |
| `IReadOnlyReactiveProperty<T>` | `Onity.Reactive` | The read-only face of a property, for consumers. | `Value` plus `Subscribe(observer, emitCurrentValue = true)`. Not an `IOnityObservable<T>`. |
| `CompositeDisposable` | `Onity.Reactive` | A lifetime bag for plain C# owners. | `Add`, `Remove`, `Clear` (dispose all, keep the bag), `Count`, `Dispose` (final: a later `Add` disposes the item at once). |
| `OnityObservable` factories | `Onity.Reactive` | `FromEvent<T>(addHandler, removeHandler)`, `Return<T>(value)`, `Empty<T>()`. | Wrap a callback-style event, emit one value per subscriber, or never emit. |
| `new OnityObservable<T>(subscribe)` | `Onity.Reactive` | A custom source. | The constructor takes `Func<Observer<T>, IDisposable>`; the returned disposable ends the subscription. |

```csharp
using System;
using Onity.Reactive;           // Subject<T>, ReactiveProperty<T>, CompositeDisposable, AddTo
using UnityEngine;

Subject<int> damage = new Subject<int>();
IDisposable token = damage.Subscribe(amount => Debug.Log(amount));
damage.OnNext(10);
token.Dispose();
damage.Dispose();

ReactiveProperty<int> health = new ReactiveProperty<int>(100);
health.Value = 90;                                   // notifies only when the value changed
bool changed = health.SetValue(90);                  // false: same value, nothing published
IDisposable current = health.Subscribe(value => Debug.Log(value));                          // logs 90 now, then each change
IDisposable changes = health.Subscribe(value => Debug.Log(value), emitCurrentValue: false);  // changes only
IReadOnlyReactiveProperty<int> readOnly = health;    // hand this to consumers
current.Dispose();                                   // every Subscribe result is kept and disposed by its owner
changes.Dispose();

CompositeDisposable bag = new CompositeDisposable();
health.Subscribe(value => { }).AddTo(bag);
bag.Clear();                                         // dispose everything, keep the bag
```

## Choose the shape

| Shape | Use it for | Example |
| --- | --- | --- |
| `Subject<T>` | A local hot stream owned by one class. | Input samples, internal model events. |
| `ReactiveProperty<T>` | Current state that late subscribers must see immediately. | Health, score, selected weapon, current wave. |
| `OnityEvent.Observe<T>()` / `OnityEventHub.Observe<T>()` | A message type as a stream. | `PlayerDamaged`, `PlayerDied`, `WaveStarted`. |
| `Where`, `Select`, `CombineLatest`, `Scan` | Filtering, projection, derived state, accumulation. | Heavy hits only, effective health, a combo counter. |
| `SelectAwait`, `WhereAwait` | Sequential async work where source order matters. | Validate one request at a time. |
| `ObserveOnThreadPool`, `SelectOnThreadPool` | Pure managed CPU work off the Unity main thread. | Score calculation, path-cost estimation. |
| `ObserveOnMainThread`, `ObserveOn` | Return to a Unity loop before touching Unity objects. | `Transform`, UI Toolkit, uGUI, `Animator`, `AudioSource`. |
| `EveryUpdate(OnityUnityThreadMode ...)` | A frame stream with a Jobs, Burst or DOTS boundary. | High-frequency frame signals, DOTS-driven emission. |

All of these compose through `IOnityObservable<T>`, so the operator shape is
the same whether the source is state, a local subject or a message stream.

## Synchronous operators

Every synchronous operator returns `IOnityObservable<T>`, allocates only when
it is subscribed, and forwards each value without allocating. The
[operator reference](../reference/reactive-operators.html) lists every
signature and edge case.

| Operator | Shape |
| --- | --- |
| `Where` | `Where(Predicate<T>)` |
| `Select` | `Select(Func<TSource, TResult>)` |
| `DistinctUntilChanged` | `DistinctUntilChanged(IEqualityComparer<T> = null)` |
| `Skip` / `Take` | `Skip(int)` / `Take(int)` |
| `SkipWhile` / `TakeWhile` | `SkipWhile(Predicate<T>)` / `TakeWhile(Predicate<T>)` |
| `StartWith` | `StartWith(T)` |
| `Scan` | `Scan<TState>(seed, Func<TState, T, TState>)` |
| `Pairwise` | `Pairwise() -> IOnityObservable<OnityPair<T>>` (`Previous`, `Current`) |
| `Buffer` | `Buffer(int count) -> IOnityObservable<IReadOnlyList<T>>` |
| `Merge` | `Merge(params IOnityObservable<T>[])` |
| `CombineLatest` | `CombineLatest<T1, T2, TResult>(other, selector)`, two sources |
| `Sample` | `Sample<TSignal>(sampler)` |

Fragment, in a service that holds `ReactiveProperty<int> m_health`, `m_shield`
and `m_effectiveHealth` and an injected `IOnityScopeLifetime scope`:

```csharp
// Derived state: emit whenever either side changes, once both have a value.
m_health.CombineLatest(m_shield, (health, shield) => health + shield)
        .DistinctUntilChanged()
        .Subscribe(total => m_effectiveHealth.SetValue(total))
        .AddTo(scope);

// Each value paired with the previous one.
m_health.Pairwise()
        .Subscribe(pair => Debug.Log($"{pair.Previous} -> {pair.Current}"))
        .AddTo(scope);
```

## Time and async operators

`Debounce` emits the last value after a quiet window, `ThrottleLast` emits the
latest value once per interval, `Throttle` emits the first value of a window
and drops the rest until the interval elapses, and `Buffer(timeSpan)` emits the
values gathered in each window. Each takes an optional `OnityTimeProvider`.
Without one they wait on `OnityTimeProvider.System`, which is wall-clock
`Task.Delay`. In gameplay pass one of `OnityTimeProviders`
(`Onity.Unity.Reactive`) so the wait follows a Unity loop and time mode:
`UpdateScaled`, `UpdateUnscaled`, `UpdateRealtime`, plus the `Fixed*` and
`Late*` variants. In EditMode tests, subclass `OnityTimeProvider` and override
`DelayAsync` to control time; no manual or fake provider ships.

`SelectAwait(Func<T, CancellationToken, ValueTask<TResult>>)` and
`WhereAwait(Func<T, CancellationToken, ValueTask<bool>>)` run their delegate one
value at a time, in order, on a thread-pool thread; the token is canceled when
the subscription is disposed. `TakeUntil(CancellationToken)` and
`TakeUntil(Task)` stop a stream on a signal, and `TakeUntilCancellation(token)`
is the synchronous form. Values that leave `SelectAwait` or `WhereAwait` arrive
on the thread that completed the delegate, so hop back before touching Unity
objects (next section).

## Back to the Unity loop

`ObserveOn(frameProvider)` (`Onity.Reactive`) queues each value and delivers
the queue on the provider's next tick; `OnityFrameProviders.Update`,
`FixedUpdate` and `LateUpdate` (`Onity.Unity.Reactive`) are the Unity
providers. `ObserveOnMainThread()` is `ObserveOn(OnityFrameProviders.Update)`,
and `ObserveOnMainThread(OnityFrameProviders.FixedUpdate)` picks another loop.
This is the required hop after `SelectAwait`, `WhereAwait` and the thread-pool
operators. Fragment, in `HealthHud.OnEnable`:

```csharp
OnityEvent.Observe<PlayerDied>(this)
          .SelectAwait((message, ct) => UploadRunAsync(ct))   // ValueTask<bool>, runs on the thread pool
          .ObserveOnMainThread()                              // back onto the Update loop
          .Subscribe(ShowUploadResult)                        // safe to touch UI here
          .TakeUntilDisable(this);
```

## Thread-pool work

`ObserveOnThreadPool()` re-posts values onto one thread-pool worker in source
order. `SelectOnThreadPool(selector, maxConcurrency)` runs a CPU-bound selector
on up to `maxConcurrency` workers (`0` means the processor count) and emits
results as they finish, so pass `maxConcurrency: 1` when order matters. The
selector and any observer before the hop back run off the main thread and must
not touch `UnityEngine` members.

```csharp
using Onity.DI;                 // IOnityScopeLifetime, AddTo(scope)
using Onity.Reactive;           // ReactiveProperty<T>, Where, SelectOnThreadPool
using Onity.Unity.Messaging;    // OnityEventHub
using Onity.Unity.Reactive;     // ObserveOnMainThread

public sealed class ScoreService
{
    private readonly ReactiveProperty<int> m_score = new ReactiveProperty<int>(0);

    public ScoreService(OnityEventHub events, IOnityScopeLifetime scope)
    {
        m_score.AddTo(scope);                                            // the scope disposes the property

        events.Observe<PlayerDamaged>()
              .Where(message => message.Amount > 0)
              .SelectOnThreadPool((message, ct) => CalculateScoreDelta(message), maxConcurrency: 4)
              .ObserveOnMainThread()                                     // back on the Update loop
              .Subscribe(delta => m_score.SetValue(m_score.Value + delta))
              .AddTo(scope);                                             // unsubscribed when the scope ends
    }

    public IReadOnlyReactiveProperty<int> Score => m_score;

    private static int CalculateScoreDelta(PlayerDamaged message)
    {
        return message.Amount * 10;                                      // pure managed work only
    }
}
```

## Disposal

A subscription lives until its `IDisposable` is disposed. The lifetime helpers
extend `IDisposable`, so they chain after `Subscribe`, not on the observable:

| Call | Namespace | Disposes when |
| --- | --- | --- |
| `subscription.AddTo(this)` / `TakeUntilDestroy(this)` | `Onity.Unity.Reactive` | the `Component` is destroyed (`AddTo(Component)` is the same call). |
| `subscription.TakeUntilDisable(this)` | `Onity.Unity.Reactive` | the `Behaviour` is disabled, and on destroy. |
| `subscription.AddTo(compositeDisposable)` | `Onity.Reactive` | the bag is cleared or disposed. |
| `subscription.AddTo(scope)` | `Onity.DI` | the `IOnityScopeLifetime` (a container or context scope) ends; see [Scope lifetime token](lifecycle-and-scopes.html#scope-lifetime-token). |

There is no `AddTo(GameObject)` overload; pass the component. On first use the
Unity helpers add an internal `OnityLifetimeNotifier` component to the owner's
`GameObject`, which disposes the registered subscriptions from its `OnDisable`
and `OnDestroy`.

Subscribe in `OnEnable` and dispose in `OnDisable` (`TakeUntilDisable(this)`
does exactly that), so a re-enabled component does not subscribe twice. The
`ONITY003` analyzer flags a `Subscribe` whose result is neither stored nor
passed to `AddTo`. To find a subscription that outlives its owner, turn on
`OnityObservableTracker.EnableTracking` (it records subscriptions made with a
lifecycle observer, including the internal ones of the time and async
operators) and open `Onity/Tools/Observable Tracker`; leave tracking off in
players.

## Unity frame loops and timers

`OnityUnityObservable` (`Onity.Unity.Reactive`) turns Unity loops into
`IOnityObservable<Unit>` streams, pumped by a hidden component that is created
on first use and survives scene loads in Play Mode. Fragment:

```csharp
OnityUnityObservable.EveryUpdate()            // once per rendered frame
OnityUnityObservable.EveryFixedUpdate()       // once per physics step
OnityUnityObservable.EveryLateUpdate()        // once per late update
OnityUnityObservable.EveryUpdate(token)       // stops when the CancellationToken is canceled
OnityUnityObservable.Timer(2f)                // one Unit after two seconds (useUnscaledTime overload)
OnityUnityObservable.Interval(1f)             // IOnityObservable<int>: the tick count, starting at 1, every second
```

For a singleton service that ticks for the life of its scope, implement
`IOnityTickable` instead; it needs no subscription (see
[Lifecycle and Scopes](lifecycle-and-scopes.html#the-automatic-lifecycle)). A
`MonoBehaviour` that wants a frame stream it can compose uses `EveryUpdate()`
with `TakeUntilDisable(this)`; see [Tick until disabled](#tick-until-disabled).

`OnityFrameProviders.Update` / `FixedUpdate` / `LateUpdate` are the
`OnityFrameProvider` instances behind `ObserveOn`, and `OnityTimeProviders`
builds on them. The timers behind `Timer`, `Interval` and `Delay`
(`OnityCountdownTimer`, `OnityIntervalTimer`, `OnityStopwatchTimer`) are public
too, for code that wants `Start`, `Pause`, `Resume` and progress without a
stream.

### Frame threading modes

The `EveryUpdate`, `EveryFixedUpdate` and `EveryLateUpdate` overloads that take
an `OnityUnityThreadMode` add a Unity job boundary around each frame signal.
They do not run observers off the main thread; observers still follow normal
Unity rules.

| Mode | Behavior |
| --- | --- |
| `SingleThread` | The plain frame signal. |
| `JobMultiThread` | Schedules a small `IJobParallelFor` marker job after each emission and completes it before the next. |
| `BurstJobMultiThread` | The same marker job, Burst-compiled when Burst AOT is enabled. |
| `DotsEventDriven` | Emits only when the DOTS integer event bridge's accumulated value changed; emits every frame when the bridge is unavailable. |

Fragment, in a `MonoBehaviour`'s `OnEnable`:

```csharp
OnityUnityObservable
    .EveryUpdate(OnityUnityThreadMode.BurstJobMultiThread, jobWorkItemCount: 128, minCommandsPerJob: 32)
    .Subscribe(_ => TickPresentation())
    .TakeUntilDisable(this);
```

### Input System

With `ENABLE_INPUT_SYSTEM`, `Onity.Unity.Input` adds
`action.StartedAsObservable(ct)`, `PerformedAsObservable(ct)` and
`CanceledAsObservable(ct)` over an `InputAction`, each an
`IOnityObservable<InputAction.CallbackContext>`. `OnityReactiveInputPlayer`
routes an `InputActionAsset` into button, `Vector2`, `float` and long-press
streams (`GetButtonObservable`, `GetVector2Observable`, `GetFloatObservable`,
`GetLongPressObservable`, `GetLongPressProgressObservable`) with a context
stack.

## Recipes

### Shared state from DI

Bind one property, expose it read-only to views, and change it from the
service that owns it. `BindReactiveProperty` (`Onity.Composition`) binds one
instance as both `ReactiveProperty<int>` and `IReadOnlyReactiveProperty<int>`
and disposes it with the scope. Fragment of the canonical `GameInstaller`:

```csharp
// ReactiveProperty<int> and IReadOnlyReactiveProperty<int>; the scope disposes it.
container.BindReactiveProperty(initialValue: 100);

// IPublisher<PlayerDamaged> and ISubscriber<PlayerDamaged> on the scope's message broker.
container.BindMessageChannel<PlayerDamaged>();

// The container constructs it, runs Initialize() at Build() and disposes it with the scope.
container.BindInterfacesAndSelfTo<HealthService>().AsSingle();
```

`HealthHud` (top of this page) injects the read-only contract and receives the
current value on subscribe, so a HUD enabled mid-match shows the right number
at once. `HealthService` (next recipe) injects the `ReactiveProperty<int>` and
writes it.

### Event stream updates reactive state

Messages carry what happened; a property holds what is true now. The service
that owns the state subscribes to the message stream, folds it into the
property, and publishes follow-up messages. This is the canonical
`HealthService` with the regeneration loop left out and a `PlayerDied`
publication added:

```csharp
using System;
using Onity.DI;                 // IOnityScopeLifetime, AddTo(scope)
using Onity.Messaging;          // ISubscriber<T>
using Onity.Reactive;           // ReactiveProperty<T>, Where
using Onity.Unity.Messaging;    // OnityEventHub, Observe

public readonly struct PlayerDied { }

public sealed class HealthService
{
    private readonly ReactiveProperty<int> m_health;
    private readonly OnityEventHub m_events;

    public HealthService(
        ReactiveProperty<int> health,
        ISubscriber<PlayerDamaged> damage,
        OnityEventHub events,
        IOnityScopeLifetime scope)
    {
        m_health = health;
        m_events = events;
        damage.Observe()
              .Where(message => message.Amount > 0)
              .Subscribe(OnDamaged)
              .AddTo(scope);                                  // unsubscribed when the scope ends
    }

    private void OnDamaged(PlayerDamaged message)
    {
        int next = Math.Max(0, m_health.Value - message.Amount);

        if (m_health.SetValue(next) && next == 0)
        {
            m_events.Publish(new PlayerDied());
        }
    }
}
```

The flow for UI state: gameplay publishes `PlayerDamaged`, `HealthService`
updates the property, views subscribe to the read-only property and get the
current value first, and `HealthService` publishes `PlayerDied` when the state
crosses a threshold.

### Health reaches zero

A property replays its current value, so a gate fires at once when the player
is already dead. Fragment, in `HealthHud.OnEnable`, where `m_health` is the
injected `IReadOnlyReactiveProperty<int>`:

```csharp
m_health.Subscribe(value =>
        {
            if (value <= 0)
            {
                ShowGameOver();                       // runs now if the player is already dead
            }
        })
        .TakeUntilDisable(this);
```

With a `ReactiveProperty<int>` in hand, `m_health.Where(value => value <= 0)`
expresses the same gate as an operator.

### Tick until disabled

Fragment, in `HitMarker.OnEnable`:

```csharp
OnityUnityObservable.EveryUpdate()
    .Subscribe(_ => Fade())                       // one step of the fade per frame
    .TakeUntilDisable(this);                      // disposed on disable, and on destroy
```

### Debounce

Fragment, in `HealthHud.OnEnable`: hide the hit markers two seconds after the
last hit, measured in scaled game time.

```csharp
OnityEvent.Observe<PlayerDamaged>(this)
          .Debounce(TimeSpan.FromSeconds(2), OnityTimeProviders.UpdateScaled)
          .Subscribe(_ => HideHitMarkers())
          .TakeUntilDisable(this);
```

### Await the first value

`FirstAsync(ct)` completes a `Task<T>` with the first value and unsubscribes;
`ToTask(ct)` does the same for an `IOnityObservable<Unit>`. Cancellation
surfaces as `OperationCanceledException`, which is normal cancellation. For
`OnityTask` code use `FirstOnityTask(ct)`, or `ToOnityTask(ct)` for the last
value of a source that completes (`Onity.Unity.Async`). Fragment, in a service
that holds an injected `OnityEventHub m_events`:

```csharp
public Task WaitForDeathAsync(CancellationToken ct)
{
    return m_events.Observe<PlayerDied>().FirstAsync(ct);   // completes on the first PlayerDied, then unsubscribes
}
```

## Await a ReactiveProperty with OnityTask

`Onity.Unity.Async` adds pooled, subscription-free waits on any
`IReadOnlyReactiveProperty<T>`:

| Member | Result |
| --- | --- |
| `property.WaitAsync(ct)` | `OnityTask<T>`: the next accepted change. The current value does not complete it, and a set that the property skipped (an equal value) is not a change. |
| `property.WaitUntilAsync(predicate, ct)` | `OnityTask<T>`: the first value that matches; completes synchronously when the current value already matches. A throwing predicate faults the task. |
| `property.AsLatestAsyncEnumerable(includeCurrent: true)` | `IOnityAsyncEnumerable<T>` that conflates: a slow consumer gets the latest value, never a backlog and never an overflow fault. |
| `property.ToAsyncReactiveProperty(ct)` | An `OnityReadOnlyAsyncReactiveProperty<T>` that starts with the current value and follows the property until the token is canceled or it is disposed. |
| `asyncProperty.BindTo(reactiveProperty, ct)` | Writes an async property's current and later values into a `ReactiveProperty<T>`; dispose the result to stop. |

```csharp
using System.Threading;
using Onity.DI;                 // IOnityInitializable, IOnityScopeLifetime
using Onity.Reactive;           // IReadOnlyReactiveProperty<T>
using Onity.Unity.Async;        // WaitUntilAsync, WaitAsync, OnityTaskVoid
using Onity.Unity.Messaging;    // OnityEventHub

public readonly struct WaveStarted
{
    public readonly int Index;

    public WaveStarted(int index)
    {
        Index = index;
    }
}

public sealed class WaveDirector : IOnityInitializable
{
    private readonly IReadOnlyReactiveProperty<int> m_health;
    private readonly OnityEventHub m_events;
    private readonly IOnityScopeLifetime m_scope;

    public WaveDirector(IReadOnlyReactiveProperty<int> health, OnityEventHub events, IOnityScopeLifetime scope)
    {
        m_health = health;
        m_events = events;
        m_scope = scope;
    }

    public void Initialize()
    {
        RunAsync(m_scope.Token).Forget();             // the scope cancels the token before it disposes its services
    }

    private async OnityTaskVoid RunAsync(CancellationToken token)
    {
        int wave = 0;

        while (!token.IsCancellationRequested)
        {
            await m_health.WaitUntilAsync(value => value == 100, token);   // at once when already at full health
            m_events.Publish(new WaveStarted(++wave));
            await m_health.WaitAsync(token);                               // the next change: the first hit of the wave
        }
    }
}
```

Waits on one property share one subscription, and their waiters are pooled; an
EditMode test locks in that a warmed `WaitAsync` cycle allocates nothing. A wait
that starts while the property is publishing (for example in a continuation that
the publication resumed) waits for the following change, so a `while` loop over
`WaitAsync` sees every change once. Use these on the property's thread, the main
thread; continuations run inline on the thread that sets the value.

`ReactiveProperty<T>.Dispose` does not notify its observers, so disposing the
property does not complete a pending wait or enumeration. Pass the scope token
(`IOnityScopeLifetime.Token`, `component.GetScopeCancellationToken()`) so the
wait ends with its scope; a wait started on an already disposed property is
canceled.

## What is not shipped

`Window`, `Zip`, `Switch`, `Concat`, and the multicast set (`Publish`, `Share`,
`RefCount`) are not part of `IOnityObservable<T>`; a `Subject<T>` is already a
multicast source, so share one instance instead. There are no `Never`, `Create`
or `Defer` factories; write a custom source with `new OnityObservable<T>(subscribe)`.
Pull-based async streams (`IOnityAsyncEnumerable<T>`) do carry `Zip`, `Concat`,
`Publish` and `Queue`; convert with `observable.AsOnityAsyncEnumerable(capacity)`
or `property.AsLatestAsyncEnumerable()` and read
[Stream operators](onitytask.html#stream-operators). Do not assume R3 or UniRx
parity beyond the operators listed on this page; the mapping is in
[From R3 and UniRx](../Migration/From-R3.html).

## Error handling

- A subscriber that throws inside `Subject<T>.OnNext` (and therefore inside a
  `ReactiveProperty<T>` set) is caught, the exception goes to
  `OnityObservableExceptionHandler.Handler`, and the remaining subscribers are
  still notified. The default handler does nothing, so assign one at startup,
  for example `OnityObservableExceptionHandler.Handler = Debug.LogException;`.
  Exceptions from `SelectOnThreadPool` selectors and `ObserveOnThreadPool`
  observers take the same route.
- A stream whose source is a message channel is different: a throwing
  subscriber propagates to the `Publish` call (see
  [Events and Messaging](events-messaging.html#error-handling)).
- A `SelectAwait` or `WhereAwait` delegate that throws ends that subscription;
  the exception is not forwarded to the subscriber. Guard inside the delegate
  when a failure must not stop the stream.
- `ObjectDisposedException` means `OnNext`, `Subscribe` or `SetValue` after
  `Dispose()`; `ArgumentNullException` means a null source, predicate, selector
  or observer; `ArgumentOutOfRangeException` means a negative count or a
  non-positive interval.
- `OperationCanceledException` from `FirstAsync`, `ToTask` or the `OnityTask`
  bridges is normal cancellation; catch it where the flow starts.
- `OnityReactiveException` exists for reactive-core failures; the shipped
  operators throw the standard exceptions above.

## See also

- [Reactive Operators](../reference/reactive-operators.html): every signature.
- [Events and Messaging](events-messaging.html): `Observe<T>()` feeds this same chain.
- [Dependency Injection](dependency-injection.html#shared-reactive-and-messaging-primitives): `BindReactiveProperty` and `BindSubject`.
- [Lifecycle and Scopes](lifecycle-and-scopes.html): scope tokens, `AddTo(scope)`, `IOnityTickable`.
- [Async with OnityTask](onitytask.html): streams, channels and the async reactive property.
- [Reactive vs R3 and UniRx](../comparisons/reactive-vs-r3-unirx.html): the measured comparison, and the [Comparisons](../comparisons/index.html) hub.
