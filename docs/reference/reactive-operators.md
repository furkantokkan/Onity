---
title: "Reactive Operators"
parent: "Reference"
nav_order: 2
description: "Every reactive operator, factory, subscribe overload, lifetime helper, Unity frame source, provider and OnityTask bridge that Onity ships, with signatures."
---

# Reactive Operators

Use this catalog to look up the exact shape of a reactive member: every
operator, factory, subscribe overload, lifetime helper, Unity frame source,
provider and `OnityTask` bridge that Onity ships for `IOnityObservable<T>`.
The [Reactive guide](../guide/reactive.html) explains when to use what.

`Subject<T>`, `ReactiveProperty<T>`, every operator below and the message
bridge `Observe<T>()` return `IOnityObservable<T>`, so one chain composes over
local streams, shared state and messages. The synchronous, time, async and
thread-pool operators live in `Onity.Reactive` and are engine-free. Frame
scheduling (`ObserveOnMainThread`, `Delay`), the frame and timer sources and
the lifetime helpers live in `Onity.Unity.Reactive`; the `OnityTask` bridges
live in `Onity.Unity.Async`. A synchronous operator allocates when it is
subscribed and forwards each value without allocating; the core uses no
`System.Linq` and no third-party runtime dependency.

Conventions:

- **Signature** omits the extension receiver; the receiver is the source stream unless noted.
- **Kind**: `Sync` forwards synchronously; `Time` waits on an `OnityTimeProvider`; `Async` awaits a delegate or a task; `Thread` uses the .NET thread pool; `Frame` re-posts onto a Unity loop.
- Every operator throws `ArgumentNullException` for a null source or delegate.

Every `Subscribe` returns an `IDisposable`; dispose it, or scope it with
`AddTo(this)`, `TakeUntilDisable(this)`, `AddTo(compositeDisposable)` or
`AddTo(scope)`.

---

## Filtering and projection

| Operator | Signature | Namespace | Description | Kind |
| --- | --- | --- | --- | --- |
| `Where` | `Where<T>(Predicate<T> predicate) -> IOnityObservable<T>` | `Onity.Reactive` | Forwards the values for which `predicate` returns true. | Sync |
| `Select` | `Select<TSource, TResult>(Func<TSource, TResult> selector) -> IOnityObservable<TResult>` | `Onity.Reactive` | Projects each value through `selector`. | Sync |
| `DistinctUntilChanged` | `DistinctUntilChanged<T>(IEqualityComparer<T> comparer = null) -> IOnityObservable<T>` | `Onity.Reactive` | Suppresses consecutive duplicates; `EqualityComparer<T>.Default` when `comparer` is null. | Sync |
| `Skip` | `Skip<T>(int count) -> IOnityObservable<T>` | `Onity.Reactive` | Drops the first `count` values. `count == 0` returns the source; a negative count throws `ArgumentOutOfRangeException`. | Sync |
| `SkipWhile` | `SkipWhile<T>(Predicate<T> predicate) -> IOnityObservable<T>` | `Onity.Reactive` | Drops leading values while `predicate` is true, then forwards everything. | Sync |
| `Take` | `Take<T>(int count) -> IOnityObservable<T>` | `Onity.Reactive` | Forwards the first `count` values, then disposes the upstream subscription. `count == 0` returns `OnityObservable.Empty<T>()`; negative throws. | Sync |
| `TakeWhile` | `TakeWhile<T>(Predicate<T> predicate) -> IOnityObservable<T>` | `Onity.Reactive` | Forwards values while `predicate` is true; the first false value disposes the upstream subscription. | Sync |
| `StartWith` | `StartWith<T>(T initialValue) -> IOnityObservable<T>` | `Onity.Reactive` | Emits `initialValue` on subscribe, then the source values. | Sync |

## State and combination

