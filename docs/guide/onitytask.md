---
title: "Async with OnityTask"
parent: "Guides"
nav_order: 4
description: "Use OnityTask for cancellable Unity frame waits, scene loading, web requests, reactive streams, and async messaging."
---

# Async with OnityTask

`OnityTask` and `OnityTask<T>` are Onity's Unity-facing awaitables. They cover
frame waits, delays, predicates, scene loading, `AsyncOperation`, web requests,
reactive streams, and async message delivery without adding a third-party
runtime package.

```csharp
using Onity.Unity.Async;
```

Use `OnityTask` for gameplay flows driven by Unity. Keep `Task` when a plain .NET
service already exposes it as part of its contract; bridge at the boundary with
`OnityTask.FromTask(...)` or `AsTask()`.

## Start and cancel a Unity flow

Own a `CancellationTokenSource` for the same lifetime as the component that
started the work. Cancel it in `OnDisable` when the flow must stop while the
component is inactive.

```csharp
using System;
using System.Threading;
using Onity.Unity.Async;
using UnityEngine;

public sealed class BootFlow : MonoBehaviour
{
    private CancellationTokenSource m_lifetime;

    private void OnEnable()
    {
        m_lifetime = new CancellationTokenSource();
        RunAsync(m_lifetime.Token).Forget(Debug.LogException);
    }

    private void OnDisable()
    {
        m_lifetime.Cancel();
        m_lifetime.Dispose();
        m_lifetime = null;
    }

    private static async OnityTask RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await OnityTask.NextFrame(cancellationToken);
            await OnityTask.Delay(0.25f, cancellationToken);
            await OnityTask.WaitUntil(() => IsReady(), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal lifetime cancellation; real faults still reach Forget's handler.
        }
    }

    private static bool IsReady()
    {
        return true;
    }
}
```

Cancellation of pooled Unity waits is completed through the Onity player-loop
runner, so the awaiting continuation resumes on Unity's main thread. Canceling a
fixed-frame wait also completes while `Time.timeScale` is zero.

In Onity 0.4.0, destroying the legacy runner also cancels its
accepted frame, delay, predicate and operation waits on the main thread.
The cancellation retains each wait's original token, including a token whose
owner has not requested cancellation. Destruction does not cancel the caller's
token source or stop the underlying Unity operation. An active predicate or
progress callback unwinds before its wait publishes cancellation.

Manual destruction permits a replacement runner in the same active session.
Session exit, reload and shutdown close acceptance before retiring pending
work; callbacks cannot create a replacement during that closure. The separate
explicit PlayerLoop waits retain their own session ownership.
See the [retirement verification and measured limits](../assets/benchmarks/onitytask-legacy-retirement-2026-09-27.md).

## Common operations

| Need | API |
| --- | --- |
| Next rendered frame | `await OnityTask.NextFrame(ct)` |
| Next several rendered frames | `await OnityTask.DelayFrames(frameCount, ct)` |
| Next fixed update | `await OnityTask.NextFixedFrame(ct)` |
| Next late update | `await OnityTask.NextLateFrame(ct)` |
| Scaled delay | `await OnityTask.Delay(seconds, ct)` |
| Unscaled delay | `await OnityTask.DelayUnscaled(seconds, ct)` |
| Wait for a condition | `await OnityTask.WaitUntil(predicate, ct)` |
| Wait while a condition holds | `await OnityTask.WaitWhile(predicate, ct)` |
| Wait for several operations | `await OnityTask.WhenAll(tasks)` |
| Collect typed results in input order | `T[] results = await OnityTask.WhenAll(typedTasks)` |
| First of two untyped operations | `int winner = await OnityTask.WhenAny(first, second)` |
| First of two typed operations | `(int winnerIndex, T result) = await OnityTask.WhenAny(first, second)` |
| First of a nonempty array | `await OnityTask.WhenAny(taskArray)` |
| Stop waiting without canceling the producer | `await task.AttachExternalCancellation(ct)` |
| Treat actual cancellation as a result | `await task.SuppressCancellationThrow()` |
| Completed typed result | `await OnityTask.FromResult(value)` |
| Resume on Unity's main thread | `await OnityTask.SwitchToMainThread(ct)` |
| Queue a continuation to a worker | `await OnityTask.SwitchToThreadPool(ct)` |
| Run synchronous background work, then return to the main thread | `await OnityTask.RunOnThreadPool(action, cancellationToken: ct)` |
| Complete a scheduled Unity job safely | `await handle.AsOnityTask()` |

## Explicit PlayerLoop phases

Onity 0.4.0 supports three explicit phases:

```csharp
await OnityTask.Yield(OnityPlayerLoopTiming.Update, ct);
await OnityTask.NextFrame(OnityPlayerLoopTiming.LateUpdate, ct);
await OnityTask.DelayFrames(2, OnityPlayerLoopTiming.FixedUpdate, ct);
```

`Update`, `FixedUpdate` and `LateUpdate` run immediately after Unity's
corresponding script callbacks. `Yield` waits for the next selected drain,
which can occur in the current rendered frame. A wait registered during that
drain resumes on a later occurrence. `NextFrame` requires a later rendered
frame; `DelayFrames` counts rendered frames, even when fixed updates run
several times per frame. `LateUpdate` does not mean end of frame.

These factories require the main thread and an active Play/player session.
Arguments are validated before execution context, cancellation and the
zero-frame fast path. The explicit `NextFrame` and `DelayFrames` overloads
require a token argument; use `default` when cancellation is unnecessary.
Worker cancellation is published on the main thread, including through Update
when fixed time is paused. Native results remain single-consumer; use one
`Preserve()` or `AsTask()` conversion before sharing.

