# OnityTask comparison harness

## Setup

- Use a dedicated Unity `2022.3.62f3` benchmark host with the current Onity package.
- Install official UniTask `2.5.11`, tag commit
  `2e993ff18f28c931602a07292df0b0804eebef99`, using this pinned UPM URL:
  `https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#2e993ff18f28c931602a07292df0b0804eebef99`.
- This dedicated comparison host compiles both benchmark assemblies directly.
  Leave `ONITY_BENCHMARKS` disabled unless the separate DI comparison dependencies
  are installed.
- New runtime/editor assemblies are `Onity.TaskBenchmarks` and
  `Onity.TaskBenchmarks.Editor`. No VContainer or Zenject assembly is referenced.
- Keep a clean benchmark scene and close profiling/diagnostic windows that add
  unrelated work. Record package lock revisions with published results.

Run `Onity/Benchmarks/Run OnityTask Benchmarks (Play Mode)`. The existing CLI
entry point remains
`Onity.Editor.Benchmarks.OnityTaskBenchmarkMenu.RunFromCommandLine` and accepts
`-onityTaskBenchmarkOutput <absolute-json-path>`. Do not pass `-quit`; the menu
controller exits after writing the report or hitting its 15-minute timeout.
Verify the exact Editor version and host with the Unity CLI first. The installed
`unity run` 1.0.0-beta.3 adds `-quit` automatically, which closes the Editor
before a Play Mode benchmark can start. Invoke the pinned Unity 2022 Editor
directly for this entry point, without `-quit`:

```text
Unity.exe -batchmode -nographics -projectPath <benchmark-host> -executeMethod Onity.Editor.Benchmarks.OnityTaskBenchmarkMenu.RunFromCommandLine -onityTaskBenchmarkOutput <absolute-json-path> -logFile <absolute-log-path>
```

## Measurement contract

- Two synchronous primitives: completed `GetResult`, and `FromResult<int>` plus
  `GetResult`. Each uses 4,096 warmup calls and eight samples of 1,000,000 calls.
- Two synchronous async-method cases call actual `async OnityTask`/`async UniTask`
  methods and consume their result. Each method awaits its library's completed
  task without suspending; the typed method returns `42`. These include the method
  builder path, unlike the direct primitives. They use the same warmup, sample
  count and synchronous iteration count as the primitive cases.
- The empty delegate loop is measured separately using the same invocation
  pattern. Raw times retain harness overhead; the baseline is not subtracted.
- Frame operations have two distinct cohorts: 128 concurrent operations below
  Onity's 256-source retention cap, and repeated bursts of 4,096 operations above
  that cap. A burst result must not be described as pooled steady state.
- Each library completes and consumes two warmup batches at the exact cohort
  size. Each measured sample sums 32 batches; there are eight samples per cohort.
  Library order alternates per sample and batch. All awaiter arrays are allocated
  before measurement, and all operations must complete before consumption.
- `NextFrame scheduling` times creation and storage of native awaiters.
  `NextFrame GetResult` times result consumption and clearing awaiter references.
  Scheduling and consumption measure synchronous main-thread slices separately.
  Frame waiting, runner ticking, continuation dispatch, async builders and the
  complete async lifecycle are **outside** these timings.
- `Async method NextFrame` and `Async method NextFrame<int>` each await exactly
  one native `NextFrame`; the typed method then returns `42`. Both use the same
  128/4,096 concurrency cohorts, two completed warmup batches, eight samples and
  32 batches/sample. `scheduling` includes calling the async method, its builder's
  initial suspension/continuation registration, and storing its returned awaiter.
  `GetResult` includes consuming the completed outer method result and clearing
  the awaiter; each typed result is checked against `42`, and the sum is stored
  in the same observable static sink for both libraries. Every outer operation
  is checked for completion before consumption, with the same 240-frame timeout
  as the primitive cases.
- Async-method frame timings and allocation deltas cover **only** the scheduling
  and consumption slices. Suspended-frame time, PlayerLoop work, resuming the
  method, and builder completion during that resumption remain outside them.
  These measurements are not full-lifecycle async-method costs. Builder pooling
  can differ from the primitive frame-source pool and is not assumed identical.