| Operator | Signature | Namespace | Description | Kind |
| --- | --- | --- | --- | --- |
| `Scan` | `Scan<TSource, TState>(TState seed, Func<TState, TSource, TState> accumulator) -> IOnityObservable<TState>` | `Onity.Reactive` | Folds each value into a running state and emits the state after every value. | Sync |
| `Pairwise` | `Pairwise<T>() -> IOnityObservable<OnityPair<T>>` | `Onity.Reactive` | Emits each value with the previous one as `OnityPair<T>` (`Previous`, `Current`); the first value only primes the pair. | Sync |
| `Buffer` (count) | `Buffer<T>(int count) -> IOnityObservable<IReadOnlyList<T>>` | `Onity.Reactive` | Emits a new list every `count` values. `count <= 0` throws `ArgumentOutOfRangeException`. | Sync |
| `Merge` | `Merge<T>(params IOnityObservable<T>[] others) -> IOnityObservable<T>` | `Onity.Reactive` | Forwards every value from the receiver and all `others`. No `others` returns the receiver; a null element throws. | Sync |
| `CombineLatest` | `CombineLatest<TFirst, TSecond, TResult>(IOnityObservable<TSecond> second, Func<TFirst, TSecond, TResult> resultSelector) -> IOnityObservable<TResult>` | `Onity.Reactive` | Emits `resultSelector(latestFirst, latestSecond)` whenever either source emits, once both have a value. Two sources only. | Sync |
| `Sample` | `Sample<T, TSignal>(IOnityObservable<TSignal> sampler) -> IOnityObservable<T>` | `Onity.Reactive` | Emits the latest source value each time `sampler` emits, once a source value exists. | Sync |

## Time

These wait on an `OnityTimeProvider`. When `timeProvider` is null they use
`OnityTimeProvider.System` (wall-clock `Task.Delay`); in gameplay pass one of
`OnityTimeProviders` so the wait follows a Unity loop and time mode. A
non-positive `interval` or `timeSpan` throws `ArgumentOutOfRangeException`.

| Operator | Signature | Namespace | Description | Kind |
| --- | --- | --- | --- | --- |
| `Debounce` | `Debounce<T>(TimeSpan dueTime, OnityTimeProvider timeProvider = null) -> IOnityObservable<T>` | `Onity.Reactive` | Emits the last value after a quiet window of `dueTime`. `TimeSpan.Zero` returns the source; negative throws. | Time |
| `ThrottleLast` | `ThrottleLast<T>(TimeSpan interval, OnityTimeProvider timeProvider = null) -> IOnityObservable<T>` | `Onity.Reactive` | Trailing edge: emits the latest value once per `interval` while values flow. | Time |
| `Throttle` | `Throttle<T>(TimeSpan interval, OnityTimeProvider provider = null) -> IOnityObservable<T>` | `Onity.Reactive` | Leading edge: emits the first value, then drops values until `interval` elapses. | Time |
| `Buffer` (time) | `Buffer<T>(TimeSpan timeSpan, OnityTimeProvider provider = null) -> IOnityObservable<IReadOnlyList<T>>` | `Onity.Reactive` | Emits the values gathered in each `timeSpan` window; an empty window emits nothing. | Time |

`ThrottleLast` is the periodic "latest value" tick; `Throttle` fires at once and
then ignores the rest of a burst. UniRx's `Throttle` corresponds to `Debounce`.

## Async

`SelectAwait` and `WhereAwait` run their delegate one value at a time, in order,
inside `Task.Run`, and forward on the thread that completed it. Hop back with
`ObserveOnMainThread()` before touching `UnityEngine`. A delegate that throws
ends the subscription without notifying the subscriber.

| Operator | Signature | Namespace | Description | Kind |
| --- | --- | --- | --- | --- |
| `SelectAwait` | `SelectAwait<TSource, TResult>(Func<TSource, CancellationToken, ValueTask<TResult>> selector) -> IOnityObservable<TResult>` | `Onity.Reactive` | Sequential async projection; the token is canceled when the subscription is disposed. | Async |
| `WhereAwait` | `WhereAwait<T>(Func<T, CancellationToken, ValueTask<bool>> predicate) -> IOnityObservable<T>` | `Onity.Reactive` | Sequential async filter. | Async |
| `TakeUntil` (token) | `TakeUntil<T>(CancellationToken cancellationToken) -> IOnityObservable<T>` | `Onity.Reactive` | Stops forwarding when the token is canceled; an already canceled token returns an empty stream. | Async |
| `TakeUntil` (task) | `TakeUntil<T>(Task untilTask) -> IOnityObservable<T>` | `Onity.Reactive` | Stops forwarding when `untilTask` completes, faults or cancels; an already completed task returns an empty stream. | Async |
| `TakeUntilCancellation` | `TakeUntilCancellation<T>(CancellationToken cancellationToken) -> IOnityObservable<T>` | `Onity.Reactive` | The synchronous form: disposes the upstream subscription when the token is canceled; an already canceled token returns `Empty<T>()`. | Sync |

