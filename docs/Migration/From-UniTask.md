---
title: "From UniTask"
parent: "Migration"
nav_order: 4
description: "Map common UniTask patterns to OnityTask, including scene loading, web requests, cancellation, and safe pooled-task consumption."
---

# Migrating from UniTask to OnityTask

`Onity.Unity.Async` provides `OnityTask` and `OnityTask<T>` for common Unity
gameplay async flows: frame waits, delays, scene loads, `AsyncOperation`,
`UnityWebRequest`, reactive stream awaits, async message delivery,
cancellation, and fire-and-forget diagnostics.

OnityTask is the Onity-owned call surface for Unity async gameplay code. Frame
waits, delays, predicates, and `AsyncOperation.AsOnityTask()` use PlayerLoop
sources directly; `Task` remains available through `AsTask()` and legacy interop
helpers.

OnityTask covers the common Unity flows below; it is not a drop-in replacement
for UniTask's full API. Suspended `async OnityTask` methods and many `WhenAll`
cases use .NET `Task` internally, so equivalent allocation behavior is not
guaranteed. Two already successful untyped inputs complete directly; eligible
pending callback-owned inputs use a pooled coordinator with a Task-backed
output.

For a task-oriented introduction, read [Async with OnityTask](../guide/onitytask.html).
For measured Unity 2022 workloads and the current feature gaps, read
[OnityTask and UniTask comparison](../guide/onitytask-comparison.html).

> **Pooled-task safety:** frame, delay, predicate, and
> `AsyncOperation.AsOnityTask()` values are single-consumer. Await each value
> once. If several consumers must share the operation, call `Preserve()` once
> before sharing its returned `OnityTask`, or call `AsTask()` once and share the
> returned `Task`. Do not copy or re-await the original pooled value.

For local timing evidence, run `Onity/Benchmarks/Run OnityTask Benchmarks (Play Mode)`.
It writes `Packages/com.onity.framework/Benchmarks/Results/onity-task-benchmark-latest.*`.
Treat that local output as machine-specific evidence. Published, scoped
Editor/Mono comparison reports and raw samples are linked from the
[OnityTask and UniTask comparison](../guide/onitytask-comparison.html); they
do not establish overall UniTask parity or superiority.

## Namespace

```csharp
using Onity.Unity.Async;
```

## Common Mappings

| UniTask-style code | Onity |
| --- | --- |
| `UniTask.CompletedTask` | `OnityTask.CompletedTask` |
| `UniTask.FromResult(value)` | `OnityTask.FromResult(value)` |
| `await UniTask.NextFrame(ct)` | `await OnityTask.NextFrame(ct)` |
| `await UniTask.DelayFrame(count, cancellationToken: ct)` | `await OnityTask.DelayFrames(count, ct)` |
| `await UniTask.WaitForFixedUpdate(ct)` | `await OnityTask.NextFixedFrame(ct)` |
| `await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: ct)` | `await OnityTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: ct)` |
| `await UniTask.WaitUntil(predicate, cancellationToken: ct)` | `await OnityTask.WaitUntil(predicate, ct)` |
| `await SceneManager.LoadSceneAsync("Game").ToUniTask(...)` | `await OnityTask.LoadScene("Game", onProgress, ct)` |
| `await asyncOperation.ToUniTask(...)` | `await asyncOperation.AsOnityTask(onProgress, ct)` |
| `await request.SendWebRequest().ToUniTask(...)` | `await OnityTask.Send(request, onProgress, ct)` |
| `task.Forget()` | `task.Forget()` |
| `T[] values = await UniTask.WhenAll(typedTasks)` | `T[] values = await OnityTask.WhenAll(typedTasks)` |
| `await UniTask.WhenAll(first, second)` for two untyped inputs | `await OnityTask.WhenAll(first, second)` |
| `int winner = await UniTask.WhenAny(first, second)` for two untyped inputs | `int winner = await OnityTask.WhenAny(first, second)` |
| First completed input from two `UniTask<T>` values | `(int winnerIndex, T result) = await OnityTask.WhenAny(first, second)` for two `OnityTask<T>` values |
| `await observable.FirstAsync(ct)` | `await observable.FirstOnityTask(ct)` |
| `await asyncPublisher.PublishAsync(message, ct).AsTask()` | `await asyncPublisher.PublishOnityTask(message, ct)` |

For two untyped `OnityTaskCompletionSource` inputs, `WhenAll` can observe
pending completion without converting either input to a .NET Task first. It
waits for both inputs, reports faults in argument order ahead of cancellation,
and keeps the output shareable. For pending calls, an input with a preexisting
`AsTask()` bridge or another source type uses the existing Task-based composition.
Preserve or bridge a pooled single-consumer operation when multiple consumers
need its result; do not pass the same pooled value twice.

Typed `WhenAny<T>` takes two inputs with the same result type and returns the
winner's index and value. It consumes both inputs once and observes the loser
without canceling it; duplicate single-consumer native inputs are rejected.
The winning fault or cancellation propagates, and a faulted
`OperationCanceledException` stays faulted. Its native continuation follows
the completion thread rather than capturing Unity's `SynchronizationContext`.
If the next step needs Unity's main thread, await its `AsTask()` bridge from the
Unity context instead. The typed result source allocates; this mapping makes
no performance equivalence claim with UniTask.

## Scene Loading

