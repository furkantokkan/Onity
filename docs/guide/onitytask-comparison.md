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

### Completion-source bridge follow-up

An isolated bridge-only candidate at `38fe3e4` avoids capturing Unity's
synchronization context while constructing the .NET task bridge. The bridge
still runs its continuations asynchronously. We applied only this change to
the same benchmark host and repeated all 16 synchronous scenarios. The two
candidate runs used eight timing samples each; the first also used eight
profiler allocation samples. The positive and empty allocation controls again
read 65,568 and zero bytes.

| Pending `AsTask()` slice | Baseline Onity B/op | Candidate Onity B/op | UniTask B/op |
| --- | ---: | ---: | ---: |
| Typed source | 848 | 176 | 120 |
| Untyped source | 848 | 176 | 120 |

The bridge change removed **672 B/op (79.2%)** from each Onity conversion. The
other 30 allocation metrics were unchanged. Typed conversion measured 1,103
ns/op in the baseline and 808 and 770 ns/op in the two bridge-only runs; the
matching UniTask values were 497, 491, and 465 ns/op. These are separate runs,
and the untyped absolute times varied with their controls, so they do not
establish an untyped speedup.

The [first candidate JSON](../assets/benchmarks/onity-completion-source-bridge-38fe3e4-2026-09-23.json),
[CSV](../assets/benchmarks/onity-completion-source-bridge-38fe3e4-2026-09-23.csv),
[second timing JSON](../assets/benchmarks/onity-completion-source-bridge-38fe3e4-rerun-2026-09-23.json),
and [provenance record](../assets/benchmarks/onity-completion-source-bridge-38fe3e4-2026-09-23.provenance.json)
preserve the samples and exact tested source identity. The unchanged harness
embeds the baseline runtime commit in both candidate reports; the provenance
record identifies the candidate change commit and runtime Git blob. The
[candidate benchmark branch](https://github.com/furkantokkan/Onity/tree/benchmark/completion-source-bridge)
contains the tested sources and pinned UniTask dependency.

The `0.3.12` source also fixes a concurrent status-read race; the exact tested
runtime blob is `91017f4e0ed4fc3fd300094de8d0e8ee8c8706ab` from commit
`6dffb69`. Its repeat of the unchanged 16-scenario harness passed all 32
allocation metrics with eight samples each and the same 65,568/0 B controls.
All 32 allocation results matched the bridge-only candidate. Pending typed
`AsTask()` measured 1,095 ns/op and 176 B/op for Onity versus 756 ns/op and
120 B/op for UniTask; untyped measured 1,015 ns/op and 176 B/op versus 593
ns/op and 120 B/op. Timing controls fluctuated, so these means do not establish
a cross-run speedup. Onity remains slower and allocates more for this conversion;
pending registration and four-consumer dispatch remain faster in this run.
The changed pending `IsCompleted` path is outside these 16 scenarios.

The [final-source JSON](../assets/benchmarks/onity-completion-source-bridge-final-6dffb69-2026-09-23.json),
[CSV](../assets/benchmarks/onity-completion-source-bridge-final-6dffb69-2026-09-23.csv),
and [provenance](../assets/benchmarks/onity-completion-source-bridge-final-6dffb69-2026-09-23.provenance.json)
retain the final samples and identity. The unchanged harness still embeds the
baseline runtime commit in this report; use the provenance record for the
packaged source identity.

A separate four-scenario probe measured pending and completed-success
`GetAwaiter().IsCompleted` reads on the same final source, with setup and
completion outside each slice. It used eight timing and profiler allocation
samples per library; the 65,568/0 B controls passed. Both libraries allocated
zero bytes per read.

| `IsCompleted` read | Onity ns/op | UniTask ns/op |
| --- | ---: | ---: |
| Untyped pending | 126 | 93 |
| Typed pending | 115 | 80 |
| Untyped completed | 98 | 85 |
| Typed completed | 89 | 76 |

Onity is slower in this one Editor/Mono probe. The raw timing ranges are broad,
so these means are not a precise speed ratio. The pending status read currently
takes a lock to keep it coherent with an existing .NET task bridge during
completion. See the [raw JSON](../assets/benchmarks/onity-completion-source-status-final-6dffb69-2026-09-23.json),
[CSV](../assets/benchmarks/onity-completion-source-status-final-6dffb69-2026-09-23.csv),
and [source provenance](../assets/benchmarks/onity-completion-source-status-final-6dffb69-2026-09-23.provenance.json).

### Pending status follow-up

At `25c8e20`, a pending `OnityTaskCompletionSource<T>` without a .NET task
bridge returns its observed pending status without entering the source gate.
Bridge creation uses a volatile write, and pending reads with a bridge still
wait for the terminal publication under that gate. The typed and untyped gate
tests, existing bridge/completion race tests, and the Unity EditMode suite pass.

| Pending `IsCompleted` read | Earlier Onity ns/op | Earlier UniTask ns/op | Candidate Onity ns/op | Candidate UniTask ns/op |
| --- | ---: | ---: | ---: | ---: |
| Untyped | 128 | 91 | 91 | 78 |
| Typed | 120 | 82 | 94 | 84 |

Each column comes from an eight-sample Unity 2022 Editor/Mono run of the same
four-scenario harness. All pending and terminal status reads allocated 0 B/op;
the 65,568 B positive and 0 B empty controls passed. The candidate's
same-run Onity/UniTask pending ratios were smaller than the earlier run's.
Absolute times varied between Editor processes, and terminal-read results
varied too, so these runs do not establish a general speed lead or a precise
cross-run improvement. The [earlier samples](https://github.com/furkantokkan/Onity/blob/benchmark/onitytask-preserve/docs/assets/benchmarks/onity-completion-source-status-pr14-aa4c5a5-2026-09-23.json)
and [candidate samples](https://github.com/furkantokkan/Onity/blob/benchmark/onitytask-preserve/docs/assets/benchmarks/onity-completion-source-status-25c8e20-2026-09-23.json)
retain the raw data; adjacent provenance files identify the tested source.

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

### Builder allocation attribution and later candidate

A [calibrated callstack report](https://github.com/furkantokkan/Onity/blob/benchmark/onitytask-builder-attribution/docs/assets/benchmarks/onitytask-builder-attribution-25c8e20-2026-09-23.json)
for the current source (`25c8e20`) repeated the warm 128-operation scheduling
result: suspended Onity async methods allocated 304 B/op untyped and 312 B/op
typed, versus 64 and 72 B/op for UniTask. The extra 240 B/op appeared at
`AsyncMethodBuilderCore.GetCompletionAction()` (160 B/op) and
`AsyncTaskMethodBuilder<T>.get_Task()` (80 B/op). These are measured allocation
sites, not confirmed object types. All ten callstack totals matched the
Profiler markers; the 65,568 B positive and 0 B empty controls passed.

A later isolated [value-continuation candidate](https://github.com/furkantokkan/Onity/tree/experiment/onitytask-value-measure/docs/assets/benchmarks)
at `8292166` passed 29 focused builder tests. Its warm typed scheduling path
still allocated **752 B/op**, versus **72 B/op** for UniTask in that run and
**312 B/op** for current Onity in a separate baseline run. This candidate was
also rejected. Its [callstack report](https://github.com/furkantokkan/Onity/blob/benchmark/onitytask-value-attribution/docs/assets/benchmarks/onitytask-builder-value-attribution-8292166-2026-09-23.json)
attributed 552 B/op to `UnitySynchronizationContext..ctor`, 72 B/op to
`ExecutionContext.Capture`, 48 B/op to
`UnitySynchronizationContext.CreateCopy`, and 80 B/op to the async method call.
The first three measured sites are under the candidate's native continuation
creation. Neither result covers continuation dispatch, the full async
lifecycle, or IL2CPP/player behavior.

## Native task sharing

The isolated [Preserve benchmark host](https://github.com/furkantokkan/Onity/tree/benchmark/onitytask-preserve)
measured commit `956e5cb` against pinned UniTask 2.5.11 in Unity
`2022.3.62f3` Editor/Mono. Each timing result is an eight-sample mean; the
allocation pass used eight samples with 65,568 B positive and 0 B empty
controls. Pending typed `WhenAny` sources had two unresolved inputs. The
measured slices exclude source creation, pending observer registration,
completion, and first consumption.

| Slice | Pending consumers | Onity ns/task | UniTask ns/task | Onity B/task | UniTask B/task |
| --- | ---: | ---: | ---: | ---: | ---: |
| `Preserve()` conversion | 1 | 363 | 94 | 248 | 40 |
| Two late result reads | 1 | 139 | 91 | 0 | 0 |
| Sharing conversion | 4 | 382 | 388 | 248 | 104 |
| Two late result reads | 4 | 144 | 82 | 0 | 0 |

The one-consumer comparison uses UniTask `Preserve()`. The four-consumer
comparison uses UniTask `AsTask()`, which returns a .NET `Task<int>` that
supports multiple pending observers. The small four-consumer timing difference
does not establish a speed lead. A prior Onity source measured 264 B/task; a
Profiler callstack attributed the removed 16 B to its separate lock object.
The current callstack records 120 B at source creation and 128 B in its
constructor; the latter is consistent with the bound completion callback.
Conversion still allocates more than either UniTask comparator. These are
synchronous slices, not full
task lifecycles or IL2CPP/player evidence. See the
[candidate samples](https://github.com/furkantokkan/Onity/blob/benchmark/onitytask-preserve/docs/assets/benchmarks/onity-preserve-native-956e5cb-2026-09-23.json)
and [allocation callstacks](https://github.com/furkantokkan/Onity/blob/benchmark/onitytask-preserve/docs/assets/benchmarks/onity-preserve-native-956e5cb-allocation-callstacks-2026-09-23.json).

### Native sharing follow-up

The owner-checked `Preserve()` source at `c2f9358` reuses the native source's
task-bridge slot for its adapter. In the same calibrated Unity 2022 Editor/Mono
harness, pending conversion fell to **120 B/task**; the callstack attributes all
120 B to the retained source and no bound callback allocation remains. Ordinary
native awaiting returned to its earlier 656 B/task level. Eight allocation
samples per case passed the 65,568 B positive and 0 B empty controls.

| Measured operation | OnityTask B/task | UniTask B/task |
| --- | ---: | ---: |
| Pending one-consumer `Preserve()` conversion | 120 | 40 |
| Pending four-consumer sharing conversion (`Preserve` / `AsTask`) | 120 | 104 |
| Complete native await lifecycle | 656 | 304 |
| Complete one-consumer sharing lifecycle | 776 | 344 |
| Complete four-consumer sharing lifecycle | 880 | 665.3 |

The conversion rows exclude source creation, completion, and first result
consumption. The lifecycle rows include those steps, pending callbacks, and
late reads where applicable. UniTask's two-input `WhenAny` uses a `params`
array inside the measured operation. Four-consumer UniTask `AsTask` callbacks
can run on another thread, outside the main-thread allocation marker. The
libraries also use different return types for four-consumer sharing, so these
figures do not establish a general speed or allocation lead. The
[conversion samples](https://github.com/furkantokkan/Onity/blob/benchmark/onitytask-preserve/docs/assets/benchmarks/onity-preserve-native-c2f9358-2026-09-23.json),
[lifecycle samples](https://github.com/furkantokkan/Onity/blob/benchmark/onitytask-preserve/docs/assets/benchmarks/onity-native-lifecycle-c2f9358-2026-09-23.json),
[allocation callstacks](https://github.com/furkantokkan/Onity/blob/benchmark/onitytask-preserve/docs/assets/benchmarks/onity-preserve-native-c2f9358-allocation-callstacks-2026-09-23.json),
and their adjacent provenance files retain the measured boundaries and exact
source identity. OnityTask still allocates more in these workflows.

### Two-input `WhenAll` experiment

An isolated native-input composition candidate (`a75793f`) passed 546 EditMode
and 23 PlayMode tests, including Unity-context resumption and fault ordering.
It was rejected for the product branch because calibrated Unity 2022 Editor/Mono
scheduling allocations increased in all three measured cases. With Onity's
tracker disabled, pending two-input calls rose from 608 to 1,248 B/op; UniTask
used 160 B/op in the same harness. With the tracker enabled, completed-input
calls rose from 276 to 1,268 B/op, versus UniTask's 128 B/op. Each allocation
case had eight identical samples and passed 65,568/0 B controls. Timing varied
between Editor processes, so this experiment does not establish a speed gain.
The [comparison and raw-evidence links](https://github.com/furkantokkan/Onity/blob/benchmark/onitytask-whenall-candidate/docs/assets/benchmarks/onity-whenall-comparison-2026-09-23.md)
record the exact source and measurement boundaries.

### Completed two-input `WhenAll` fast path

The narrower product change at `a27c14d` returns an already completed task
when both inputs have succeeded. It leaves pending, faulted, and canceled
inputs on the existing `Task.WhenAll` path. The same calibrated Unity 2022
Editor/Mono scheduling harness produced these results:

| Case | Previous Onity B/op | `a27c14d` B/op | UniTask B/op | Previous to current Onity ns/op | UniTask ns/op |
| --- | ---: | ---: | ---: | ---: | ---: |
| Pending, tracker on | 1,620 | 1,556 | 160 | 4,179.5 to 4,322.7 | 733.3 |
| Pending, tracker off | 608 | 544 | 160 | 2,030.4 to 2,087.2 | 742.1 |
| Both completed, tracker on | 276 | 0 | 128 | 1,469.1 to 115.5 | 330.2 |

All eight allocation samples per case were identical and the 65,568/0 B
controls passed. A second candidate timing process measured 117.0 ns/op for
the completed Onity case versus 316.4 ns/op for UniTask; the repeat above
measured 115.5 versus 330.2 ns/op. The small pending timing differences are
not a proven speed change across Editor processes. The completed case is a
scheduling-path result; the marker excludes input creation and full lifetime.
The [raw comparison](https://github.com/furkantokkan/Onity/blob/benchmark/onitytask-whenall-fastpath/docs/assets/benchmarks/onity-whenall-fastpath-comparison-2026-09-23.md)
and [provenance](https://github.com/furkantokkan/Onity/blob/benchmark/onitytask-whenall-fastpath/docs/assets/benchmarks/onity-whenall-fastpath-a27c14d-2026-09-23.provenance.json)
record the exact product source, benchmark runner, and UniTask commit.

### Completed typed `WhenAll<T>` fast path

At `970a4ae`, eligible already successful typed inputs are consumed into an
ordered result array without individual .NET Task bridges or a tracker entry.
The prior `c9ba354` source and candidate used the same benchmark runner and
UniTask 2.5.11 pin in isolated Unity 2022.3.62f3 Editor/Mono hosts. Input
`params` arrays were prepared before the scheduling marker; result-array
creation was included, and result consumption happened afterward.

| Inputs | Previous Onity B/op | Candidate Onity B/op | UniTask B/op | Candidate first run Onity / UniTask ns/op | Candidate repeat Onity / UniTask ns/op |
| --- | ---: | ---: | ---: | ---: | ---: |
| Two completed | 392 | 40 | 112 | 622 / 1,391 | 341 / 433 |
| Four completed | 592 | 48 | 120 | 986 / 1,640 | 408 / 593 |

The first candidate timing process was slower for both libraries than the
repeat; both same-process comparisons favored Onity in these completed cases.
The pending two-input Onity path stayed at **1,408 B/op** versus UniTask's
**144 B/op**. In the candidate repeat, pending scheduling took **4,648.7 ns/op**
for Onity versus **848.1 ns/op** for UniTask. The candidate first run measured
**14,037.2 ns/op** for Onity versus **2,932.7 ns/op** for UniTask; both libraries
had a process-wide cold slowdown. Both hosts passed the 65,568 B positive and
0 B empty allocation controls, and all eight allocation
samples per case agreed. This measures the scheduling slice in the Editor,
not completion, the full task lifetime, or Player/IL2CPP behavior.
The [raw comparison](https://github.com/furkantokkan/Onity/blob/d3495f2/docs/assets/benchmarks/onity-typed-whenall-comparison-2026-09-24.md)
and [provenance](https://github.com/furkantokkan/Onity/blob/d3495f2/docs/assets/benchmarks/onity-typed-whenall-comparison-2026-09-24.provenance.json)
record the measured samples and source pins.

### Pending two-input `WhenAll` completion-source path

For two untyped inputs, `WhenAll` now reuses an internal coordinator when each
input is either the default completed task or an `OnityTaskCompletionSource`
without an existing `AsTask()` bridge, and at least one input is pending. The
output stays Task-backed and can be awaited repeatedly. The coordinator
observes both inputs, retains faults in argument order, lets faults take precedence over
cancellation, and preserves the first canceled input's token. A pending call
with an existing input `AsTask()` bridge uses the prior `Task.WhenAll` path.
Other input types, including pooled native operations, continue to use that
path. A bridge created after the coordinator starts remains supported.

The calibrated [Unity 2022 Editor/Mono comparison](https://github.com/furkantokkan/Onity/blob/b6233dd84533c517890ecb3705b7f285269b42ca/docs/assets/benchmarks/onity-pending-whenall-v6-bridge-fallback-2026-09-24.md)
compared product baseline `6f32e15` with candidate `bc4e469` using the same
normalized runner and pinned UniTask 2.5.11. Each revision had two independent
timing and Profiler sessions with eight samples per case. The measured lifecycle
includes scheduling, input completion, output observation, and cleanup;
input creation and fresh fault exception creation occur outside the marker.

| Pending lifecycle, main-thread GC allocation | Baseline B/op | Candidate B/op |
| --- | ---: | ---: |
| Success, tracker on | 1,488.56–1,488.93 | 1,048.28 |
| Success, tracker off | 624.56 | 176 |
| Fault, tracker on | 2,152.56 | 1,752.28 |
| Fault, tracker off | 1,288.56 | 880 |
| Cancel, tracker on | 1,904.56 | 1,464.28 |
| Cancel, tracker off | 1,040.56 | 592 |

An already bridged faulted input stayed at **936.56 B/op** in both revisions
and both calibrated passes. The already successful two-input scheduling path
stayed at **0 B/op**. Its median timing was 119/117 ns/op before and 119/130
ns/op afterward, which does not support a speed claim. The first observed
pending schedule in a warmed Editor process increased from **792 to 1,226 B**;
this 434 B first-call cost is separate from steady lifecycle results. With 384
outstanding outputs, warmed scheduling totals fell from **208,896 to 115,712
B** per burst, while the UniTask companion used **61,440 B**.

These are main-thread `GC.Alloc` markers: later worker tracker and continuation
allocations are outside their byte totals. Direct worker-thread allocation
could not be calibrated. Only the first all-thread Profiler pass had a valid
ThreadPool positive control. In the candidate's second pass, UniTask used 160,
1,666, and 600 B/op for pending success, fault, and cancellation respectively;
tracker-on Onity remained above those values. The results cover Editor/Mono,
not Player, IL2CPP, device frame time, or battery use. The
[provenance record](https://github.com/furkantokkan/Onity/blob/b6233dd84533c517890ecb3705b7f285269b42ca/docs/assets/benchmarks/onity-pending-whenall-v6-bridge-fallback-2026-09-24.provenance.json)
and four adjacent raw JSON reports retain the controls and sample values.

### Experimental typed pending-pair `WhenAll<T>` prototype

At local candidate `adb4141`, two typed inputs use a pooled coordinator when
each is an inline result or an exact, unbridged `OnityTaskCompletionSource<T>`
task and at least one is pending. It retains the ordered result array after
both succeed and returns a shareable, Task-backed output. Prebridged,
Task-backed, preserved, pooled native, and derived-source inputs retain the
existing Task composition path. Promotion is **on hold**: the prototype is
parked in an isolated branch and will not be merged into PR 14 or included in
the release. It is not a migration or performance guarantee.

The matched Unity 2022.3.62f3 Editor/Mono v7 comparison used baseline
`19fa8ab` and candidate `adb4141`, with two timing and two Profiler processes
per revision. Each of 11 cases had eight allocation samples. The main-thread
positive and empty controls measured exactly 65,568 and 0 B in both revisions.

| Typed two-input case, main-thread allocation | Baseline B/op | Candidate B/op |
| --- | ---: | ---: |
| Warm pending-success schedule, tracker on | 1,408 | 1,040 |
| Warm pending-success schedule, tracker off | 544 | 176 |
| Full success lifecycle, tracker on | about 1,489 | about 1,088 |
| Full success lifecycle, tracker off | 625 | 216 |
| Two completed inputs, schedule | 40 | 40 |
| Four completed inputs, schedule | 48 | 48 |

The first tracker-off pending schedule increased from 990 to 1,480 B, with
one observation per warmed Editor process. A warmed burst with 384 outstanding
outputs fell from 544 to 304 B/op. Pinned UniTask 2.5.11 used 144 B/op for the
pending-success scheduling slice, below even the candidate's tracker-off
176 B/op. The candidate also remained slower than pinned UniTask in the
measured pending-success timing slices in both v7 runs. The prebridged-fault
mean changed from 936.93 to 937.30 B/op: its medians were equal, but each
candidate pass had one extra intermittent 376 B allocation in a 128-operation
sample. In a 32-sample v8 prebridge diagnostic, baseline and candidate each had
31 normal samples and one elevated sample, at different positions (baseline
sample 1, candidate sample 18). In v9, all 32 samples per revision were normal;
callstack controls passed and ordinary callsite counts were equal. The elevated
allocation did not recur, so its source remains unknown. Baseline v7 all-thread
controls were invalid, so no cross-revision total or worker-thread allocation
claim is supported. These are Editor/Mono main-thread measurements, not Player,
IL2CPP, or general UniTask superiority evidence. Full Unity verification passed
605/605 EditMode and 26/26 PlayMode tests. The Release build had zero errors
and 16 existing generated-reference warnings. Candidate product Git blobs
matched the benchmark worktree despite checkout line-ending differences.

### Pending task tracker registration

The `dcdcb51` change replaces the task tracker's per-operation capturing
completion callback with one cached delegate. Calibrated Unity 2022 Editor/Mono
Profiler samples for pending two-input `WhenAll` fell from **1,556 to 1,408
B/op** with tracking enabled. Tracking-disabled Onity remained at **544 B/op**,
and the pinned UniTask control remained at **160 B/op**. All eight samples per
case agreed and the 65,568/0 B controls passed. Callstacks attributed the
removed **148 B/op and two allocations** to the old direct `TrackInternal`
registration site. The remaining 864 B/op tracker-on/off difference is in
`ContinueWith` and execution/synchronization-context capture. Removing that
capture would change observable `AsyncLocal` behavior in the tracker's error
message path, so it remains in place. This marker measures scheduling, not
completion or the full task lifecycle.
The [comparison summary](https://github.com/furkantokkan/Onity/blob/693634b5f55b6ef6a4d8eccfab39b5e0e99f9bc8/docs/assets/benchmarks/onity-whenall-tracker-static-callback-dcdcb51-2026-09-24-summary.md),
[raw callstacks](https://github.com/furkantokkan/Onity/blob/693634b5f55b6ef6a4d8eccfab39b5e0e99f9bc8/docs/assets/benchmarks/onity-whenall-tracker-static-callback-dcdcb51-2026-09-24.json),
and [provenance](https://github.com/furkantokkan/Onity/blob/693634b5f55b6ef6a4d8eccfab39b5e0e99f9bc8/docs/assets/benchmarks/onity-whenall-tracker-static-callback-dcdcb51-2026-09-24.provenance.json)
retain the exact source and runner hashes.

## Feature coverage

| Capability | OnityTask status |
| --- | --- |
| Frame, fixed-frame, and late-frame waits; scaled and unscaled delays; predicate waits | Available with cancellation and single-consumer pooled sources. |
| Scene, `AsyncOperation`, and web-request bridges | Available. Deferred scene loads require the caller to activate a started operation, even after cancellation. |
| `async OnityTask<T>` with synchronous success | Stores the result inline. Suspended and exceptional methods still use .NET `Task` internals. |
| `WhenAll` | Available, including typed ordered results. Already successful two-input untyped and eligible typed calls avoid Task bridges. Eligible pending two-input untyped completion-source calls use a pooled coordinator and a Task-backed output. Pending calls with existing input `AsTask()` bridges, other pending input types, duplicate single-consumer native inputs, and larger native typed sets use the Task bridge path. The typed pending-pair coordinator above is a local prototype on hold; released behavior for pending typed inputs remains the Task bridge path. |
| Native `WhenAny` | Available for two untyped inputs; consumes both without canceling the loser. Its source and two delegates allocate per call. |
| Public completion source | Typed and untyped callback completion with retained tasks for multiple consumers; the Editor/Mono comparison above has mixed results. |
| Native task sharing | `Preserve()` retains typed or untyped pooled completion for multiple pending and late consumers. Pending typed conversion measured 120 B/task in Editor/Mono at `c2f9358`; see the follow-up above. |
| Selectable PlayerLoop phases and immediate cancellation | Limited to the supported runner phases and next-tick cancellation. |
| `await foreach` async enumerable | Not yet available. |

OnityTask is useful for common Unity flows today, but it is **not a full
UniTask replacement**. The next measured work is lower-allocation native async
continuations, source-based composition, and IL2CPP/player validation.