## Thread pool

Pure managed work only. Re-marshal with `ObserveOnMainThread()` before a
subscriber that touches Unity objects.

| Operator | Signature | Namespace | Description | Kind |
| --- | --- | --- | --- | --- |
| `ObserveOnThreadPool` | `ObserveOnThreadPool<T>() -> IOnityObservable<T>` | `Onity.Reactive` | Re-posts each value onto one thread-pool worker in source order. | Thread |
| `SelectOnThreadPool` | `SelectOnThreadPool<TSource, TResult>(Func<TSource, TResult> selector, int maxConcurrency = 0) -> IOnityObservable<TResult>` | `Onity.Reactive` | Runs `selector` on up to `maxConcurrency` workers; `0` means the processor count, negative throws. Results emit as workers finish; `maxConcurrency: 1` keeps source order. | Thread |
| `SelectOnThreadPool` (token) | `SelectOnThreadPool<TSource, TResult>(Func<TSource, CancellationToken, TResult> selector, int maxConcurrency = 0) -> IOnityObservable<TResult>` | `Onity.Reactive` | The same with a token that is canceled when the subscription is disposed. | Thread |

An exception thrown by `selector` or by a downstream observer on the worker goes
to `OnityObservableExceptionHandler`; the stream continues.

## Frame scheduling

| Operator | Signature | Namespace | Description | Kind |
| --- | --- | --- | --- | --- |
| `ObserveOn` | `ObserveOn<T>(OnityFrameProvider frameProvider) -> IOnityObservable<T>` | `Onity.Reactive` | Queues each value and delivers the queue on the provider's next frame tick. Works with any `OnityFrameProvider`. | Frame |
| `ObserveOnMainThread` | `ObserveOnMainThread<T>() -> IOnityObservable<T>` | `Onity.Unity.Reactive` | `ObserveOn(OnityFrameProviders.Update)`. | Frame |
| `ObserveOnMainThread` (phase) | `ObserveOnMainThread<T>(OnityUnityFrameProvider frameProvider) -> IOnityObservable<T>` | `Onity.Unity.Reactive` | `ObserveOn` with `OnityFrameProviders.FixedUpdate` or `LateUpdate`. | Frame |
| `Delay` | `Delay<T>(float delaySeconds, bool useUnscaledTime = false) -> IOnityObservable<T>` | `Onity.Unity.Reactive` | Delays each value with an `OnityCountdownTimer`. `0` returns the source; negative throws. | Frame |

## Task and stream bridges

