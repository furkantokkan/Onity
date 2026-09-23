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
| Completed typed result | `await OnityTask.FromResult(value)` |

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

`NextFrame` and `DelayFrames(1)` resume no earlier than the following rendered
frame in Play Mode. `DelayFrames(0)` completes immediately. Negative frame
counts throw `ArgumentOutOfRangeException`. Positive `Delay` and `DelayUnscaled`
waits also skip the frame in which they are scheduled in Play Mode.

`WhenAll` currently materializes its inputs as .NET `Task` values. Likewise,
methods declared `async OnityTask` use .NET's async method builder internally.
The two-input `WhenAny` uses a native result source, but currently allocates
that source and two continuation delegates per call.
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
