---
title: "DI and pooling measurement history"
parent: "Measurement history"
nav_order: 2
description: "DI, pooling and release measurement text moved verbatim from the README files, the home page, the performance guide and the architecture page on 2026-10-02."
---

# DI and pooling measurement history

Each section below was moved verbatim from the page it names on 2026-10-02, when those pages were
rewritten for Onity 0.7.0. The text keeps its original date, Unity version and wording; only the
relative links that changed with the move were edited. The current DI and pooling comparison is
[DI vs VContainer and Zenject](../Onity-vs-VContainer-Zenject.html); the current async comparison is
[OnityTask vs UniTask](../guide/onitytask-comparison.html).

## Moved from README.md

The Benchmarks section of the repository `README.md`, plus the two lines of its Documentation and
Requirements sections that referred to the same measurements:

## Benchmarks

Measured by `OnityDiBenchmarkRunner` / `OnityDiBenchmarkPlayerRunner` (Unity 2022.3.62f3, Windows; 512 warmup / 8 samples / mean). These numbers were measured on a single machine and are **indicative, not a guarantee**; hardware, Unity version, backend, and graph shape can change both absolute timings and relative ordering.

Editor / Mono run (`2026-09-23`, `WindowsEditor`):

| Scenario | Onity | VContainer | Zenject | Onity vs VContainer |
| --- | ---: | ---: | ---: | ---: |
| Resolve Singleton | ~106 ns | ~210 ns | ~2,773 ns | ~+50% |
| Resolve Transient | ~689 ns | ~1,872 ns | ~11,785 ns | ~+63% |
| Resolve Combined | ~547 ns | ~1,886 ns | ~13,900 ns | ~+71% |
| Resolve Complex (6-level) | ~12,465 ns | ~37,633 ns | ~272,418 ns | ~+67% |
| Prepare & Register Complex | ~53,284 ns | ~153,133 ns | ~183,996 ns | ~+65% |
| Resolve Keyed Singleton | ~140 ns | ~252 ns | ~2,951 ns | ~+45% |
| Resolve Scoped Singleton | ~166 ns | ~300 ns | ~2,819 ns | ~+45% |

IL2CPP release player run (`2026-09-23`, `WindowsPlayer`, `19` generated activators registered):

| Scenario | Onity | VContainer | Zenject | Onity vs VContainer |
| --- | ---: | ---: | ---: | ---: |
| Resolve Singleton | ~21 ns | ~91 ns | ~463 ns | ~+77% |
| Resolve Transient | ~129 ns | ~529 ns | ~2,333 ns | ~+76% |
| Resolve Combined | ~126 ns | ~588 ns | ~2,901 ns | ~+79% |
| Resolve Complex (6-level) | ~3,936 ns | ~13,078 ns | ~61,883 ns | ~+70% |
| Prepare & Register Complex | ~22,575 ns | ~40,919 ns | ~63,623 ns | ~+45% |
| Resolve Keyed Singleton | ~31 ns | ~86 ns | ~574 ns | ~+64% |
| Resolve Scoped Singleton | ~47 ns | ~109 ns | ~563 ns | ~+57% |

On Mono/JIT, the speed comes from a process-wide compiled-activator cache (`Expression.Compile` once per `ConstructorInfo`), compiled member setters, a `[ThreadStatic]` lock-free argument-array pool, and a per-plan per-slot constructor-dependency cache. On IL2CPP, generated activators register direct constructor delegates before construction plans are built. The [September 23 timing and allocation reports](../benchmarks/advanced-di-summary-2026-09-23.md) support the tables above; a [September 24 repeat](../benchmarks/di-speed-followup-2026-09-24.md) records fresh IL2CPP results and measurement variability.

A [2026-10-01 re-measurement](../benchmarks/remeasure-2026-10-01.md) on Unity 2022.3.62f2 reproduced the IL2CPP ordering in three fresh processes (Onity/VContainer 0.23x–0.57x across scenarios) and the Editor allocation event counts exactly.

