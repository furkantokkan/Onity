# Array WhenAny verification — 2026-09-27

## Candidate and behavior

- Unreleased Plan 13 candidate on `3f098937e9198a17edcf72cd75e73bd5bc76e2fd`;
  package version remains 0.3.14. The source and warm verification host match
  across [439 runtime/test files](onitytask-stage3b-whenany-2026-09-27/runtime-test-source-hashes.json)
  and [476 files including benchmarks](onitytask-stage3b-whenany-2026-09-27/source-hashes.json).
- Typed/untyped array overloads snapshot before registration, reject duplicate
  single-consumer source/token identities before claiming any input, and observe
  every accepted loser without cancellation. First observed terminal input wins.
  Outputs are native single-consumer tasks, with explicit sharing/Task bridges.
- Retained storage covers up to 16 inputs, capped at 256 sources per output
  shape. Reuse requires released output, observed inputs and completed callbacks.
  Larger arrays and registration-error instances are unpooled.
- Review found that exact-type outcome checks excluded supported public
  completion-source subclasses. Three bridge-timing regressions failed before
  the two-reader fix and passed afterward. Readers now use inherited locked
  outcome observation, including existing/concurrent/future Task bridges.
  Stage 3a's separate exact-type optimization eligibility is unchanged.

## Verification

- Unity 2022.3.62f2: **744/744 EditMode and 52/52 PlayMode** passed in both
  normal and Release optimization, with zero skipped cases. All 29 new EditMode
  cases passed, plus two new PlayMode worker/context checks.
- Non-development Windows x64 Mono and IL2CPP Players each passed **18/18
  smoke cases**. IL2CPP used native Release compilation and Minimal stripping.
  Both used InternalPair context flow, default flow enabled and runner cap 128.
- Build settings, PlayerSettings and Editor settings hashes were restored;
  the temporary scene was removed. [Tests and red/green identities](onitytask-stage3b-whenany-2026-09-27/test-results-summary.json).
- Malicious implementations of inaccessible internal interfaces remain a
  code-review case; no public/test-only runtime hooks were added. Real stale,
  preclaimed, preserved, derived and Task-backed sources are exercised.

## Measurement method

Ryzen 9 5900X, 12 cores/24 logical processors. Compare Onity with pinned UniTask
2.5.11 (`2e993ff18f28c931602a07292df0b0804eebef99`) using typed-int and untyped
arrays of 2, 16 and 32 fresh pending public completion sources. Input/source
and output-holder allocation is outside measurement. Inside each slice:

1. Construct all 128 outputs using explicit array overloads.
2. Complete every producer synchronously in reverse order, observing losers.
3. Consume each output once and store its winner/value.

Validate every winner/value outside timing. Three warmups and eight samples
of two cohorts give **384 warmup and 2,048 measured groups per row**. Alternate
library order and run each Player twice in independent processes. Every report
completed with all 12 rows and eight valid samples per row. Both trackers are
disabled; pinned UniTask tracker calls are absent in Players. Flow remains on.

This measures a synthetic complete composition cycle, including library-specific
public-source completion. It does not isolate scheduler CPU, include caller
input allocation, or represent native single-consumer/cancellation workloads.

## Results

Mean microseconds per group, run 1 / run 2:

| Backend | Shape | Inputs | Onity | UniTask |
| --- | --- | ---: | ---: | ---: |
| Mono | Untyped | 2 | 1.176 / 0.887 | 0.763 / 0.630 |
| Mono | Untyped | 16 | 5.606 / 4.439 | 4.207 / 4.105 |
| Mono | Untyped | 32 | 13.623 / 11.404 | 9.401 / 8.912 |
| Mono | Typed | 2 | 1.202 / 0.950 | 0.722 / 0.646 |
| Mono | Typed | 16 | 5.007 / 3.982 | 4.308 / 3.948 |
| Mono | Typed | 32 | 12.019 / 10.403 | 8.391 / 8.849 |
| IL2CPP | Untyped | 2 | 1.095 / 1.273 | 0.567 / 0.632 |
| IL2CPP | Untyped | 16 | 5.951 / 6.032 | 4.203 / 4.148 |
| IL2CPP | Untyped | 32 | 14.744 / 12.902 | 8.451 / 7.511 |
| IL2CPP | Typed | 2 | 1.157 / 1.175 | 0.561 / 0.549 |
| IL2CPP | Typed | 16 | 5.592 / 5.584 | 3.797 / 3.876 |
| IL2CPP | Typed | 32 | 14.313 / 13.672 | 7.854 / 8.666 |

Onity/UniTask mean-time ratios are **1.008–1.664× on Mono** and
**1.416–2.142× on IL2CPP**. All recorded means favor UniTask; Mono typed-16
run 2 is near parity and does not prove a material difference.

| Inputs | Onity heap delta | UniTask heap delta |
| --- | ---: | ---: |
| 2 / 16 | 0 measured B/group | 88–600 B/group |
| 32, Mono | 6,404–6,438 B/group | 530–558 B/group |
| 32, IL2CPP | 7,478–7,512 B/group | 1,076–1,100 B/group |

The **allocation jump above 16 inputs** follows the current unpooled policy.
Small warmed cohorts show no measurable heap growth, not proof of exact zero
allocation. Per-thread/profiler counters were unavailable; process-wide
HeapDelta passed a 64 KiB control (69,632 bytes) and an empty control (0 bytes),
with GC disabled in each slice. The counter is coarse and page-granular.

An unrelated Unity Editor remained active, consuming 1.34–3.23 CPU seconds
during each 0.90–1.73 second process window; another CLI process showed no CPU
increase. Sidecars retain these observations. No Onity build/test or second
benchmark ran concurrently. These are short workload measurements on a shared
machine, not an isolated-machine ranking or a general speed claim.

## Outcome and evidence

The API/ownership milestone is verified; **Onity has not overtaken UniTask in
this measured workload**. Continue approved cancellation API work. Profile
composition phases and allocation sites before changing pool sizes or removing
synchronization. Preserve the execution-context default and runner capacity.

- [Mono run 1](onitytask-stage3b-whenany-2026-09-27/plan13-stage3b-mono-whenany-1.json),
  [run 2](onitytask-stage3b-whenany-2026-09-27/plan13-stage3b-mono-whenany-2.json).
- [IL2CPP run 1](onitytask-stage3b-whenany-2026-09-27/plan13-stage3b-il2cpp-whenany-1.json),
  [run 2](onitytask-stage3b-whenany-2026-09-27/plan13-stage3b-il2cpp-whenany-2.json).
- [Derived measurements](onitytask-stage3b-whenany-2026-09-27/measurement-summary.json),
  [provenance and log identities](onitytask-stage3b-whenany-2026-09-27/provenance.json).
- [Mono smoke](onitytask-stage3b-whenany-2026-09-27/plan13-stage3b-mono-smoke.json),
  [IL2CPP smoke](onitytask-stage3b-whenany-2026-09-27/plan13-stage3b-il2cpp-smoke.json).
- Reproduce with `-onityTaskBenchmarkSuite whenany` through the existing
  Player build entry, then reuse the built executable in separate processes
  after the build Editor exits. Full flags and scope are in the benchmark README.
