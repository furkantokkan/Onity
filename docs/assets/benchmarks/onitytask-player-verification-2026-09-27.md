# OnityTask Player verification - 2026-09-27

## Scope and result

- Runtime candidate: `528d52c3a2db2f07abb0612d2784413623c6c45d`, with benchmark
  startup, verification and reporting changes. Runtime library sources are unchanged.
- Unity `2022.3.62f2` (`7670c08855a9`), Windows x64, Ryzen 9 5900X, 64 GB RAM.
  UniTask `2.5.11`, commit `2e993ff18f28c931602a07292df0b0804eebef99`.
- Non-development Release Players; Mono stripping disabled, IL2CPP stripping
  Minimal. Tracking and stack capture off, runner capacity 128. Both Players
  reported the proven internal execution-context capture/run pair.
- Startup, semantics, repeated baselines and full-cycle attribution passed. The performance
  target is **not met**. Keep `FlowExecutionContext = true` by default.
- The [source and result provenance](onitytask-player-verification-2026-09-27.provenance.json)
  records the Release checkpoint's report/test hashes, build GUIDs and restored
  settings. All 302 runtime files and 12 changed benchmark source/meta files
  matched the measurement host at that checkpoint, before profiling additions.
  That host's checkout commit differs from its staged runtime package; use the
  candidate revision and source hashes, not the host's Git HEAD, as test identity.

## Startup fix and correctness

The previous IL2CPP executable omitted the benchmark, Onity.Unity and UniTask
assemblies from native registration and shipped metadata. Its empty scene did
not reference the package entry assembly. Assembly-level `AlwaysLinkAssembly`
keeps the initialization methods reachable, as specified in the
[Unity 2022.3 documentation](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Scripting.AlwaysLinkAssemblyAttribute.html).
The rebuilt Player entered and completed the benchmark instead of timing out.

The launcher now records startup stages, separates a 60-second startup deadline
from the 15-minute measurement deadline, and rejects missing handshakes or stale
reports. It stops polling the trace after entry to avoid measurement-period I/O.

| Verification | Default optimization | Release optimization |
| --- | ---: | ---: |
| EditMode full suite | 668/668 | 668/668 |
| PlayMode full suite | 41/41 | 41/41 |

Full suites ran after the startup fix, including 48 builder tests and nine new
watchdog cases. Later changes only added benchmark verification/reporting; both
final Player binaries were rebuilt and separately passed all 13 smoke cases:
synchronous and suspended typed/untyped methods, safe and critical awaiters,
native/bridge exclusivity, stale tokens, bridge recycling, deferred pool return,
re-rent during completion, held-worker MoveNext protection, execution-context
flow/suppression/isolation, and synchronization-context restoration.

- [Mono smoke](onitytask-player-mono-smoke-2026-09-27.json)
- [IL2CPP smoke](onitytask-player-il2cpp-smoke-2026-09-27.json)

## Primary results

Ratios below are Onity mean time / UniTask mean time for typed and untyped async
NextFrame **scheduling slices**, spanning both independent processes per backend.
Lower is better; a value above 1 means Onity took longer. Completion, resumption,
PlayerLoop work and deferred pool return are outside these timing slices.

| Backend / workload | Concurrency | Flow on ratio | Flow off ratio | Onity scheduling B/op, both modes |
| --- | ---: | ---: | ---: | ---: |
| Mono, default order | 128 | 1.41-1.53 | 1.23-1.26 | 0 measured |
| Mono, default order | 4096 | 1.76-1.88 | 1.48-1.61 | 398-415 |
| IL2CPP, default order | 128 | 1.77-1.94 | 1.70-1.74 | 242-245 |
| IL2CPP, default order | 4096 | 1.94-2.29 | 1.81-2.13 | 616-618 |
| IL2CPP, separate two-frame drain control | 128 | 1.62-1.65 | 1.42-1.44 | 0 measured |
| IL2CPP, separate two-frame drain control | 4096 | 2.05-2.25 | 1.89-2.14 | 607-611 |

Each report contains 24 scenarios and eight raw samples per metric. All
allocation metrics retained 8/8 valid samples. Every process selected HeapDelta,
with a 69,632-byte positive control and zero-byte empty control. Per-thread and
ProfilerCounter rejection reasons are retained in schema 6 reports. HeapDelta
is process-wide and coarse: **zero is not proof of zero allocation**, and small
positive readings are not exact object-size evidence. For example, UniTask
flow-on rows read approximately 1.4-1.7 B/op in the default IL2CPP runs; the
separate drain control read zero for those rows.

One unrelated Unity Editor remained open. No task-owned tests or builds ran
concurrently with the five final measurement processes. Raw samples retain
variability; do not turn these slice results into a full-lifecycle speed claim.

