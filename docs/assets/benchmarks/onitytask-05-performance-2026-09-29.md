# Onity 0.5 development: async performance verification

Measured on 2026-09-29 UTC with Unity **2022.3.62f2**, Windows x64,
Mono and IL2CPP Release Players, against UniTask **2.5.11**
(`2e993ff18f28c931602a07292df0b0804eebef99`). This is an unreleased development
checkpoint. Overall UniTask speed superiority and the 1.2x async-scheduling
milestone remain unmet. Context flow stays enabled by default.

The later [dispatch investigation](onitytask-dispatch-performance-2026-09-30.md)
tested and reverted two additional runtime experiments. This packet remains
the retained runtime; its historical hashes and verification are unchanged.

Host: AMD Ryzen 9 5900X, 12 physical cores / 24 logical processors,
Windows 11 Pro 64-bit (`10.0.26200`). These shared-workstation measurements
are not a hardware-independent performance guarantee.

## Source and measurement identity

- Baseline: pulled `main` at `5c09749b7531cdc28678153a0b4421ccc4eafdfe`,
  including the packed source-state implementation. Candidate: the local
  changes on `codex/onity-0.5-async-performance` described below.
- Candidate `OnityAsync.cs` SHA-256:
  `3D66BDB8C0A0F27DFA75649283C75AB9E0289866F9D61DDDA80A99B74685128F`.
  `OnityWhenAnyArrayTaskSource.cs`:
  `26C30E9D4236846C1A9309F8FEDCC8312C73DEF4040FA1589256B13073167B40`.
- For primary/WhenAny verification, the isolated host's Runtime, Tests and
  Benchmarks trees matched all 652 staged source files. Its 28 Editor and four
  analyzer files also matched. Later benchmark-only additions have separate
  provenance.
  [Baseline hashes](onitytask-05-performance-2026-09-29/baseline-source-hashes.json)
  and [candidate hashes](onitytask-05-performance-2026-09-29/final-source-hashes.json)
  identify the measured code, rather than the host's historical Git HEAD.
- The source package is 0.4.0 with unreleased changes. The reused host's
  sidecars still say **0.3.14**; this stale package label does not identify the
  staged runtime. Raw reports are retained unchanged.
- Primary and WhenAny each have three independent processes per revision and
  backend: initial, repeat and control. Each metric has eight samples. Players
  use `BuildOptions.None`, Release optimization, tracking off and runner
  capacity 128. The common harness and UniTask revision are unchanged.
- Controls ran after builds completed, with reversed revision order on IL2CPP.
  Another project remained open on this shared workstation. Timing outliers
  affected both libraries, especially the second candidate IL2CPP process.
  [Process observations](onitytask-05-performance-2026-09-29/control-process-observations.json)
  and all initial results are retained; no outlier report was discarded.
- [Provenance](onitytask-05-performance-2026-09-29/provenance.json) records build
  binary hashes, generated inline declarations and the restored project-setting
  hashes. Temporary benchmark scenes were removed after builds.
- [Final staging hashes](onitytask-05-performance-2026-09-29/final-staging-hashes.json)
  verify all 654 source/host Runtime, Tests and Benchmarks files after the new
  probe, plus restored settings and absence of the temporary scene.

## Changes and correctness

- Reserve a task-bridge mode before writing a pooled source field; validate
  repeated bridge reads after reading their reference. Pin continuation
  registration until its fields are published. These prevent an old caller
  from modifying a source rented by a successor.
- Replace frame-source pool monitors with the existing intrusive pool, keeping
  the 256-source limit. Contended rents may allocate; contended/full returns
  may be discarded. Async-runner capacity remains 128.
- Add a separate 32-slot WhenAny bucket, retaining at most 128 coordinators per
  output shape. The existing 16-slot/256-coordinator bucket remains separate.
  Arrays above 32 remain unpooled; shareable-only arrays skip native identity
  HashSet allocation. Pending losers and callbacks still pin their coordinator.
- Inline six small builder/awaiter paths. All six intended sites appear as
  `IL2CPP_MANAGED_FORCE_INLINE` in generated candidate C++; this alone does not
  prove a machine-code timing improvement.

Roslyn compiled the affected runtime/test assemblies during development.
Final Unity verification used one full Release suite per mode, followed only
by repairs of failing cases and the planned focused default-optimization run:

| Verification | Result |
| --- | --- |
| Release EditMode initial | 977 passed, six failed, 983 total |
| Release EditMode corrected cases | 6/6 passed |
| Release PlayMode | 94/94 passed |
| Default EditMode: source state, Preserve, builders, array WhenAny and repaired cases | 127/127 passed |
| Mono / IL2CPP Release semantic smoke | 35/35 each |

Five initial failures queried native status after consumption, contrary to
the incoming packed-state token retirement behavior. Tests now check success
before consumption or on the returned bridge, and assert stale native status
rejection. The sixth reflected the former Stack pool; it now uses TryPop and
writes the changed boxed pool back before reuse. Outcome, identity, late-loser
observation and preserved-result checks remain. Production code was unchanged
between the full suites and these focused repairs.

