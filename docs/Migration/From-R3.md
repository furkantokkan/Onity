---
title: "From R3 / UniRx"
parent: "Migration"
nav_order: 3
---

# Migrating from R3 / UniRx to Onity.Reactive

`Onity.Reactive` is a push-based, hot-by-default reactive layer with an R3-shaped vocabulary: `Subject<T>`, `ReactiveProperty<T>`, and operators like `Where`/`Select`/`Merge`/`CombineLatest`/`Scan`. The public stream contract is `IOnityObservable<T>` (R3's `Observable<T>`), and `Subject<T>`, `ReactiveProperty<T>`, every operator, and `broker.Observe<T>()` all speak it, so events and state share one operator surface. Three behavioral differences matter most: trailing-edge throttle is explicit as **`ThrottleLast`** while leading-edge cool-down is **`Throttle`**; there is **no `Publish`/`Share`/`RefCount`** (multicast is served directly by `Subject<T>`); and lifetime is explicit — every `Subscribe` returns an `IDisposable` you must scope with `AddTo(this)` (Unity `Component`/`Behaviour`) or `AddTo(compositeDisposable)` (plain C#), with **no `AddTo(GameObject)` overload** and no implicit ref-counting. Time operators take an optional `OnityTimeProvider` (deterministic in tests). Every mapping below is verified against the shipped Onity public API (`Onity.Reactive`, `Onity.Unity.Reactive`).

## Primitives

| R3 / UniRx | Onity | Notes |
| --- | --- | --- |
| `new Subject<T>()` | `new Subject<T>()` (`Onity.Reactive`) | Multicast source. `OnNext` is 0-alloc steady-state; `OnNext`/`Subscribe` after `Dispose()` throw `ObjectDisposedException`. |
| `subject.OnNext(v)` | `subject.OnNext(v)` | Same. |
| `new ReactiveProperty<T>(initial)` | `new ReactiveProperty<T>(initial)` | `DistinctUntilChanged` is **built in** (default comparer). Subscribing emits the current value first. |
| `rp.Value = x` | `rp.Value = x` | Notifies only if changed. |
| *(R3 has no bool-returning set)* | `rp.SetValue(x)` → `bool` | Sets and returns whether the value actually changed. |
| `rp.Subscribe(...)` (BehaviorSubject-style replay) | `rp.Subscribe(...)` | Emits current value, then each real change. Use `rp.Subscribe(onNext, emitCurrentValue: false)` to skip the initial emit. |
| `IReadOnlyReactiveProperty<T>` | `IReadOnlyReactiveProperty<T>` | Read-only facade (`Value` + `Subscribe(..., emitCurrentValue = true)`). Note: it does **not** itself extend `IOnityObservable<T>`. |
| `new CompositeDisposable()` | `new CompositeDisposable()` | Lifetime bag. `Add`/`Remove`/`Clear`/`Count`/`Dispose`. (Not an `ICollection<IDisposable>`.) |
| `Observable.FromEvent<T>(add, remove)` | `OnityObservable.FromEvent<T>(add, remove)` | Factory. |
| `Observable.Return(v)` | `OnityObservable.Return<T>(v)` | Factory. |
| `Observable.Empty<T>()` | `OnityObservable.Empty<T>()` | Factory. |
| `Observable<T>` (single observable type) | `IOnityObservable<T>` | The one public stream contract. (`Observer<T>` is `delegate void Observer<T>(T value)`; the `Subscribe(Action<T>)` you normally write is an extension wrapping it.) |

## Synchronous operators (`OnityObservableExtensions`)

All return `IOnityObservable<T>` and allocate only at subscribe time (0 alloc per emitted value).

| R3 / UniRx | Onity | Notes |
| --- | --- | --- |
| `Where(predicate)` | `Where(Predicate<T>)` | |
| `Select(selector)` | `Select(Func<TSource, TResult>)` | |
| `DistinctUntilChanged()` | `DistinctUntilChanged(IEqualityComparer<T> = null)` | |
| `Skip(n)` / `SkipWhile(p)` | `Skip(int)` / `SkipWhile(Predicate<T>)` | Negative count throws `ArgumentOutOfRangeException`. |
| `Take(n)` / `TakeWhile(p)` | `Take(int)` / `TakeWhile(Predicate<T>)` | |
| `StartWith(v)` | `StartWith(T)` | |
| `Scan(seed, accumulator)` | `Scan<TState>(TState seed, Func<TState, T, TState>)` | Stateful fold; state lives in the wrapping observer. |
| `Pairwise()` | `Pairwise()` → `IOnityObservable<OnityPair<T>>` | Emits `OnityPair<T>` with `Previous`/`Current`; skips the first value. |
| `Merge(a, b, …)` | `Merge(params IOnityObservable<T>[])` | |
| `CombineLatest(other, selector)` | `CombineLatest<T1, T2, TResult>(IOnityObservable<T2>, Func<T1, T2, TResult>)` | **2-arity only** today; 3-4 arity is planned, not shipped. |
| `Sample(sampler)` | `Sample<TSignal>(IOnityObservable<TSignal> signalSource)` | |
| `Subscribe(onNext)` | `Subscribe(Action<T>)` | Returns `IDisposable`. |
| `Subscribe(onNext, onError, onCompleted)` | `Subscribe(Action<T>, Action<Exception>, Action<OnityResult>)` | Completion carries an `OnityResult`. |
| `TakeUntil(cancellationToken)` | `TakeUntilCancellation(CancellationToken)` | Stop on a token. |
| `FirstAsync(ct)` | `FirstAsync(CancellationToken = default)` → `Task<T>` | First value or `OperationCanceledException`. |
| `ForEachAsync` / `ToTask` (on Unit stream) | `ToTask(CancellationToken = default)` (on `IOnityObservable<Unit>`) | |

```csharp
using Onity.Reactive;

hp.Where(v => v <= 0)
  .Select(_ => "dead")
  .Subscribe(msg => Debug.Log(msg))
  .AddTo(this);
```

## Async / time operators (`OnityObservableAsyncExtensions`)

Each takes an optional `OnityTimeProvider` (deterministic in tests; pass a Unity time provider in gameplay).

| R3 / UniRx | Onity | Notes |
| --- | --- | --- |
| `Debounce(dueTime)` | `Debounce(TimeSpan dueTime, OnityTimeProvider = null)` | Emit the LAST value after a quiet window. |
| `ThrottleLast(interval)` | `ThrottleLast(TimeSpan interval, OnityTimeProvider = null)` | Emit the latest value once per interval. |
| `Throttle(dueTime)` (leading edge) | `Throttle(TimeSpan interval, OnityTimeProvider = null)` | Emits the first value immediately, then ignores values until the interval elapses. |
| trailing throttle / sample latest | `ThrottleLast(TimeSpan interval, OnityTimeProvider = null)` | Emits the latest value once per interval. Use this when you want trailing/sampled behavior. |
| `TakeUntil(otherObservable)` | `TakeUntil(CancellationToken)` / `TakeUntil(Task)` | Signal is a token or a task, not another observable. |
| `SelectAwait(async selector)` | `SelectAwait(Func<T, CancellationToken, ValueTask<TResult>>)` | Sequential async projection. **Resumes on a threadpool thread** — follow it with `ObserveOnMainThread()` before any `Subscribe` that touches `UnityEngine`. |
| `WhereAwait(async predicate)` | `WhereAwait(Func<T, CancellationToken, ValueTask<bool>>)` | Sequential async filter; same off-main-thread caveat — re-marshal with `ObserveOnMainThread()`. |
| `ObserveOn(scheduler)` | `ObserveOn(OnityFrameProvider)` (`Onity.Reactive`) | Re-posts each value onto the provider's frame loop (buffered, replayed on the next tick). Pass `OnityFrameProviders.Update` / `FixedUpdate` / `LateUpdate`. |
| `ObserveOn(ThreadPoolScheduler)` / thread-pool scheduler hop | `ObserveOnThreadPool()` (`Onity.Reactive`) | Re-posts values onto a .NET thread-pool worker while preserving source order. |
| `Observable.Start` / CPU work on thread pool | `SelectOnThreadPool(selector, maxConcurrency)` (`Onity.Reactive`) | Runs pure managed CPU-bound projection on the .NET thread pool. Results emit as workers finish when concurrency is greater than one. |
| `ObserveOnMainThreadDispatcher()` | `ObserveOnMainThread()` / `ObserveOnMainThread(OnityUnityFrameProvider)` (`Onity.Unity.Reactive`) | Convenience hop onto the Unity Update loop (or a chosen phase). This is the documented re-marshal after `SelectAwait`/`WhereAwait`. |

`SelectAwait`/`WhereAwait` resume off the Unity main thread, so re-marshal before touching Unity API:

```csharp
using Onity.Reactive;            // SelectAwait, Subscribe
using Onity.Unity.Reactive;      // ObserveOnMainThread, AddTo

m_requests
    .SelectAwait((id, ct) => LoadProfileAsync(id, ct))   // runs on a threadpool thread
    .ObserveOnMainThread()                               // hop back to the Update loop
    .Subscribe(profile => m_nameLabel.text = profile.Name)
    .AddTo(this);
```

For CPU-bound pure managed work that should run concurrently, use
`SelectOnThreadPool` and then re-marshal before Unity API access:

```csharp
m_damageEvents
    .SelectOnThreadPool((damage, ct) => CalculateScoreDelta(damage), maxConcurrency: 4)
    .ObserveOnMainThread()
    .Subscribe(delta => m_scoreLabel.text = delta.ToString())
    .AddTo(this);
```

## Unity bridges — frame loops, timers, lifetime (`Onity.Unity.Reactive`)

| R3.Unity / UniRx | Onity | Notes |
| --- | --- | --- |
| `Observable.EveryUpdate()` | `OnityUnityObservable.EveryUpdate()` → `IOnityObservable<Unit>` | Shared singleton, pumped by a hidden `DontDestroyOnLoad` object. |
| `Observable.EveryFixedUpdate()` / `EveryLateUpdate()` | `OnityUnityObservable.EveryFixedUpdate()` / `EveryLateUpdate()` | |
| `Observable.Timer(t)` | `OnityUnityObservable.Timer(float seconds, bool unscaled = false)` | One `Unit` after the delay. |
| `Observable.Interval(t)` | `OnityUnityObservable.Interval(float seconds, bool unscaled = false)` → `IOnityObservable<int>` | Tick index every interval. |
| `.Delay(t)` | `OnityUnityObservableExtensions.Delay<T>(seconds, useUnscaledTime = false)` | |
| `.AddTo(this)` (MonoBehaviour) | `someDisposable.AddTo(this)` | `this` is a `Component`. Disposes on Destroy (alias for `TakeUntilDestroy`). |
| `.AddTo(gameObject)` | **no `AddTo(GameObject)`** | Divergence: lifetime helpers take `Component`/`Behaviour` only. Pass `this` from a MonoBehaviour. |
| `.TakeUntilDestroy(this)` | `someDisposable.TakeUntilDestroy(this)` (`Component`) | Disposes on Destroy. |
| `.TakeUntilDisable(this)` | `someDisposable.TakeUntilDisable(this)` (`Behaviour`) | Disposes on disable. |
| `.AddTo(compositeDisposable)` | `someDisposable.AddTo(compositeDisposable)` (`OnityDisposableExtensions`) | For plain-C# owners. |

> Lifetime helpers extend `IDisposable`, not `IOnityObservable<T>`, so they go **after** `Subscribe` (which returns the `IDisposable`), not on the observable:
> `stream.Subscribe(...).AddTo(this);`

### Time providers (`OnityTimeProviders`)

Pass one into `Debounce`/`ThrottleLast` in gameplay so they honor `Time.timeScale`:

```csharp
using Onity.Reactive;
using Onity.Unity.Reactive;     // OnityTimeProviders

m_query
    .Debounce(TimeSpan.FromMilliseconds(250), OnityTimeProviders.UpdateUnscaled)
    .Subscribe(onSearch)
    .AddTo(this);
```

Available: `UpdateScaled`/`UpdateUnscaled`/`UpdateRealtime`, `FixedScaled`/`FixedUnscaled`/`FixedRealtime`, `LateScaled`/`LateUnscaled`/`LateRealtime`. In EditMode tests, subclass the abstract `OnityTimeProvider` to drive delays deterministically (no public `Manual`/`Fake` provider ships yet).

## Events as a stream (`broker.Observe<T>()`)

R3 needs a manual adapter to turn a message bus into an observable; Onity's messaging and reactive pillars share `IOnityObservable<T>`:

```csharp
using Onity.Reactive;            // Where, Select, Subscribe
using Onity.Unity.Messaging;     // Observe<T> on IMessageBroker / OnityEventHub
using Onity.Unity.Reactive;      // AddTo

broker.Observe<DamageEvent>()
      .Where(e => e.Amount > 0)
      .Select(e => e.Amount)
      .Subscribe(amount => Debug.Log($"Took {amount}"))
      .AddTo(this);
```

`broker.Observe<T>()`, `subscriber.Observe<T>()`, and `OnityEventHub.Observe<T>()` all return `IOnityObservable<T>` (the hub caches one stream per message type).

## Errors

`Onity.Reactive` does **not** ship dedicated exception types yet; it throws standard .NET exceptions. Map your R3 error handling accordingly:

| Exception | Cause | Fix |
| --- | --- | --- |
| `ObjectDisposedException` | `OnNext`/`Subscribe` after `Subject.Dispose()` (also `MessageChannel`/`MessageBroker`). | Tie subscriptions to lifetime with `AddTo`; stop emitting to a disposed source. |
| `ArgumentNullException` | A null source, handler, predicate, or selector passed to an operator / `FromEvent` / `Subscribe`. | Pass non-null delegates and sources. |
| `ArgumentOutOfRangeException` | Negative count to `Skip`/`Take`/etc. | Pass a count `>= 0`. |
| `OperationCanceledException` | The `CancellationToken` cancelled before the awaited value arrived in `FirstAsync`/`ToTask`/`TakeUntil`. **Normal cancellation, not a bug.** | Catch it where you start the async flow; do not treat as failure. |

## Not supported — do this instead

These R3 / UniRx features are deliberate Onity non-goals (see `docs/Plan/07-Competitive-And-AI-Roadmap.md` sections 2.2 and 6). Do not call the R3 API; use the Onity replacement.

| R3 / UniRx feature | Why it is a non-goal | Do this in Onity |
| --- | --- | --- |
| Hot/cold conversion `Publish` / `Share` / `RefCount` / `Multicast` | Implicit ref-counting contradicts the "one subscribe = one disposable" principle. | Multicast is already served by `Subject<T>` — subscribe a `Subject<T>` directly; share one instance via DI (`BindInstance` / `BindInterfacesAndSelfTo`). |
| Leading-edge `Throttle` | Only the trailing/sampled variant ships. | Use `ThrottleLast(interval, timeProvider)` (latest-per-interval) or `Debounce(dueTime, timeProvider)` (last-after-quiet). |
| Cold factories `Create` / `Defer` / `Never` | Onity is hot-by-default; cold factories are deferred (some docs once claimed them — they are not implemented). | Drive a `Subject<T>` / `ReactiveProperty<T>` yourself, or use `OnityObservable.Return`/`Empty`/`FromEvent`. |
| `ObserveEveryValueChanged(poll)` | Inherently a per-frame polling allocation/CPU pattern that conflicts with push-based + 0-alloc. | Hold the value in a `ReactiveProperty<T>` and subscribe, or expose it as a message and `Observe<T>()`. |
| `Buffer` / `Window` / `Zip` / `Switch` / `Concat` | Not shipped yet (planned). | Compose with shipped operators (`Scan`/`Pairwise`/`Merge`/`CombineLatest`/`Sample`), or accumulate in a `ReactiveProperty<T>`. |
| Error-flow `Catch` / `Retry` / `Timeout` | The model does not yet carry a rich `OnError` channel through operators. | Handle failures in the `Subscribe(onNext, onError, onCompleted)` overload, or guard inside the operator delegate. |
| `IObservable<T>` (System.Reactive) compatibility adapters | Explicitly not shipped to avoid a third-party type leak. | Stay on `IOnityObservable<T>`; bridge events via `Observe<T>()`. |
| Job/Burst/DOTS **parallel** managed operator execution | Unity Job/Burst frame modes are frame boundaries, not a way to run managed observers or DI inside Burst. | Use `SelectOnThreadPool` for pure managed CPU work, `ObserveOnThreadPool` for ordered thread-pool hops, and `ObserveOnMainThread` before Unity API access. Keep Burst/DOTS work in blittable bridge modules. |
| `AddTo(gameObject)` | Lifetime helpers extend `IDisposable` and take `Component`/`Behaviour`. | `AddTo(this)` / `TakeUntilDestroy(this)` (Component), `TakeUntilDisable(this)` (Behaviour), or `AddTo(compositeDisposable)`. |