Benchmark verification behind these numbers: Unity batchmode Editor DI benchmark,
Windows IL2CPP player DI benchmark, separate raw Profiler allocation passes, and
the local EditMode/PlayMode suites. GitHub runs the engine-free build on every
push; Unity jobs run when the repository Unity license secret is configured —
see [`.github/workflows/onity-ci.yml`](https://github.com/furkantokkan/Onity/blob/main/.github/workflows/onity-ci.yml).

Advanced DI verification (`2026-09-23`): the full Onity EditMode suite passed
`464/464`; seven DI scenarios were measured in Editor/Mono and Windows IL2CPP,
with separate raw Profiler allocation passes. See the [run summary](../benchmarks/advanced-di-summary-2026-09-23.md).

- **DI benchmark results** — [`di-benchmark-summary.md`](https://github.com/furkantokkan/Onity/blob/main/Packages/com.onity.framework/Benchmarks/Results/di-benchmark-summary.md).

- **Unity 2022.3 LTS or newer** (the current benchmark numbers were captured on Unity 2022.3.62f3, Windows Editor/Mono and Windows IL2CPP Player).

## Moved from Packages/com.onity.framework/README.md

The UniTask Comparison Snapshot and Benchmark Snapshot sections of the package `README.md`:

## UniTask Comparison Snapshot

The 2026-10-02 Release Player gate (Unity 2022.3.62f2, Windows x64, UniTask 2.5.11,
three processes per backend and suite) measured OnityTask faster in all 29 gated
IL2CPP rows, with median Onity/UniTask time ratios from 0.085 to 0.808, at the
default `FlowExecutionContext = false` and with pool retention matched for the
1,024- and 4,096-operation bursts. It is a timing result for those workloads on
one machine, not an allocation or cross-platform claim. Reported limits:

- Mono: the four synchronous completed-result rows are 1.09x to 1.92x slower.
- Opt-in `AsyncLocal` flow: a complete method lifecycle with four suspensions is
  1.5x to 1.6x slower on IL2CPP and 2.6x to 2.7x on Mono.
- Default pool retention: 4,096-call bursts of one async method are up to 2.6x
  slower; raise `OnityTask.RunnerPoolCapacity` / `SourcePoolCapacity` for them.

Remaining API gaps against UniTask: timing and `cancelImmediately` overloads of the
Unity operation adapters, a native shareable `WhenAll` output,
`UniTaskSynchronizationContext`, index-only `SelectAwait` / `WhereAwait`, and an
integer-milliseconds `Delay`. The
[measured OnityTask comparison](https://furkantokkan.github.io/Onity/guide/onitytask-comparison.html)
records every row, the feature coverage and the evidence.

## Benchmark Snapshot

Latest published DI runs: Unity 2022.3.62f3, Windows, 512 warmup iterations,
8 measured samples, mean ns/op. See `Benchmarks/Results/di-benchmark-summary.md`
and `Benchmarks/Results/di-benchmark-player-latest.md` for full reports.

Editor / Mono (`2026-07-12T13:31:37Z`):

| Scenario | Onity Standard | Onity Baked | VContainer | Zenject | Lower ns/op vs VContainer |
| --- | ---: | ---: | ---: | ---: | ---: |
| Resolve Singleton | ~69 ns | ~78 ns | ~217 ns | ~2,778 ns | ~68% |
| Resolve Transient | ~1,030 ns | ~1,366 ns | ~2,352 ns | ~12,561 ns | ~56% |
| Resolve Combined | ~980 ns | ~875 ns | ~1,905 ns | ~14,382 ns | ~49% |
| Resolve Complex (6-level) | ~20,874 ns | ~20,828 ns | ~40,270 ns | ~281,814 ns | ~48% |
| Prepare & Register Complex | ~40,613 ns | ~54,996 ns | ~139,246 ns | ~188,865 ns | ~71% |

Windows IL2CPP Player (`2026-07-12T13:34:55Z`, source-generated activators, 10,000 iterations):

| Scenario | Onity Standard | Onity Baked | VContainer | Zenject | Lower ns/op vs VContainer |
| --- | ---: | ---: | ---: | ---: | ---: |
| Resolve Singleton | ~18 ns | ~18 ns | ~95 ns | ~449 ns | ~82% |
| Resolve Transient | ~159 ns | ~191 ns | ~541 ns | ~2,448 ns | ~71% |
| Resolve Combined | ~176 ns | ~196 ns | ~612 ns | ~3,080 ns | ~71% |
| Resolve Complex (6-level) | ~5,107 ns | ~5,071 ns | ~12,475 ns | ~59,327 ns | ~59% |
| Prepare & Register Complex | ~21,128 ns | ~24,490 ns | ~34,888 ns | ~59,567 ns | ~39% |

Timing numbers are from a Windows PC and are indicative, not a guarantee. The
committed allocation columns are withdrawn until the allocation harness is
corrected, because the earlier Editor harness reported 0 B for every container.
Onity is ahead on every measured Editor/Mono and Windows IL2CPP player timing
path in the current benchmark reports. The raw `Onity (Reflection)` label is the
standard dense-provider lane; reflection is only its activation fallback. A
focused 1000-sample IL2CPP singleton gate measured standard Onity at `18.80 ns/op`
versus VContainer at `94.39 ns/op`. Source-generated activators keep hot
IL2CPP construction away from `ConstructorInfo.Invoke` on AOT builds.

## Moved from docs/index.md

The Performance snapshot section of the documentation home page:

## Performance snapshot

In the focused Windows IL2CPP singleton gate (Unity 2022.3.62f3, 1000
samples × 10,000 resolves), Onity standard measured `18.80 ns/op` and
VContainer measured `94.39 ns/op`: approximately **80.1% lower resolve time**
in that run. Results are indicative, not a guarantee; see
[Performance & IL2CPP](../guide/performance-and-il2cpp.html) for the setup and
caveats.

In the 2026-10-02 Release Player gate (Unity 2022.3.62f2, Windows x64, three
processes per backend and suite), OnityTask was faster than UniTask 2.5.11 in all
29 gated IL2CPP rows, with median Onity/UniTask time ratios from 0.085 to 0.808,
at Onity's default context flow and with pool retention matched for the large
bursts. Mono, opt-in flow and default-retention results are in the
[comparison](../guide/onitytask-comparison.html).

## Moved from docs/guide/performance-and-il2cpp.md

The Timing claims and Managed pooling and factory timing sections of the Performance and IL2CPP guide:

## Timing claims

The committed DI benchmark reports resolve **timing** (speed) numbers. Treat them as **indicative only**: they were measured on a Windows PC and are not a guaranteed result for every Unity version, scripting backend, or graph shape. They are useful for relative comparison of resolve paths within the same run, not as an absolute performance guarantee.

The current Editor/Mono and Windows IL2CPP reports include keyed and scoped
singleton resolution along with the five original scenarios. The Editor's
cumulative allocation byte counters failed a 1 MiB positive control, so the
timing report marks inline bytes unavailable. Separate raw Profiler passes
validated empty, 1 MiB, and 2 MiB controls before measuring `GC.Alloc` inside
each marker. [Full run summary and raw reports](../benchmarks/advanced-di-summary-2026-09-23.md).

| Environment | Result |
| --- | --- |
| Unity Editor / Mono (`2026-09-23`) | Onity baked was faster than VContainer and Zenject in all seven measured scenarios. [Raw timing report](../benchmarks/advanced-di-editor-mono-direct-keyed/di-benchmark-latest.json). |
| Windows IL2CPP release Player (`2026-09-23`) | Onity baked was faster than VContainer and Zenject in all seven measured scenarios with `19` generated activators registered. [Raw timing report](../benchmarks/advanced-di-il2cpp-release.json). |
| Windows IL2CPP release Player re-run (`2026-10-01`, three processes) | Onity baked was fastest in every scenario in every process; Onity/VContainer per-run ratios ranged from 0.23x to 0.57x. [Re-measurement](../benchmarks/remeasure-2026-10-01.md). |
| Windows IL2CPP singleton gate (`2026-07-12T13:30:25Z`) | 1000 samples measured Onity standard at 18.80 ns/op versus VContainer at 94.39 ns/op. |

In the Editor/Mono allocation pass, Onity baked and VContainer both used
0, 16, 16, and 384 B per operation for singleton, transient, combined, and
complex resolves. Keyed and scoped singleton resolves used 0 B in all three
frameworks. Onity baked used 10,364 B for complex prepare/register versus
VContainer's 15,296 B and Zenject's 23,166 B.

A separate Windows IL2CPP Development player raw Profiler pass validated an
empty marker at 0 B and 1 MiB / 2 MiB controls at 1,053,728 B / 2,102,304 B.
Each case used three identical samples of 64 operations after warming the same
container. Onity baked and VContainer both used 0, 16, 16, and 384 B per
resolve operation; Zenject used 0, 200, 200, and 4,800 B. Keyed and scoped
singleton resolves used 0 B in all three frameworks. Complex prepare/register
used 10,424 B for Onity baked, 16,136 B for VContainer, and 23,405 B for
Zenject. [Raw IL2CPP byte report](../benchmarks/advanced-di-il2cpp-allocation.json).
Development profiling supplies allocation bytes; the release player supplies
the timing comparison. The release report's allocation fields are unavailable.

## Managed pooling and factory timing

A separate Windows IL2CPP benchmark compared Onity's checked two-parameter
pooled factory with Zenject `MemoryPool<int,int,int[]>` using 32 prewarmed
items, fixed capacity, matching initialization/reset callbacks, and rotated
bursts. In an interleaved Release confirmation, Onity/Zenject paired median
ratios were **0.803x** and **0.776x**, or **19.7%** and **22.4%** less elapsed
time in that workload. Development player runs recorded zero warmed
`GC.Alloc` events with a positive control; Release allocation was unavailable.
Both pools rejected duplicate returns and passed the final-state checks.

`OnityObjectPool<T>` keeps its own array stack instead of wrapping Unity's
`ObjectPool<T>`; the top of the stack is the checked-mode duplicate-return fast
slot. In the checked single-item Editor/Mono test, which previously favored
Zenject (**1.429x/1.566x**), two fresh runs measured Onity checked/Zenject
paired medians of **0.590x/0.579x** and Onity default/Zenject **0.674x/0.559x**,
with zero warmed allocation. In an old/new/new/old sequence of the 32-item
IL2CPP Release burst, factory/Zenject moved from **0.796x/0.856x** to
**0.738x/0.729x**. The Unity player control does not check duplicate returns and
is not an equivalent checked comparison. These results do not rank prefab
pools, VContainer pool adapters, or other workloads.
[Method, raw reports, and binary hashes](../benchmarks/factory-pooling-2026-09-23.md);
[own-stack pool measurements](../benchmarks/pool-own-stack-2026-10-01.md).

## Moved from docs/Architecture-Review.md

The Verification snapshot section of the Architecture page, which described the v0.3.10 release:

## Verification snapshot

The `v0.3.10` release candidate was verified on Unity `2022.3.62f3` with:

- 476 passing EditMode tests and 21 passing PlayMode tests;
- engine-free core, analyzer, and source-generator Release builds with zero warnings;
- metadata coverage for all 234 package files.

The Editor/Mono, Windows IL2CPP, and focused 1000-sample singleton DI benchmark
results published with `v0.3.6` remain historical measurements; they were not
rerun for `v0.3.10`. Exact counts are release evidence, not a permanent promise;
use the current CI and Unity Test Runner results for later versions.