- Allocation deltas surround those same synchronous slices. Coroutine suspension,
  report construction, sample-array allocation and explicit full collections are
  outside them. Collections occur once before each measured sample, not inside a
  measured slice. Burst source allocations remain included.
- Editor allocation measurement uses a separate process started with
  `-profiler-enable -onityTaskAllocationsOnly`. Unity Profiler `GC.Alloc` sample
  metadata must detect a known 64 KiB allocation and report zero for an empty
  marker before bytes/op is enabled. Failed calibration leaves bytes/op at `-1`.
  IL2CPP player allocation claims require separate evidence.
- Timer frequency/resolution, allocation controls, all timing/allocation samples,
  mean, median, range and standard deviation are retained in JSON. CSV and Markdown
  summarize the measurements. The report schema is version 3; the original
  six primitive scenarios retain their names, indices and metric fields. Two
  synchronous async-method cases and eight frame-method cases are appended, for
  16 scenarios total. Consumers should identify cases by name and concurrency.

The expanded suite requires at least 3,096 rendered frames for its frame cases
(six workload/cohort pairs, each with four warmup and 512 measured frame waits).
The command-line runner has a 15-minute timeout. Run the benchmark in a dedicated,
responsive host because a heavily throttled Editor may still hit that limit.

Results compare these specific primitives, async-method slices and workloads.
An Editor result is not an IL2CPP player result. Timing noise and pool retention
differences must be considered before making a comparison claim. The separate
completion-source baseline and attribution artifacts are linked below.

## Change note

- Split comparison assemblies and preserved the moved scripts' `.meta` GUIDs.
- Replaced the unlabelled 10,000-operation burst with matched, explicitly labelled
  steady-state and burst cohorts; added calibrated allocation measurements.
- Added completion checks, bounded frame waits, alternating execution order,
  raw samples and failure propagation from frame measurements.
- Added matched typed/untyped async-method cases for synchronous completion and
  one-frame suspension; existing primitive cases are retained.
- Unity compilation and execution must be performed in the dedicated host after
  installing the pinned comparison package. Static review is not runtime proof.

## Completion-source comparison host

The isolated `benchmark/completion-source` worktree pins UniTask `2.5.11` at
`2e993ff18f28c931602a07292df0b0804eebef99`. The benchmarked Onity runtime
is commit `a7c7c9c07867a0f2a20a0b817ca0482e75b77795`. This host enables the
two comparison asmdefs directly; those host-only package and asmdef edits are
not part of the shipped Onity package.

Use `Onity/Benchmarks/Run Completion Source Benchmarks (Play Mode)` or the pinned
Unity 2022.3.62f3 Editor with an absolute output path. Do not pass `-quit`:

```text
Unity.exe -batchmode -nographics -projectPath <host> -executeMethod Onity.Editor.Benchmarks.OnityTaskBenchmarkMenu.RunFromCommandLine -onityCompletionSourceBenchmark -onityTaskBenchmarkOutput <report.json> -logFile <timing.log>
Unity.exe -batchmode -nographics -profiler-enable -projectPath <host> -executeMethod Onity.Editor.Benchmarks.OnityTaskBenchmarkMenu.RunFromCommandLine -onityCompletionSourceBenchmark -onityTaskAllocationsOnly -onityTaskBenchmarkOutput <report.json> -logFile <allocations.log>
```

The suite compares fresh typed and untyped `OnityTaskCompletionSource` and
`UniTaskCompletionSource` instances. It measures construction, pending
registration, completion with callback dispatch, late registration, and pending
`AsTask` conversion. Registration, completion, and late registration each use
one and four consumers; construction and `AsTask` use one. Each timing sample
aggregates 16 batches of 256 sources (4,096 operations), while each allocation
sample uses one 256-source batch. There are two warmup batches and eight samples
per pass, with library order alternated. The empty timing path is also warmed
before measurement. `AsTask` measures conversion of a pending source; source
completion, bridge completion and result consumption occur after the measured
slice. A static callback and post-sample completion/result checks verify
the expected callback count and result. Preparation, cleanup, and full GC run
outside each measured slice. The empty-loop baseline is reported without
subtraction. Timing and allocation are separate passes; allocation requires
the calibrated 64 KiB/empty controls. The JSON retains all samples and the
completion-source report schema is version 1. Results are Editor/Mono
synchronous-slice evidence, not end-to-end or IL2CPP player results.

