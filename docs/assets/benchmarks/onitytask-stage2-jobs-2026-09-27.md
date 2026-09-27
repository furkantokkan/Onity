# OnityTask Jobs and Burst verification — 2026-09-27

## Candidate and verification

- Unreleased Stage 1/2 changes on `3f098937e9198a17edcf72cd75e73bd5bc76e2fd`;
  package version remains 0.3.14. The warm test host's Git HEAD is not the
  runtime identity. [Source hashes](onitytask-stage2-jobs-2026-09-27/source-hashes.json)
  match all 462 runtime/test/benchmark source and metadata files in both checkouts.
- Unity 2022.3.62f2, Windows x64, Ryzen 9 5900X (12 cores/24 logical processors).
  Installed Burst 1.8.29, Collections 2.6.8 and UniTask 2.5.11 at
  `2e993ff18f28c931602a07292df0b0804eebef99`; no new package dependency.
- Normal and Release Editor optimization each passed **690/690 EditMode and
  50/50 PlayMode**, with zero skipped cases. [XML identities and counts](onitytask-stage2-jobs-2026-09-27/test-results-summary.json).
- The eight new EditMode and four new PlayMode cases cover completed/pending
  jobs, dependencies, safe disposal, worker rejection, sharing/consumption,
  unawaited retirement, callback reentrancy and runner recreation. Actual
  reload-disabled Play/Edit/Play checks passed, including registration in Awake
  before EnteredPlayMode. The focused corrected run had no serialization repair,
  unexpected domain reload or native allocation leak. Full suites separately
  include an intentional reload-enabled lifecycle test.
- Both non-development Release Players passed all **16 smoke cases**, with
  InternalPair context flow enabled and runner capacity 128. IL2CPP used the
  Release native compiler and Minimal stripping. PlayerSettings, Editor build
  settings and Editor settings hashes were restored; no temporary scene remains.

## Compute measurement

Three warmups and eight samples of two executions per variant; every one of
the 19 executions validates the full output and four independent golden values.
A BurstDiscard probe must prove the Burst path for Burst jobs and the ordinary
C# path for the other variants. A wrong marker fails the run. All four processes
passed these checks and all six compute rows.

The same 32-round uint xorshift/add kernel uses persistent buffers prepared
outside measurement. Serial time covers its loop. Job time covers Schedule
through Complete, including scheduling overhead; output validation is excluded.

Mean milliseconds per execution at **65,536 elements**:

| Backend/run | Serial C# | Jobs without Burst | Jobs with Burst | Serial/Burst | Plain Jobs/Burst |
| --- | ---: | ---: | ---: | ---: | ---: |
| Mono 1 | 3.277 | 0.309 | 0.229 | 14.34× | 1.35× |
| Mono 2 | 3.409 | 0.319 | 0.236 | 14.46× | 1.35× |
| IL2CPP 1 | 3.346 | 0.281 | 0.232 | 14.43× | 1.21× |
| IL2CPP 2 | 3.153 | 0.279 | 0.224 | 14.08× | 1.25× |

Most of the serial comparison includes parallelism. The final column isolates
the incremental benefit of enabling Burst for this job. At 1,024 elements,
serial time was 0.053–0.055 ms and Burst job time 0.043–0.057 ms: job scheduling
overhead can erase the benefit for small workloads. IL2CPP serial/plain C# is
also native code; these labels do not imply that only Burst is machine code.

## Adapter measurement

One outstanding 65,536-element Burst job per cohort. Both the pre-registration
handle and returned awaiter must be incomplete. Each library retained two
eligible warmups and 16 measured observations in every process, with two drain
frames outside measurement. Onity excluded two already-completed observations
per Mono run and one per IL2CPP run; UniTask excluded none. Exclusions remain in
the raw reports and are not averaged into pending-path measurements.

| Backend/run | Onity registration mean/median, µs | UniTask registration mean/median, µs | Onity completion, ms | UniTask completion, ms |
| --- | ---: | ---: | ---: | ---: |
| Mono 1 | 1.088 / 1.050 | 0.800 / 0.700 | 0.243 | 0.240 |
| Mono 2 | 1.150 / 1.100 | 0.950 / 0.900 | 0.258 | 0.260 |
| IL2CPP 1 | 13.831 / 1.500 | 1.288 / 1.050 | 0.259 | 0.260 |
| IL2CPP 2 | 1.750 / 1.550 | 1.344 / 1.050 | 0.249 | 0.240 |

The first IL2CPP Onity registration mean includes a 197.2 µs outlier; it is
retained, not discarded. Registration favors UniTask in these runs. Completion
is Schedule-to-native-consumption wall time, including computation and polling.
Onity's MonoBehaviour Update and UniTask's injected Update occupy different
positions. Headless, uncapped frames are not normal gameplay frame latency.
Similar completion times do not establish equal scheduler CPU cost.

Allocation is **unavailable**, recorded as -1. Mono's per-thread counter failed
the 64 KiB positive control; IL2CPP skips it after earlier crashes. The selected
heap-delta fallback cannot isolate concurrent workers. Neither registration nor
the complete worker lifecycle has a zero-allocation claim from this run.

An unrelated Unity Editor remained open and consumed 1.09–2.84 CPU seconds
during each 0.69–1.67 second process window. Its activity is retained in the
execution sidecars. No Onity build, test or second benchmark ran concurrently.
These short measurements are workload evidence, not an isolated-machine ranking.

## Result and evidence

The native JobHandle bridge works on Mono and IL2CPP, and Burst accelerates
this data-processing job. **This does not demonstrate that OnityTask is faster
than UniTask.** Keep the execution-context default and runner retention unchanged.
Next work is the approved behavior-preserving typed WhenAll optimization, then
cancellation/composition and async streams.

- [Mono run 1](onitytask-stage2-jobs-2026-09-27/plan13-stage2-mono-jobs-1.json),
  [run 2](onitytask-stage2-jobs-2026-09-27/plan13-stage2-mono-jobs-2.json).
- [IL2CPP run 1](onitytask-stage2-jobs-2026-09-27/plan13-stage2-il2cpp-jobs-1.json),
  [run 2](onitytask-stage2-jobs-2026-09-27/plan13-stage2-il2cpp-jobs-2.json).
- [Mono smoke](onitytask-stage2-jobs-2026-09-27/plan13-stage2-mono-smoke.json),
  [IL2CPP smoke](onitytask-stage2-jobs-2026-09-27/plan13-stage2-il2cpp-smoke.json).
- [Derived measurement summary](onitytask-stage2-jobs-2026-09-27/measurement-summary.json).
- [Environment, log identities and restored settings](onitytask-stage2-jobs-2026-09-27/provenance.json).
- Reproduction: the existing Player build entry accepts
  `-onityTaskBenchmarkSuite jobs`; use the same build twice in separate
  processes after the build Editor exits. Full setup and timing boundaries are
  in the [benchmark README](https://github.com/furkantokkan/Onity/blob/main/Packages/com.onity.framework/Benchmarks/Tasks/README.md).
