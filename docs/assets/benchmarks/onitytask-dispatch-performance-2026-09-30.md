# Onity 0.5: async runner dispatch experiments

Measured on September 29 UTC / September 30, 2026 in Istanbul. These are
unreleased development experiments on Unity **2022.3.62f2**, Windows x64,
Mono and IL2CPP Release Players, with UniTask **2.5.11** pinned to
`2e993ff18f28c931602a07292df0b0804eebef99`.

## Decision

**Neither dispatch experiment is retained.** The exact runtime from the
[previous verified optimization packet](onitytask-05-performance-2026-09-29.md)
has been restored. Twelve new regression cases for context capture across
two suspensions are retained.
General async superiority over UniTask remains unmet, and
`FlowExecutionContext` remains enabled by default.

The experiments reduced generated dispatch work, but the measurements did
not establish a repeatable general benefit without adverse observations.
Some IL2CPP scheduling rows improved while Mono results and logical-cycle
results were mixed. Other Unity activity contaminated the final comparison;
these observations cannot establish a causal improvement or regression.
Reverting avoids accepting an unproven runtime change. It does not prove
that fewer dispatches can never help.

## Variants and identity

All variants start from pulled commit `5c09749` plus the previous local,
verified source-publication, frame-pool, WhenAny and inlining changes.
In this report, **frozen is that prior local packet**, not unmodified `5c09749`.

| Variant | Experiment |
| --- | --- |
| `frozen` | Prior verified packet; retained final runtime |
| `fused` | Combine context capture and cached continuation retrieval into one runner interface call per suspension |
| `based` | Build on `fused`; use fieldless runner bases, nonvirtual Task/SetResult members and virtual continuation retrieval |

Only `OnityAsync.cs` and `OnityAsyncStateMachineRunner.cs` differed between
these variants. Context semantics, native state synchronization, pool limits,
MoveNext and deferred returns were preserved. The base experiment added no
fields, but changed the builder's private runner field type; it was not a
claim of compatibility with already compiled consumer binaries.

The [provenance manifest](onitytask-dispatch-performance-2026-09-30/provenance.json)
records exact source hashes, six build identities, generated C++ hashes,
654 matching final source/host files, restored settings and test evidence.
The source package is 0.4.0 plus unreleased changes. Reused host sidecars
still contain the stale **0.3.14** label; identify staged code by hashes.
Both experiment patches are retained as evidence, outside runtime sources:
[fused](onitytask-dispatch-performance-2026-09-30/fused-experiment.patch),
[based](onitytask-dispatch-performance-2026-09-30/based-experiment.patch).

Generated IL2CPP C++ for `based` confirmed direct Task/SetResult calls and
ordinary virtual continuation retrieval in typed/untyped safe/critical
builder paths. That confirms the intended mechanism, not a timing benefit.

## Measurement boundaries and noise

- Hardware: AMD Ryzen 9 5900X, 12 cores / 24 logical processors, Windows 11
  Pro 64-bit (`10.0.26200`). Another project remained open on this workstation.
- Both Players use `BuildOptions.None`, Release optimization, tracker off,
  runner capacity 128 and the `InternalPair` execution-context path.
- The primary suite has 24 scenarios, two libraries and eight samples per
  result. Scheduling and GetResult are separate slices; they exclude
  resumption, frame waiting and later deferred pool-return/drain CPU.
  Enqueue work called by GetResult remains timed.
- The builder-cycle suite uses one pending manual-awaitable suspension,
  128 operations, typed/untyped and flow-on/off cases, two warmups and eight
  measured batches. It sums schedule, manual completion and consumption
  windows. Enqueue work inside consumption is included; later deferred drain
  CPU and frame waiting are excluded. Two real drain frames follow each batch.
- Every library batch validates 128 registrations, callbacks, completions,
  gate reads and consumptions, plus the typed/untyped checksum. The same
  manual gate is used by both libraries; library order alternates. Loop and
  timestamp controls are reported without subtraction. Short batches remain
  exploratory measurements, not full physical-cycle timing.
- Four preliminary `frozen` r1 reports were excluded from performance decisions
  **before** inspecting decision results because Roslyn could overlap them.
  Four build-coupled startup reports are also retained separately as validation.