- Mono: [run 1](onitytask-player-mono-run1-2026-09-27.json), [run 2](onitytask-player-mono-run2-2026-09-27.json)
- IL2CPP: [run 1](onitytask-player-il2cpp-run1-2026-09-27.json), [run 2](onitytask-player-il2cpp-run2-2026-09-27.json)
- [Separate IL2CPP drain control](onitytask-player-il2cpp-drain-control-2026-09-27.json)

## What the drain control establishes

Alternating library order can place two Onity batches next to each other.
Consumption retires the runner immediately, but IL2CPP returns it to the pool
on a later dispatcher drain. The next batch can therefore allocate before the
previous runners become reusable, even at concurrency 128. The optional
`-onityTaskBenchmarkDrainBetweenBatches` inserts two real frames after each
consumed batch for both libraries, outside timed slices. It is a separate
control; the default workload is preserved.

The control removes the observed 128-operation scheduling heap delta, strongly
supporting deferred pool replenishment as its cause. At 4096, runner retention
128 and frame-source retention 256 are still exceeded. This does not demonstrate
a runtime performance improvement: no pool or locking algorithm was changed.

## Full-cycle attribution

Windows Performance Recorder failed with policy error `0xc5585011`; no recording
remained active. A separate Unity Development IL2CPP build with deep profiling
provided a working non-admin route. Its first diagnostic build used class state
machines and introduced 64/72 B per helper call. Those results were retained
locally but are not the final attribution evidence.

The final diagnostic build temporarily appended `/optimize+` through
`PlayerSettings.SetAdditionalCompilerArguments`. The generated response file's
last optimization argument was `/optimize+`, and both generated async helpers
were independently verified as `System.ValueType`. This removes the class-shape
confounder; it does not make deep-profile durations equivalent to Release timing.

All eight configurations (128/4096, flow on/off, immediate/drained reuse) passed
capture and headless export, giving 16 valid library windows. Each contains two
Schedule and Consume samples, all 256 or 8192 resumptions, deep runtime paths,
and no missing allocation metadata. Every measured deferred runner return also
appears in Lifecycle: Onity `ReturnToPool` and UniTask `Return` each run once per
operation. Two warmup batches precede capture; two terminal frames remain inside
the window. The final diagnostic Player also passed all 13 smoke cases.

Allocation counts matched with flow on and off. These totals cover **two batches**
per library; they are diagnostic allocation sites, not replacement Release B/op:

| Concurrency / reuse | Onity bytes | UniTask bytes |
| --- | ---: | ---: |
| 128, immediate | 58,368 | 50,176 |
| 128, drained | No events observed | No events observed |
| 4096, immediate | 4,598,784 | 1,605,632 |
| 4096, drained | 4,540,416 | No events observed |

The exporter retains `-1` when no allocation events exist, rather than claiming
a calibrated zero. At 128 immediate, each library allocates 128 runners and 256
cached delegates at its first expansion. UniTask can retain those extra runners
for later batches; Onity retains 128. At 4096 drained, Onity allocates 7,936
runners, 15,872 delegates and 7,680 frame sources, consistent with the retention
caps. Exact object sizes apply to this diagnostic build.

The [compact attribution and provenance](onitytask-player-fullcycle-2026-09-27.json)
retains all configurations, full allocation paths, phase and call counts, source
shape evidence and hashes/locations of the raw captures and complete exports.
Self sample durations include profiler overhead and retained wait paths; they
must not be summed as aggregate active CPU or compared with Release durations.

## Next bounded decision

Plan 12's verification objective is complete. The 128-operation drained capture
shows nine operation-related monitor acquisitions per operation, plus six
dispatcher drains for the window. `OnCompleted`, `TrySetStatus` and
`GetResultCore` on the typed/untyped source bases account for five of the nine;
frame-source rent/reset/release account for three and deferred-return enqueue
for one. Flow adds one capture/run pair per operation without changing the
observed allocation sites.

The next task should design and verify native-only synchronization reduction in
those three source methods. Preserve bridge/shared-task behavior, stale-token
checks, concurrency guarantees and IL2CPP deferred-return safety. Keep flow on
and capacity 128; do not bundle pool widening or early return into that change.
This is a concrete investigation target, not a measured Release bottleneck
percentage or a guarantee of reaching the 1.2x milestone.

Build settings were restored to their original hashes and the temporary scene
was removed. Restoring compiler arguments through Unity's API left an empty
`Standalone: []` map entry; after the Editor exited, that serialization-only
entry was returned to its original `{}` representation and the original hash
was verified. No commit, push or release was performed.
