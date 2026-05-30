---
title: "Reactive Operators"
parent: "Reference"
nav_order: 2
---

# Reactive Operator Reference

A complete catalog of every reactive operator shipped by Onity. The everyday contract is `IOnityObservable<T>`: `Subject<T>`, `ReactiveProperty<T>`, every operator below, and the messaging bridge `broker.Observe<T>()` all return the same interface, so one operator chain composes over state, raw streams, and events alike.

The synchronous core lives in `Onity.Reactive` and is engine-free. The async/time operators are also engine-free (they take an injectable `OnityTimeProvider`), while the frame-loop scheduling operators (`ObserveOnMainThread`, `Delay`) live in the Unity bridge layer `Onity.Unity.Reactive` and depend on `UnityEngine`. The reactive emit path is designed to avoid per-call managed allocation: a synchronous operator allocates only at subscribe time and forwards each value without allocating. (Onity itself depends on ZLinq for the `Onity.Unity` layer; the reactive core does not use `System.Linq`.)

Conventions used in the tables below:

- **Operator** — the method name as you call it.
- **Signature** — the public signature (extension `this` receiver omitted for brevity; `source`/`first` is the receiver).
- **Description** — what it does.
- **Kind** — `Sync` (forwards synchronously, no timers/threads), `Time` (uses an `OnityTimeProvider` timer), `Async` (awaits a delegate / completes a `Task`), or `Schedule` (re-posts onto a Unity frame loop).

> Disposal is mandatory. Every `Subscribe` returns an `IDisposable`; an undisposed subscription leaks. Scope it with `AddTo(this)` in a MonoBehaviour or `AddTo(compositeDisposable)` in plain C#.

---

## Filtering and projection

| Operator | Signature | Description | Kind |
| --- | --- | --- | --- |
| `Where` | `Where<T>(Predicate<T> predicate) -> IOnityObservable<T>` | Forwards only values for which `predicate` returns true. | Sync |
| `Select` | `Select<TSource, TResult>(Func<TSource, TResult> selector) -> IOnityObservable<TResult>` | Projects each value through `selector`. | Sync |
| `DistinctUntilChanged` | `DistinctUntilChanged<T>(IEqualityComparer<T> comparer = null) -> IOnityObservable<T>` | Suppresses consecutive duplicate values. Defaults to `EqualityComparer<T>.Default`. | Sync |
| `Skip` | `Skip<T>(int count) -> IOnityObservable<T>` | Drops the first `count` values, forwards the rest. Throws `ArgumentOutOfRangeException` if `count < 0`. | Sync |
| `SkipWhile` | `SkipWhile<T>(Predicate<T> predicate) -> IOnityObservable<T>` | Drops leading values while `predicate` is true; forwards everything once it first returns false. | Sync |
| `Take` | `Take<T>(int count) -> IOnityObservable<T>` | Forwards the first `count` values then disposes the upstream subscription. `count == 0` returns `OnityObservable.Empty<T>()`. | Sync |
| `TakeWhile` | `TakeWhile<T>(Predicate<T> predicate) -> IOnityObservable<T>` | Forwards values while `predicate` is true; completes once it first returns false. | Sync |

## Stateful and pairing

| Operator | Signature | Description | Kind |
| --- | --- | --- | --- |
| `Scan` | `Scan<TSource, TState>(TState seed, Func<TState, TSource, TState> accumulator) -> IOnityObservable<TState>` | Folds each value into a running state and emits the state after every value. | Sync |
| `Pairwise` | `Pairwise<T>() -> IOnityObservable<OnityPair<T>>` | Emits each value paired with the previous one as `OnityPair<T>` (`Previous`, `Current`). Skips the first value until a second arrives. | Sync |
| `StartWith` | `StartWith<T>(T initialValue) -> IOnityObservable<T>` | Emits `initialValue` immediately on subscribe, then forwards source values. | Sync |
| `Buffer` (count) | `Buffer<T>(int count) -> IOnityObservable<IReadOnlyList<T>>` | Gathers values and emits a list every `count` values. Throws `ArgumentOutOfRangeException` if `count <= 0`. | Sync |

## Combining