For a separate, short allocation attribution probe, pass
`-profiler-enable -onityCompletionSourceBenchmark -onityCompletionSourceAttribution`
with a new output JSON path. The probe captures typed construction and pending
`AsTask` conversion once per library after two warmup batches. It requires the
64 KiB/empty controls and full GC.Alloc callstack coverage before setting
`available` to true. Its single samples diagnose allocation sites; the eight
sample comparison report above remains the measurement source.

The measured baseline is saved as [JSON](../../../../docs/assets/benchmarks/onity-completion-source-a7c7c9c-warmed-2026-09-23.json),
[CSV](../../../../docs/assets/benchmarks/onity-completion-source-a7c7c9c-warmed-2026-09-23.csv),
and [Markdown](../../../../docs/assets/benchmarks/onity-completion-source-a7c7c9c-warmed-2026-09-23.md).
The separate [callstack attribution JSON](../../../../docs/assets/benchmarks/onity-completion-source-a7c7c9c-attribution-2026-09-23.json)
contains one diagnostic sample for typed construction and pending `AsTask` per
library. The baseline supports stage-specific comparisons; it does not establish
overall superiority for either implementation.

## Native Preserve comparison

The isolated Preserve host runs Onity runtime commit
`83308ebb20cfbc4100d43b9c18521356292158c9` with pinned UniTask commit
`2e993ff18f28c931602a07292df0b0804eebef99` on Unity `2022.3.62f3`.
Each pending typed task is a `WhenAny` over two unresolved completion sources.
The runner checks that the Onity task contains a native `OnityWhenAnyTaskSource`.
The first input completes after measurement, and the second input also completes
before the batch ends. This keeps source construction and completion outside the
timed or profiled conversion slice.

- Sequential reuse: `OnityTask<int>.Preserve()` versus
  `UniTask<int>.Preserve()`, with one pending callback and two later result reads.
- Four pending consumers: `OnityTask<int>.Preserve()` versus
  `UniTask<int>.AsTask()`, with four callbacks registered while the task is
  pending. The UniTask side returns a .NET `Task<int>`; this API difference is
  part of the comparison and must remain visible in reports.
- Four scenarios measure pending conversion or two late `GetResult` reads for
  each consumer count. Callback registration, completion, first consumption,
  and loser completion are outside the measured slices. The late-read metric
  is per shared task and includes both reads.
- Each timing sample contains 16 batches of 256 tasks per library. Eight raw
  timing and allocation samples are kept, with two warmup batches. Library
  order alternates per batch. Allocation samples measure one 256-task batch.
  The empty-loop timing is reported without subtraction.
- The allocation pass uses Unity Profiler `GC.Alloc` metadata. A 64 KiB
  positive control and zero-allocation empty control must both pass before
  bytes per operation are reported. Unavailable allocations are marked `-1`.

Run timing and allocations in separate pinned Editor processes. Do not add
`-quit`, because the menu exits after the report is saved:

```text
Unity.exe -batchmode -nographics -projectPath <host> -executeMethod Onity.Editor.Benchmarks.OnityTaskBenchmarkMenu.RunFromCommandLine -onityPreserveBenchmark -onityTaskBenchmarkOutput <absolute-report.json> -logFile <absolute-timing.log>
Unity.exe -batchmode -nographics -profiler-enable -projectPath <host> -executeMethod Onity.Editor.Benchmarks.OnityTaskBenchmarkMenu.RunFromCommandLine -onityPreserveBenchmark -onityTaskAllocationsOnly -onityTaskBenchmarkOutput <same-absolute-report.json> -logFile <absolute-allocations.log>
Unity.exe -batchmode -nographics -profiler-enable -projectPath <host> -executeMethod Onity.Editor.Benchmarks.OnityTaskBenchmarkMenu.RunFromCommandLine -onityPreserveBenchmark -onityPreserveAttribution -onityTaskBenchmarkOutput <absolute-attribution.json> -logFile <absolute-attribution.log>
```

The allocation pass rejects a timing JSON whose runtime revision, UniTask
revision, Unity version, benchmark settings, or scenario definitions differ
from the current runner.

