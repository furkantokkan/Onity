---
title: "OnityTask and UniTask comparison"
parent: "Guides"
nav_order: 8
description: "Measured Unity 2022 OnityTask and UniTask workloads, API coverage, and current limits."
---

# OnityTask and UniTask comparison

This page records a **Unity 2022.3.62f3 Windows Editor / Mono** comparison with
official UniTask 2.5.11 pinned to commit
`2e993ff18f28c931602a07292df0b0804eebef99`. It describes measured
workloads, not a general winner or a result for IL2CPP players. The separate
[benchmark harness](https://github.com/furkantokkan/Onity/blob/main/Packages/com.onity.framework/Benchmarks/Tasks/README.md)
defines every timing boundary and how to reproduce the run.

## Measured result

The table shows mean nanoseconds per operation from one eight-sample run after
the synchronous typed builder improvement. Each frame cohort had two completed
warmup batches, then 32 batches per sample. Library order alternated. The 128
operation cohort fits Onity's retained frame-source pool; the 4,096 operation
cohort exceeds it. Values are rounded from the raw report.

| Workload | Concurrent operations | OnityTask ns/op | UniTask ns/op |
| --- | ---: | ---: | ---: |
| `NextFrame` scheduling | 128 | 373 | 352 |
| `NextFrame` scheduling | 4,096 | 371 | 311 |
| Completed typed `async` method and `GetResult` | 1 | 550 | 318 |
| Untyped `async` method with one `NextFrame`: scheduling | 128 | 1,814 | 982 |
| Typed `async` method with one `NextFrame`: scheduling | 128 | 1,863 | 998 |
| Typed `async` method with one `NextFrame`: `GetResult` | 128 | 63 | 102 |

Scheduling and `GetResult` are separate **main-thread synchronous slices**.
Suspended-frame time, PlayerLoop work, continuation dispatch, and builder
completion during resumption are outside them. The faster `GetResult` slice
does not show that the whole async operation is faster. The synchronous cases
showed substantial timing variance, so use the raw samples when assessing a
small difference. This run does **not** establish OnityTask performance
superiority over UniTask.

Allocation bytes per operation are **unavailable**. Unity 2022 Mono's
`GC.GetAllocatedBytesForCurrentThread()` returned zero for the harness's known
64 KiB allocation, so calibration rejected it. A `GC.Alloc` ProfilerRecorder
probe reported sample values of 200/400 for that allocation; those are not
byte counts. No zero-allocation comparison follows from these reports.

The comparison host uses a pinned UniTask test dependency. The Onity runtime
package does not depend on UniTask. The raw before/after JSON, CSV, and Markdown
reports are kept with the release artifacts for independent inspection.

## Feature coverage

| Capability | OnityTask status |
| --- | --- |
| Frame, fixed-frame, and late-frame waits; scaled and unscaled delays; predicate waits | Available with cancellation and single-consumer pooled sources. |
| Scene, `AsyncOperation`, and web-request bridges | Available. Deferred scene loads require the caller to activate a started operation, even after cancellation. |
| `async OnityTask<T>` with synchronous success | Stores the result inline. Suspended and exceptional methods still use .NET `Task` internals. |
| `WhenAll` | Available, including typed ordered results; currently materializes inputs as .NET Tasks. |
| Native `WhenAny` | Under development; current `OnityAsync.WhenAny` accepts .NET Tasks. |
| Selectable PlayerLoop phases and immediate cancellation | Limited to the supported runner phases and next-tick cancellation. |
| `await foreach` async enumerable and public completion source | Not yet available. |

OnityTask is useful for common Unity flows today, but it is **not a full
UniTask replacement**. The next measured work is a pooled async-method runner,
source-based composition, and a separate calibrated allocation-byte pass.