The 28 added cases cover competing consumption/bridge/registration calls and
16/17/32/33-input boundaries, reuse, duplicate validation and pending losers.
The threaded tests are bounded stress checks, not exhaustive interleaving
proof. [Test summary and original failures](onitytask-05-performance-2026-09-29/test-summary.json)
link to retained XML reports in the same evidence directory.

## Release timing

Ratios below are **Onity / UniTask mean time**: below 1 is faster. Ranges span
all three processes and, where indicated, both typed and untyped methods.
Primary scheduling and GetResult are separate synchronous slices: PlayerLoop
waiting, resumption and deferred pool returns are outside them.

| Async NextFrame scheduling | Baseline flow on | Candidate flow on | Baseline flow off | Candidate flow off |
| --- | ---: | ---: | ---: | ---: |
| Mono, 128 | 1.279–1.436 | 1.177–1.260 | 1.076–1.159 | 0.959–1.114 |
| Mono, 4096 | 1.649–1.790 | 1.514–1.639 | 1.449–1.547 | 1.327–1.437 |
| IL2CPP, 128 | 1.570–1.608 | 1.576–1.632 | 1.392–1.521 | 1.404–1.563 |
| IL2CPP, 4096 | 1.787–2.006 | 1.586–1.953 | 1.508–1.819 | 1.510–1.794 |

Mono typed scheduling at 128 improved modestly, but untyped raw Onity times
did not improve despite the better ratios. Some ratio gains reflect variation
in the UniTask denominator. IL2CPP scheduling and GetResult do not show a
consistent general improvement. Flow-off also removes ambient-context behavior
that Onity normally preserves; it is not the default product configuration.

Two narrower results are clearer:

| Mono synchronous workload | Baseline Onity ns/op | Candidate Onity ns/op |
| --- | ---: | ---: |
| Completed untyped async method and GetResult | 42.31–42.75 | 30.92–33.27 |
| Completed typed async method and GetResult | 45.89–49.54 | 36.29–42.27 |
| Plain NextFrame scheduling, 128 | 121.78–128.44 | 113.19–115.45 |

Plain NextFrame scheduling candidate ratios are 0.916–0.944 on Mono and
0.635–0.679 on IL2CPP. This establishes a win for that scheduling slice, not
for a complete async method. Synchronous async methods still lag UniTask on
Mono. IL2CPP synchronous raw times overlap baseline despite improved typed
ratios, so no corresponding latency improvement is claimed.

### Array WhenAny: 32 inputs

This harness times construction, completing every producer in reverse order,
then consuming/storing the winner. Caller arrays and public producer creation
are outside timing. Each metric validates 384 warmup and 2,048 measured groups.

| Backend / shape | Baseline Onity ns/group | Candidate Onity ns/group | Baseline ratio | Candidate ratio |
| --- | ---: | ---: | ---: | ---: |
| Mono untyped | 9,906–10,989 | 7,217–7,715 | 1.206–1.249 | 0.886–1.000 |
| Mono typed | 9,873–10,793 | 6,977–8,627 | 1.176–1.317 | 0.907–1.004 |
| IL2CPP untyped | 11,758–12,826 | 10,357–10,927 | 1.657–1.756 | 1.361–1.437 |
| IL2CPP typed | 12,238–14,042 | 10,525–13,193 | 1.525–1.864 | 1.387–1.551 |

The 32-input path improves most clearly on Mono and untyped IL2CPP. Typed
IL2CPP raw ranges overlap. Mono approaches parity; IL2CPP remains slower.
The 2- and 16-input results are mixed, so this is not a general WhenAny win.

All reports selected calibrated **HeapDelta**, with a 69,632-byte positive
control and zero-byte empty control. At 32 inputs, Onity estimates moved from
6,368–6,410 B/group on Mono and 7,468–7,512 on IL2CPP to zero in each candidate
process. This is no measured heap growth in those windows, not proof of exact
zero allocation. Cold pools, caller arrays, native identity validation and
larger inputs can allocate.

## Logical builder-cycle Release probe

The added `buildercycle` suite uses the same preallocated manual awaitable for
both libraries: one suspension, 128 concurrent operations, typed/untyped
results and context flow on/off. Workload selection occurs outside the timed
loops. Scheduling, manual-gate completion and single native consumption are
timed separately; their sum includes common harness bookkeeping but excludes
frame waits and deferred pool-return/drain CPU. This is not a full physical
lifecycle or a NextFrame timing.

Each scenario/library has two warmups and eight measured batches; library
order alternates. Two real Unity frames drain pools outside timing after
every batch. The harness checks every callback and result, and reports
unsubtracted array-loop/timestamp controls. These controls do not model all
manual-gate overhead. No allocation measurement is made by this suite.