The attribution command profiles only pending native conversion. It saves
`GC.Alloc` callstacks for one and four consumer cases, plus a 64 KiB positive
control and a zero-allocation empty control. Callstack bytes must sum to each
case's total sample bytes before `available` becomes true.

The [native Preserve allocation callstacks](../../../../docs/assets/benchmarks/onity-preserve-native-0f17822-allocation-callstacks-2026-09-23.json)
and [provenance](../../../../docs/assets/benchmarks/onity-preserve-native-0f17822-allocation-callstacks-2026-09-23.provenance.json)
record two byte-identical Unity Editor captures of the `0f17822` candidate.
Each Onity conversion allocated 264 B: 120 B at the preserved-source creation
site, 128 B in its constructor when registering the bound `Complete` action,
and 16 B in the base completion source for its gate object. These site-to-object
interpretations follow the pinned source code; the measured callstack bytes are
in the JSON. UniTask `Preserve` allocated 40 B and `AsTask` allocated 104 B.
Source construction, callback dispatch, completion, and result consumption
remain outside the profiled conversion slice.

The report is Unity Editor/Mono evidence for these synchronous slices. It does
not measure complete frame latency, player IL2CPP, or all UniTask APIs.

The first native-source result is preserved as [JSON](../../../../docs/assets/benchmarks/onity-preserve-native-83308eb-2026-09-23.json),
[CSV](../../../../docs/assets/benchmarks/onity-preserve-native-83308eb-2026-09-23.csv),
[Markdown](../../../../docs/assets/benchmarks/onity-preserve-native-83308eb-2026-09-23.md),
and [provenance](../../../../docs/assets/benchmarks/onity-preserve-native-83308eb-2026-09-23.provenance.json).
The allocation controls passed (65,568 B positive; 0 B empty). Pending conversion
allocated 264 B/task for Onity, 40 B/task for UniTask `Preserve`, and 104 B/task
for UniTask `AsTask`. All later result reads allocated 0 B/task. The measured
Onity conversion and late-read timing means were slower in this run; see the raw
samples rather than treating any slice as an overall library ranking.

The terminal `GetResult` fast-path candidate from commit `0f17822` was measured
with the same operations, pinned UniTask version, and Unity Editor. Its [JSON](../../../../docs/assets/benchmarks/onity-preserve-native-0f17822-2026-09-23.json),
[CSV](../../../../docs/assets/benchmarks/onity-preserve-native-0f17822-2026-09-23.csv),
[Markdown](../../../../docs/assets/benchmarks/onity-preserve-native-0f17822-2026-09-23.md),
and [provenance](../../../../docs/assets/benchmarks/onity-preserve-native-0f17822-2026-09-23.provenance.json)
retain the raw samples. Allocation results were unchanged: Onity `Preserve`
264 B/task, UniTask `Preserve` 40 B/task, UniTask `AsTask` 104 B/task, and
late reads 0 B/task. Timing fell substantially for both libraries between
processes, so the separate-run means do not isolate the fast-path effect.

The retained-source gate candidate from commit `956e5cb` uses its own object
as the internal lock. Its [JSON](../../../../docs/assets/benchmarks/onity-preserve-native-956e5cb-2026-09-23.json),
[CSV](../../../../docs/assets/benchmarks/onity-preserve-native-956e5cb-2026-09-23.csv),
[Markdown](../../../../docs/assets/benchmarks/onity-preserve-native-956e5cb-2026-09-23.md),
and [provenance](../../../../docs/assets/benchmarks/onity-preserve-native-956e5cb-2026-09-23.provenance.json)
retain eight samples per case. The 64 KiB/empty allocation controls passed
(65,568 B/0 B). Pending conversion allocated 248 B/task for Onity, down from
264 B/task in both earlier reports; UniTask allocated 40 B for `Preserve` and
104 B for four-consumer `AsTask`. Late reads allocated 0 B/task on both sides.
The separate [callstack capture](../../../../docs/assets/benchmarks/onity-preserve-native-956e5cb-allocation-callstacks-2026-09-23.json)
and [provenance](../../../../docs/assets/benchmarks/onity-preserve-native-956e5cb-allocation-callstacks-2026-09-23.provenance.json)
fully attribute Onity's 248 B/task to the retained-source and callback sites;
the earlier 16 B gate allocation site is absent. The object labels are inferred
from the pinned source; the byte counts and stack sites are measured.