| Operator | Signature | Description | Kind |
| --- | --- | --- | --- |
| `Merge` | `Merge<T>(params IOnityObservable<T>[] others) -> IOnityObservable<T>` | Forwards every value from the receiver and all `others`. An empty `others` returns the receiver unchanged. | Sync |
| `CombineLatest` | `CombineLatest<TFirst, TSecond, TResult>(IOnityObservable<TSecond> second, Func<TFirst, TSecond, TResult> resultSelector) -> IOnityObservable<TResult>` | Emits `resultSelector(latestFirst, latestSecond)` whenever either source emits, once both have produced at least one value. | Sync |
| `Sample` | `Sample<T, TSignal>(IOnityObservable<TSignal> sampler) -> IOnityObservable<T>` | Emits the latest source value each time `sampler` emits (once a source value is available). | Sync |

## Time-based

These operators take an optional `OnityTimeProvider` (deterministic in EditMode tests; pass a Unity provider such as `OnityTimeProviders.UpdateUnscaled` in gameplay so timers honor `Time.timeScale`). When omitted they default to `OnityTimeProvider.System` (wall-clock `Task.Delay`).

| Operator | Signature | Description | Kind |
| --- | --- | --- | --- |
| `Debounce` | `Debounce<T>(TimeSpan dueTime, OnityTimeProvider timeProvider = null) -> IOnityObservable<T>` | Emits the **last** value after a quiet window of `dueTime`. `dueTime == TimeSpan.Zero` returns the source unchanged. | Time |
| `ThrottleLast` | `ThrottleLast<T>(TimeSpan interval, OnityTimeProvider timeProvider = null) -> IOnityObservable<T>` | Emits the latest value once per `interval` (trailing edge). **Named `ThrottleLast`, not `Throttle`.** | Time |
| `Throttle` | `Throttle<T>(TimeSpan interval, OnityTimeProvider provider = null) -> IOnityObservable<T>` | Leading edge: emits the first value immediately, then ignores values until `interval` elapses (cool-down). | Time |
| `Buffer` (time) | `Buffer<T>(TimeSpan timeSpan, OnityTimeProvider provider = null) -> IOnityObservable<IReadOnlyList<T>>` | Emits the values gathered during each `timeSpan` window. Empty windows emit nothing. | Time |

> **`ThrottleLast` vs `Throttle`.** `ThrottleLast` is the trailing-edge sampler (emit the most recent value once per interval). `Throttle` is the leading-edge cool-down (emit the first value of a window, drop the rest). Reach for `ThrottleLast` when you want a periodic "latest value" tick; reach for `Throttle` when you want to fire immediately then debounce a burst.

## Asynchronous

`SelectAwait` and `WhereAwait` run their delegate sequentially (one value at a time, in order) and resume on a threadpool thread (they wrap the delegate in `Task.Run` internally). Do **not** touch `UnityEngine` APIs directly in a `Subscribe` placed after them — hop back to the main thread with `ObserveOnMainThread` first.

| Operator | Signature | Description | Kind |
| --- | --- | --- | --- |
| `SelectAwait` | `SelectAwait<TSource, TResult>(Func<TSource, CancellationToken, ValueTask<TResult>> selector) -> IOnityObservable<TResult>` | Sequential async projection. Each value is projected via the awaited `selector` before the next is processed. | Async |
| `WhereAwait` | `WhereAwait<T>(Func<T, CancellationToken, ValueTask<bool>> predicate) -> IOnityObservable<T>` | Sequential async filter. Forwards a value only when the awaited `predicate` returns true. | Async |

## Completion signals

| Operator | Signature | Description | Kind |
| --- | --- | --- | --- |
| `TakeUntilCancellation` | `TakeUntilCancellation<T>(CancellationToken cancellationToken) -> IOnityObservable<T>` | Stops forwarding values when the token is canceled. An already-canceled token returns `Empty<T>()`. | Sync |
| `TakeUntil` (token) | `TakeUntil<T>(CancellationToken cancellationToken) -> IOnityObservable<T>` | Stops the source when the token is canceled. Bridges through the v2 observer pipeline (propagates completion). | Async |
| `TakeUntil` (task) | `TakeUntil<T>(Task untilTask) -> IOnityObservable<T>` | Stops the source when `untilTask` completes; a faulted task surfaces its exception to `OnError`. | Async |
| `FirstAsync` | `FirstAsync<T>(CancellationToken cancellationToken = default) -> Task<T>` | Returns a task completed by the first emitted value. A canceled token completes the task as canceled (`OperationCanceledException`). | Async |
| `ToTask` | `ToTask(this IOnityObservable<Unit> source, CancellationToken cancellationToken = default) -> Task` | Awaits a single `Unit` emission. Convenience over `FirstAsync` for unit streams. | Async |

