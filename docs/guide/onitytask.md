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
| Completed typed result | `await OnityTask.FromResult(value)` |
| Resume on Unity's main thread | `await OnityTask.SwitchToMainThread(ct)` |

## Single-consumer rule for pooled tasks

Frame, delay, predicate, and `AsyncOperation.AsOnityTask()` operations use pooled
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
path. Typed `WhenAll` also
collects eligible already successful results directly in input order, without
.NET task bridges or a tracker entry. Repeated single-consumer native inputs,
large native input sets, and pending, faulted, or canceled typed inputs retain
the .NET path. Typed calls still need a result array and callers using inline
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

The task returned by a suspended method is a **single-consumer native task**,
like a frame wait: await it once, or call `AsTask()` once, and call
`Preserve()` before sharing it. Reading its status after it was consumed
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
exactly two inputs, use the pooled coordinator; the typed `WhenAll<T>` and the
`params` overloads with other input counts still bridge pending inputs through
`AsTask()`.

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
execution context, like the other native Onity awaiters. There is no
thread-pool switch yet; use `Task.Run` or the reactive `ObserveOnThreadPool`
operator for worker work.

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