```csharp
using System.Threading;
using Onity.Unity.Async;

public sealed class SceneLoader
{
    public async OnityTask LoadGameplay(CancellationToken ct)
    {
        await OnityTask.LoadScene("Gameplay", ReportProgress, ct);
    }

    private void ReportProgress(float progress)
    {
        // Update a loading bar from 0..1.
    }
}
```

For additive loading:

```csharp
await OnityTask.LoadSceneAdditive("GameplayUI", ct);
```

For delayed activation:

```csharp
OnityTask<AsyncOperation> load = OnityTask.LoadSceneAsync(
    "Gameplay",
    activateOnLoad: false,
    cancellationToken: ct);

AsyncOperation operation = await load;

try
{
    // Wait for input or complete a fade here.
    await WaitForFadeAsync(ct);
}
finally
{
    await OnityTask.ActivateScene(operation, CancellationToken.None);
}
```

After Unity starts a deferred load, the token cannot cancel the underlying
scene operation. Always activate the returned operation, including when the
wait for input or a fade is canceled.

## AsyncOperation Bridge

```csharp
using System.Threading;
using Onity.Unity.Async;
using UnityEngine;

public sealed class AssetWarmup
{
    public async OnityTask<TextAsset> LoadText(string resourcePath, CancellationToken ct)
    {
        ResourceRequest request = Resources.LoadAsync<TextAsset>(resourcePath);
        ResourceRequest completed = await request.AsOnityTask(cancellationToken: ct);
        return (TextAsset)completed.asset;
    }
}
```

## Web Requests

`OnityTask.Send` awaits an existing `UnityWebRequest` and returns the completed
request. The caller owns disposal of that request.

```csharp
using Onity.Unity.Async;
using UnityEngine.Networking;

using UnityWebRequest request = UnityWebRequest.Get(url);
UnityWebRequest completed = await OnityTask.Send(request, ct);
string json = completed.downloadHandler.text;
```

For simple `JsonUtility` DTOs, use the built-in JSON helpers:

```csharp
using System;
using System.Threading;
using Onity.Unity.Async;

[Serializable]
public sealed class SaveRequest
{
    public int Slot;
    public string Payload;
}

[Serializable]
public sealed class SaveResponse
{
    public bool Ok;
}

public sealed class SaveClient
{
    public OnityTask<SaveResponse> Save(string url, int slot, string payload, CancellationToken ct)
    {
        SaveRequest request = new SaveRequest
        {
            Slot = slot,
            Payload = payload
        };

        return OnityTask.PostJson<SaveRequest, SaveResponse>(url, request, ct);
    }
}
```

Use your own serializer around `OnityTask.Send` when payloads need features
`JsonUtility` does not support.

## Reactive Bridge

```csharp
using System.Threading;
using Onity.Reactive;
using Onity.Unity.Async;

public sealed class WaveGate
{
    private readonly Subject<int> m_remainingEnemies = new Subject<int>();

    public OnityTask<int> WaitForFirstReport(CancellationToken ct)
    {
        return m_remainingEnemies.FirstOnityTask(ct);
    }

    public OnityTask WaitUntilClear(CancellationToken ct)
    {
        return m_remainingEnemies
            .Where(count => count == 0)
            .Select(_ => Unit.Default)
            .ToOnityTask(ct);
    }
}
```

## Async Messaging Bridge

Use `ValueTask` in engine-free messaging internals. Use `OnityTask` at the
Unity-facing orchestration layer.

```csharp
using System;
using System.Threading;
using Onity.Messaging;
using Onity.Unity.Async;

public readonly struct InventorySaved
{
    public readonly int Slot;

    public InventorySaved(int slot)
    {
        Slot = slot;
    }
}

public sealed class SaveFlow
{
    private readonly IAsyncPublisher<InventorySaved> m_savedPublisher;

    public SaveFlow(IAsyncPublisher<InventorySaved> savedPublisher)
    {
        m_savedPublisher = savedPublisher;
    }

    public async OnityTask Save(CancellationToken ct)
    {
        // Save file, cloud state, or profile data here.
        await m_savedPublisher.PublishOnityTask(new InventorySaved(1), ct);
    }
}

public sealed class SaveHud
{
    private readonly IAsyncSubscriber<InventorySaved> m_savedSubscriber;
    private IDisposable m_subscription;

    public SaveHud(IAsyncSubscriber<InventorySaved> savedSubscriber)
    {
        m_savedSubscriber = savedSubscriber;
    }

    public void Start()
    {
        m_subscription =
            m_savedSubscriber.SubscribeOnityTask(
                async (message, ct) =>
                {
                    await OnityTask.Delay(0.25f, ct);
                    ShowSaved(message.Slot);
                });
    }

    public void Stop()
    {
        m_subscription?.Dispose();
        m_subscription = null;
    }

    private void ShowSaved(int slot)
    {
        // Update HUD state here.
    }
}
```

## Fire and Forget

```csharp
OnityTask.LoadScene("Gameplay").Forget(Debug.LogException);
```

Without a handler, exceptions are routed to the Unity log. Long-running tasks
are visible in the Onity Task Tracker window when tracking is enabled.

## Guidance

- Use `OnityTask` for Unity-facing gameplay async flows.
- Use normal `Task` for plain .NET service APIs when that is already the
  project contract.
- Keep `ValueTask` in engine-free low-level messaging paths where it avoids
  allocation and the API is already shipped.
- Do not add UniTask as a runtime dependency just for frame waits, scene loads,
  web requests, or reactive awaits.