The nodes install before scene loading and survive normal ECS additions to
the current loop. If a custom bootstrap replaces the PlayerLoop, call
`OnityTaskPlayerLoop.Initialize()` afterward. Repair retains pending waits,
removes only Onity's markers and preserves foreign nodes. Missing script
anchors fault pending waits and reject new queued work until explicit repair
succeeds. There is no per-frame loop-replacement watchdog.

Session exit cancels pending timing work and removes owned nodes. This owner
is independent of the legacy task runner. Default ECS simulation runs after
the injected Update node, so a Yield requested there normally resumes on the
next Update occurrence. Both Release Players pass the timing smoke cases.
Repeated warmed, tokenless Update Yield brackets show no measured heap growth
with a calibrated coarse counter; other timing paths are not allocation-proven.
See the [verification report and limits](../assets/benchmarks/onitytask-stage3d-playerloop-2026-09-27.md).

## Rendering end of frame

Onity 0.4.0 adds `WaitForEndOfFrame`, verified in graphics-enabled
Mono and IL2CPP Release Players. Each passed six render/lifecycle groups and
27 paired headless checks; both Editor optimizations passed 814 EditMode and
84 PlayMode tests. See [evidence and limits](../assets/benchmarks/onitytask-stage3f-endofframe-2026-09-27.md).

```csharp
static async OnityTask ReadRenderedFrameAsync(
    Texture2D destination, Rect region, CancellationToken ct)
{
    await OnityTask.WaitForEndOfFrame(ct);
    destination.ReadPixels(region, 0, 0, false);
    destination.Apply(false);
}
```

The caller owns the texture and keeps it alive until the read completes. The
wait uses a shared coroutine yielding Unity's real rendering primitive, which
can complete in the registration frame. Work queued from its completion callback
waits for a later drain. Native task values remain single-consumer.

Calls require Unity's main thread, an accepting Play/player session and a
non-null graphics device. Editor batch mode is rejected. These checks precede
pre-cancellation, which otherwise returns a canceled task without creating a
host. Token cancellation is published on the main thread during Update even
when the rendering coroutine is stalled.