| Member | Signature | Namespace | Description |
| --- | --- | --- | --- |
| `FirstAsync` | `FirstAsync<T>(CancellationToken cancellationToken = default) -> Task<T>` | `Onity.Reactive` | Completes with the first value and unsubscribes. A canceled token completes the task as canceled. |
| `ToTask` | `ToTask(this IOnityObservable<Unit> source, CancellationToken cancellationToken = default) -> Task` | `Onity.Reactive` | `FirstAsync` for a `Unit` stream. |
| `FirstOnityTask` | `FirstOnityTask<T>(CancellationToken cancellationToken = default) -> OnityTask<T>` | `Onity.Unity.Async` | `FirstAsync` as an `OnityTask<T>`. |
| `ToOnityTask` (Unit) | `ToOnityTask(this IOnityObservable<Unit> source, CancellationToken cancellationToken = default) -> OnityTask` | `Onity.Unity.Async` | `ToTask` as an `OnityTask`. |
| `ToOnityTask` (last) | `ToOnityTask<T>(CancellationToken cancellationToken = default) -> OnityTask<T>` | `Onity.Unity.Async` | Completes with the last value when the source completes; a hot source that never completes needs the token. |
| `ToOnityTask` (first or last) | `ToOnityTask<T>(bool useFirstValue, CancellationToken cancellationToken = default) -> OnityTask<T>` | `Onity.Unity.Async` | `useFirstValue: true` completes with the first value. A source that completes without a value faults the task with `InvalidOperationException`. |
| `ToObservable` | `ToObservable<T>(this OnityTask<T> task) -> IOnityObservable<T>`; `ToObservable(this OnityTask task) -> IOnityObservable<Unit>` | `Onity.Unity.Async` | Exposes a task's result (or one `Unit`) to every subscriber, including late ones. |
| `AsOnityAsyncEnumerable` | `AsOnityAsyncEnumerable<T>(int capacity) -> IOnityAsyncEnumerable<T>` | `Onity.Unity.Async` | Buffers a subscription for pull-based consumption; a full buffer faults after the accepted values drain. |
| `AsObservable` | `AsObservable<T>(this IOnityAsyncEnumerable<T> source) -> IOnityObservable<T>` | `Onity.Unity.Async` | Enumerates once per subscription and pushes the items. |

`OperationCanceledException` from these is normal cancellation; catch it where
the flow starts.

## Reactive property members

| Member | Signature | Namespace | Description |
| --- | --- | --- | --- |
| Constructor | `new ReactiveProperty<T>(T initialValue = default, IEqualityComparer<T> comparer = null)` | `Onity.Reactive` | The comparer decides which sets are changes; `EqualityComparer<T>.Default` when null. |
| `Value` | `T Value { get; set; }` | `Onity.Reactive` | The setter calls `SetValue`. On `IReadOnlyReactiveProperty<T>` the property is read-only. |
| `SetValue` | `SetValue(T value) -> bool` | `Onity.Reactive` | Publishes when the value differs under the comparer and returns `true`; an equal value returns `false` and publishes nothing. Throws `ObjectDisposedException` after `Dispose()`. |
| `Subscribe` | `Subscribe(Observer<T> observer, bool emitCurrentValue = true) -> IDisposable` | `Onity.Reactive` | Also on `IReadOnlyReactiveProperty<T>`. Emits the current value first unless `emitCurrentValue` is false, then each change. |
| `Dispose` | `Dispose() -> void` | `Onity.Reactive` | Disposes the inner subject; observers are not notified, and a pending `WaitAsync` is not completed. |

`IReadOnlyReactiveProperty<T>` declares only `Value` and the `Subscribe` above;
it does not implement `IOnityObservable<T>`, so the operators need the
`ReactiveProperty<T>` itself or a wrapper (`new OnityObservable<T>(observer => property.Subscribe(observer))`).

## Reactive property waits

