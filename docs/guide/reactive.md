---
title: "Reactive"
parent: "Guides"
nav_order: 2
---

# Reactive

Onity's reactive layer is push-based and hot by default. The everyday contract is `IOnityObservable<T>`: `Subject<T>`, `ReactiveProperty<T>`, every operator, and the messaging bridge `broker.Observe<T>()` all return the **same** interface, so one operator chain works over state, raw streams, and events alike. The core (`Onity.Reactive`) is engine-free; the Unity bridges (frame loops, timers, component lifetime) live in `Onity.Unity.Reactive`.

Every `Subscribe` returns an `IDisposable`. Disposing it is mandatory — an undisposed subscription leaks. Scope it with `AddTo(this)` in a MonoBehaviour or `AddTo(compositeDisposable)` in plain C#.

```csharp
using Onity.Reactive;

ReactiveProperty<int> hp = new ReactiveProperty<int>(100);
hp.Where(v => v <= 0)
  .Select(_ => "dead")
  .Subscribe(message => Debug.Log(message));   // dispose this (see Disposal below)
```

## Primitives

```csharp
using Onity.Reactive;

// Subject<T>: a multicast event source. OnNext is allocation-free in steady state.
Subject<int> damage = new Subject<int>();
IDisposable sub = damage.Subscribe(v => Debug.Log(v));
damage.OnNext(10);
sub.Dispose();
damage.Dispose();                       // OnNext / Subscribe after Dispose throw ObjectDisposedException

// ReactiveProperty<T>: a value plus change notification. DistinctUntilChanged is built in.
ReactiveProperty<int> hp = new ReactiveProperty<int>(100);
int now = hp.Value;                     // read
hp.Value = 90;                          // set (notifies only if the value changed)
bool changed = hp.SetValue(90);         // set + return whether it actually changed (false here)
hp.Subscribe(v => Debug.Log(v));        // emits the CURRENT value (90) first, then on each real change
hp.Subscribe(v => Debug.Log(v), emitCurrentValue: false);   // skip the initial emit
IReadOnlyReactiveProperty<int> readOnly = hp;               // expose read-only to consumers

// CompositeDisposable: a lifetime bag for plain C# owners.
CompositeDisposable bag = new CompositeDisposable();
hp.Subscribe(v => { }).AddTo(bag);
bag.Clear();                            // dispose all, keep the bag reusable
bag.Dispose();                          // dispose all, final
```

A `ReactiveProperty<T>` is the only primitive that replays its current value to a new subscriber. Model **current state** (health, score, current wave) as a `ReactiveProperty<T>`; model **transient notifications** as messages (see [Events & Messaging](events-messaging.html)).

## Synchronous operators

All synchronous operators return `IOnityObservable<T>` and allocate only at subscribe time (no allocation per emitted value).

| Operator | Shape |
| --- | --- |
| `Where` | `Where(Predicate<T>)` |
| `Select` | `Select(Func<TSource,TResult>)` |
| `DistinctUntilChanged` | `DistinctUntilChanged(IEqualityComparer<T> = null)` |
| `Skip` / `Take` | `Skip(int)` / `Take(int)` |
| `SkipWhile` / `TakeWhile` | `SkipWhile(Predicate<T>)` / `TakeWhile(Predicate<T>)` |
| `StartWith` | `StartWith(T)` |
| `Scan` | `Scan<TState>(seed, Func<TState,T,TState>)` |
| `Pairwise` | `Pairwise() -> IOnityObservable<OnityPair<T>>` |
| `Merge` | `Merge(params IOnityObservable<T>[])` |
| `CombineLatest` | `CombineLatest<T1,T2,TResult>(other, selector)` |
| `Sample` | `Sample<TSignal>(sampler)` |

Static factories on `OnityObservable`: `FromEvent<T>(addHandler, removeHandler)`, `Return<T>(value)`, `Empty<T>()`. (There is no `Never`, `Create`, or `Defer`.)

```csharp
using Onity.Reactive;

// Combine two streams; emit a result whenever either side updates.
health.CombineLatest(shield, (h, s) => h + s)
      .DistinctUntilChanged()
      .Subscribe(total => Debug.Log($"Effective HP: {total}"));

// Pair each value with the previous one.
score.Pairwise()
     .Subscribe(pair => Debug.Log($"{pair.Previous} -> {pair.Current}"));
```

## Async and time operators

These extend `IOnityObservable<T>` directly. Each time-based operator takes an optional `OnityTimeProvider` — deterministic in tests (`OnityTimeProvider.System`), or a Unity time provider in gameplay (see Unity bridges below).

- `Debounce(TimeSpan dueTime, OnityTimeProvider = null)` — emit the **last** value after a quiet window.
- `ThrottleLast(TimeSpan interval, OnityTimeProvider = null)` — emit the latest value once per interval. The operator is named **`ThrottleLast`**; there is no leading-edge `Throttle`.
- `TakeUntil(CancellationToken)` / `TakeUntil(Task)` — stop on a signal.
- `SelectAwait(Func<T,CancellationToken,ValueTask<TResult>>)` / `WhereAwait(Func<T,CancellationToken,ValueTask<bool>>)` — sequential async projection / filter.