In this candidate process, one-consumer conversion averaged 363 ns/task for
Onity and 94 ns/task for UniTask `Preserve`; two late reads averaged 139 ns and
91 ns. Four-consumer conversion averaged 382 ns/task for Onity and 388 ns/task
for UniTask `AsTask`; two late reads averaged 144 ns and 82 ns. These are
same-process comparisons of the named slices. Cross-process timing changes
cannot be assigned to the 16 B gate removal. The result does not demonstrate
overall OnityTask superiority, a complete lifecycle advantage, or Player IL2CPP
behavior.

## Native sharing lifecycle comparison

The separate `-onityNativeLifecycleBenchmark` mode measures three complete
Editor/Mono operations on fresh pending typed `WhenAny` sources, using the same
Unity `2022.3.62f3` host and pinned UniTask commit as the narrow Preserve mode:

- One pending observer: Onity `Preserve` versus UniTask `Preserve`.
- Four pending observers: Onity `Preserve` versus UniTask `AsTask`.
- One ordinary native observer without sharing conversion or repeated reads.

The timed and allocation-measured operation includes both input sources and
native `WhenAny` construction, conversion where applicable, pending callback
registration, winner completion and callback dispatch, callback wait, one result
read per observer, two additional late reads for sharing, and loser completion.
Preallocated benchmark arrays, the one-time native source type check, and array
cleanup are outside the operation. UniTask 2.5.11 uses a `params` array for its
two-input `WhenAny`, while Onity has a two-argument overload. The natural API
calls therefore include an extra array allocation on the UniTask side, inside
the measured operation. The
four-observer UniTask path also uses .NET `Task<int>` with
`ConfigureAwait(false)` callbacks; callback scheduling and wait time are inside
the timed operation. Profiler `GC.Alloc` measures the marked main-thread work;
`AsTask` callbacks can run on another thread, so their allocations may be
outside the marker. The four-observer timing includes the wait for those
callbacks, and is not a general library speed ratio. These API and dispatch
differences limit direct attribution to sharing implementation alone.

Each case retains eight raw timing samples of 4,096 operations and eight
allocation samples of 256 operations. The allocation pass uses the same
Profiler `GC.Alloc` 64 KiB/empty calibration controls as the narrow mode. Run
the timing and allocation passes separately without `-quit`:

```text
Unity.exe -batchmode -nographics -projectPath <host> -executeMethod Onity.Editor.Benchmarks.OnityTaskBenchmarkMenu.RunFromCommandLine -onityNativeLifecycleBenchmark -onityTaskBenchmarkOutput <absolute-report.json> -logFile <absolute-timing.log>
Unity.exe -batchmode -nographics -profiler-enable -projectPath <host> -executeMethod Onity.Editor.Benchmarks.OnityTaskBenchmarkMenu.RunFromCommandLine -onityNativeLifecycleBenchmark -onityTaskAllocationsOnly -onityTaskBenchmarkOutput <same-absolute-report.json> -logFile <absolute-allocations.log>
```

The [baseline JSON](../../../../docs/assets/benchmarks/onity-native-lifecycle-956e5cb-2026-09-23.json),
[CSV](../../../../docs/assets/benchmarks/onity-native-lifecycle-956e5cb-2026-09-23.csv),
[Markdown](../../../../docs/assets/benchmarks/onity-native-lifecycle-956e5cb-2026-09-23.md),
and [provenance](../../../../docs/assets/benchmarks/onity-native-lifecycle-956e5cb-2026-09-23.provenance.json)
record the `956e5cb` runtime with 65,568 B/0 B allocation controls. Onity's
ordinary native path measured 656 B/task versus 304 B/task for UniTask, and
its one-observer sharing path measured 904 B/task versus 344 B/task for UniTask
`Preserve`. Four-observer sharing measured 1,008 B/task for Onity and about
665 B/task for UniTask `AsTask`. The complete raw samples and timing means are
in the reports; none establishes universal library superiority.