Destroying or disabling the private EOF host retires its waits while other
explicit timing and timeout work remains live. A valid session may create a
replacement afterward. Session closure cancels all owned lanes; failed loop
repair faults them. Unity's primitive can stall when the Editor switches to
Scene view; see [Unity's documented behavior](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/WaitForEndOfFrame.html).

Cold host/coroutine creation allocates. Pending sources use a bounded pool;
this stage does not establish allocation quantities or speed against UniTask.

## External cancellation and cancellation results

Onity 0.4.0 adds typed and untyped cancellation decorators:

```csharp
static async OnityTask<bool> WaitForScoreAsync(
    OnityTask<int> producer, CancellationToken stopWaiting)
{
    var (isCanceled, score) = await producer
        .AttachExternalCancellation(stopWaiting)
        .SuppressCancellationThrow();
    if (isCanceled)
    {
        return false;
    }

    await OnityTask.SwitchToMainThread();
    Debug.Log(score);
    return true;
}
```

`AttachExternalCancellation` returns the original task if its input is already
complete or the token cannot cancel. A completed input takes precedence over
an already-canceled external token. For a pending input, external cancellation
competes with producer completion; the first observed outcome wins. A pending
input with an already-canceled external token chooses external cancellation.
The reported cancellation retains the winning token.

The producer is never canceled by this decorator. Its pending single-consumer
input is claimed: do not consume it separately, even after the wrapper cancels.
The observer remains until the producer finishes, including late faults and
existing/future completion-source Task bridges. An indefinitely pending
producer retains its observer. Preserve the input before wrapping if another
consumer also needs it.

Untyped suppression returns `false` for success or `true` for actual canceled
status. Typed suppression returns `(false, result)` or `(true, default)`.
Faults propagate, including a faulted `OperationCanceledException`; inspecting
the source status distinguishes that case from cancellation. Suppression
consumes its input once.

Pending wrappers have native single-consumer outputs; use `Preserve()` or
`AsTask()` explicitly for sharing. Publication can run on the producer or
canceling thread, so switch explicitly before using Unity APIs. Internal
observation does not capture the creation synchronization/execution context;
an enclosing async method still follows its own builder's context rules.
Completed suppression success/cancellation is returned inline. Pending wrappers
are initially unpooled and allocate; no zero-allocation or speed claim is made.
See the [verification report](../assets/benchmarks/onitytask-stage3c-cancellation-2026-09-27.md)
for the 777/54 Editor suites, 20-case Player checks and retention limits.

## Timeouts

Onity 0.4.0 provides typed and untyped timeout decorators.
Both optimization modes pass 812 EditMode/82 PlayMode tests; both Release
Players pass 26 smoke cases. See [verification and limits](../assets/benchmarks/onitytask-stage3e-timeout-2026-09-27.md).

```csharp
static async OnityTask<int> ReadWithDeadlineAsync(OnityTask<int> producer)
{
    // Uses unscaled Unity time by default.
    return await producer.Timeout(2f);
}

static async OnityTask<bool> TryReadWithDeadlineAsync(OnityTask<int> producer)
{
    var (isTimeout, value) = await producer.TimeoutWithoutException(2f);
    if (isTimeout)
    {
        return false;
    }

    await OnityTask.SwitchToMainThread();
    Debug.Log(value);
    return true;
}
```

`Timeout` preserves the input result shape and faults with `TimeoutException`
only when its own timer wins. `TimeoutWithoutException` returns a bool for an
untyped input, or `(bool isTimeout, T result)` for a typed input. Only that
private timeout becomes `true` / `(true, default)`; producer faults and
cancellation still propagate, including a producer's own `TimeoutException`
or a faulted `OperationCanceledException`.

Seconds must be finite and nonnegative, checked before inspecting or claiming
the input. An input already observed complete wins even at zero seconds.
A pending zero-second input times out immediately. Those paths can be used
outside Play Mode or on a worker. A positive timeout on a pending input must
be created on Unity's main thread in an accepting Play/player session; an
invalid context rejects before claiming the input.

Positive timers use the explicit PlayerLoop owner. They are checked after
public Update waits, using Unity's double-precision frame clocks. Pass
`useUnscaledTime: false` for scaled time, which pauses at timeScale zero.
Timers registered during an Update drain first become eligible in a later
pass. Session closure cancels pending wrappers; failed loop repair faults them.

Timeout stops waiting without canceling the producer. The wrapper consumes its
pending input and continues observing it after timeout, including late faults.
Do not consume that single-consumer input again; preserve it beforehand when
another consumer needs it. An indefinitely pending producer retains its observer.
Native wrapper outputs are also single-consumer unless preserved or bridged.

Producer completion may publish on a worker; timeout/session publication runs
on Unity's main thread. There is no implicit return to the creation context.
Wrappers and private timer entries are initially unpooled and allocate; neither
zero allocation nor speed equivalence with UniTask is claimed.

## Background work

Use `RunOnThreadPool` for synchronous computation that does not access Unity
objects. Start this example on Unity's main thread and provide an input snapshot
that other code will not mutate while the worker reads it:

```csharp
static async OnityTask SumAsync(int[] snapshot, CancellationToken cancellationToken)
{
    int sum = await OnityTask.RunOnThreadPool(
        () =>
        {
            int total = 0;
            for (int i = 0; i < snapshot.Length; i++)
            {
                total += snapshot[i];
            }

            return total;
        },
        cancellationToken: cancellationToken);

    Debug.Log(sum); // The default return publishes completion on the main thread.
}
```

Both `Action` and `Func<T>` overloads reject null delegates. Pre-canceled work
returns a canceled task without dispatching. Once dispatched, cancellation is
checked before invocation and before publishing successful completion, including
after the optional return hop; it cannot interrupt an executing delegate.
A delegate exception takes precedence over cancellation.
The default `returnToMainThread: true` returns through the existing dispatcher
before publishing success, fault or cancellation, using an uncanceled cleanup
hop and the session captured before dispatch. Returns from ended sessions are
discarded; they do not resume in a later Play session. Background work itself
is not forcibly stopped when its session ends.

Set `returnToMainThread: false` to publish completion on the worker. An await
of an already completed task can still run inline on the consumer's thread.
For a direct switch, `await OnityTask.SwitchToThreadPool(ct)` always queues,
even when already on a worker, and observes cancellation on that worker.
Its awaiter does not capture execution context for either registration method;
async builders retain responsibility for context flow. Keep the process-wide
`FlowExecutionContext` setting configured before starting async methods.
Disabling or suppressing flow prevents caller-context capture; it does not clear
ambient values already present on a reused worker thread.

Thread-pool factories and queue entry throw `PlatformNotSupportedException`
in WebGL Players. The Editor remains supported when WebGL is selected. These
APIs do not introduce an allocation or speed guarantee; measurements and
Player verification are recorded in the [thread-pool report](../assets/benchmarks/onitytask-stage1-threadpool-2026-09-27.md).

## Unity Jobs and Burst

Schedule a job normally, then call `JobHandle.AsOnityTask()` on Unity's main
thread. The adapter calls `Complete()` before publishing the result, so the
caller can safely read and dispose its native containers after the await:

```csharp
using Unity.Collections;
using Unity.Jobs;

struct DoubleJob : IJob
{
    public NativeArray<int> Values;

    public void Execute()
    {
        Values[0] *= 2;
    }
}

static async OnityTask<int> DoubleAsync(int value)
{
    var values = new NativeArray<int>(1, Allocator.Persistent);
    JobHandle handle = default;
    try
    {
        values[0] = value;
        handle = new DoubleJob { Values = values }.Schedule();
        await handle.AsOnityTask();
        return values[0];
    }
    finally
    {
        // Also covers rejection before the adapter accepts the handle.
        handle.Complete();
        values.Dispose();
    }
}
```

The adapter owns the obligation to complete an accepted handle, including an
unawaited handle. It never owns or disposes your containers. Pending handles are
polled in Update; Edit Mode uses the Editor update callback. A completed or
default handle completes inline. Pending tasks are single-consumer; use
`Preserve()` before sharing.

There is no job-cancellation overload. Runner destruction and session teardown
finish accepted jobs before canceling their tasks; cleanup can therefore wait
for computation. A `Complete()` failure faults the task. Calls from workers or
during a closed/retiring session throw before accepting the handle, leaving
completion with the caller.

Burst can compile a compatible job's computation. Managed delegates, task
sources and execution-context flow remain outside that job. The benchmark's
serial C#, Jobs without Burst and Jobs with Burst rows measure computation
including scheduling/completion where applicable. Its separate adapter panel
measures Onity/UniTask registration and observed completion latency; the two
adapters run at different positions within Update.

Both Release backends passed the bridge and computation checks. See the
[Jobs/Burst report](../assets/benchmarks/onitytask-stage2-jobs-2026-09-27.md)
for measured benefits, remaining adapter overhead and allocation limits.

## Finite async streams

Onity 0.4.0 adds native `IOnityAsyncEnumerable<T>` and
`IOnityAsyncEnumerator<T>` in `Onity.Unity.Async`. Descriptions are reusable;
each enumeration owns its state and cleanup. C# `await foreach` uses the native
`OnityTask` move and disposal methods, including cleanup on `break` or an
exception in the loop body.

```csharp
private static async OnityTask ReadValues(CancellationToken ct)
{
    var values = OnityAsyncEnumerable.Range(1, 20)
        .Where(value => value % 2 == 0)
        .Select(value => value * 10)
        .Take(3)
        .WithCancellation(ct);

    await foreach (int value in values)
    {
        UnityEngine.Debug.Log(value); // 20, 40, 60
    }

    int first = await OnityAsyncEnumerable.Return(7).FirstAsync(ct);
    int[] all = await OnityAsyncEnumerable.Range(1, 3).ToArrayAsync(ct);
}
```

- Factories: `Empty<T>()`, `Return(value)` and `Range(start, count)`.
- Operators: synchronous `Select` / `Where`, `Take(count)` and
  `WithCancellation(token)`. Operators acquire their upstream lazily.
- Consumers: `FirstAsync(token)` and `ToArrayAsync(token)` await cleanup on
  every exit. Empty `FirstAsync` faults. `Take` awaits cleanup before exposing
  its final item; cleanup failure takes precedence over an ordinary result.
- Each enumerator accepts one outstanding move. `Current` is valid after a
  true result until the next move or disposal. Normal exhaustion stays false;
  faults and cancellation keep their status and exception/token until disposal.
- `WithCancellation` combines wrapper and enumeration tokens. It owns only
  any needed linked source; the caller retains ownership of its token source.
  Arbitrary upstream implementations must cooperate with the supplied token.
- `Empty`, zero-count `Range`, and `Take(0)` complete without upstream work,
  even when pre-canceled. Their terminal consumers preserve that empty result.
- Explicit disposal closes the enumerator, ends an uncommitted move false and
  waits for upstream cleanup and pending observation. A cleanup failure is
  reported by `DisposeAsync`; it cannot replace that false move result.

These APIs add no implicit thread hop. Switch to Unity's main thread when a
producer completes on a worker and the next action needs Unity APIs. Native
interfaces support consumption by `await foreach`; compiler-generated
`async` / `yield return` methods use BCL async interfaces. The adapters below
connect those interfaces. Sequential awaitable operators and channels are
documented below; reactive adapters follow in [Plan 13](../Plan/13-OnityTask-ApiCoverageAndJobs.md).

Descriptions, enumerators, linked token sources and pending completion state
may allocate; `ToArrayAsync` also allocates storage. Inline finite iteration
does not establish a general allocation or speed claim.
See the [finite-stream verification and bounded measurement](../assets/benchmarks/onitytask-stage4a-streams-2026-09-27.md)
for both Player backends, full suites and retained-heap limits.

## Update streams and BCL async iterators

Onity 0.4.0 adds `OnityAsyncEnumerable.EveryUpdate()` and two
adapters for `System.Collections.Generic.IAsyncEnumerable<T>`:

```csharp
private static async OnityTask ReadUpdates(CancellationToken ct)
{
    await foreach (var tick in OnityAsyncEnumerable.EveryUpdate()
        .Take(60).WithCancellation(ct))
    {
        // Runs at the explicit after-script Update node.
    }
}

private static async OnityTask<int[]> ReadBcl(
    System.Collections.Generic.IAsyncEnumerable<int> source,
    CancellationToken ct)
{
    return await source.AsOnityAsyncEnumerable().Take(3).ToArrayAsync(ct);
}

// Native streams can also be consumed by existing BCL async-stream APIs.
System.Collections.Generic.IAsyncEnumerable<int> bcl =
    OnityAsyncEnumerable.Range(0, 3).AsAsyncEnumerable();
```

- `EveryUpdate` is pull-based: each accepted move schedules one `Yield(Update)`.
  Creation and idle disposal need no Unity access. Active moves require the
  main thread and an accepting Play/player session, including pre-canceled
  moves. There is no buffering or replay while the consumer is idle.
- A move created before the Update node may complete in the same frame.
  A move created from its continuation waits for a later Update pass. Worker
  disposal can end the exposed move false immediately; cleanup still waits
  for the underlying Unity wait to be observed on the main thread.
- BCL imports acquire the upstream enumerator lazily with an owned cancellation
  token. They consume each `ValueTask` exactly once and wait for a pending move
  to be consumed before invoking upstream `DisposeAsync`. An uncooperative
  pending move can therefore keep disposal pending. The caller retains its CTS.
- Both adapters preserve actual cancellation status and tokens. A faulted
  `OperationCanceledException` remains a fault. Repeated disposal shares cleanup;
  one move may be outstanding per enumerator. Cleanup failure takes precedence
  over an ordinary terminal result, while explicit-disposal false stays false.
- Internal observation adds no creator-context dispatch. An ordinary BCL caller
  awaiting an exported Task-backed `ValueTask` keeps normal context-capture
  behavior. Use an explicit main-thread switch when consuming a worker producer
  before calling Unity APIs.

These adapters and pending Update moves allocate enumerator, cancellation and
completion state. No zero-allocation or comparative speed claim is made.
Both Editor optimizations pass 879 EditMode and 90 PlayMode cases; both Release
Players pass 31 smoke cases. See the [adapter verification and limits](../assets/benchmarks/onitytask-stage4b-adapters-2026-09-27.md).

## Sequential awaitable operators

Onity 0.4.0 adds SelectAwait, WhereAwait and ForEachAsync. Each
delegate receives an item and a cooperative cancellation token. Only one
upstream move and one delegate run at a time; these operators do not prefetch
or run items in parallel.

```csharp
private static async OnityTask PrintValues(CancellationToken ct)
{
    await OnityAsyncEnumerable.Range(1, 5)
        .SelectAwait(DoubleNextFrame)
        .WhereAwait((value, token) => OnityTask<bool>.FromResult(value >= 6))
        .ForEachAsync((value, token) =>
        {
            UnityEngine.Debug.Log(value); // 6, 8, 10
            return OnityTask.Completed;
        }, ct);
}

private static async OnityTask<int> DoubleNextFrame(int value, CancellationToken ct)
{
    await OnityTask.NextFrame(ct);
    return value * 2;
}
```

Start this frame-wait example on Unity's main thread in Play Mode.
SelectAwait and WhereAwait descriptions are lazy and reusable; ForEachAsync
starts consuming immediately. An empty upstream returning normal end succeeds
even with a pre-canceled token. An upstream that returns cancellation retains
that outcome.

- The original enumeration token goes upstream. Delegates receive a lazily
  created owned token, which disposal cancels without canceling the caller's
  token source. A successful delegate that ignores cancellation retains its
  result; later upstream work can still cancel the overall enumeration.
- Synchronously thrown exceptions, including OCE, are faults. Returned task
  fault/cancellation status and actual cancellation token remain authoritative.
- Disposal signals pending delegate work, starts eligible native upstream
  cleanup and waits for accepted work, cleanup and cancellation callbacks to
  settle. An uncooperative delegate can keep disposal pending. Upstream cleanup
  failure takes precedence over operation or cancellation-callback failure.
- There is no implicit thread/context switch. Descriptions, enumerators, owned
  tokens and pending observation state may allocate; no speed or zero-allocation
  claim is made.

Both Editor optimizations pass 947 EditMode and 94 PlayMode cases; fresh Mono
and IL2CPP Release Players each pass 35 smoke cases. See the
[awaitable-operator checkpoint](../assets/benchmarks/onitytask-stage4c-await-operators-2026-09-27.md).

## Channels

Onity 0.4.0 adds single-consumer channels with multiple producers:
`OnityChannel.CreateBounded<T>(capacity)` and `CreateUnbounded<T>()`.
The bounded channel waits when full; it never drops accepted values to make space.

```csharp
private static async OnityTask ReadChannel()
{
    var channel = OnityChannel.CreateBounded<int>(2);
    channel.Writer.TryWrite(1);
    channel.Writer.TryWrite(2);

    OnityTask waitingWrite = channel.Writer.WriteAsync(3); // Full: waits.
    int first = await channel.Reader.ReadAsync();          // 1; admits 3.
    await waitingWrite;
    channel.Writer.TryComplete();

    await foreach (int value in channel.Reader.ReadAllAsync())
    {
        UnityEngine.Debug.Log(value); // 2, then 3, then normal completion.
    }
}
```

- `TryWrite` and `TryRead` are immediate. Queued writers keep FIFO priority;
  `TryWrite` cannot overtake them. FIFO follows acceptance under the channel
  gate, not the wall-clock order of calls on different threads.
- `ReadAllAsync` acquires the consumer lease on its first move and holds it
  while idle. Dispose it when stopping early. Disposal releases only that
  consumer; it neither closes the channel nor clears buffered items.
- A pending standalone `ReadAsync` also owns a consumer lease. Conflicting
  reads and overlapping enumeration moves throw synchronously; `TryRead`
  returns false while another consumer owns the lease.
- Cancellation removes an unaccepted read or write. A committed delivery or
  write remains successful. Direct operations preserve the supplied token.
  ReadAll observes both its captured and enumeration tokens directly: captured
  token wins when both are pre-canceled; the first pending cancellation committed
  under the gate supplies its exact token. Caller token sources remain owned
  by the caller.
- `TryComplete(error)` closes admission once and drains accepted buffered
  values before the terminal result. Normal empty closure ends ReadAll and
  faults ReadAsync with `OnityChannelClosedException`. Error closure faults
  reads with the original error, including faulted OCE. Rejected writes use
  the closed exception with the terminal error as its inner exception.

Pending waiters, registrations, descriptions and unbounded growth may allocate.
Warmed bounded immediate operations showed no retained-heap increase in 16
calibrated windows per capacity/backend; this does not prove exact zero allocated
bytes. Full suites passed 913 EditMode/92 PlayMode cases in both optimizations;
the strengthened 34-case channel fixture also passed both. Each Release Player
passed 33 smoke cases. Channels add no implicit Unity thread switch. See the
[channel verification and measurement limits](../assets/benchmarks/onitytask-stage4c-channels-2026-09-27.md).

## Single-consumer rule for pooled tasks

Frame, delay, predicate, `JobHandle.AsOnityTask()` and `AsyncOperation.AsOnityTask()` operations use pooled
sources. Each returned `OnityTask` value is **single-consumer**:

- Await the value once, or call `AsTask()` once.
- Do not copy the value to several consumers.
- Do not await it and then call `AsTask()` on the old copy.
- `WhenAll` and `WhenAny` consume their input task values; do not await those
  inputs separately. `WhenAny` does not cancel the loser, which is consumed
  when it eventually completes.
- Call `Preserve()` once before sharing a pooled task with multiple consumers.
  Consume only the returned task; the original is claimed by `Preserve()`.

For two inputs with the same result type, `WhenAny<T>` returns the winner's
argument index (zero or one) and value:

```csharp
using Onity.Unity.Async;

OnityTaskCompletionSource<int> first = new OnityTaskCompletionSource<int>();
OnityTaskCompletionSource<int> second = new OnityTaskCompletionSource<int>();
OnityTask<(int winnerIndex, int result)> race =
    OnityTask.WhenAny(first.Task, second.Task);

second.TrySetResult(20);
(int winnerIndex, int result) = await race; // (1, 20)
first.TrySetResult(10); // The loser is still observed.
```

The pair consumes each input once and observes the loser without canceling it.
Passing the same single-consumer native input twice throws `ArgumentException`.
A winning fault or cancellation propagates; an `OperationCanceledException`
reported as a fault remains a fault. The result source is single-consumer and
allocates rather than using a pool. Native awaits follow the input's completion
thread; creation and native await registration do not capture Unity's
`SynchronizationContext`. When a Unity main-thread continuation is required,
await `race.AsTask()` instead of `race` from the Unity context so the .NET Task
await captures it.

Onity 0.4.0 also supports arbitrary nonempty arrays. Typed arrays
return `(winnerIndex, result)` and untyped arrays return the winner index:

```csharp
var sources = new[]
{
    new OnityTaskCompletionSource<int>(),
    new OnityTaskCompletionSource<int>(),
    new OnityTaskCompletionSource<int>()
};
var race = OnityTask.WhenAny(new[] { sources[0].Task, sources[1].Task, sources[2].Task });
sources[1].TrySetResult(42);
var (winnerIndex, result) = await race; // (1, 42)
sources[0].TrySetResult(10);
sources[2].TrySetResult(30); // Every loser is consumed, without cancellation.
```

Array inputs are snapshotted before registration. Null/empty arrays and repeated
single-consumer source/token identities are rejected before any input is claimed.
One/default input and duplicated shareable, preserved or Task-backed inputs are
supported. Already-terminal inputs favor the lowest index; pending callbacks
race by observation, so repeated pending Task-backed inputs need not choose index
zero. Caller mutation after the factory returns does not change the race.

Array outputs are single-consumer native tasks. Call `Preserve()` once for
sharing or use `AsTask()` for an explicit bridge. Publication can run on a
producer worker; internal Task-backed observers do not capture the creator's
Unity context. Stale/preclaimed inputs become fault outcomes, and a later
registration failure does not replace an earlier winner. Every accepted loser
is observed, including faults on completion-source subclasses and later Task
bridges. An indefinitely pending loser retains its observer.

Coordinators retain storage for up to 16 inputs, with at most 256 pooled sources
per output shape. Reuse waits for output release, all input observations and
in-flight callbacks. Larger arrays and registration-error instances are not
pooled. Caller-created `params` arrays and cold pool growth can allocate; this
API has no general zero-allocation claim.

The [Release Player comparison](../assets/benchmarks/onitytask-stage3b-whenany-2026-09-27.md)
verified 18 smoke cases per backend. UniTask had lower measured composition
times. Onity reported no measurable heap growth at 2/16 inputs with a coarse
counter, but substantially greater growth at 32 inputs. These results are
specific to the measured completion-source workloads.

`NextFrame` and `DelayFrames(1)` resume no earlier than the following rendered
frame in Play Mode. `DelayFrames(0)` completes immediately. Negative frame
counts throw `ArgumentOutOfRangeException`. Positive `Delay` and `DelayUnscaled`
waits also skip the frame in which they are scheduled in Play Mode.

Two successful, already completed inputs to the untyped
`WhenAll(first, second)` are consumed immediately and return the completed
task without .NET task bridges or a tracker entry. Pending untyped inputs use
a pooled coordinator with a Task-backed output when each input is a completion
source without an `AsTask()` bridge or an unclaimed single-consumer native
source, such as a frame wait or a suspended async method; Task-backed,
preserved, bridged, or already awaited inputs use the .NET `Task.WhenAll`
path. Typed `WhenAll` also collects eligible already successful results directly
in input order, without .NET task bridges or a tracker entry. Onity 0.4.0
additionally handles 1–16 unique exact built-in typed completion
sources without existing bridges, mixed with inline/default values, through
a pooled coordinator when at least one source is pending. Its output remains
Task-backed, tracked and shareable; faults retain input order and take precedence
over cancellation. Pooled native, preserved, derived, duplicate, prebridged,
Task-backed or larger typed sets keep the previous bridge fallback.
The [construction measurements](../assets/benchmarks/onitytask-stage3a-whenall-2026-09-27.md)
show lower heap growth but slower IL2CPP construction; they do not measure the
complete lifecycle. Typed calls still need a result array and callers using inline
`params` arguments create an input array. Untyped calls with other input counts
also materialize their inputs as .NET `Task` values. Methods declared
`async OnityTask` that suspend are backed by a pooled native runner; see
"Async methods" below. The two-input
untyped `WhenAny` uses a native result source, but currently allocates that
source and two continuation delegates per call. The typed pair also uses a
nonpooled source that allocates.
Use the pooled frame, delay, predicate, and `AsyncOperation` waits directly in
hot paths; do not assume every OnityTask composition is allocation-free.

When several consumers must observe one operation, preserve it before the first
consumer starts. Both pending and later consumers can await the retained result:

```csharp
using Onity.Unity.Async;

OnityTask shared = OnityTask.NextFrame().Preserve();
OnityTask first = ObserveAsync(shared);
OnityTask second = ObserveAsync(shared);
await OnityTask.WhenAll(first, second);

static async OnityTask ObserveAsync(OnityTask task)
{
    await task;
}
```

`Preserve()` returns completed, Task-backed, and completion-source tasks without
an additional allocation. For a pooled native task it allocates one retained
source and registers one native continuation. Use it only when sharing is needed;
directly awaiting a pooled task remains cheaper. The retained result, fault, or
cancellation can be observed repeatedly. Use `AsTask()` once when a .NET API
requires a `Task`; the returned `Task` can also be shared.

## Async methods

`async OnityTask` and `async OnityTask<T>` methods use Onity's own builders.
A method that completes without suspending returns the completed task or its
result inline; a synchronous exception or cancellation produces a Task-backed
faulted or canceled task with the thrown instance preserved. A method that
suspends binds a pooled runner that stores the state machine by value and
resumes it through one cached delegate, so no .NET `Task`, boxed state machine,
or per-suspension delegate is created.

Released runners return to a per-method pool guarded by one compare-and-swap
gate rather than a lock, and `OnityTask.RunnerPoolCapacity` (default 128)
caps how many are kept per method; a burst above the cap allocates a runner
per extra call and lets it be collected, so raise the cap before a burst
whose retained memory is acceptable. The task returned by a suspended method
is a **single-consumer native task**, like a frame wait: await it once, or
call `AsTask()` once, and call `Preserve()` before sharing it. Reading its status after it was consumed
throws `InvalidOperationException`. A fault thrown after a suspension is
rethrown as the same instance with its stack trace, and an
`OperationCanceledException` thrown after a suspension cancels the task and is
rethrown as the same instance.

By default the builder flows the execution context across awaits, so
`AsyncLocal<T>` values set before an await are visible after it, and a value
written after an await does not leak into the resuming thread. The capture
detaches Unity's synchronization context for its duration and restores the
resuming thread's context before the method continues, so
`SynchronizationContext.Current` after an await is the resuming thread's
context. Unlike the .NET builder, writes made before the first await are not
isolated from the caller: on the main thread they persist in the thread's
ambient context, as any synchronous code's writes do. A
`SetSynchronizationContext` call made before the first await persists on the
thread as well; one made after an await is reverted when that resumption
unwinds. The builders bind the class library's own capture and run pair, the
internal `ExecutionContext.FastCapture` and
`RunInternal(context, callback, state, preserveSyncCtx: true)` that .NET's
`AsyncTaskMethodBuilder` uses, through reflection, proven with a probe at
first use. Managed code stripping keeps both members because the binding
class references the class library's own async builder, whose completion path
calls them; Unity ignores a `link.xml` inside a package, so none is shipped.
A development player logs one warning when the pair is unavailable. On that
path a suspension on a thread that has never stored an
`AsyncLocal<T>` value captures the shared default context without
allocating, and resumption keeps the thread's synchronization context; once a
thread has stored a value, each suspension captures a context of about 72
bytes, as the .NET builder does. When the pair is missing the builders fall
back to the public `ExecutionContext.Capture()` and `Run`, which allocate the
captured context on every suspension, about 72 bytes plus a call-context
object of about 56 bytes on the Mono JIT profile, and re-install the resuming
thread's synchronization context inside the callback. The fast path binds on
Unity 2022.3.62f2 Mono, where a test asserts it, and the 2026-09-25 Release
verification at `f682b7c` measured async-method scheduling with it at 1.62x
to 2.10x UniTask against 2.25x to 2.55x on the public path; flow off measured
1.40x to 1.83x. On desktop Mono 6.8, which compiles the same reference-source
`ExecutionContext`, a capture-and-run pair measured 0 bytes and about 93 ns
on the fast path without stored values against 72 bytes and about 135 ns on
the public path; that is not a Unity measurement.
Set `OnityTask.FlowExecutionContext = false` before any async Onity method
starts to skip the capture entirely; that gives UniTask's semantics, where
`AsyncLocal<T>` does not flow, writes after an await stay on the resuming
thread, and a `SetSynchronizationContext` call after an await persists. The
primary benchmark measures the async-method cases under both settings.
Suppressing flow with `ExecutionContext.SuppressFlow()`
around the call skips the capture for the awaits reached while flow is
suppressed, normally the first one; later awaits capture on the resuming
thread again, as they do with the .NET builder.

The same-instance guarantee for a fault or cancellation holds for the native
await. A consumer of `AsTask()` receives a `TaskCanceledException` for a
canceled method, and `Preserve()` re-raises the cancellation as a new
`OperationCanceledException` with the same token.

`Forget()` on a single-consumer native task registers a direct observer when
task tracking is disabled and bridges the task when tracking is enabled, which
it is by default, so tracked tasks stay visible. Two suspended methods passed
to the two-input untyped `WhenAll`, including the `params` overload with
exactly two inputs, use the pooled coordinator. Suspended async-method inputs
to typed `WhenAll<T>` and untyped `params` calls with other input counts still
bridge through `AsTask()`. The bounded pending typed optimization described
above applies to public completion sources, not pooled async-method runners.

## Switch to the main thread

`OnityTask.SwitchToMainThread` returns an awaitable that resumes on Unity's main
thread. Awaiting it on the main thread completes synchronously without a frame
delay, on a path designed not to allocate; that target is not yet measured, see
the [comparison](onitytask-comparison.html). Awaiting it on a worker thread
queues the continuation, which resumes during the Update phase of a following
frame.

```csharp
using System.Threading;
using System.Threading.Tasks;
using Onity.Unity.Async;
using UnityEngine;

private static async OnityTask LoadAndApplyAsync(string path, CancellationToken cancellationToken)
{
    byte[] bytes = await Task.Run(() => System.IO.File.ReadAllBytes(path), cancellationToken);

    await OnityTask.SwitchToMainThread(cancellationToken);

    // Unity API is safe again here.
    Debug.Log($"Loaded {bytes.Length} bytes on the main thread.");
}
```

Cancellation is observed when the switch completes: `GetResult` throws
`OperationCanceledException` with the token on the destination thread, even
when the token was already canceled. A continuation canceled while it is
queued still resumes on the main thread before it throws, so cleanup code
never runs on the canceling thread.

Outside Play Mode the Editor resumes queued continuations from its update loop
while it is not compiling or importing assets. Entering or exiting Play Mode,
and quitting the player, ends the current session: continuations queued in an
earlier session are discarded rather than resumed in the next one. Compiler
generated `async Task` methods, and `async OnityTask` methods while
`OnityTask.FlowExecutionContext` is on, flow `AsyncLocal` values across the
switch through their builders and keep Unity's synchronization context;
`OnityTaskThreadSwitchAwaiter.OnCompleted` itself does not capture an
execution context, like the other native Onity awaiters. Use
`OnityTask.SwitchToThreadPool` or `OnityTask.RunOnThreadPool` for worker work;
the background-work section above describes cancellation and return behavior.

## Complete a task from a callback

Use `OnityTaskCompletionSource<T>` when an external callback owns completion.
Its `Task` can be awaited by several consumers, including consumers that arrive
after it completes. Only the first completion attempt succeeds. The untyped
`OnityTaskCompletionSource` has the same behavior without a result value.

```csharp
using System;
using Onity.Unity.Async;

OnityTaskCompletionSource<string> source = new OnityTaskCompletionSource<string>();
OnityTask<string> completion = source.Task;

// Each callback tries to complete the same operation.
void OnLoaded(string value) => source.TrySetResult(value);
void OnFailed(Exception error) => source.TrySetException(error);

string first = await completion;
string second = await completion; // Retained result; no pool token is consumed.
```

Use `TrySetCanceled(cancellationToken)` for cancellation. `TrySetException`
also treats `OperationCanceledException` as cancellation and preserves its
token. A fault that nobody observes may be reported to the Unity log after
garbage collection; use `Forget` with an error handler for fire-and-forget work.

Generation checks reject stale pooled task copies instead of letting them read a
later operation that reused the same source.

## Scene loading

```csharp
private static async OnityTask LoadGameplayAsync(CancellationToken cancellationToken)
{
    await OnityTask.LoadScene(
        "Gameplay",
        progress => Debug.Log($"Loading: {progress:P0}"),
        cancellationToken);
}
```

`LoadSceneAdditive`, `UnloadScene`, and `ActivateScene` use the same progress and
cancellation shape. `LoadSceneAsync` returns the underlying `AsyncOperation` when
you need to control activation yourself.

For `LoadSceneAsync(..., activateOnLoad: false)`, cancellation can prevent Unity
from starting the load. After Unity starts it, Onity returns the prepared
operation even if the token was canceled. The caller must eventually activate
that operation: Unity holds it at 90% progress and blocks later async operations
until activation is allowed. Use an uncanceled token for this cleanup:

```csharp
AsyncOperation operation = await OnityTask.LoadSceneAsync(
    "Gameplay",
    activateOnLoad: false,
    cancellationToken: cancellationToken);

try
{
    await WaitForFadeAsync(cancellationToken);
}
finally
{
    await OnityTask.ActivateScene(operation, CancellationToken.None);
}
```

The built-in loading-scene initiator follows this ownership rule when its
minimum display duration is canceled.

## Unity AsyncOperation bridge

```csharp
ResourceRequest request = Resources.LoadAsync<TextAsset>("GameConfig");
ResourceRequest completed = await request.AsOnityTask(
    cancellationToken: cancellationToken);
```

For a general `AsyncOperation.AsOnityTask()` bridge, cancellation stops the
await and reports `OperationCanceledException`; the Unity operation may
continue. Deferred scene loading has the ownership rule described above.

## Web requests

The caller owns a request passed to `Send` and must dispose it:

```csharp
using UnityEngine.Networking;

using UnityWebRequest request = UnityWebRequest.Get(url);
UnityWebRequest completed = await OnityTask.Send(
    request,
    progress => Debug.Log($"Download: {progress:P0}"),
    cancellationToken);
```

`GetJson<TResponse>` and `PostJson<TRequest, TResponse>` provide compact
`JsonUtility`-based DTO helpers. Failed requests throw
`OnityUnityWebRequestException` with the response details.

## Reactive and messaging bridges

```csharp
public readonly struct SaveRequested
{
}

int firstScore = await scoreStream.FirstOnityTask(cancellationToken);

await asyncPublisher.PublishOnityTask(
    new SaveRequested(),
    cancellationToken);
```

Use `FirstOnityTask` / `ToOnityTask` for reactive streams and
`PublishOnityTask` / `SubscribeOnityTask` for Onity's async message channels.

## Fire and forget

Prefer `await`. When a detached operation is intentional, call `Forget` so
exceptions reach a callback or the Unity log:

```csharp
OnityTask.LoadScene("Gameplay").Forget(Debug.LogException);
```

Long-running operations appear in **Onity → Diagnostics → Task Tracker** when
tracking is enabled. Stack-trace capture is useful for leak diagnosis but adds
Editor allocation overhead, so leave it disabled during performance runs.

## See also

- [Migrating from UniTask](../Migration/From-UniTask.html)
- [OnityTask and UniTask comparison](onitytask-comparison.html)
- [Reactive](reactive.html)
- [Events & Messaging](events-messaging.html)
- [Performance & IL2CPP](performance-and-il2cpp.html)