> `SelectAwait` and `WhereAwait` resume **off** the originating thread. Do not touch `UnityEngine` APIs in a `Subscribe` that sits directly after them. Hop back onto a Unity loop first with `ObserveOn`.

### Marshalling back onto a Unity loop — `ObserveOn`

`ObserveOn(OnityFrameProvider)` re-posts each value onto a frame provider's loop instead of forwarding it synchronously, so it is the required hop after `SelectAwait` / `WhereAwait` before any `UnityEngine` call.

```csharp
using Onity.Reactive;
using Onity.Unity.Reactive;   // OnityFrameProviders

requests
    .SelectAwait(async (id, ct) => await LoadAsync(id, ct))   // resumes off-thread
    .ObserveOn(OnityFrameProviders.Update)                    // back onto the Update loop
    .Subscribe(result => transform.position = result.Spawn)   // safe: on the main thread
    .AddTo(this);
```

## Disposal

Disposal is uniform across DI, reactive, and messaging — it is always an `IDisposable` plus `AddTo`.

```csharp
using Onity.Reactive;        // AddTo(CompositeDisposable)
using Onity.Unity.Reactive;  // AddTo(Component) / TakeUntilDestroy / TakeUntilDisable

someDisposable.AddTo(this);                // dispose on Component destroy (== TakeUntilDestroy)
someDisposable.TakeUntilDestroy(this);     // dispose on Component destroy
someDisposable.TakeUntilDisable(this);     // dispose on Behaviour disable
someDisposable.AddTo(compositeDisposable); // add to a CompositeDisposable (plain C#)
```

> The Unity lifetime overloads take `Component` / `Behaviour`. There is no `AddTo(GameObject)` overload — pass `this` from a MonoBehaviour. These helpers extend `IDisposable`, so they chain **after** `Subscribe` (which returns the disposable), not on the observable.

## Unity bridges — frame loops and timers

```csharp
using Onity.Unity.Reactive;   // OnityUnityObservable

OnityUnityObservable.EveryUpdate()        // IOnityObservable<Unit>, pumped by a hidden DontDestroyOnLoad object
OnityUnityObservable.EveryFixedUpdate()
OnityUnityObservable.EveryLateUpdate()
OnityUnityObservable.Timer(2f)            // emits one Unit after 2s (overload: useUnscaledTime)
OnityUnityObservable.Interval(1f)         // IOnityObservable<int> tick index every 1s (overload: useUnscaledTime)
```

`OnityTimeProviders` exposes ready-made providers for `Debounce`/`ThrottleLast` — for example `OnityTimeProviders.UpdateScaled`, `UpdateUnscaled`, and `UpdateRealtime` (with `Fixed*` and `Late*` variants). `OnityFrameProviders.Update` / `FixedUpdate` / `LateUpdate` back `ObserveOn`.

## Recipes

### Health reaches zero

`ReactiveProperty<T>` emits its current value on subscribe, so the gate fires immediately if the player is already dead.

```csharp
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

### Tick every frame until disabled

```csharp
using Onity.Unity.Reactive;
using UnityEngine;

public sealed class AiTicker : MonoBehaviour
{
    private void OnEnable()
    {
        OnityUnityObservable.EveryUpdate()
            .Subscribe(_ => TickAi())
            .TakeUntilDisable(this);      // disposed on disable (and on destroy)
    }

    private void TickAi() { }
}
```

> For singletons, prefer the automatic `IOnityTickable` lifecycle over `EveryUpdate()` — see [Lifecycle & Scopes](lifecycle-and-scopes.html).

### Debounce a search box, honoring time scale

```csharp
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

### Await the first matching value

```csharp
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

`FirstAsync(CancellationToken)` and `ToTask()` (on an `IOnityObservable<Unit>`) bridge a stream to a `Task`. A cancellation surfaces as `OperationCanceledException`; that is normal cancellation, not a failure.

## What is not shipped

`Merge`, `CombineLatest`, `Scan`, `Pairwise`, `Sample`, leading-edge naming aside (`ThrottleLast`), and `ObserveOn` **are** shipped. Still intentionally absent: `Window`, `Zip`, `Switch`, `Concat`, and the multicast set (`Publish`, `Share`, `RefCount`). Do not assume R3/UniRx parity beyond the operators listed here.

## Error handling

`Subject<T>.OnNext` catches a throwing subscriber, routes it to the settable `OnityObservableExceptionHandler` hook, and keeps notifying the rest — one bad observer never breaks a frame. `OnityReactiveException` is the dedicated reactive exception type. `ObjectDisposedException` indicates use after `Dispose()` (tie subscriptions to lifetime); `ArgumentNullException` indicates a null source/predicate/selector.

## See also

- [Events & Messaging](events-messaging.html) — `broker.Observe<T>()` feeds the same operator chain.
- [Dependency Injection](dependency-injection.html) — bind a `ReactiveProperty<T>` as shared state.
- [Lifecycle & Scopes](lifecycle-and-scopes.html) — `IOnityTickable` vs `EveryUpdate()`.
- [Migration: From R3 / UniRx](../Migration/From-R3.html).