The experimental stateful callback candidate at `6a15305` has separate
[lifecycle JSON](../../../../docs/assets/benchmarks/onity-native-lifecycle-6a15305-2026-09-23.json),
[CSV](../../../../docs/assets/benchmarks/onity-native-lifecycle-6a15305-2026-09-23.csv),
[Markdown](../../../../docs/assets/benchmarks/onity-native-lifecycle-6a15305-2026-09-23.md),
and [provenance](../../../../docs/assets/benchmarks/onity-native-lifecycle-6a15305-2026-09-23.provenance.json).
The 64 KiB/empty allocation controls passed (65,568 B/0 B). Compared with the
`956e5cb` baseline using the same operations, Onity one-observer sharing fell
from 904 to 792 B/task and four-observer sharing fell from 1,008 to 896 B/task.
Ordinary native awaiting rose from 656 to 672 B/task. These Profiler values
cover the main-thread marker; the four-observer UniTask AsTask path may do
unmeasured work on another thread. Timing samples are from separate Editor
processes and do not isolate the candidate's effect.

The candidate's narrow conversion [JSON](../../../../docs/assets/benchmarks/onity-preserve-native-6a15305-2026-09-23.json),
[CSV](../../../../docs/assets/benchmarks/onity-preserve-native-6a15305-2026-09-23.csv),
[Markdown](../../../../docs/assets/benchmarks/onity-preserve-native-6a15305-2026-09-23.md),
and [provenance](../../../../docs/assets/benchmarks/onity-preserve-native-6a15305-2026-09-23.provenance.json)
show Onity pending Preserve conversion falling from 248 to 120 B/task;
UniTask Preserve and AsTask remained 40 and 104 B/task. All two-late-read
allocation samples were zero. A separate calibrated [callstack report](../../../../docs/assets/benchmarks/onity-preserve-native-6a15305-allocation-callstacks-2026-09-23.json)
and [provenance](../../../../docs/assets/benchmarks/onity-preserve-native-6a15305-allocation-callstacks-2026-09-23.provenance.json)
attribute all 120 B/task of Onity conversion to one allocation site. The
bound callback allocation site recorded for `956e5cb` is absent. The 16 B/task
ordinary native-await regression remains a release concern for this candidate.

The union-state follow-up at `c2f9358` reuses the native source's existing
reference field for a Preserve continuation or an `AsTask` bridge. Its
[lifecycle JSON](../../../../docs/assets/benchmarks/onity-native-lifecycle-c2f9358-2026-09-23.json),
[CSV](../../../../docs/assets/benchmarks/onity-native-lifecycle-c2f9358-2026-09-23.csv),
[Markdown](../../../../docs/assets/benchmarks/onity-native-lifecycle-c2f9358-2026-09-23.md),
and [provenance](../../../../docs/assets/benchmarks/onity-native-lifecycle-c2f9358-2026-09-23.provenance.json)
retain the same eight timing and allocation samples per case. The allocation
controls passed (65,568 B/0 B). Ordinary native awaiting returned to 656
B/task, matching `956e5cb` and improving on `6a15305` by 16 B/task. One- and
four-observer sharing fell from 792 to 776 B/task and 896 to 880 B/task,
respectively, compared with `6a15305`. These bytes cover main-thread Profiler
work; four-observer UniTask `AsTask` callbacks may allocate on another thread.
The timing samples come from separate Editor processes and do not establish
a causal speed change.

The matching narrow [conversion JSON](../../../../docs/assets/benchmarks/onity-preserve-native-c2f9358-2026-09-23.json),
[CSV](../../../../docs/assets/benchmarks/onity-preserve-native-c2f9358-2026-09-23.csv),
[Markdown](../../../../docs/assets/benchmarks/onity-preserve-native-c2f9358-2026-09-23.md),
and [provenance](../../../../docs/assets/benchmarks/onity-preserve-native-c2f9358-2026-09-23.provenance.json)
show 120 B/task for Onity `Preserve`, 40 B/task for UniTask `Preserve`, and
104 B/task for UniTask `AsTask`; all late-read allocation samples are zero.
The calibrated [callstack report](../../../../docs/assets/benchmarks/onity-preserve-native-c2f9358-allocation-callstacks-2026-09-23.json)
and [provenance](../../../../docs/assets/benchmarks/onity-preserve-native-c2f9358-allocation-callstacks-2026-09-23.provenance.json)
attribute Onity's full 30,720 B conversion sample to one stack (120 B/task).

