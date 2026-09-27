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
for UniTask's full API. Suspended `async OnityTask` methods are backed by a
pooled native runner and return single-consumer tasks, like UniTask; unlike
UniTask they flow `AsyncLocal<T>` values across awaits by default; on Unity's
Mono class library that costs an execution-context allocation per suspension
only once a thread has stored an `AsyncLocal<T>` value. Set
`OnityTask.FlowExecutionContext = false` for UniTask's no-flow semantics. Many
`WhenAll` cases still use .NET `Task` internally, so equivalent allocation
behavior is not guaranteed. Two already successful untyped inputs complete
directly; eligible pending inputs use a pooled coordinator with a Task-backed
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
| `await UniTask.Yield(timing, ct)` | 0.4.0: `await OnityTask.Yield(onityTiming, ct)`; Update, FixedUpdate or LateUpdate after script callbacks |
| Explicit-phase next-frame waits | 0.4.0: `OnityTask.NextFrame(onityTiming, ct)` / `DelayFrames(count, onityTiming, ct)`; rendered-frame distance |
| `await UniTask.WaitForFixedUpdate(ct)` | `await OnityTask.NextFixedFrame(ct)` |
| Real rendering end-of-frame wait | 0.4.0: `await OnityTask.WaitForEndOfFrame(ct)`; shared coroutine, main-thread Play/player calls, Editor batch/null graphics rejected |
| `await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: ct)` | `await OnityTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: ct)` |
| `await UniTask.WaitUntil(predicate, cancellationToken: ct)` | `await OnityTask.WaitUntil(predicate, ct)` |
| `await SceneManager.LoadSceneAsync("Game").ToUniTask(...)` | `await OnityTask.LoadScene("Game", onProgress, ct)` |
| `await asyncOperation.ToUniTask(...)` | `await asyncOperation.AsOnityTask(onProgress, ct)` |
| `await request.SendWebRequest().ToUniTask(...)` | `await OnityTask.Send(request, onProgress, ct)` |
| `task.Forget()` | `task.Forget()` |
| `await UniTask.SwitchToMainThread(ct)` | `await OnityTask.SwitchToMainThread(ct)` |
| `await UniTask.SwitchToThreadPool()` | 0.4.0: `await OnityTask.SwitchToThreadPool(ct)`; WebGL Players reject explicitly |
| `T[] values = await UniTask.WhenAll(typedTasks)` | `T[] values = await OnityTask.WhenAll(typedTasks)` |
| `await UniTask.WhenAll(first, second)` for two untyped inputs | `await OnityTask.WhenAll(first, second)` |
| `int winner = await UniTask.WhenAny(first, second)` for two untyped inputs | `int winner = await OnityTask.WhenAny(first, second)` |
| First completed input from two `UniTask<T>` values | `(int winnerIndex, T result) = await OnityTask.WhenAny(first, second)` for two `OnityTask<T>` values |
| `await UniTask.WhenAny(taskArray)` | 0.4.0: `await OnityTask.WhenAny(onityTaskArray)`; untyped index or same-type index/value |
| `task.AttachExternalCancellation(ct)` | 0.4.0: same extension on `OnityTask` / `OnityTask<T>`; stops waiting and still observes the producer |
| `task.SuppressCancellationThrow()` | 0.4.0: bool / `(bool isCanceled, T result)`; actual canceled status becomes a result, faults remain faults |
| `task.Timeout(...)` | 0.4.0: `task.Timeout(seconds, useUnscaledTime: true)`; finite nonnegative float seconds; the producer continues and is observed |
| `task.TimeoutWithoutException(...)` | 0.4.0: bool / `(bool isTimeout, T result)`; only the wrapper's own timeout becomes a flag, producer faults/cancellation propagate |
| `await observable.FirstAsync(ct)` | `await observable.FirstOnityTask(ct)` |
| Finite async streams and synchronous operators | 0.4.0: `OnityAsyncEnumerable.Empty/Return/Range`, `Select/Where/Take/WithCancellation`, native `await foreach`, `FirstAsync` and `ToArrayAsync`; [ownership and limits](../guide/onitytask.html#finite-async-streams) |
| Update streams and BCL async iterators | 0.4.0: pull-based `OnityAsyncEnumerable.EveryUpdate()`, BCL `AsOnityAsyncEnumerable()` and native `AsAsyncEnumerable()`; [cancellation, cleanup and allocation limits](../guide/onitytask.html#update-streams-and-bcl-async-iterators) |
| Channels | 0.4.0: `OnityChannel.CreateBounded<T>(capacity)` / `CreateUnbounded<T>()`, Reader/Writer handles, FIFO backpressure and `ReadAllAsync`; one consumer lease, multiple producers, no drop modes or separate Completion task. [Ownership and limits](../guide/onitytask.html#channels) |
| Sequential async stream operators | 0.4.0: `SelectAwait`, `WhereAwait` and `ForEachAsync` with `(value, CancellationToken) => OnityTask` delegates; one item at a time, owned delegate cancellation and shared cleanup. [Semantics and example](../guide/onitytask.html#sequential-awaitable-operators) |
| `await asyncPublisher.PublishAsync(message, ct).AsTask()` | `await asyncPublisher.PublishOnityTask(message, ct)` |

For two untyped `OnityTaskCompletionSource` inputs, `WhenAll` can observe
pending completion without converting either input to a .NET Task first. It
waits for both inputs, reports faults in argument order ahead of cancellation,
and keeps the output shareable. For pending calls, an input with a preexisting
`AsTask()` bridge or another source type uses the existing Task-based composition.
Preserve or bridge a pooled single-consumer operation when multiple consumers
need its result; do not pass the same pooled value twice.

The 0.4.0 typed `WhenAll<T>` path can also avoid input bridges for 1–16
unique exact built-in completion sources without existing bridges, mixed with
inline/default values. Its output remains shareable and Task-backed. Other
pending typed inputs keep the existing fallback. This saves construction heap
growth in the recorded probes but regresses IL2CPP construction time; see the
[scoped report](../assets/benchmarks/onitytask-stage3a-whenall-2026-09-27.md).

Typed `WhenAny<T>` takes two inputs with the same result type and returns the
winner's index and value. It consumes both inputs once and observes the loser
without canceling it; duplicate single-consumer native inputs are rejected.
The winning fault or cancellation propagates, and a faulted
`OperationCanceledException` stays faulted. Its native continuation follows
the completion thread rather than capturing Unity's `SynchronizationContext`.
If the next step needs Unity's main thread, await its `AsTask()` bridge from the
Unity context instead. The typed result source allocates; this mapping makes
no performance equivalence claim with UniTask.

The 0.4.0 array overloads extend this behavior to any positive input count.
They snapshot before registration and reject duplicate single-consumer identities
before claiming any input. Already-completed inputs favor the lowest index;
pending callbacks race by observation. Every loser is observed, including faults
on completion-source subclasses and their later Task bridges. Outputs remain
single-consumer unless preserved or bridged. Bounded pooling covers up to 16
inputs; larger arrays are unpooled. Pair overloads keep their previous behavior.
The [array comparison](../assets/benchmarks/onitytask-stage3b-whenany-2026-09-27.md)
favors UniTask in measured time and shows a substantial Onity allocation increase
at 32 inputs; do not infer performance equivalence from this API mapping.

`OnityTask.SwitchToMainThread` completes synchronously on the main thread and
queues a worker-thread continuation for the Update phase of a following frame.
Cancellation is observed at `GetResult` on the destination thread. Outside Play
Mode the Editor drains the queue from its update loop, and continuations
queued in an earlier Play Mode session are discarded when the next session
starts. Timing selection remains limited. Onity 0.4.0 adds
`SwitchToThreadPool(ct)` and synchronous `RunOnThreadPool(Action/Func<T>,
returnToMainThread, ct)` overloads. Cancellation cannot interrupt a running
delegate; faults take precedence, and optional main-thread returns belong to
the originating session. WebGL Players reject thread-pool use. Repeated Mono
and IL2CPP [thread-pool measurements](../assets/benchmarks/onitytask-stage1-threadpool-2026-09-27.md)
do not establish an overall performance winner.

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
