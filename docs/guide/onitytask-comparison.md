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

Allocation bytes per operation are **unavailable in the original release
reports**. Unity 2022 Mono's
`GC.GetAllocatedBytesForCurrentThread()` returned zero for the harness's known
64 KiB allocation, so calibration rejected it. A `GC.Alloc` ProfilerRecorder
probe reported sample values of 200/400 for that allocation; those are not
byte counts. No zero-allocation comparison follows from those reports.

The [isolated comparison host](https://github.com/furkantokkan/Onity/tree/codex/onitytask-benchmarks)
pins UniTask as a test dependency and enables the benchmark define. The Onity
runtime package does not depend on UniTask. The
[before-builder JSON](https://github.com/furkantokkan/Onity/releases/download/v0.3.10/onitytask-expanded-before-2026-09-23.json)
and [after-builder JSON](https://github.com/furkantokkan/Onity/releases/download/v0.3.10/onitytask-expanded-after-builder-2026-09-23.json)
retain every raw sample. The release also includes their CSV and Markdown
summaries.

## Calibrated allocation follow-up

A later run used the isolated comparison host at commit `832310f` and a
**separate Unity Editor profiler process** for allocation samples. Both
libraries received the same completed warmup batches before measurement. The
profiler read `GC.Alloc` sample byte metadata inside each measured synchronous
slice. Its 64 KiB positive control read 65,568 bytes, and its empty control
read zero. Each library has eight raw samples per scenario.

| Scheduling slice | Concurrent operations | OnityTask B/op | UniTask B/op |
| --- | ---: | ---: | ---: |
| `NextFrame` | 128 | 0 | 0 |
| `NextFrame` | 4,096 | 112.5 | 0 |
| Untyped `async` method awaiting `NextFrame` | 128 | 304 | 64 |
| Typed `async` method awaiting `NextFrame` | 128 | 312 | 72 |
| Typed `async` method awaiting `NextFrame` | 4,096 | 424.5 | 72 |

The 4,096 cohort exceeds Onity's retained frame-source pool capacity. These
figures cover scheduling only, under Editor/Mono profiler instrumentation; they
exclude continuation dispatch and PlayerLoop work. The [raw baseline JSON](../assets/benchmarks/onitytask-unity2022-mono-warm-baseline-2026-09-23.json)
and [CSV](../assets/benchmarks/onitytask-unity2022-mono-warm-baseline-2026-09-23.csv)
contain all 16 timing scenarios, 32 allocation metrics, controls, and samples.

## Completion-source baseline

The new callback-owned completion sources were compared with pinned UniTask
`2.5.11` in a separate Unity 2022.3.62f3 Editor/Mono host. The Onity runtime
was commit `a7c7c9c`; the [reproducible benchmark host](https://github.com/furkantokkan/Onity/tree/benchmark/completion-source)
pins both implementations. Each of 16 scenarios has eight timing samples and
eight allocation samples. Timing uses 4,096 operations per sample; allocation
uses 256. The profiler's 64 KiB positive control read 65,568 bytes and the
empty control read zero.

| Synchronous slice | Consumers | Onity ns/op | UniTask ns/op | Onity B/op | UniTask B/op |
| --- | ---: | ---: | ---: | ---: | ---: |
| Typed source construction | 1 | 210 | 81 | 120 | 80 |
| Typed pending registration | 1 | 138 | 220 | 0 | 16 |
| Typed pending registration | 4 | 786 | 994 | 104 | 152 |
| Typed completion and callback dispatch | 4 | 148 | 358 | 0 | 0 |
| Typed pending `AsTask()` conversion | 1 | 1,103 | 497 | 848 | 120 |
| Untyped source construction | 1 | 482 | 216 | 120 | 72 |
| Untyped pending registration | 1 | 179 | 448 | 0 | 16 |
| Untyped completion and callback dispatch | 4 | 176 | 402 | 0 | 0 |

Onity wins the measured pending-registration slices and four-consumer
dispatch; it loses source construction and pending `AsTask()` conversion.
These are separate synchronous slices, not a measured end-to-end workflow.
`AsTask()` measures conversion before completion; completion and result
consumption occur outside that slice. One diagnostic allocation sample
attributed 744 of Onity's 848 B to context capture and Unity synchronization
context copying during bridge creation. That diagnostic identifies allocation
sites, while the eight-sample report supplies the comparison values.

The [raw JSON](../assets/benchmarks/onity-completion-source-a7c7c9c-warmed-2026-09-23.json),
[CSV](../assets/benchmarks/onity-completion-source-a7c7c9c-warmed-2026-09-23.csv),
and [allocation attribution](../assets/benchmarks/onity-completion-source-a7c7c9c-attribution-2026-09-23.json)
retain every scenario, sample, control, and measured boundary. This is an
Editor/Mono result; it does not establish IL2CPP or device performance.

## Pooled builder experiment

An isolated typed async-method runner passed 23 focused builder tests and 51
existing async tests, but was **not merged or released**. In its matched
benchmark host, typed `async` `NextFrame` scheduling at 128 concurrent
operations measured 2,687 ns/op and 920 B/op for the candidate, versus
1,018 ns/op and 72 B/op for UniTask. The released Onity implementation measured
1,713 ns/op and 312 B/op in the separate baseline run. Its UniTask timing
control measured 925 ns/op in that run, so the cross-run timing difference is
not a precise speedup or slowdown ratio. The candidate's allocation regression
is consistent across all eight samples. The [candidate JSON](../assets/benchmarks/onitytask-unity2022-mono-pooled-candidate-2026-09-23.json)
and [CSV](../assets/benchmarks/onitytask-unity2022-mono-pooled-candidate-2026-09-23.csv)
preserve the full evidence. Work continues on reducing native-await scheduling
cost before this change can be considered for release.

## Feature coverage

| Capability | OnityTask status |
| --- | --- |
| Frame, fixed-frame, and late-frame waits; scaled and unscaled delays; predicate waits | Available with cancellation and single-consumer pooled sources. |
| Scene, `AsyncOperation`, and web-request bridges | Available. Deferred scene loads require the caller to activate a started operation, even after cancellation. |
| `async OnityTask<T>` with synchronous success | Stores the result inline. Suspended and exceptional methods still use .NET `Task` internals. |
| `WhenAll` | Available, including typed ordered results; currently materializes inputs as .NET Tasks. |
| Native `WhenAny` | Available for two untyped inputs; consumes both without canceling the loser. Its source and two delegates allocate per call. |
| Public completion source | Typed and untyped callback completion with retained tasks for multiple consumers; the Editor/Mono comparison above has mixed results. |
| Selectable PlayerLoop phases and immediate cancellation | Limited to the supported runner phases and next-tick cancellation. |
| `await foreach` async enumerable | Not yet available. |

OnityTask is useful for common Unity flows today, but it is **not a full
UniTask replacement**. The next measured work is lower-allocation native async
continuations, source-based composition, and IL2CPP/player validation.
