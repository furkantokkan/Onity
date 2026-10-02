---
title: "OnityTask and UniTask comparison"
parent: "Guides"
nav_order: 8
description: "Measured OnityTask and UniTask 2.5.11 Release Player results, API coverage, and current limits."
---

# OnityTask and UniTask comparison

This page records Unity Editor and Windows Player comparisons with official
UniTask 2.5.11 pinned to commit `2e993ff18f28c931602a07292df0b0804eebef99`.
The latest verification uses **Unity 2022.3.62f2, Mono and IL2CPP Release Players**;
older sections retain their measured Editor versions and source revisions.
Each result applies to its stated workload and timing boundary. The separate
[benchmark harness](https://github.com/furkantokkan/Onity/blob/main/Packages/com.onity.framework/Benchmarks/Tasks/README.md)
defines every timing boundary and how to reproduce the run.

## Latest Player verification (2026-10-02)

Onity 0.6.0 was measured against UniTask 2.5.11 in non-development Release
Players (Unity 2022.3.62f2, Windows x64) built from source `6cc2115`. Three
Player processes per backend ran each of three suites; the gate reads the median
and the worst process of the Onity/UniTask time ratio, so a ratio below 1 means
OnityTask took less time. Onity ran with its default `FlowExecutionContext =
false` (UniTask has no context flow) and with pool retention raised to the
cohort for the 1,024- and 4,096-operation bursts, because UniTask's pools retain
every released object. The [full report](../assets/benchmarks/onitytask-surpass-2026-10-02.md)
lists every row, the per-process ratios, the configuration and the raw data.

**IL2CPP, the gate backend: all 29 gated rows pass.** No process measured Onity
slower in any gated row.

| Suite and rows | Rows | Median Onity/UniTask | Worst process |
| --- | ---: | ---: | ---: |
| `primary`: consume a completed task or a synchronously completed async method (N = 1) | 4 | 0.515-0.808 | 0.906 |
| `primary`: `NextFrame` scheduling and `GetResult` (128, 4,096) | 4 | 0.085-0.125 | 0.126 |
| `primary`: async method awaiting `NextFrame`, typed and untyped, scheduling and `GetResult` (128, 4,096) | 8 | 0.307-0.415 | 0.418 |
| `builderlifecycle`: complete async-method lifecycle with 1 or 4 suspensions, typed and untyped (128, 4,096) | 8 | 0.490-0.605 | 0.874 |
| `throughput`: 16-await `NextFrame` / `Yield` loops under the real PlayerLoop (1,024, 4,096) | 5 | 0.280-0.353 | 0.377 |

**Mono, reported and not gated.** Onity's median is lower in 25 of 29 rows:

| Suite and rows | Rows | Median Onity/UniTask |
| --- | ---: | ---: |
| `primary`: completed results (N = 1) | 4 | 1.087-1.924 (Onity slower) |
| `primary`: `NextFrame` scheduling and `GetResult` | 4 | 0.120-0.319 |
| `primary`: async method awaiting `NextFrame` | 8 | 0.564-0.814 |
| `builderlifecycle`, 1 suspension | 4 | 0.885-0.909 |
| `builderlifecycle`, 4 suspensions | 4 | 0.932-0.973 (one process 1.009) |
| `throughput` | 5 | 0.361-0.721 |

**Report-only results to read with the gate:**

- **Opt-in context flow.** With `FlowExecutionContext = true`, every suspension
  captures and restores the execution context, which UniTask never does. The
  complete lifecycle with four suspensions is then 1.51-1.60x slower than
  UniTask on IL2CPP and 2.65-2.72x on Mono (128 and 4,096 calls); with one
  suspension it stays faster on IL2CPP (0.82-0.84) and is 1.63-1.65x slower on
  Mono. The synchronous scheduling and consumption slices stay faster with flow
  on (IL2CPP 0.31-0.53, Mono 0.72-0.82).
- **Default retention in 4,096-call bursts.** With the default caps (128
  runners per async method, 256 sources per source type) a burst above the cap
  allocates, and the complete lifecycle of three of the four IL2CPP rows is
  1.47-2.58x slower (typed 4-suspension: 0.95); on Mono all four are 1.63-2.50x
  slower. Raise `OnityTask.RunnerPoolCapacity` / `SourcePoolCapacity` for
  bursty methods; see [Bursts and pool retention](onitytask.html#bursts-and-pool-retention).
  The 4,096 `primary` slices stay faster at the default caps.
- **Single calls.** The builder lifecycle with one call at a time (N = 1) is
  0.86-0.90 on IL2CPP and 0.97-1.00 on Mono.

**Noise.** The quiet pre-gate refused the host because other Unity Editors used
CPU, and the noise screen flagged all 22 processes. On each backend 21 of the 29
rows have their three per-process ratios within 5% of each other; the widest
IL2CPP spread is typed `int` with 4 suspensions at N = 128 (0.603-0.874).

**Correctness.** The same Mono and IL2CPP Players passed all 42 Player smoke
cases. The generated IL2CPP C++ shows that the async-method runner calls its
stored state machine by address with no copy back and clears it with
`il2cpp_codegen_initobj` when it returns to the pool, which is what makes the
immediate runner return safe, and that `OnityTask` members carry no
class-initialization checks.

What changed since the measurements below: pooled sources are class-rooted with
a single-word completion protocol, pools are array-slot stacks with a shared
`SourcePoolCapacity`, async-method runners return to their pool when their task
is consumed on every backend, default frame waits are stateless in Play,
`Yield()` returns a reference-free awaitable, `OnityTask` has no static
constructor, the hot task, runner, source and PlayerLoop types turn off IL2CPP
null checks, and context flow is off by default.

These are timing results for the measured workloads on one Windows PC. They do
not establish allocation results, other platforms (ARM64, consoles, mobile) or
Development Player behavior.

## Feature coverage

Onity 0.6.0 covers UniTask 2.5.11's runtime API, except for the gaps listed
below, under Onity names; [Migrating from UniTask](../Migration/From-UniTask.html)
maps each UniTask member. Addressables, DOTween and TextMeshPro integrations
(UniTask's `External` folder) are out of scope.

| Area | OnityTask 0.6.0 |
| --- | --- |
| Core task types | `OnityTask`, `OnityTask<T>`, `OnityTaskVoid`, Onity method builders, `Status` / `OnityTaskStatus`, implicit `OnityTask<T>` to `OnityTask` view, `Preserve()`, `CompletedTask`, `FromResult`, `FromException`, `FromCanceled`, `Create`, `Defer`, `Never`, `Lazy` / `OnityAsyncLazy`, `Void` / `Action` / `UnityAction` |
| PlayerLoop timing | 19 `OnityPlayerLoopTiming` members (UniTask's 16 plus three after-script positions), `Yield` / `NextFrame` / `DelayFrame` with `cancelImmediately`, `WaitForFixedUpdate`, `WaitForEndOfFrame`, `Post`, `OnityTaskPlayerLoop` items, continuations, `Initialize`, `InitializeAll`, `IsInjected`, `DumpCurrentPlayerLoop` |
| Timed waits and timers | `Delay(TimeSpan, ignoreTimeScale or OnityDelayType, timing)`, `WaitForSeconds`, `WaitUntil` / `WaitWhile` with timing or state, `WaitUntilCanceled`, `WaitUntilValueChanged`, `OnityPlayerLoopTimer`, timed `CancelAfterSlim`, `OnityTimeoutController`, timed `Timeout` / `TimeoutWithoutException` |
| Threading | `SwitchToMainThread` (also with a timing), `ReturnToMainThread`, `SwitchToThreadPool`, `SwitchToTaskPool`, `SwitchToSynchronizationContext`, `ReturnToSynchronizationContext`, `ReturnToCurrentSynchronizationContext`, every `RunOnThreadPool` shape |
| Composition | `WhenAll` / `WhenAny` over arrays, sequences, typed tuples (2-15) and mixed types (2-15), left/right `WhenAny`, `WhenEach`, `await` on arrays, sequences and tuples, `IEnumerable<T>.Select` to tasks |
| Cancellation | destroy tokens, `RegisterRaiseCancelOnDestroy`, `AttachExternalCancellation`, `SuppressCancellationThrow`, `ToCancellationToken`, token `ToOnityTask` / `WaitUntilCanceled`, `AddTo(CancellationToken)`, `RegisterWithoutCaptureExecutionContext`, `IsOperationCanceledException`, `OnityCancellationTokenEqualityComparer` |
| Completion sources | `OnityTaskCompletionSource(<T>)`, `OnityAutoResetTaskCompletionSource(<T>)` |
| Unity operations | `AsyncOperation` (with `Action<float>` or `IProgress<float>`), `ResourceRequest`, `AssetBundleRequest`, `AssetBundleCreateRequest`, `UnityWebRequestAsyncOperation`, `AsyncGPUReadbackRequest`, `AsyncInstantiateOperation`, `Awaitable`, `JobHandle` (`await`, `AsOnityTask`, `WaitAsync`), `OnityProgress`, `OnityUnityWebRequestException` |
| Coroutines | `ToCoroutine`, awaiting `IEnumerator`, `ToOnityTask` (PlayerLoop or `MonoBehaviour`), `StartAsyncCoroutine` |
| Triggers and UI events | lifecycle triggers, 55 MonoBehaviour message triggers, public trigger base and waiter list, `UnityEvent` waits and streams, uGUI waits, streams, EventSystems triggers and `BindTo` (optional `Onity.Unity.UGUI`), UI Toolkit waits, streams and `BindTo` (beyond UniTask) |
| Async streams | native `IOnityAsyncEnumerable<T>`, `Create` with `IOnityAsyncWriter<T>`, `Empty`, `Return`, `Range`, `Repeat`, `Never`, `Throw`, timing streams, conversions, the async LINQ operators and consumers, `Subscribe` / `SubscribeAwait`, `Publish`, `Queue`, generic `BindTo`, BCL and reactive adapters |
| Channels | bounded (beyond UniTask) and unbounded channels, `Completion`, `WaitToReadAsync`, `Complete`, `ReadAllAsync` |
| Async reactive properties | `OnityAsyncReactiveProperty<T>`, `OnityReadOnlyAsyncReactiveProperty<T>`, `ToReadOnlyAsyncReactiveProperty` |
| Diagnostics and policy | `OnityTaskScheduler`, `Forget(handler, handleExceptionOnMainThread)`, task tracker, `RunnerPoolCapacity` / `SourcePoolCapacity` |
| Task interop | `ContinueWith`, `Unwrap`, `ValueTask` / `Task` bridges, `AsUnitTask`, observable bridges |

Beyond UniTask, the same package connects async work to DI scopes
(`IOnityScopeLifetime`, `AddTo(scope)`, `GetScopeCancellationToken`,
`IOnityAsyncInitializable`, `OnityContext.WaitReadyAsync`), reactive properties
(`WaitAsync`, `WaitUntilAsync`, `AsLatestAsyncEnumerable`) and message channels
(`ReceiveAsync`, `ReceiveAllAsync`, `SubscribeQueued`), plus the bundled scene,
web JSON and opt-in `AsyncLocal<T>` flow helpers.

Remaining gaps:

- `PlayerLoopTiming` and `cancelImmediately` overloads of the Unity operation
  adapters (`AsyncOperation`, `ResourceRequest`, `AssetBundleRequest`,
  `AsyncGPUReadbackRequest`); they poll at Update and observe cancellation at
  the next Update.
- A native shareable `WhenAll` output: the array `WhenAll` paths still return a
  Task-backed output when inputs are pending.
- `UniTaskSynchronizationContext` (no `OnityTaskSynchronizationContext`).
- Index-only `SelectAwait(Func<T, int, OnityTask<R>>)` and
  `WhereAwait(Func<T, int, OnityTask<bool>>)`, which would be ambiguous with
  Onity's token forms; the indexed forms exist with a token.
- `Delay(int milliseconds)`: `Delay(2)` means two seconds in Onity; use
  `Delay(TimeSpan.FromMilliseconds(ms))`.
- Public custom task sources (`IUniTaskSource`, `UniTaskCompletionSourceCore<T>`),
  the `IPromise` interfaces, `TaskPool.GetCacheSizeInfo` and the obsolete
  `UniTask.Run`.
- Task tracking of `async OnityTaskVoid` starts (only `Forget` is tracked
  natively).

## Earlier measurements

The sections below record earlier implementations, harnesses and decisions in
the order they were made. They are superseded by the 2026-10-02 verification
above and kept as history: statements such as "superiority remains unmet" or
"keep context flow on by default" describe the state at their date. Context flow
was on by default until 0.6.0; it is now off by default (UniTask semantics), and
flow on is an opt-in that preserves ambient `AsyncLocal<T>` values and is
reported separately, never gated.

## Dispatch investigation (2026-09-30)

Two runner-dispatch experiments were tested and reverted: neither established
a repeatable general async benefit. Some IL2CPP slices improved, Mono results
were mixed, and other Editor activity contaminated the final comparison.
Keep the verified runtime packet below and the default context-flow behavior.
Twelve new two-suspension context regression cases are retained. The
[experiment report](../assets/benchmarks/onitytask-dispatch-performance-2026-09-30.md)
records all variants, raw results, noise, focused verification and the decision.
General async superiority over UniTask remains unmet.

## Retained Player verification (2026-09-29)

The unreleased 0.5 development changes were compared with pulled commit
`5c09749` in three Release Player processes per revision/backend. They protect
pooled sources during bridge/continuation publication, remove frame-pool
monitors, add a separate 32-input WhenAny pool, and inline small builder paths.

| Measured workload | Candidate Mono / UniTask | Candidate IL2CPP / UniTask |
| --- | ---: | ---: |
| Plain NextFrame scheduling, 128 | 0.916–0.944 | 0.635–0.679 |
| Async NextFrame scheduling, 128, flow on | 1.177–1.260 | 1.576–1.632 |
| Async NextFrame scheduling, 128, flow off | 0.959–1.114 | 1.404–1.563 |
| Array WhenAny cycle, 32 inputs | 0.886–1.004 | 1.361–1.551 |

Ratios above 1 are slower; typed/untyped results and all three processes are
included. Scheduling slices exclude resumption and deferred returns. Mono
synchronous async completion improved, and 32-input WhenAny improved most
clearly on Mono and untyped IL2CPP. Other results are mixed; some ratio changes
reflect a varying UniTask denominator. Overall superiority and the 1.2x async
milestone remained unmet at this point, and context flow stayed on by default
(it is off by default since 0.6.0).

Release coverage includes all 983 EditMode cases (six test-maintenance failures
passed on focused rerun), 94 PlayMode cases and 35 smoke cases per Player;
127 focused default-optimization cases also pass. Sixteen diagnostic profiles
confirm removal of two frame-pool monitors per operation, unchanged warm-cycle
allocation events, and eight additional bytes per cold frame-source allocation
on this Windows x64 build. Calibrated HeapDelta zero readings are not proof of
zero allocation. See the [full report, raw samples and test evidence](../assets/benchmarks/onitytask-05-performance-2026-09-29.md).

## Previous Player verification (2026-09-27)

Runtime candidate `528d52c` plus benchmark-only startup/verification changes
passed 668/668 EditMode and 41/41 PlayMode tests under both default and Release
code optimization. Final Mono and IL2CPP Players each passed 13 semantic smoke
cases and used the internal execution-context capture/run pair. Runtime library
sources were unchanged; full suites preceded the final benchmark-only reporting
and smoke additions, which were rebuilt and verified in both Players.

Two independent processes per backend measured 24 primary scenarios with
tracking off and runner capacity 128. These are Onity/UniTask mean-time ratios
for typed/untyped async `NextFrame` **scheduling slices**; above 1 is slower.

| Backend | Concurrency | Flow on | Flow off |
| --- | ---: | ---: | ---: |
| Mono | 128 | 1.41-1.53 | 1.23-1.26 |
| Mono | 4096 | 1.76-1.88 | 1.48-1.61 |
| IL2CPP | 128 | 1.77-1.94 | 1.70-1.74 |
| IL2CPP | 4096 | 1.94-2.29 | 1.81-2.13 |

All allocation metrics retained eight valid samples using calibrated HeapDelta
(69,632-byte positive control, zero-byte empty control). At 128 operations,
Mono read zero and IL2CPP read 242-245 B/op. A separate two-frame drain control
reduced IL2CPP's reading to zero, supporting delayed pool replenishment as the
cause; it still read 607-611 B/op at 4096. HeapDelta is coarse and process-wide:
zero does not prove zero allocation. The control does not replace the default
workload or demonstrate a runtime optimization.

At the time `FlowExecutionContext = true` stayed the default to preserve
ambient-context semantics; since 0.6.0 flow is off by default (UniTask
semantics) and flow on is an opt-in reported separately. Neither superiority nor
the intermediate 1.2x timing milestone was established by this run. Separate optimized Development IL2CPP profiles passed all eight
configurations and captured all resumptions and deferred returns. They identify
native source synchronization as the next bounded investigation: five of nine
operation-related monitor acquisitions occur in registration, completion and
result consumption. Their instrumented durations do not establish a Release
bottleneck percentage. See the [verification report and raw samples](../assets/benchmarks/onitytask-player-verification-2026-09-27.md)
for startup diagnosis, allocation sites, build settings, test freshness and limits.
The subsequent investigation implemented a packed source-state word without
source-base monitors. The historical desktop-Mono before/after numbers
are in [plan 12](https://github.com/furkantokkan/Onity/blob/main/docs/Plan/12-OnityTask-PlayerVerification.md#native-synchronization-reduction---2026-09-29);
Unity verification and the further changes are covered by the newer report
above. These earlier ratios retain their original source identity.

## Historical Editor measured result

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

## Typed two-input `WhenAny` comparison

Product commit `37c309a` was measured against UniTask commit `2e993ff` in
Unity 2022.3.62f3 Windows Editor/Mono. Two independent timing processes and
two calibrated Profiler processes each recorded eight raw samples per case.
The 12 scenarios covered tracker ON and OFF; the allocation values below were
the same in both modes. Times are median ranges across the two runs, rounded.

| Measured case | OnityTask B/op | UniTask B/op | Timing result |
| --- | ---: | ---: | --- |
| Pending success, scheduling | 424 | 160 | Onity ~679–709 ns/op; UniTask ~784–838 ns/op |
| Pending success, full lifecycle | 424 | 160 | Onity ~1.93–1.97 µs/op; UniTask ~2.01–2.03 µs/op |
| Pending fault, full lifecycle | 936 | 1,666 | Onity faster |
| Pending cancellation, full lifecycle | 936 | 600 | Onity slower |
| Both inputs completed, lifecycle | 424 | 128 | Onity slower |
| Second input completed first, lifecycle | 424 | 144 | Onity slower |

Sources, faults, and cancellation tokens were prepared outside the markers.
The pending scheduling slice includes `WhenAny` creation, including UniTask's
fresh two-element `params` array, and excludes completion and consumption.
Lifecycle markers also include winner completion and observation, loser
completion and validation, and cleanup. Completed-input controls measure the
corresponding lifecycle. These are calibrated **main-thread** Editor/Mono
results, not whole-library, Player, or IL2CPP claims. The
[benchmark note and raw samples](https://github.com/furkantokkan/Onity/blob/benchmark/onitytask-typed-whenany-candidate/docs/assets/benchmarks/onity-typed-whenany-unity2022-mono-2026-09-24.md)
record the exact boundaries and provenance.

### Typed `WhenAny` callback follow-up

Candidate `d66946f` defers each bound completion callback until its input is
pending, compared with product baseline `37c309a`. In Unity 2022.3.62f3
Editor/Mono, four calibrated Profiler processes and six timing processes
measured both tracker ON and OFF. Main-thread allocation was identical across
tracker modes and both Profiler repetitions:

| Case | Baseline B/op | Candidate B/op |
| --- | ---: | ---: |
| Both inputs completed | 424 | 152 |
| Second input completed first | 424 | 280 |
| Pending success, scheduling and lifecycle | 424 | 408 |
| Pending fault or cancellation, lifecycle | 936 | 920 |

Prepared-host B2/C2 and B3/C3 timing pairs did not show a repeatable pending
scheduling slowdown; the first-import B1/C1 pair was noisy. Completed-input
timing improved in both prepared-host pairs. UniTask still allocated less for
pending success (160 B/op), both completed (128 B/op), and cancellation
(600 B/op), so this change does not establish overall superiority. The
candidate passed 602/602 EditMode and 28/28 PlayMode tests; the Release
`Onity.Unity.csproj` build had zero errors and 16 existing MSB3277 warnings.
The [immutable comparison and raw evidence](https://github.com/furkantokkan/Onity/blob/9d68815f38847731377f59c25a34d5b736f118c3/docs/assets/benchmarks/onity-typed-whenany-callbacks-2026-09-24.md)
record the controls, sample values, and Editor/Mono measurement boundaries.

## Thread-switch comparison

`OnityTask.SwitchToMainThread` at product commit `3260c40` was measured against
pinned UniTask `2.5.11` with the dedicated
`Onity/Benchmarks/Run OnityTask Thread Switch Benchmarks (Play Mode)` harness
in two separate Editor/Mono processes on 2026-09-25. The host was the
installed Unity 2022.3.62f2 Windows Editor with Debug code optimization and
incremental GC enabled, on an AMD Ryzen 9 5900X with other Unity instances
open; the repository's pinned 2022.3.62f3 Editor was not installed there.
Values are mean ns/op from eight samples per case with alternating library
order; Delta is Onity relative to UniTask, so a negative value favors Onity.
These are descriptive comparisons, not significance tests.

| Scenario | N | Run 1 Onity | Run 1 UniTask | Delta | Run 2 Onity | Run 2 UniTask | Delta |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Main-thread switch | 1 | 207.00 | 209.88 | -1.4% | 198.09 | 199.01 | -0.5% |
| Pre-canceled main-thread switch | 1 | 9882.97 | 10569.85 | -6.5% | 6263.22 | 6613.02 | -5.3% |
| Worker scheduling | 128 | 486.77 | 433.72 | +12.2% | 299.10 | 402.55 | -25.7% |
| Main-thread dispatch | 128 | 101.06 | 81.07 | +24.7% | 100.15 | 73.57 | +36.1% |
| Worker scheduling | 4096 | 323.81 | 361.80 | -10.5% | 204.28 | 261.87 | -22.0% |
| Main-thread dispatch | 4096 | 102.58 | 80.57 | +27.3% | 76.00 | 59.20 | +28.4% |
| Thread-pool hop then switch | 128 | 47351.62 | 47224.63 | +0.3% | 19014.39 | 17793.82 | +6.9% |

The synchronous main-thread switch is close in both runs. Enqueueing 4,096
continuations from a worker favored Onity in both runs, while the 128-operation
enqueue changed direction between runs and has no winner. Main-thread callback
dispatch favored UniTask in both runs by about 25 to 36 percent (24.7 to 36.1
percent). Dispatch measures
the interval between the first and last callback of one batch divided by N-1;
queue setup, buffer swapping, and teardown are outside it. The round trip uses
`async Task` for both libraries, so it compares neither native builder, and its
batch time divided by concurrency is not an individual call latency.

The measured `3260c40` drain read each entry through a `List<T>` indexer, read
the session number per entry, and invoked every continuation through a separate
non-inlined helper with its own null check and exception guard. The pinned
UniTask `ContinuationQueue` runs a raw `Action[]` with an inline exception
guard. The drain was rewritten afterwards to array-backed double buffers with
one session read per batch and an inline exception guard; the rerun below
measures that change. In the Editor's default Debug mode the Mono JIT disables
inlining and register allocation, which penalizes helper calls such as the old
drain's indexer and invoke helper more than a Release build would, and
identical code varied by 25 percent between the two runs (Onity 4,096
dispatch: 102.58 versus 76.00 ns/op). Later reports record the mode and the
incremental GC setting, and differences below about 5 ns/op should be treated
as noise.

### Rerun after the drain rewrite

The same harness, host, and settings were rerun on product commit `6c978a6`
on 2026-09-25, again in two separate Editor/Mono processes. The package tree
of the benchmark host matched the commit exactly.

| Scenario | N | Run 1 Onity | Run 1 UniTask | Delta | Run 2 Onity | Run 2 UniTask | Delta |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Main-thread switch | 1 | 223.11 | 213.41 | +4.5% | 169.09 | 164.22 | +3.0% |
| Pre-canceled main-thread switch | 1 | 8588.33 | 8666.96 | -0.9% | 5170.93 | 5278.80 | -2.0% |
| Worker scheduling | 128 | 425.78 | 459.70 | -7.4% | 322.85 | 322.66 | +0.1% |
| Main-thread dispatch | 128 | 63.82 | 99.89 | -36.1% | 68.44 | 75.25 | -9.0% |
| Worker scheduling | 4096 | 330.56 | 398.55 | -17.1% | 265.63 | 286.33 | -7.2% |
| Main-thread dispatch | 4096 | 62.58 | 91.47 | -31.6% | 58.02 | 68.03 | -14.7% |
| Thread-pool hop then switch | 128 | 30908.04 | 24774.77 | +24.8% | 15972.01 | 15997.38 | -0.2% |

Main-thread dispatch reversed direction in both cohorts and both runs: the
`3260c40` deltas were +24.7 and +36.1 percent at 128 and +27.3 and +28.4
percent at 4,096, and the `6c978a6` deltas are -36.1 and -9.0 percent at 128
and -31.6 and -14.7 percent at 4,096. Worker enqueue of 4,096 continuations
again favored Onity in both runs. The synchronous main-thread switch is 3 to
4.5 percent slower in this rerun after being 0.5 to 1.4 percent faster in the
previous one; both differences are about 5 to 10 ns and inside the run-to-run
noise of this host. The 128-continuation enqueue and the round trip changed
direction between runs and have no winner. Absolute times still varied
substantially across processes, so only within-process comparisons are used.

Allocation values remain unavailable: both switch runs and the primary report
failed the 64 KiB positive control again, and no new Profiler procedure was
attempted. The focused `OnityTaskThreadSwitch` EditMode (15 of 15) and
PlayMode (12 of 12) test filters passed on `6c978a6` through the Unity CLI on
the same Editor; that is not the complete repository suite. This rerun does
not establish overall superiority: it covers seven thread-switch slices on one
Editor build without allocation data, IL2CPP, or player results.

Allocation values are unavailable for these runs: the main-thread and
worker-thread `GC.GetAllocatedBytesForCurrentThread` counters reported zero for
the 64 KiB positive control, so every bytes/op value is `-1` and the empty
baseline's literal zero is not a zero-allocation measurement. No zero-allocation
claim follows from these runs; the design target of 0 B/op for the synchronous
main-thread path is unverified.

## Primary suite rerun

The 16-scenario primary harness was rerun on the same source and host on
2026-09-25. Mean ns/op; scheduling and result consumption remain separate
slices, and frame waits, resumption, and builder completion during resumption
are excluded. The separate Profiler allocation pass passed its 65,568 B and 0 B
controls but did not capture a synchronous sample in two attempts, so its
bytes/op values are also unavailable and the timing arrays were verified
unchanged by that pass.

| Scenario | N | Onity | UniTask |
| --- | ---: | ---: | ---: |
| Completed GetResult | 1 | 71.11 | 70.54 |
| FromResult<int> GetResult | 1 | 88.49 | 88.61 |
| NextFrame scheduling | 128 | 559.78 | 568.87 |
| NextFrame GetResult | 128 | 249.46 | 212.04 |
| NextFrame scheduling | 4096 | 473.76 | 459.89 |
| NextFrame GetResult | 4096 | 164.31 | 152.44 |
| Async method completed GetResult | 1 | 775.42 | 354.33 |
| Async method completed<int> GetResult | 1 | 702.54 | 379.77 |
| Async method NextFrame scheduling | 128 | 2734.70 | 1440.73 |
| Async method NextFrame GetResult | 128 | 75.70 | 153.91 |
| Async method NextFrame scheduling | 4096 | 2414.53 | 1337.64 |
| Async method NextFrame GetResult | 4096 | 63.04 | 138.53 |
| Async method NextFrame<int> scheduling | 128 | 2571.04 | 1350.38 |
| Async method NextFrame<int> GetResult | 128 | 79.28 | 144.61 |
| Async method NextFrame<int> scheduling | 4096 | 2107.73 | 1175.43 |
| Async method NextFrame<int> GetResult | 4096 | 66.71 | 116.52 |

Starting a native `async OnityTask` method that suspends once is roughly 1.8 to
1.9 times slower than the UniTask equivalent, and consuming a synchronously
completed async method is about 1.9 to 2.2 times slower (untyped 2.19x, typed
1.85x), while consuming the suspended method's result is faster. A fresh
sixteen-scenario report on `6c978a6`, same host and settings, repeated the
pattern: completed untyped `GetResult` 786.13 versus 335.93 ns/op (2.34x),
`NextFrame` scheduling 2511.64 versus 1345.87 at 128 and 2173.23 versus
1148.62 at 4,096 (1.87x and 1.89x), `NextFrame<int>` scheduling 2450.09 versus
1320.53 at 128 and 2218.59 versus 1231.76 at 4,096 (1.86x and 1.80x), and
`NextFrame<int>` `GetResult` 65.87 versus 127.93 at 4,096 in Onity's favor.
The drain rewrite did not touch this path, and it was not expected to. The
[async builder gap analysis](https://github.com/furkantokkan/Onity/blob/main/docs/Plan/11-OnityTask-AsyncBuilderGap.md)
traces this to the wrapped `AsyncTaskMethodBuilder` and records the decision
the product owner has to make before that gap can close. Raw run reports were
retained on the benchmark host and are not yet committed to this repository.

### Pooled runner verification at `4be50dc`

The pooled native builder was verified on 2026-09-25 in the same isolated host
(Unity 2022.3.62f2 Windows Editor/Mono, which the repository now pins).
The full EditMode and PlayMode suites passed under both the Editor's default
code optimization and `-releaseCodeOptimization`: 655/655 and 41/41 each
time, including the 38 EditMode and 1 PlayMode builder tests. Two independent
Release-optimization runs of the 24-scenario primary suite gave these paired
Onity/UniTask mean scheduling ratios; above 1 means Onity is slower.

| Async method awaiting `NextFrame` | N | Run 1 flow on | Run 1 flow off | Run 2 flow on | Run 2 flow off |
| --- | ---: | ---: | ---: | ---: | ---: |
| Untyped | 128 | 2.36x | 1.44x | 2.55x | 1.50x |
| Untyped | 4096 | 2.43x | 1.74x | 2.46x | 1.77x |
| Typed (`int`) | 128 | 2.52x | 1.47x | 2.25x | 1.44x |
| Typed (`int`) | 4096 | 2.51x | 1.68x | 2.35x | 1.78x |

With flow on (then the default; an opt-in since 0.6.0), the pooled runner was
slower than the previous .NET-builder implementation's 1.8x to 1.9x; with flow
off it improved on it but stayed above the 1.2x target. UniTask's own figures
also moved between the two blocks, so the raw change cannot be attributed wholly
to the setting. Allocation values were unavailable in both runs because the
heap-delta fallback tried to disable the collector, which the Editor rejects.
The owner then kept `FlowExecutionContext = true` as the default because turning
it off changes `AsyncLocal` semantics; 0.6.0 reversed that decision and made
flow off the default. These are scheduling and `GetResult` slices only, not
end-to-end await costs. In response the builders now bind the class library's
`FastCapture` and `RunInternal` pair through reflection, which removes the
per-suspension context allocation until a thread stores an `AsyncLocal` value
and keeps the resuming thread's synchronization context, and both runners
use a calibrated counter chain that never changes the collector mode in the
Editor. On desktop Mono 6.8, which compiles the same reference-source
`ExecutionContext`, the fast path bound and probed correctly and a
capture-and-run pair read 0 B/op without stored `AsyncLocal` values against
72 B/op on the public path.

A rerun at `f682b7c` on the same host verified both changes in Unity: 658/658
EditMode and 41/41 PlayMode in both code optimizations, the fast path bound,
and two Release runs of the primary suite.

| Async method awaiting `NextFrame`, Release, `f682b7c` | Flow on | Flow off |
| --- | ---: | ---: |
| Scheduling, Onity/UniTask across both runs and all four rows | 1.62x to 2.10x | 1.40x to 1.83x |
| Onity allocation at 128 concurrent operations | about 0.04 to 0.06 B/op | same |
| Onity allocation in the 4,096 burst | about 399 to 407 B/op | same |

Onity was slower in every one of the 24 scenarios. A desktop Mono 6.8
micro-benchmark of the builder wrapper alone (see the [builder gap
memo](https://github.com/furkantokkan/Onity/blob/main/docs/Plan/11-OnityTask-AsyncBuilderGap.md))
measured Onity at 164 to 224 ns per full cycle against 75 ns for UniTask
after the pool change, and put both wrappers an order of magnitude below what
the Editor attributes to them. The separate Player verification above supplies
Player scheduling evidence; these Editor ratios are not Player results. The
counter chain calibrated in both runs with every sample valid. The steady-state figure is
within the counter's noise of zero; the burst figure follows from the
128-runner and 256-source pool caps, above which each operation allocates a
runner and a frame source, and it is not a zero-allocation result. The
scheduling gap that remains sits in the pooled runner and source path
itself, not in the context flow.

## Thread-pool additions - 2026-09-27

Onity 0.4.0 adds `SwitchToThreadPool` and
`RunOnThreadPool(Action/Func<T>)`, with optional main-thread return. All four
Editor suites passed (682 EditMode + 46 PlayMode per optimization); Mono and
IL2CPP Release Players each passed 16 smoke cases. Two processes per backend
measured submission and worker cohort completion separately.

Matched flow-off submission ratios (Onity/UniTask) were 1.16–1.23 for Mono
Action/Func workloads and 0.98–1.14 for IL2CPP. IL2CPP cohort completion favored
Onity in these runs, while Mono generally favored UniTask. This is not an
overall async speed result. Allocation values are unavailable because the
per-thread counters failed calibration; process-wide counters cannot isolate
concurrent worker allocation. Flow on was then the default, a different context
contract from UniTask; flow off is the default since 0.6.0. See the
[full report and raw samples](../assets/benchmarks/onitytask-stage1-threadpool-2026-09-27.md).

## Jobs and Burst additions - 2026-09-27

The 0.4.0 `JobHandle.AsOnityTask()` adapter completes accepted jobs before
publishing results and preserves caller ownership of native containers. Normal
and Release suites each passed 690 EditMode and 50 PlayMode cases; both Release
Players passed 16 smoke cases and two independent jobs runs.

At 65,536 elements, the same kernel ran 14.08–14.46 times faster with Jobs/Burst
than serial C#, and 1.21–1.35 times faster than plain parallel Jobs. This is
compute evidence. The separate adapter registration measurements favored
UniTask; completion wall time was similar at different Update positions.
Allocation was unavailable and an IL2CPP timing outlier is retained. See the
[full report and raw samples](../assets/benchmarks/onitytask-stage2-jobs-2026-09-27.md).

## Array `WhenAny` in 0.4.0

Onity 0.4.0 passes 744 EditMode and 52 PlayMode cases in both
optimization modes, plus 18 smoke cases on each Release Player backend.
Two runs per backend compare typed/untyped arrays at 2, 16 and 32 inputs.
Measured Onity/UniTask mean time is 1.008–1.664x on Mono and 1.416–2.142x on
IL2CPP. The near-parity Mono case is not evidence of a material difference.

At 2/16 inputs Onity reported no measurable heap growth; at 32 its unpooled
path measured about 6.4 kB/group on Mono and 7.5 kB/group on IL2CPP, exceeding
UniTask. Coarse process-wide HeapDelta zeros do not prove zero allocation.
Another Editor used CPU during these measurements. The slice includes producer
completion and loser observation; it does not isolate scheduler CPU. See the
[method, limits and raw evidence](../assets/benchmarks/onitytask-stage3b-whenany-2026-09-27.md).

## Feature coverage in 0.4.0 (superseded)

The table below is the 0.4.0 coverage record; the current coverage is in
[Feature coverage](#feature-coverage) above.

| Capability | OnityTask status |
| --- | --- |
| Frame, fixed-frame, and late-frame waits; scaled and unscaled delays; predicate waits | Available with cancellation and single-consumer pooled sources. |
| Scene, `AsyncOperation`, and web-request bridges | Available. Deferred scene loads require the caller to activate a started operation, even after cancellation. |
| `async OnityTask` and `async OnityTask<T>` | Synchronous success stores the result inline; synchronous faults and cancellations are Task-backed. A suspended method uses a pooled native runner holding the state machine by value and a cached continuation delegate; its task is single-consumer. Execution context flowed by default until 0.6.0 through the internal capture/run pair, with a public fallback. Both Mono and IL2CPP passed the 13-case Player smoke suite at `528d52c` with the internal pair active. |
| `WhenAll` | Available, including typed ordered results. Already successful two-input untyped and eligible typed calls avoid Task bridges. Pending two-input untyped calls whose inputs are completion sources without a bridge or unclaimed single-consumer native sources use a pooled coordinator and Task-backed output. Onity 0.4.0 also removes input bridges for 1–16 unique exact built-in typed completion sources without preexisting bridges, mixed with inline/default values. Other pending typed inputs retain the prior fallback. [Construction measurements](../assets/benchmarks/onitytask-stage3a-whenall-2026-09-27.md) show lower heap growth and an IL2CPP construction regression; they do not establish end-to-end speed. |
| Native `WhenAny` | Pair overloads return winner index or same-type index/value and retain their previous allocation behavior. Onity 0.4.0 adds nonempty arbitrary arrays, snapshots before registration, rejects duplicate single-consumer identities before claiming inputs, and observes every loser without canceling it. Array outputs are native single-consumer tasks with bounded pooling for up to 16 inputs; larger/error paths allocate. Pending callbacks race by observation. Completion-source subclasses and later bridges retain fault observation. Array performance is measured separately from the historical pair results above. |
| Public completion source | Typed and untyped callback completion with retained tasks for multiple consumers; the Editor/Mono comparison above has mixed results. |
| External cancellation and suppression | Onity 0.4.0 adds typed/untyped `AttachExternalCancellation` and `SuppressCancellationThrow`. Pending producers remain observed, winning tokens are preserved and faulted OCE remains faulted. Pending native wrappers allocate and are unpooled; speed/allocation quantities are unmeasured. Both optimization modes pass 777 EditMode/54 PlayMode cases and each Release Player passes 20 smoke cases. [Verification and limits](../assets/benchmarks/onitytask-stage3c-cancellation-2026-09-27.md). |
| Native task sharing | `Preserve()` retains typed or untyped pooled completion for multiple pending and late consumers. Pending typed conversion measured 120 B/task in Editor/Mono at `c2f9358`; see the follow-up above. |
| Timeouts | Onity 0.4.0 adds typed/untyped `Timeout` and `TimeoutWithoutException`, with validated seconds, completed-input precedence, scaled/unscaled private timers and late producer observation. Producer faults and cancellation remain distinct from timeout. Both optimizations pass 812 EditMode/82 PlayMode tests, plus 26 smoke cases per Release Player. Wrappers/timer entries allocate; allocation quantities and speed are unmeasured. [Verification and limits](../assets/benchmarks/onitytask-stage3e-timeout-2026-09-27.md). |
| Main-thread switch | `OnityTask.SwitchToMainThread` completes synchronously on the main thread and queues worker-thread continuations for the Update phase. Cancellation is observed at `GetResult` on the destination thread; Edit Mode dispatch and Play Mode session isolation are defined in the [contract](https://github.com/furkantokkan/Onity/blob/main/docs/Plan/10-OnityTask-MainThreadSwitch.md). Timing selection remains limited. In the Editor/Mono comparison above the synchronous switch is within noise of UniTask, large worker enqueues and, after the array-backed drain, callback dispatch favored Onity in both runs, and the 128-continuation enqueue and round trip have no winner. Allocations are unmeasured. |
| Thread-pool switch and background work | Available in Onity 0.4.0, verified on Windows Mono/IL2CPP. Switch always queues; synchronous Action/Func overloads support cancellation and optional session-aware main-thread return. WebGL Players reject explicitly. Async-delegate/state-argument overloads remain follow-up work. |
| Jobs/Burst bridge | `JobHandle.AsOnityTask()` accepts handles on the main thread, completes them before publication, and settles accepted work during teardown. No cancellation overload or container ownership transfer. Verified on Mono/IL2CPP with actual Burst compute; this does not compile the managed async scheduler with Burst. |
| Selectable PlayerLoop phases and immediate cancellation | Onity 0.4.0 adds Update/FixedUpdate/LateUpdate after script callbacks, with Yield occurrence and strict rendered-frame NextFrame/DelayFrames semantics. Main-thread cancellation drains during Update even when fixed time is paused; immediate cancellation remains unsupported. Current-loop repair, ECS coexistence and session teardown pass 780 EditMode/64 PlayMode cases in both optimizations, plus 22 smoke cases per Release Player. Repeated warmed no-token Update Yield brackets show no measured heap growth under calibrated HeapDelta, not exact zero GC or general speed superiority. [Verification and limits](../assets/benchmarks/onitytask-stage3d-playerloop-2026-09-27.md). |
| `await foreach` async enumerable | Onity 0.4.0 adds native covariant interfaces, Empty/Return/Range, synchronous Select/Where/Take, WithCancellation and First/ToArray consumers. The finite-stream checkpoint passed 851 EditMode/86 PlayMode cases in both optimizations and 29 smoke cases per Release Player. Primed finite iteration read zero calibrated HeapDelta in 16 windows per scenario/backend, without proving exact zero allocation or speed superiority. [Verification and limits](../assets/benchmarks/onitytask-stage4a-streams-2026-09-27.md). |
| Sequential awaitable operators | Onity 0.4.0 adds cancellation-aware SelectAwait, WhereAwait and ForEachAsync with exact observation and shared cleanup. Full suites pass 947 EditMode/94 PlayMode in both optimizations; both Release Players pass 35 smoke cases. Pending state allocates; allocation quantity and comparative speed are unmeasured. [Verification and limits](../assets/benchmarks/onitytask-stage4c-await-operators-2026-09-27.md). |
| Update streams and BCL adapters | Onity 0.4.0 adds on-demand EveryUpdate and both native/BCL async-enumerable adapters, including compiler-generated async iterators and reusable ValueTask sources. Both optimizations pass 879 EditMode/90 PlayMode cases and each Release Player passes 31 smoke cases. Pending adapters allocate; speed and allocation quantities are unmeasured. [Verification and limits](../assets/benchmarks/onitytask-stage4b-adapters-2026-09-27.md). |
| Channels | Onity 0.4.0 adds bounded/unbounded FIFO channels with multiple producers, a single consumer lease, waiting writes, cancellation and ReadAll cleanup. Full suites pass 913 EditMode/92 PlayMode in both optimizations, with a strengthened 34-case fixture also passing both; each Release Player passes 33 smoke cases. Warmed buffered Try operations show zero HeapDelta in 16 windows per capacity/backend. Exact zero allocation, pending-operation cost and comparative speed are unproven. [Verification and limits](../assets/benchmarks/onitytask-stage4c-channels-2026-09-27.md). |
| Real rendering end of frame | Onity 0.4.0 uses one shared Unity coroutine, with Update cancellation during stalls and isolated host/session retirement. Both optimizations pass 814 EditMode/84 PlayMode tests; Mono and IL2CPP each pass six graphics groups with pixel/frame proof and 27 paired headless cases. Actual Scene-view interaction, allocation quantities and speed are unmeasured. [Verification and limits](../assets/benchmarks/onitytask-stage3f-endofframe-2026-09-27.md). |

At 0.4.0 OnityTask covered the common Unity flows but was not a full UniTask
replacement; 0.6.0 closed most of those gaps (see [Feature coverage](#feature-coverage)).