## Completion-source status probe

The `-onityCompletionSourceBenchmark -onityCompletionSourceStatusProbe` mode
compares pending and completed-success `GetAwaiter().IsCompleted` reads on
fresh typed and untyped completion sources. Construction and completion are
outside each measured slice. The benchmark alternates library order and keeps
eight timing samples of 4,096 reads and eight allocation samples of 256 reads
per case. Run the timing pass, then the allocation pass against the same JSON
path with `-onityTaskAllocationsOnly -profiler-enable`; the latter rejects a
report from a different runtime revision or benchmark configuration before
modifying it. The allocation controls measured 65,568 B positive and 0 B
empty in both runs. Every status-read case measured 0 B per operation.

The fresh PR #14 `aa4c5a5` [baseline JSON](../../../../docs/assets/benchmarks/onity-completion-source-status-pr14-aa4c5a5-2026-09-23.json),
[CSV](../../../../docs/assets/benchmarks/onity-completion-source-status-pr14-aa4c5a5-2026-09-23.csv),
[Markdown](../../../../docs/assets/benchmarks/onity-completion-source-status-pr14-aa4c5a5-2026-09-23.md),
and [provenance](../../../../docs/assets/benchmarks/onity-completion-source-status-pr14-aa4c5a5-2026-09-23.provenance.json)
are kept separate from the `25c8e20` candidate [JSON](../../../../docs/assets/benchmarks/onity-completion-source-status-25c8e20-2026-09-23.json),
[CSV](../../../../docs/assets/benchmarks/onity-completion-source-status-25c8e20-2026-09-23.csv),
[Markdown](../../../../docs/assets/benchmarks/onity-completion-source-status-25c8e20-2026-09-23.md),
and [provenance](../../../../docs/assets/benchmarks/onity-completion-source-status-25c8e20-2026-09-23.provenance.json).
Both use Unity 2022.3.62f3 Editor/Mono and pinned UniTask 2.5.11.

| Status read | Baseline Onity / UniTask mean ns | Candidate Onity / UniTask mean ns | Median paired Onity / UniTask ratio, baseline to candidate |
| --- | ---: | ---: | ---: |
| Pending, untyped | 127.9 / 90.7 | 90.5 / 77.5 | 1.430 to 1.170 |
| Pending, typed | 120.1 / 82.4 | 93.7 / 83.6 | 1.433 to 1.108 |
| Completed, untyped | 85.7 / 78.3 | 96.1 / 78.8 | 1.082 to 1.151 |
| Completed, typed | 98.2 / 79.7 | 83.3 / 78.0 | 1.141 to 1.086 |

The candidate narrows the pending same-run gap but remains slower than UniTask
in these samples. Terminal sample ratios vary. Baseline and candidate timings
come from separate Editor processes and cannot alone establish a causal speed
change; these synchronous Editor/Mono results do not rank full task lifecycles
or Player IL2CPP. A negative guard check used a temporary copy of the baseline
timing JSON with the candidate allocation pass: it rejected the stale runtime
revision and left the copy unchanged.

## Warm async builder allocation callstacks

The focused `-onityTaskBuilderAttribution` mode captures one Profiler `GC.Alloc`
callstack sample for each of five matched scheduling operations and both
libraries. Each operation runs 128 calls after two completed 128-call warmup
batches. Native `NextFrame` scheduling and synchronously completed async methods
are controls; the suspended cases use the same `ScheduleAsyncMethods` path as
the expanded suite. Full GC runs before each marked batch. Frame waiting,
resumption, and result consumption stay outside the marker. Every native and
suspended awaiter is checked pending after the marker and before a frame yield.
The mode requires
`-profiler-enable`; run the pinned Editor without `-quit`:

```text
Unity.exe -batchmode -nographics -profiler-enable -projectPath <benchmark-host> -executeMethod Onity.Editor.Benchmarks.OnityTaskBenchmarkMenu.RunFromCommandLine -onityTaskBuilderAttribution -onityTaskBenchmarkOutput <absolute-report.json> -logFile <absolute-log>
```

