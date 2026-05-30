# 03 - Reactive Design

This is the headline design document for `Onity.Reactive` plus the closely
related `Onity.Async` and reactive helpers in `Onity.Unity`. It captures the
current implementation, the target operator catalog, threading modes, and
the public API contract.

## 1. Current implementation summary

**Locations:**

- `Assets/Onity-Packages/Onity/Runtime/Reactive/Scripts/` (pure C# core)
- `Assets/Onity-Packages/Onity/Runtime/Unity/Scripts/Reactive/` (Unity bridges)
- `Assets/Onity-Packages/Onity/Runtime/Unity/Scripts/Async/` (async helpers,
  will extract to `Onity.Async` in Phase 0)

**Core types already shipped:**

- `IOnityObservable<T>` (subscribe contract)
- `Observer<T>` delegate (`void Observer<T>(T value)`)
- `OnityObserver<T>` (lifecycle-aware observer: OnNext / OnCompleted / Dispose)
- `Subject<T>` (multicast subject, swap-back unsubscribe, no allocation on
  steady-state OnNext)
- `ReactiveProperty<T>` (with default-comparer change detection, emits current
  value on subscribe)
- `CompositeDisposable`
- `OnityObservable` static factory (`Empty`, `Return`, `FromEvent`). There is
  **no** `Never`/`Create`/`Defer` on the shipped factory - see the Phase 2 note
  in section 8.
- `OnityObservableExtensions` (`Where`, `Select`, `DistinctUntilChanged`, `Skip`,
  `SkipWhile`, `Take`, `TakeWhile`, `StartWith`, `Merge`, `CombineLatest`,
  `Sample`, `Scan`, `Pairwise`, `Subscribe`, `TakeUntilCancellation`,
  `FirstAsync`, `ToTask`, `ObserveOnThreadPool`, `SelectOnThreadPool`)
- `OnityObservableAsyncExtensions` (`SelectAwait`, `WhereAwait`, `ThrottleLast`,
  `Debounce`, `TakeUntil`). The async/time operators are authored on the
  internal `IOnityObservableV2<T>` and re-exposed as public
  `IOnityObservable<T>` wrappers via `ToV2`/`ToLegacy`. (`ToTask`/`FirstAsync`
  ship on `OnityObservableExtensions`, not on this async facade.)
- `OnityObservableTracker` (editor diagnostics)
- `OnityProviders` (operator implementation provider)

**Unity bridges already shipped:**

- `OnityUnityObservable.EveryUpdate()`
- `OnityUnityObservable.EveryFixedUpdate()`
- `OnityUnityObservable.EveryLateUpdate()`
- `OnityUnityObservable.Timer(...)`, `Interval(...)`
- `OnityLifetimeNotifier` (used by `TakeUntilDestroy`)
- `ReactiveLifetimeExtensions` (`AddTo(this IDisposable, Component)`,
  `TakeUntilDestroy(this IDisposable, Component)`,
  `TakeUntilDisable(this IDisposable, Behaviour)`). These extend `IDisposable`,
  not `IOnityObservable<T>`, and there is **no** `GameObject` overload.
- `OnityInputActionReactiveExtensions`
  (`StartedAsObservable`, `PerformedAsObservable`, `CanceledAsObservable`)
- Timer types: `OnityTimer`, `OnityCountdownTimer`, `OnityStopwatchTimer`,
  `OnityIntervalTimer`

## 2. Design principles

1. **Push-based.** Observables push values to subscribers. No pull-based
   enumerable bridge in the core (a future helper may live in extensions).
2. **Hot by default.** `Subject<T>` and `ReactiveProperty<T>` are hot. Cold
   factories like `Defer`, `Create` are **Phase 2 (not yet shipped)** and would
   be added only if a sample needs them.
3. **One subscribe = one disposable.** Disposing the subscription removes the
   observer. There is no implicit ref counting.
4. **Lifecycle integration is opt-in.** `AddTo(this)`,
   `TakeUntilDestroy(this)` etc. exist as extensions that take a
   `Component`/`Behaviour` (pass `this` from a MonoBehaviour). The core types
   are unaware of Unity lifecycle.
5. **Allocation-free in steady state.** `OnNext` paths inside `Subject<T>`
   and `ReactiveProperty<T>` allocate zero bytes after warmup.
6. **No third-party deps.** R3 / UniRx are reference only.
7. **Threading is explicit.** Managed thread-pool work is opt-in through
   `ObserveOnThreadPool` / `SelectOnThreadPool`; Unity API consumers must hop
   back to the main thread through `ObserveOnMainThread`.

## 3. Operator catalog

### 3.1 Already implemented

Core operators (`OnityObservableExtensions`, on `IOnityObservable<T>`):

- `Where(Predicate<T>)`
- `Select<TSource, TResult>(Func<TSource, TResult>)`
- `DistinctUntilChanged(IEqualityComparer<T> = null)`
- `Skip(int count)`
- `SkipWhile(Predicate<T>)`
- `Take(int count)`
- `TakeWhile(Predicate<T>)`
- `StartWith(T initialValue)`
- `Merge(params IOnityObservable<T>[] others)`
- `CombineLatest<TFirst, TSecond, TResult>(IOnityObservable<TSecond>, Func<TFirst, TSecond, TResult>)`
  (2-arity only today)
- `Sample<T, TSignal>(IOnityObservable<TSignal> sampler)`
- `Scan<TSource, TState>(TState seed, Func<TState, TSource, TState> accumulator)`
- `Pairwise()` -> `IOnityObservable<OnityPair<T>>` (emits a `OnityPair<T>` with
  `Previous`/`Current`, skipping the first value)
- `Subscribe(Action<T>)` and
  `Subscribe(Action<T>, Action<Exception>, Action<OnityResult>)`
- `TakeUntilCancellation(CancellationToken)`
- `FirstAsync(CancellationToken = default) -> Task<T>`
- `ToTask(CancellationToken = default)` (on `IOnityObservable<Unit>`)
- `ObserveOnThreadPool()` (ordered thread-pool re-post)
- `SelectOnThreadPool(Func<TSource, TResult>, int maxConcurrency = 0)`
- `SelectOnThreadPool(Func<TSource, CancellationToken, TResult>, int maxConcurrency = 0)`

Async/time operators (`OnityObservableAsyncExtensions`, on `IOnityObservable<T>`):

- `Debounce(TimeSpan dueTime, OnityTimeProvider = null)`
- `ThrottleLast(TimeSpan interval, OnityTimeProvider = null)` -
  **the operator is named `ThrottleLast`, not `Throttle`.** There is no
  leading-edge `Throttle`.
- `SelectAwait(Func<T, CancellationToken, ValueTask<TResult>>)`
- `WhereAwait(Func<T, CancellationToken, ValueTask<bool>>)`
- `TakeUntil(CancellationToken)` and `TakeUntil(Task)`

> `SelectAwait`/`WhereAwait` run the user callback on the thread pool (via
> `Task.Run`) and therefore **resume off the main thread**. Do not touch
> `UnityEngine` members directly in a `Subscribe` placed immediately after them.

Thread-pool scheduling operators (`OnityObservableExtensions`, on
`IOnityObservable<T>`):

- `ObserveOnThreadPool()` re-posts each source value onto a .NET thread-pool
  worker while preserving source order.
- `SelectOnThreadPool(...)` runs CPU-bound selectors on the .NET thread pool
  with configurable max concurrency. When max concurrency is greater than one,
  results emit as workers complete; use `maxConcurrency: 1` when source order is
  required.

These operators are for pure managed work. Downstream observers run off the
Unity main thread, so chains that touch UnityEngine must use
`ObserveOnMainThread()` from `Onity.Unity.Reactive` before the Unity-facing
subscriber.

Unity lifetime helpers (`ReactiveLifetimeExtensions` in `Onity.Unity.Reactive`,
on `IDisposable`):

- `AddTo(this IDisposable, Component)` (alias for `TakeUntilDestroy`)
- `TakeUntilDestroy(this IDisposable, Component)`
- `TakeUntilDisable(this IDisposable, Behaviour)`

Plus `AddTo(this IDisposable, CompositeDisposable)` in `OnityDisposableExtensions`
(`Onity.Reactive`). There is **no** `AddTo(GameObject)` /
`TakeUntilDestroy(GameObject)` overload.

### 3.2 Phase 2 additions (not yet shipped)

> `Merge`, `CombineLatest`, `Sample`, `Scan`, and `Pairwise` are **already
> shipped** (see section 3.1). The rows below are the operators that are still
> outstanding. `CombineLatest` ships only the 2-arity overload today; higher
> arities (3-4) are the remaining Phase 2 work.

| Operator | Signature | Notes |
|---|---|---|
| `Concat` | `(IOnityObservable<T> next)` | |
| `CombineLatest` | `<T1, T2, T3, TOut>(...)` | 3-4 arity (2-arity already shipped) |
| `Zip` | `<T1, T2, TOut>(...)` | Up to 4-arity in v1 |
| `Switch` | `(...)` | For `IOnityObservable<IOnityObservable<T>>` |
| `Aggregate` | `<TState>(TState seed, Func<TState, T, TState>)` | Hot terminal |
| `Buffer` | `(int count)`, `(TimeSpan)` | |
| `Window` | `(int count)` | |
| `Timeout` | `(TimeSpan)` | Errors via OnCompleted with cause |
| `Catch` | `(Func<Exception, IOnityObservable<T>>)` | |
| `Retry` | `(int count)` | |

Each operator ships with:

- Implementation in `Onity.Reactive/Scripts/Operators/<Name>.cs`.
- A focused EditMode test in `Onity.Tests.Reactive.EditMode`.
- A benchmark entry in `Packages/com.onity.framework/Benchmarks/Editor/`.

### 3.3 Operator implementation pattern

Operators return a sealed observable that holds the source and the operator
state. Subscription forwards to source with a wrapping observer.

```csharp
internal sealed class WhereObservable<T> : IOnityObservable<T>
{
    private readonly IOnityObservable<T> m_source;
    private readonly Func<T, bool> m_predicate;

    public WhereObservable(IOnityObservable<T> source, Func<T, bool> predicate)
    {
        m_source = source;
        m_predicate = predicate;
    }

    public IDisposable Subscribe(Observer<T> observer)
    {
        return m_source.Subscribe(value =>
        {
            if (m_predicate(value))
            {
                observer(value);
            }
        });
    }

    public IDisposable Subscribe(OnityObserver<T> observer)
    {
        // analogous lifecycle-aware path
        ...
    }
}
```

For operators that maintain state across emissions (ThrottleLast, Debounce,
Scan), the state lives in the wrapping observer to avoid per-emission
allocation.

## 4. Threading and PlayerLoop integration

### 4.1 Threading modes

```csharp
public enum OnityUnityThreadMode
{
    SingleThread,            // main thread, default
    JobMultiThread,          // batch into IJob between frames
    BurstJobMultiThread,     // batch into IJob with [BurstCompile]
    DotsEventDriven          // sourced from ECS event queue
}
```

Most stream creators accept this enum:

```csharp
OnityUnityObservable.EveryUpdate(OnityUnityThreadMode.SingleThread);
OnityUnityObservable.EveryUpdate(OnityUnityThreadMode.JobMultiThread);
```

Managed thread-pool scheduling is implemented in `Onity.Reactive` through
`ObserveOnThreadPool` and `SelectOnThreadPool`.

`OnityUnityThreadMode` is a separate Unity frame-stream boundary. `SingleThread`
emits directly from the frame pump. The Job/Burst/DOTS modes currently insert a
Unity job or DOTS accumulator boundary around frame ticks; they do not move
managed `Where` / `Select` / `Subscribe` operator execution into Burst jobs.
Managed DI and managed reactive observer callbacks remain outside Burst.

### 4.2 PlayerLoop hooks

Onity registers its own subsystem nodes inside the PlayerLoop:

- `OnityUpdate` before user `Update`
- `OnityFixedUpdate` before user `FixedUpdate`
- `OnityLateUpdate` after user `LateUpdate`

These are owned by `OnityPlayerLoopRunner` (to be created in
`Onity.Unity/Reactive/`). The runner pumps cached subjects per frame. The
implementation is allocation-free.

### 4.3 EveryUpdate semantics

`OnityUnityObservable.EveryUpdate()` returns a shared singleton observable
that emits `Unit` once per frame. Multiple subscribers share one underlying
subject. Disposing the last subscriber **does not** tear down the PlayerLoop
hook; the hook stays installed for the session.

This trades a small constant cost for predictable behavior.

## 5. Lifecycle helpers

```csharp
healthProperty
    .Where(v => v <= 0)
    .Subscribe(_ => OnDeath())
    .AddTo(this);                              // disposed when this Component
                                                // is destroyed

OnityUnityObservable.EveryUpdate()
    .TakeUntilDisable(this)                    // ends when this is disabled
    .Subscribe(_ => TickAi());
```

`AddTo(this)` uses `OnityLifetimeNotifier` attached on demand to the target
GameObject. The notifier raises a single completion signal in `OnDestroy`.

`TakeUntilDisable(this)` listens for `OnDisable` on the notifier and completes
the stream.

`AddTo(CompositeDisposable)` is for non-Unity owners (services held by
contexts).

## 6. Async bridge (`Onity.Async`)

Phase 0 extracts the helpers currently in `Runtime/Unity/Scripts/Async/` into
a new asmdef `Onity.Async` under `Runtime/Async/`.

### 6.1 Static facade

The user-facing facade is `Onity` (no suffix - matches the package name):

```csharp
await Onity.NextFrame(cancellationToken);
await Onity.NextFixedFrame(cancellationToken);
await Onity.Delay(2.5f, useUnscaled: false, cancellationToken);
await Onity.DelayFrames(3, cancellationToken);
await Onity.WaitUntil(() => isReady, cancellationToken);
await Onity.WaitWhile(() => isPaused, cancellationToken);
```

The current implementation already supports these via `OnityAsync`. Phase 0
renames `OnityAsync` to `Onity` and moves the file.

### 6.2 Task bridge from observables

```csharp
Task<int> first = healthProperty
    .Where(v => v <= 0)
    .FirstAsync(cancellationToken);

await first;
```

`FirstAsync` returns the first matching value or throws
`OperationCanceledException` if the token cancels.

### 6.3 AsyncOperation bridge

```csharp
SceneManager.LoadSceneAsync("Game")
    .WithCancellation(cancellationToken)
    .AsTask();

await SceneManager.LoadSceneAsync("Game").WithCancellation(token);
```

Already implemented in `OnityAsyncOperationExtensions`. Stays as is.

### 6.4 CancellationToken helpers

```csharp
var cts = new CancellationTokenSource();
cts.CancelAfterSlim(TimeSpan.FromSeconds(5));   // no GC allocation
```

Backed by `OnityTimeoutController` (already shipped).

### 6.5 Onity.Async vs Task vs ValueTask

Onity.Async exposes `Task` / `Task<T>` returns. Rationale:

- `Task` is the .NET standard and integrates with every async library a user
  might already know.
- Hot allocation cost is mitigated by `OnityTaskTracker` caching common
  completed tasks.
- Switching to a custom `OnityTask` struct (UniTask-style) is **not** done
  in Phase 1. It is reconsidered after Phase 2 ships and profiling shows
  whether `Task` allocations are a real bottleneck in production gameplay.

## 7. Messaging integration

`Onity.Messaging` exposes:

```csharp
public interface IPublisher<TMessage>
{
    void Publish(TMessage message);
}

public interface ISubscriber<TMessage>
{
    IDisposable Subscribe(MessageHandler<TMessage> handler);
}
```

Reactive integration is via extensions:

```csharp
broker.Observe<PlayerDied>()                    // -> IOnityObservable<PlayerDied>
    .Where(e => e.Reason == DeathReason.Fall)
    .Subscribe(e => ShowFallScreen(e))
    .AddTo(this);
```

`MessageChannel<T>` already implements `IOnityObservable<T>`. The `Observe<T>`
extension lives in `OnityMessageReactiveExtensions`.

`OnityEventHub` (`Runtime/Unity/Scripts/Messaging/OnityEventHub.cs`) is the
ergonomic facade. See `EVENT_HUB_PLAN.md` for the planned emitter/channel/
attribute-based expansion. That plan stays as is and is referenced from
Phase 3 in `05-Implementation-Phases.md`.

## 8. Public API contract

`IOnityObservable<T>` is the only public observable contract. There is a second
contract, `IOnityObservableV2<T>` (subscribe-with-`OnityObserver<T>`), but it is
**internal**: the async/time operators (`Debounce`, `ThrottleLast`,
`SelectAwait`, `WhereAwait`, `TakeUntil`) are authored on v2 and bridged back to
public `IOnityObservable<T>` via internal `ToV2`/`ToLegacy` adapters. Users and
AI agents should never reference `IOnityObservableV2<T>`.

The public surface of `Onity.Reactive` after Phase 2 is:

```
namespace Onity.Reactive

// Contracts (IOnityObservableV2<T> exists but is internal - see note above)
public interface IOnityObservable<T>
public delegate void Observer<T>(T value)
public abstract class OnityObserver<T> : IDisposable

// Primitives
public sealed class Subject<T> : IOnityObservable<T>, IDisposable
public sealed class ReactiveProperty<T> : IReadOnlyReactiveProperty<T>,
                                          IOnityObservable<T>, IDisposable
public interface IReadOnlyReactiveProperty<T>   // standalone: T Value { get; }
                                                // + Subscribe(Observer<T>,
                                                //   bool emitCurrentValue = true);
                                                // does NOT extend IOnityObservable<T>

// Factories (shipped today)
public static class OnityObservable
{
    Empty<T>(), Return<T>(value),
    FromEvent<T>(Action<Action<T>> add, Action<Action<T>> remove)
    // Phase 2 (not yet shipped): Never<T>(),
    //   Defer<T>(Func<IOnityObservable<T>> factory),
    //   Create<T>(Func<Observer<T>, IDisposable>)
}

// Operators (OnityObservableExtensions)
public static class OnityObservableExtensions
{
    // Shipped today:
    Where, Select, DistinctUntilChanged, Skip, SkipWhile, Take, TakeWhile,
    StartWith, Merge, CombineLatest (2-arity), Sample, Scan, Pairwise,
    Subscribe, TakeUntilCancellation, FirstAsync, ToTask,
    ObserveOnThreadPool, SelectOnThreadPool
    // Phase 2 (not yet shipped): Concat, CombineLatest (3-4 arity), Zip,
    //   Switch, Aggregate, Buffer, Window, Timeout, Catch, Retry
}

// Async / time (OnityObservableAsyncExtensions)
// (ToTask/FirstAsync live on OnityObservableExtensions above, not here.)
public static class OnityObservableAsyncExtensions
{
    SelectAwait, WhereAwait, ThrottleLast, Debounce, TakeUntil
}

// Disposal
public sealed class CompositeDisposable : IDisposable   // exposes Count + Add;
                                                        // not ICollection<IDisposable>
public static class OnityDisposableExtensions
{
    AddTo(this IDisposable, CompositeDisposable)
}
```

The Unity surface (in `Onity.Unity`) adds:

```
public static class OnityUnityObservable
{
    EveryUpdate(OnityUnityThreadMode = SingleThread),
    EveryFixedUpdate(OnityUnityThreadMode = SingleThread),
    EveryLateUpdate(OnityUnityThreadMode = SingleThread),
    Timer(float seconds, bool unscaled = false),
    Interval(float seconds, bool unscaled = false)
}

public static class ReactiveLifetimeExtensions
{
    AddTo(this IDisposable, Component),               // alias for TakeUntilDestroy
    TakeUntilDestroy(this IDisposable, Component),
    TakeUntilDisable(this IDisposable, Behaviour)
    // No GameObject overload; helpers extend IDisposable, not IOnityObservable<T>.
}

public sealed class OnityTimer
public sealed class OnityCountdownTimer
public sealed class OnityStopwatchTimer
public sealed class OnityIntervalTimer
```

## 9. Acceptance criteria

Phase 2 ships when:

- All operators in section 3.2 have implementations, tests, and benchmarks.
- `Subject<T>.OnNext` steady-state allocates 0 bytes for 100 successive
  subscribers.
- `EveryUpdate().Subscribe(...)` frame loop allocates 0 bytes per frame after
  warmup.
- `ThrottleLast` / `Debounce` correctness tests pass with deterministic time
  sources.
- `OnityObservableTracker` correctly shows subscription leaks in the
  diagnostics window for a sample scene that creates a leak intentionally.
- Managed thread-pool operators have focused EditMode coverage for thread hop,
  configured parallelism, ordering with max concurrency one, and argument
  validation.
- Unity Job/Burst frame modes are documented as explicit frame boundaries, not
  managed operator parallelism.

## 10. Out of scope for Phase 2

- Hot/cold conversion operators (`Publish`, `RefCount`).
- Backpressure operators (`BufferUntilThrottle`, etc.).
- `IObservable<T>` (System.Reactive) compatibility adapters - explicitly not
  shipped to avoid a third-party type leak.
- A full custom `OnityTask` struct.

## 11. References

- Current core: `Assets/Onity-Packages/Onity/Runtime/Reactive/Scripts/`
- Current Unity bridges: `Assets/Onity-Packages/Onity/Runtime/Unity/Scripts/Reactive/`
- Current async helpers: `Assets/Onity-Packages/Onity/Runtime/Unity/Scripts/Async/`
- Engineering doc: `Assets/Onity-Packages/Onity/ENGINEERING.md` (section 8)
- EventHub plan: root `EVENT_HUB_PLAN.md`
- Style guide: root `codex-code-style.md`