Pooled waits on any `IReadOnlyReactiveProperty<T>`; use them on the property's
thread (the main thread) and pass the scope token so a wait ends with its
scope. See [Await a ReactiveProperty with OnityTask](../guide/reactive.html#await-a-reactiveproperty-with-onitytask)
for the rules.

| Member | Signature | Namespace | Description |
| --- | --- | --- | --- |
| `WaitAsync` | `WaitAsync<T>(this IReadOnlyReactiveProperty<T> property, CancellationToken cancellationToken = default) -> OnityTask<T>` | `Onity.Unity.Async` | The next accepted change; the current value does not complete it. |
| `WaitUntilAsync` | `WaitUntilAsync<T>(this IReadOnlyReactiveProperty<T> property, Func<T, bool> predicate, CancellationToken cancellationToken = default) -> OnityTask<T>` | `Onity.Unity.Async` | The first matching value; completes synchronously when the current value matches. A throwing predicate faults the task; a canceled token wins over a matching current value. |
| `AsLatestAsyncEnumerable` | `AsLatestAsyncEnumerable<T>(this IReadOnlyReactiveProperty<T> property, bool includeCurrent = true) -> IOnityAsyncEnumerable<T>` | `Onity.Unity.Async` | A conflating stream: each move yields the latest value since the previous move or waits for the next change. Never ends on its own; the token cancels it. |
| `ToAsyncReactiveProperty` | `ToAsyncReactiveProperty<T>(this IReadOnlyReactiveProperty<T> property, CancellationToken cancellationToken) -> OnityReadOnlyAsyncReactiveProperty<T>` | `Onity.Unity.Async` | Starts with the current value and follows the property until the token is canceled or it is disposed. |
| `BindTo` | `BindTo<T>(this IOnityReadOnlyAsyncReactiveProperty<T> source, ReactiveProperty<T> target, CancellationToken cancellationToken) -> IDisposable` | `Onity.Unity.Async` | Writes the async property's current and later values into `target`; dispose the result to stop. Main thread only. |

---

## Factories and sources

| Member | Signature | Namespace | Description |
| --- | --- | --- | --- |
| `OnityObservable.FromEvent` | `FromEvent<T>(Action<Action<T>> addHandler, Action<Action<T>> removeHandler) -> IOnityObservable<T>` | `Onity.Reactive` | Wraps a callback-style event pair; disposing the subscription removes the handler. |
| `OnityObservable.Return` | `Return<T>(T value) -> IOnityObservable<T>` | `Onity.Reactive` | Emits `value` to each subscriber on subscribe. |
| `OnityObservable.Empty` | `Empty<T>() -> IOnityObservable<T>` | `Onity.Reactive` | A shared instance that never emits. |
| Custom source | `new OnityObservable<T>(Func<Observer<T>, IDisposable> subscribe)` | `Onity.Reactive` | The function receives the observer callback and returns the disposable that ends the subscription (`DisposableAction.Empty` from `Onity.Core` when there is nothing to release). |

There are no `Never`, `Create` or `Defer` factories, and no `Window`, `Zip`,
`Switch`, `Concat`, `Publish`, `Share` or `RefCount` on `IOnityObservable<T>`.

## Subscribe overloads

`Observer<T>` is `delegate void Observer<T>(T value)`, the callback every
`IOnityObservable<T>` accepts. The extension overloads below are what you
normally write.

| Overload | Signature | Namespace | Description |
| --- | --- | --- | --- |
| `Subscribe` (value) | `Subscribe<T>(Action<T> onNext) -> IDisposable` | `Onity.Reactive` | On `Subject<T>`, `ReactiveProperty<T>` and the operator observables, stores `onNext` in the subscription node and invokes it directly, with no delegate wrapper; any other `IOnityObservable<T>` receives it wrapped in an `Observer<T>`. |
| `Subscribe` (lifecycle) | `Subscribe<T>(Action<T> onNext, Action<Exception> onError, Action<OnityResult> onCompleted) -> IDisposable` | `Onity.Reactive` | Lifecycle callbacks. `onCompleted` runs with a success `OnityResult` when the subscription is disposed. `onError` is raised only by sources that report faults, such as `OnityTask.ToObservable()` and `IOnityAsyncEnumerable<T>.AsObservable()`; the core operators do not forward errors to it. |

Every `IOnityObservable<T>` also accepts a subclass of the public abstract
`OnityObserver<T>` through `Subscribe(OnityObserver<T>)`: override
`OnNextCore`, optionally `OnErrorCore`, `OnCompletedCore` and `OnDisposed`, and
the observer is stopped (`IsStopped`) after an error, a completion or
`Dispose`.

### Lifetime helpers

| Helper | Signature | Namespace | Disposes when |
| --- | --- | --- | --- |
| `AddTo` (bag) | `AddTo(this IDisposable disposable, CompositeDisposable compositeDisposable) -> IDisposable` | `Onity.Reactive` | The bag is cleared or disposed. |
| `AddTo` (scope) | `AddTo<TDisposable>(this TDisposable disposable, IOnityScopeLifetime scope) -> TDisposable` | `Onity.DI` | The DI scope ends; an ended scope disposes at once. See [DI API](di-api.html). |
| `AddTo` (component) | `AddTo(this IDisposable disposable, Component owner) -> IDisposable` | `Onity.Unity.Reactive` | The component is destroyed; the same as `TakeUntilDestroy`. |
| `TakeUntilDestroy` | `TakeUntilDestroy(this IDisposable disposable, Component owner) -> IDisposable` | `Onity.Unity.Reactive` | The component is destroyed. |
| `TakeUntilDisable` | `TakeUntilDisable(this IDisposable disposable, Behaviour owner) -> IDisposable` | `Onity.Unity.Reactive` | The behaviour is disabled, and on destroy. |

The helpers return the same disposable, so they chain after `Subscribe`. There
is no `AddTo(GameObject)` overload.

---

## Unity frame loops and timers

`OnityUnityObservable` (`Onity.Unity.Reactive`). The frame streams share one
hidden pump component created on first use.

| Member | Signature | Namespace | Description |
| --- | --- | --- | --- |
| `EveryUpdate` | `EveryUpdate() -> IOnityObservable<Unit>` | `Onity.Unity.Reactive` | Once per rendered frame. |
| `EveryFixedUpdate` | `EveryFixedUpdate() -> IOnityObservable<Unit>` | `Onity.Unity.Reactive` | Once per physics step. |
| `EveryLateUpdate` | `EveryLateUpdate() -> IOnityObservable<Unit>` | `Onity.Unity.Reactive` | Once per late update. |
| `Every*` (token) | `EveryUpdate(CancellationToken cancellationToken)` and the `Fixed` / `Late` forms | `Onity.Unity.Reactive` | The stream ends when the token is canceled. |
| `Every*` (thread mode) | `EveryUpdate(OnityUnityThreadMode threadMode, int jobWorkItemCount = 64, int minCommandsPerJob = 32)`, also with a `CancellationToken` after `threadMode`, and the `Fixed` / `Late` forms | `Onity.Unity.Reactive` | Adds a job boundary around each emission: `SingleThread`, `JobMultiThread`, `BurstJobMultiThread`, `DotsEventDriven`. Observers still run on the main thread. Non-positive job parameters throw. |
| `Timer` | `Timer(float dueTimeSeconds, bool useUnscaledTime = false) -> IOnityObservable<Unit>` | `Onity.Unity.Reactive` | One `Unit` after the delay; `0` emits on subscribe, negative throws. |
| `Interval` | `Interval(float intervalSeconds, bool useUnscaledTime = false) -> IOnityObservable<int>` | `Onity.Unity.Reactive` | The cumulative tick count, starting at 1, every interval; non-positive throws. |

### Providers

| Provider set | Namespace | Members |
| --- | --- | --- |
| `OnityFrameProviders` | `Onity.Unity.Reactive` | `Update`, `FixedUpdate`, `LateUpdate`: `OnityUnityFrameProvider` instances for `ObserveOn`. |
| `OnityTimeProviders` | `Onity.Unity.Reactive` | `UpdateScaled`, `UpdateUnscaled`, `UpdateRealtime`, `FixedScaled`, `FixedUnscaled`, `FixedRealtime`, `LateScaled`, `LateUnscaled`, `LateRealtime`: `OnityUnityTimeProvider` instances for the time operators. `Realtime` uses `Task.Delay`; the others count the loop's scaled or unscaled delta time. |
| `OnityTimeProvider.System` | `Onity.Reactive` | The wall-clock default when no provider is passed. Subclass `OnityTimeProvider` (`DelayAsync`) for deterministic tests. |

### Timers

`OnityCountdownTimer(durationSeconds, useUnscaledTime, autoUpdate)`,
`OnityIntervalTimer(intervalSeconds, ...)` and `OnityStopwatchTimer(...)`
(`Onity.Unity.Reactive`) implement `IOnityTimer`: `Start`, `Stop`, `Pause`,
`Resume`, `Reset`, `Tick(deltaTime)`, `IsRunning`, `IsFinished`,
`CurrentTimeSeconds`, `Progress01`, and the `Started`, `Stopped`, `Completed`
events (`IntervalElapsed(int)` on the interval timer). With `autoUpdate` they
advance from the shared Update stream.

### Input System

Compiled with `ENABLE_INPUT_SYSTEM`.

| Member | Signature | Namespace | Description |
| --- | --- | --- | --- |
| `StartedAsObservable` | `StartedAsObservable(this InputAction action, CancellationToken cancellationToken = default) -> IOnityObservable<InputAction.CallbackContext>` | `Onity.Unity.Input` | The action's `started` callbacks; a cancelable token ends the stream. |
| `PerformedAsObservable` | same shape | `Onity.Unity.Input` | The `performed` callbacks. |
| `CanceledAsObservable` | same shape | `Onity.Unity.Input` | The `canceled` callbacks. |
| `OnityReactiveInputPlayer` | `new OnityReactiveInputPlayer(InputActionAsset actionAsset, float longPressDurationSeconds = 0.35f)` | `Onity.Unity.Input` | Context stack (`PushContext`, `PopContext`, `SetContext`, `ClearContexts`) and `GetButtonObservable`, `GetVector2Observable`, `GetFloatObservable`, `GetLongPressObservable`, `GetLongPressProgressObservable` by action name. Long-press streams exist only on this type, not as `InputAction` extensions. |

---

## Errors

| Situation | Behavior |
| --- | --- |
| A subscriber throws during `Subject<T>.OnNext` or a `ReactiveProperty<T>` set | Caught and passed to `OnityObservableExceptionHandler.Handler` (a no-op until you assign one); the other subscribers are still notified. |
| A thread-pool selector or observer throws | Passed to `OnityObservableExceptionHandler`; the stream continues. |
| A `SelectAwait` / `WhereAwait` delegate throws | The subscription ends; nothing is forwarded to the subscriber. |
| A subscriber throws in a stream built from `Observe<T>()` | Propagates to the message `Publish` call; see [Messaging API](messaging-api.html#exceptions). |
| `ObjectDisposedException` | `OnNext`, `Subscribe` or `SetValue` after `Dispose()`. |
| `ArgumentNullException` / `ArgumentOutOfRangeException` | A null source or delegate; a negative count or a non-positive interval. |
| `OperationCanceledException` | Normal cancellation of `FirstAsync`, `ToTask`, the `OnityTask` bridges and the property waits. |
| `OnityReactiveException` | Declared for reactive-core failures; the shipped operators throw the standard exceptions above. |

## Diagnostics

`OnityObservableTracker` (`Onity.Reactive`): `EnableTracking`,
`EnableStackTrace`, `GetSnapshot(List<OnityTrackedObservableInfo> results, bool includeDisposed = true)`,
`ClearDisposed()`, `ClearAll()`. The Editor window `Onity/Tools/Observable Tracker`
reads the same rows.

---

## At a glance

Shipped: `Where`, `Select`, `DistinctUntilChanged`, `Skip`, `SkipWhile`,
`Take`, `TakeWhile`, `StartWith`, `Scan`, `Pairwise`, `Buffer` (count and
time), `Merge`, `CombineLatest`, `Sample`, `Debounce`, `ThrottleLast`,
`Throttle`, `SelectAwait`, `WhereAwait`, `TakeUntil` (token and task),
`TakeUntilCancellation`, `ObserveOnThreadPool`, `SelectOnThreadPool`,
`ObserveOn`, `ObserveOnMainThread`, `Delay`, `FirstAsync`, `ToTask`,
`FirstOnityTask`, `ToOnityTask`, `ToObservable`, `AsOnityAsyncEnumerable`,
`AsObservable`, `WaitAsync`, `WaitUntilAsync`, `AsLatestAsyncEnumerable`,
`ToAsyncReactiveProperty`, `BindTo`.

Not shipped: `Window`, `Zip`, `Switch`, `Concat`, `Publish`, `Share`,
`RefCount`; the factories `Never`, `Create`, `Defer`. Pull-based async streams
(`IOnityAsyncEnumerable<T>`) have `Zip`, `Concat`, `Publish` and `Queue`; see
[Stream operators](../guide/onitytask.html#stream-operators). Model current
state with `ReactiveProperty<T>` and notifications with messages
([Messaging API](messaging-api.html)).

The measured comparison with R3 and UniRx is in
[Reactive vs R3 and UniRx](../comparisons/reactive-vs-r3-unirx.html); the
[Reactive guide](../guide/reactive.html) has the narrative and recipes.