Two independent processes per revision/backend passed: eight reports,
640 validated library batches (512 measured, 128 warmup), 128 operations per
batch, and 256 control batches. Counter, checksum, alternating-order,
frame-delta, tick conversion and reported-mean checks all passed. The identical
new harness was built over baseline and candidate runtime snapshots; source
and binary hashes are in the [probe provenance](onitytask-05-performance-2026-09-29/buildercycle/provenance.json).

Logical-cycle ratios below are Onity / UniTask, shown as **first / repeat**.
Both processes are retained, including the anomalous Mono flow-off result.

| Backend / shape | Flow | Baseline ratio | Candidate ratio |
| --- | --- | ---: | ---: |
| Mono untyped | On | 1.772 / 2.076 | 1.947 / 1.920 |
| Mono typed | On | 2.145 / 1.935 | 2.064 / 1.814 |
| Mono untyped | Off | 1.224 / 1.338 | 1.205 / 1.237 |
| Mono typed | Off | 1.337 / 1.345 | 0.648 / 1.277 |
| IL2CPP untyped | On | 1.818 / 1.951 | 1.844 / 1.828 |
| IL2CPP typed | On | 2.107 / 1.924 | 1.872 / 1.805 |
| IL2CPP untyped | Off | 1.423 / 1.336 | 1.327 / 1.360 |
| IL2CPP typed | Off | 1.449 / 1.492 | 1.373 / 1.371 |

The first candidate Mono typed flow-off process measured 310 ns/op for Onity
and 478 for UniTask, compared with 179 and 140 in its repeat. UniTask scheduling
alone changed from 403 to 89 ns/op; Onity also slowed in that first process.
The apparent 0.648 ratio is not a repeatable win. Short batches and shared-host
variation limit this exploratory probe; no general cycle improvement or
statistical significance is claimed.

With default flow enabled, candidate logical cycles remain about 1.8–2.1x
UniTask. On Mono, candidate gate completion costs 110–124 ns/op versus 23–26
for UniTask; flow-off lowers the Onity completion slice to 40–62 ns/op.
These are phase observations, not an isolated measurement of context internals.
Consumption includes any deferred-return enqueue called by GetResult, but
the subsequent drain CPU is outside timing. The [phase summary and all raw reports](onitytask-05-performance-2026-09-29/buildercycle/comparison-summary.json)
preserve this distinction from the primary scheduling slices and diagnostic
physical-cycle profiles. Context flow remains enabled by default.

## Complete-cycle diagnostic profiles

Separate Development IL2CPP builds used deep profiling with managed
`/optimize+`. All eight configurations per revision passed: concurrency
128/4096, flow on/off, immediate reuse/two-frame drain. Every library window
contains two scheduling/consumption batches, all 256 or 8,192 resumptions and
returns, and allocation metadata with no missing entries.

At 128 operations, frame-pool Monitor.Enter counts fell from 256 rents plus
256 returns to **zero** per captured window. The 256 deferred-return enqueue
monitor entries remain. Nested ReliableEnter markers are not additional locks.

Allocation events did not fall in this diagnostic workload: both 128 immediate
profiles recorded 384 events and 58,368 bytes; drained profiles recorded none.
Some later primary IL2CPP windows show lower HeapDelta estimates, including
untyped flow-off; they are sequence-sensitive observations with an unresolved
cause, not evidence of fewer runner/delegate allocations.

There is a cold-allocation tradeoff: the new intrusive link adds one reference
per frame source. In the 4096-operation profiles, 7,680 new frame sources cost
128 rather than 120 bytes each: **61,440 additional bytes across 8,192 captured
operations**. Other allocation sites were unchanged. This is retained for the
removed monitors and measured narrow scheduling benefit; no burst-allocation
improvement is claimed. Instrumented durations are not Release timings.

[Profile summary](onitytask-05-performance-2026-09-29/profile-summary.json)
contains counts, selected call paths and hashes of the retained local binary
profiles/exports. [Comparison data](onitytask-05-performance-2026-09-29/comparison-summary.json)
contains every primary and WhenAny process mean and raw-report filename.

## Next bounded investigation

Measure and evaluate deferred-return dispatcher enqueue synchronization while
preserving worker publication, session retirement, reentrancy and deferred
IL2CPP reuse. It is one remaining monitor per returned runner. Changing it
would affect consumption and drain work; it would not directly prove faster
scheduling or eliminate immediate-reuse allocation. No such runtime change is
included in this checkpoint.

The owner is `OnityTaskMainThreadDispatcher` in `OnityTaskThreadSwitch.cs`.
A possible bounded experiment is a separate double-buffered buffer for
main-thread deferred returns, retaining the synchronized queue for workers.
Snapshot both buffers before invoking any callbacks so cross-queue reentry
and worker-depth retries wait for a later drain. Preserve shutdown cleanup,
runner recreation, exception isolation and the general switch queue's order.
First isolate Release enqueue/drain CPU at 128 and 4096 operations; adopt a
change only after matched primary/logical-cycle timings, GC/retention evidence,
mixed-worker/reentry/lifecycle tests and deferred-return Player smoke checks.
The current evidence does not justify replacing the monitor blindly.