The [raw callstack JSON](../../../../docs/assets/benchmarks/onitytask-builder-attribution-25c8e20-2026-09-23.json)
and [source provenance](../../../../docs/assets/benchmarks/onitytask-builder-attribution-25c8e20-2026-09-23.provenance.json)
record the `25c8e20` runtime and pinned UniTask 2.5.11 on Unity 2022.3.62f3
Editor/Mono. The positive and empty controls measured 65,568 B and 0 B; every
case's resolved callstack bytes sum exactly to its marked allocation bytes.
This is a single diagnostic sample. The earlier [eight-sample numerical
baseline](../../../../docs/assets/benchmarks/onitytask-unity2022-mono-warm-baseline-2026-09-23.json)
measured the same bytes per operation for these five cases.

| Scheduling operation | OnityTask B/op | UniTask B/op |
| --- | ---: | ---: |
| Native `NextFrame` | 0 | 0 |
| Completed untyped async method | 64 | 64 |
| Completed typed async method | 72 | 72 |
| Suspended untyped async method | 304 | 64 |
| Suspended typed async method | 312 | 72 |

In each suspended Onity case, the callstacks attribute 160 B/op across two
allocations per operation to `AsyncMethodBuilderCore.GetCompletionAction()` and
80 B/op across one allocation to `AsyncTaskMethodBuilder<T>.get_Task()`. The
remaining 64 B/op untyped or 72 B/op typed appears at the benchmark async
method callsite, matching its completed-method control and UniTask's suspended
case. The measured stack names identify allocation sites, not object types.
The 240 B/op Onity suspension increment is therefore localized to .NET builder
calls in this Editor/Mono workload. These synchronous scheduling measurements
do not include the remaining async lifecycle or establish an IL2CPP result.

## Two-input WhenAll scheduling comparison

The isolated `-onityWhenAllBenchmark` mode compares the exact user-level call
`OnityTask.WhenAll(first, second)` with `UniTask.WhenAll(first, second)`.
The pinned UniTask version is `2.5.11` at
`2e993ff18f28c931602a07292df0b0804eebef99`. Use the same benchmark
runner and menu source in both baseline and candidate hosts. The report records
SHA-256 values for the runner and the actual Onity runtime source. An allocation
pass rejects a timing report if either source, the Unity version, the pinned
UniTask revision, or the scenario configuration differs.

Three scenarios are kept separate: two pending completion-source inputs with
Onity's tracker at its default-on setting; the same pending inputs with only
Onity's tracker disabled; and two already completed inputs with the tracker on.
The completed-input case is a control, not part of the pending result. Input
sources and task values are prepared outside each marker. The timed/profiled
slice includes `WhenAll` scheduling and storing its returned task. Both inputs
are completed and the result is consumed and checked after the marker. This
excludes completion callbacks, continuation dispatch, consumption, and the
full task lifecycle.

There are ten warmup batches per library and scenario (enough to fill Onity's
1,024-entry default tracker), eight raw timing samples
of 16 batches × 128 operations, and eight raw allocation samples of 128
operations. Library order alternates by sample. Full GC is outside each
measured sample. Raw values, mean, median, minimum, maximum, standard
deviation, Stopwatch frequency, and empty harness samples remain in JSON;
the empty harness time is not subtracted. A separate Profiler process must
measure exactly 65,568 B in the positive control and 0 B in the empty control
before bytes/op is reported.

Run the pinned Unity 2022.3.62f3 Editor in two sequential processes without
`-quit`; the menu exits when each report is written:

```text
Unity.exe -batchmode -nographics -projectPath <benchmark-host> -executeMethod Onity.Editor.Benchmarks.OnityTaskBenchmarkMenu.RunFromCommandLine -onityWhenAllBenchmark -onityTaskBenchmarkOutput <absolute-report.json> -logFile <absolute-timing.log>
Unity.exe -batchmode -nographics -profiler-enable -projectPath <benchmark-host> -executeMethod Onity.Editor.Benchmarks.OnityTaskBenchmarkMenu.RunFromCommandLine -onityWhenAllBenchmark -onityTaskAllocationsOnly -onityTaskBenchmarkOutput <same-absolute-report.json> -logFile <absolute-allocations.log>
```

These Editor/Mono scheduling samples are not Player/IL2CPP or full-lifecycle
evidence. The two libraries' default task trackers differ; the tracker-off
scenario isolates Onity's optional tracking overhead but is not its default.