> `OperationCanceledException` from `FirstAsync` / `ToTask` / `TakeUntil` is **normal cancellation**, not a bug. Catch it where you start the async flow.

## Frame-loop scheduling (`Onity.Unity.Reactive`)

These operators move emission back onto a Unity loop. They are the required hop after `SelectAwait` / `WhereAwait` (which resume off the main thread) before any observer that touches Unity API.

| Operator | Signature | Description | Kind |
| --- | --- | --- | --- |
| `ObserveOn` | `ObserveOn<T>(OnityFrameProvider frameProvider) -> IOnityObservable<T>` | Re-posts each value onto the provider's frame tick (buffered, replayed on the next frame) rather than forwarding synchronously. Engine-agnostic over any `OnityFrameProvider`. | Schedule |
| `ObserveOnMainThread` | `ObserveOnMainThread<T>() -> IOnityObservable<T>` | Re-posts each value onto the Unity `Update` loop (`OnityFrameProviders.Update`). | Schedule |
| `ObserveOnMainThread` (phase) | `ObserveOnMainThread<T>(OnityUnityFrameProvider frameProvider) -> IOnityObservable<T>` | Re-posts onto a chosen Unity loop phase (e.g. `OnityFrameProviders.FixedUpdate` / `LateUpdate`). | Schedule |
| `Delay` | `Delay<T>(float delaySeconds, bool useUnscaledTime = false) -> IOnityObservable<T>` | Delays each value by `delaySeconds` using a Unity countdown timer. `delaySeconds <= 0` returns the source unchanged; negative throws. | Schedule |

> `ObserveOn` is defined on the abstract `OnityFrameProvider` in the core, so it is available wherever a frame provider is; `ObserveOnMainThread` and `Delay` are the Unity-specific conveniences. There is currently no separate post-back operator beyond these — keep async results pure, or marshal back with `ObserveOnMainThread`.

---

## Factories

Static helpers on the `OnityObservable` type (in `Onity.Reactive`).

| Factory | Signature | Description |
| --- | --- | --- |
| `OnityObservable.FromEvent` | `FromEvent<T>(Action<Action<T>> addHandler, Action<Action<T>> removeHandler) -> IOnityObservable<T>` | Wraps a callback-style event pair as an observable; unsubscribing removes the handler. |
| `OnityObservable.Return` | `Return<T>(T value) -> IOnityObservable<T>` | Emits one value to each subscriber on subscribe. |
| `OnityObservable.Empty` | `Empty<T>() -> IOnityObservable<T>` | A shared observable that never emits. |

> Not shipped (intentionally): there is no `Never` / `Create` / `Defer` factory, and there is no `Window` / `Zip` / `Switch` / `Concat`, nor `Publish` / `Share` / `RefCount`. Model "the last value a late subscriber must see" as a `ReactiveProperty<T>`.

## Subscribe overloads

`Subscribe` is the terminal operator. The everyday form takes an `Action<T>`; the lifecycle form adds error and completion callbacks. Both are extension methods over `IOnityObservable<T>`.