- Rounds 2/3 compare frozen/fused in opposite orders. Rounds 4/5 compare all
  three variants in opposite orders, also reversing backend order. Our builds,
  tests and Roslyn checks had completed before these comparisons.
- File names containing `quiet` mean no concurrent work launched by this task;
  they do **not** establish an idle workstation. Other Unity CPU was at most
  0.28125 seconds per r2/r3 process in total. In r4/r5, other Unity activity reached
  8.453125 CPU-seconds during an 8.396-second primary process and 0.90625
  CPU-seconds during a 0.812-second builder process. This is substantial
  competing work, not proof that all 24 logical processors were saturated.
  Paired ratios do not cancel this interference. No noisy report was removed
  after observing its numbers, and no clean performance gain is claimed.

The [validated summary](onitytask-dispatch-performance-2026-09-30/dispatch-comparison-summary.json)
retains every mean from 40 r2-r5 reports: 1,600 library batches including
warmups and 7,680 primary timing samples. All result/count/drain checks and
timing arithmetic passed. Each primary allocation metric has eight valid
samples with calibrated HeapDelta (69,632-byte positive control, zero empty
control). HeapDelta zero readings do not prove zero allocation. The
builder-cycle probe does not measure allocations.

Raw reports and process observations are retained unchanged in the linked
evidence directory, including excluded preliminary and startup results.

## Observations, not causal performance claims

Values below are raw Onity nanoseconds per operation from r4/r5. Both values
are retained; the other Editor's activity prevents treating these as clean
before/after estimates. Context flow is on in every row.

| Workload | Frozen r4 / r5 | Fused r4 / r5 | Based r4 / r5 |
| --- | ---: | ---: | ---: |
| Mono typed async scheduling, 128 | 275.72 / 262.96 | 284.16 / 272.51 | 284.70 / 274.83 |
| IL2CPP typed async scheduling, 128 | 332.00 / 329.05 | 295.17 / 313.92 | 306.81 / 289.51 |
| IL2CPP untyped synchronous completion | 15.96 / 15.99 | 18.37 / 15.71 | 17.67 / 17.91 |
| Mono untyped logical cycle, 128 | 325.10 / 326.37 | 307.52 / 326.27 | 312.40 / 314.94 |
| Mono typed logical cycle, 128 | 519.04 / 284.96 | 290.72 / 298.34 | 285.94 / 283.01 |
| IL2CPP untyped logical cycle, 128 | 292.58 / 262.79 | 254.00 / 293.65 | 266.60 / 245.90 |
| IL2CPP typed logical cycle, 128 | 286.82 / 311.72 | 312.11 / 287.89 | 292.68 / 306.45 |

The 519.04 ns Mono typed result is retained as an outlier; its UniTask control
also rose to 190.14 ns, versus 137.60 ns in r5. It must not manufacture a
large optimization claim. Earlier r2/r3 fusion comparisons also showed no
consistent general scheduling improvement. All paired UniTask values,
flow-off rows and burst-4096 cohorts are in the summary and raw reports.

## Correctness verification and final state

- Roslyn compiled affected runtime and test assemblies during development.
- The `based` candidate passed **91/91 Release EditMode**, **1/1 focused
  Release PlayMode**, and **35/35 semantic smoke cases in each Release Player**.
  These are focused checks, not a fresh full-suite result.
- Twelve new cases cover typed/untyped, safe/critical awaiters and flow-on/on,
  on/off and off/on transitions across two actual suspensions. They verify
  per-suspension capture, caller isolation and the flow decision's timing.
- After reverting both runtime experiments to the exact frozen hashes, the
  twelve new cases passed again on that final runtime. The previous packet's
  full-suite evidence remains linked with its original source identity.
- No settings, packages, public defaults or serialized assets were changed
  in the retained result. No commit, push or release was made in this pass.

## Next bounded investigation

Profile continuation execution and deferred-return enqueue/drain independently
on an idle benchmark host. The previous profile still identifies one enqueue
monitor per runner; this experiment did not change it. Preserve worker-thread,
reentry, retirement, cancellation, bridge and context-isolation behavior.
Require matched Mono/IL2CPP default-flow measurements for scheduling,
completion, consumption and deferred returns before retaining another change.
Use Roslyn during development and run focused behavior tests at the final
revision. Keep flow-off and raised-capacity experiments separately labeled.
