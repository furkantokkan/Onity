# DI Benchmark Summary

> **NOTE — allocation column is unreliable and superseded.**
> The `Alloc/sample (B)` column below reads `0.00` for every container,
> including VContainer and Zenject. That is impossible: a transient resolve
> must allocate the instance it returns, the complex resolve allocates one
> object per node in the graph, and Zenject is known to be allocation-heavy.
> A reading of `0.00` across the board means the allocation measurement was
> not capturing real bytes on this run, not that these paths allocate nothing.
> On Unity 2022.3 Editor Mono, `GC.GetAllocatedBytesForCurrentThread()` (the
> API the runner uses) does not return reliable per-thread allocation totals,
> so the column is meaningless here and must be re-measured inside the Unity
> editor with a trustworthy allocation source (for example the Unity Profiler
> GC Alloc counter) before any allocation claim is published. Do not cite
> these allocation figures, and do not state a verified "zero-allocation"
> resolve based on them. The `Mean (ms)` / speed columns are unaffected by
> this and still stand as indicative timings (Editor-Mono, a single machine,
> not a guarantee).

Generated 2026-05-24 from `OnityDiBenchmarkRunner` after Phase 1.1
(compiled constructor activators via `Expression.Compile`).

| Scenario | Container | Mean (ms) | Alloc/sample (B) |
|---|---|---:|---:|
| Resolve (Singleton) | Onity | 1.5222 | 0.00 |
| Resolve (Singleton) | VContainer | 1.9520 | 0.00 |
| Resolve (Singleton) | Zenject | 23.2601 | 0.00 |
| Resolve (Transient) | Onity | 9.9570 | 0.00 |
| Resolve (Transient) | VContainer | 14.2108 | 0.00 |
| Resolve (Transient) | Zenject | 126.7015 | 0.00 |
| Resolve (Combined) | Onity | 18.8285 | 0.00 |
| Resolve (Combined) | VContainer | 24.6200 | 0.00 |
| Resolve (Combined) | Zenject | 203.9153 | 0.00 |
| Resolve (Complex) | Onity | 378.9463 | 0.00 |
| Resolve (Complex) | VContainer | 471.1739 | 0.00 |
| Resolve (Complex) | Zenject | 3023.8274 | 0.00 |
| Prepare & Register (Complex) | Onity | 300.8530 | 0.00 |
| Prepare & Register (Complex) | VContainer | 1459.5271 | 0.00 |
| Prepare & Register (Complex) | Zenject | 1912.9742 | 0.00 |

## Relative Speedup vs VContainer

| Scenario | Onity speedup |
|---|---:|
| Resolve (Singleton) | +28.24% |
| Resolve (Transient) | +42.74% |
| Resolve (Combined) | +30.71% |
| Resolve (Complex) | +24.34% |
| Prepare & Register (Complex) | +384.39% |

## Phase 1.1 Improvement vs Prior Onity Baseline (Feb 10)

| Scenario | Before (ns/op) | After (ns/op) | Delta |
|---|---:|---:|---:|
| Resolve (Singleton) | 175.40 | 152.22 | -13.21% |
| Resolve (Transient) | 2087.58 | 995.70 | -52.30% |
| Resolve (Combined) | 2162.89 | 1882.85 | -12.94% |
| Resolve (Complex) | 52386.02 | 37894.63 | -27.66% |
| Prepare & Register (Complex) | 12491.93 | 30085.30 | +140.84% |

Notes:

- All resolve scenarios now meet or approach the Phase 1 gates in
  `docs/Plan/04-Performance-Targets.md`. Transient and Complex are the
  headline wins.
- Prepare & Register Complex regressed because `Expression.Compile` runs
  once per registered type at build time. Onity still finishes container
  build ~4.85x faster than VContainer in absolute terms.
- The allocation column is not trustworthy on this run (see the NOTE at the
  top): it read `0.00 B` for every container, which is impossible, so no
  zero-allocation conclusion can be drawn from these figures. Re-measure
  allocations in the Unity editor before publishing any allocation claim.