| Overload | Signature | Description |
| --- | --- | --- |
| `Subscribe` (value) | `Subscribe<T>(Action<T> onNext) -> IDisposable` | Subscribes with a value callback. Wraps `onNext` in an `Observer<T>`. |
| `Subscribe` (lifecycle) | `Subscribe<T>(Action<T> onNext, Action<Exception> onError, Action<OnityResult> onCompleted) -> IDisposable` | Subscribes with value, error, and completion callbacks. |
| `AddTo` (bag) | `AddTo(this IDisposable, CompositeDisposable) -> IDisposable` | Adds the subscription to a `CompositeDisposable`; returns the same disposable for chaining. (`Onity.Reactive`.) |
| `AddTo` (component) | `AddTo(this IDisposable, Component) -> IDisposable` | Disposes the subscription when the component is destroyed (alias for `TakeUntilDestroy`). (`Onity.Unity.Reactive`.) |
| `TakeUntilDestroy` | `TakeUntilDestroy(this IDisposable, Component) -> IDisposable` | Disposes on component destroy. (`Onity.Unity.Reactive`.) |
| `TakeUntilDisable` | `TakeUntilDisable(this IDisposable, Behaviour) -> IDisposable` | Disposes on behaviour disable. (`Onity.Unity.Reactive`.) |

> The Unity lifetime helpers take a `Component` / `Behaviour` (pass `this` from a MonoBehaviour). There is no `AddTo(GameObject)` overload.

---

## Unity frame loops and timers (`OnityUnityObservable`)

Frame and timer sources that feed the operators above. They are pumped by a hidden `DontDestroyOnLoad` object created on first use.

| Member | Signature | Description |
| --- | --- | --- |
| `EveryUpdate` | `EveryUpdate() -> IOnityObservable<Unit>` | Emits once per rendered frame (shared stream). |
| `EveryFixedUpdate` | `EveryFixedUpdate() -> IOnityObservable<Unit>` | Emits once per physics step. |
| `EveryLateUpdate` | `EveryLateUpdate() -> IOnityObservable<Unit>` | Emits once per late update. |
| `EveryUpdate` (cancel) | `EveryUpdate(CancellationToken) -> IOnityObservable<Unit>` | Update stream that stops on cancel (also on `EveryFixedUpdate`/`EveryLateUpdate`). |
| `EveryUpdate` (threaded) | `EveryUpdate(OnityUnityThreadMode threadMode, int jobWorkItemCount = 64, int minCommandsPerJob = 32) -> IOnityObservable<Unit>` | Optional job/Burst/DOTS threading boundary before each emit (also on `EveryFixedUpdate`/`EveryLateUpdate`, and with a `CancellationToken` overload). |
| `Timer` | `Timer(float dueTimeSeconds, bool useUnscaledTime = false) -> IOnityObservable<Unit>` | Emits one `Unit` after `dueTimeSeconds`. |
| `Interval` | `Interval(float intervalSeconds, bool useUnscaledTime = false) -> IOnityObservable<int>` | Emits an incrementing tick index every `intervalSeconds`. |

### Built-in providers

| Provider set | Members |
| --- | --- |
| `OnityFrameProviders` | `Update`, `FixedUpdate`, `LateUpdate` (each an `OnityUnityFrameProvider`). |
| `OnityTimeProviders` | `UpdateScaled` / `UpdateUnscaled` / `UpdateRealtime`, `FixedScaled` / `FixedUnscaled` / `FixedRealtime`, `LateScaled` / `LateUnscaled` / `LateRealtime` (each an `OnityUnityTimeProvider`). |
| `OnityTimeProvider.System` | Wall-clock provider used by `Debounce` / `ThrottleLast` / `Throttle` / `Buffer` when no provider is passed. |

---

## At a glance: what is and is not shipped

Shipped: `Where`, `Select`, `DistinctUntilChanged`, `Skip`, `SkipWhile`, `Take`, `TakeWhile`, `StartWith`, `Scan`, `Pairwise`, `Merge`, `CombineLatest`, `Sample`, `Buffer` (count + time), `Debounce`, `ThrottleLast`, `Throttle` (leading edge), `SelectAwait`, `WhereAwait`, `TakeUntil` (token + task), `TakeUntilCancellation`, `FirstAsync`, `ToTask`, `ObserveOn`, `ObserveOnMainThread`, `Delay`.

Not shipped: `Window`, `Zip`, `Switch`, `Concat`, `Publish`, `Share`, `RefCount`; factories `Never` / `Create` / `Defer`. Model current state with `ReactiveProperty<T>` and transient notifications with messages (see [Messaging API](messaging-api.md)).

See the [Reactive guide](../guide/reactive.md) for narrative usage and recipes.
