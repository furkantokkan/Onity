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
| Next fixed update | `await OnityTask.NextFixedFrame(ct)` |
| Next late update | `await OnityTask.NextLateFrame(ct)` |
| Scaled delay | `await OnityTask.Delay(seconds, ct)` |
| Unscaled delay | `await OnityTask.DelayUnscaled(seconds, ct)` |
| Wait for a condition | `await OnityTask.WaitUntil(predicate, ct)` |
| Wait while a condition holds | `await OnityTask.WaitWhile(predicate, ct)` |
| Wait for several operations | `await OnityTask.WhenAll(tasks)` |
| Completed typed result | `await OnityTask.FromResult(value)` |

## Single-consumer rule for pooled tasks

Frame, delay, predicate, and `AsyncOperation.AsOnityTask()` operations use pooled
sources. Each returned `OnityTask` value is **single-consumer**:

- Await the value once, or call `AsTask()` once.
- Do not copy the value to several consumers.
- Do not await it and then call `AsTask()` on the old copy.
- `WhenAll` consumes its input task values; do not await those inputs separately.

When several consumers must observe one operation, materialize one independent
`Task` and share that instead:

```csharp
using System.Threading.Tasks;
using Onity.Unity.Async;

OnityTask wait = OnityTask.NextFrame();
Task shared = wait.AsTask();

await shared;
// Other consumers may await the same Task instance.
```

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

## Unity AsyncOperation bridge

```csharp
ResourceRequest request = Resources.LoadAsync<TextAsset>("GameConfig");
ResourceRequest completed = await request.AsOnityTask(
    cancellationToken: cancellationToken);
```

The operation itself is not canceled by every Unity API; cancellation stops the
await and reports `OperationCanceledException`. Consult the Unity API you wrap
when the underlying operation has separate cancellation behavior.

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
- [Reactive](reactive.html)
- [Events & Messaging](events-messaging.html)
- [Performance & IL2CPP](performance-and-il2cpp.html)
