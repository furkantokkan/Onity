# Re-measurement at commit 7266dd8 (2026-10-01)

- Purpose: confirm the September 23–24 numbers against the committed source (`7266dd8`).
- Editor: **Unity 2022.3.62f2**. The project pins 62f3, which is no longer installed on this machine, so this is not an identical environment to the earlier reports.
- `ONITY_BENCHMARKS` was added to the Standalone defines for the run only. `ProjectSettings.asset` (SHA-256 `93CC8177…E99F8`) and `ProjectVersion.txt` were restored byte for byte afterwards, with no Git diff.
- The machine was not idle: two other Unity projects had Editors open. Absolute times are therefore less reliable than ratios measured inside the same process.
- Correctness tests at this revision: EditMode 480 passed (3 explicit benchmarks skipped), PlayMode 4 passed (1 explicit skipped), analyzer/SourceGen 27 passed.

## DI — Windows IL2CPP Release player (one binary, three fresh processes)

Each process: 512 warmups, 8 samples of 10,000 operations, mean ns/op. The table shows the median of the three processes. 19 generated activators were registered in every run.

| Scenario | Onity Baked | VContainer | Zenject | Onity/VContainer (per run) | 2026-09-24 Onity / VContainer |
| --- | ---: | ---: | ---: | --- | ---: |
| Singleton | 20.0 | 75.7 | 456 | 0.26 / 0.26 / 0.26 | 21.5 / 78.9 |
| Transient | 120.0 | 496.8 | 2,387 | 0.23 / 0.24 / 0.25 | 109.3 / 467.6 |
| Combined | 149.4 | 571.3 | 3,083 | 0.29 / 0.24 / 0.26 | 129.5 / 566.9 |
| Complex graph | 4,172 | 12,864 | 62,027 | 0.34 / 0.32 / 0.32 | 3,904 / 12,099 |
| Prepare and register complex | 23,394 | 45,224 | 61,425 | 0.55 / 0.57 / 0.52 | 28,776 / 37,518 |
| Keyed singleton | 36.5 | 84.0 | 595 | 0.49 / 0.39 / 0.40 | 37.0 / 93.2 |
| Scoped singleton | 50.5 | 108.9 | 586 | 0.48 / 0.46 / 0.45 | 50.7 / 116.7 |

Onity Baked was fastest in every scenario in all three processes. Allocation is unavailable in a Release player and is not claimed here. GameAssembly SHA-256: `9bc025c2…1a482c`.

## DI — Editor/Mono

- **Full comparison** (8 samples, mean): Onity Baked was faster than VContainer and Zenject in all seven scenarios. Examples: singleton 110 vs 186 vs 2,761 ns/op; complex graph 21,446 vs 63,779 vs 540,927 ns/op.
- **Focused hot paths** (16 interleaved samples): medians were singleton 73.4, transient 516, combined 589, and complex 13,645 ns/op. The complex median is within about 10% of the 2026-09-24 focused medians of 12,268 and 12,710.
- **`GC.Alloc` events per operation** (current-thread recorder; the empty control recorded 0 events and the 1 MiB control recorded 129). The counts match 2026-09-23 exactly:
  - Singleton, keyed, and scoped: 0 for every container.
  - Transient and combined: 1 for Onity and VContainer, 4 for Zenject.
  - Complex graph: 24 for Onity and VContainer, 96 for Zenject.
  - Prepare and register: Onity Baked 109, VContainer 238, Zenject 419.

## Pooling — 32-item two-parameter burst vs Zenject `MemoryPool` (IL2CPP)

Fresh builds of the same scenario and gates as 2026-09-24: 2,000 warmup bursts and 12 rotated samples of 20,000 bursts per arm. Every run passed the distinctness, parameter, capacity, duplicate-return, reset, checksum (`31,896,501,574,144`), and final-state (0 active, 32 inactive) checks.

| Build | Run | Factory/Zenject median (Q1–Q3) | Direct/Zenject median | Gate (median ≤ 0.95, Q3 < 1.0) |
| --- | ---: | --- | ---: | --- |
| Release | 1 | 0.809 (0.780–0.871) | 0.841 | pass |
| Release | 2 | 0.801 (0.756–0.890) | 0.859 | pass |
| Development | 1 | 0.853 (0.832–0.866) | 0.829 | pass |
| Development | 2 | 0.840 (0.777–0.854) | 0.810 | pass |

- Development runs: the positive control recorded 64 `GC.Alloc` events, and every warmed arm recorded 0.
- Factory/Zenject: 2026-09-24 confirmed Release ratios were 0.803 and 0.776. Today's Release ratios (0.809 and 0.801) are in the same range.
- Absolute times were higher on this busier machine. In Development run 1, Onity factory took 2,365 ns per burst and Zenject 2,720, compared with 1,954 and 2,439 on 2026-09-23.
- Binary hashes:
  - Release `OnityPoolBenchmark.exe` SHA-256 `5dc4ac36…ad840a`, `GameAssembly.dll` `e86359c1…142377`.
  - Development `GameAssembly.dll` `34888a45…df8a99`.

## Pooling — Editor/Mono single item

The explicit EditMode run (11 interleaved samples of 250,000 rent/return pairs) recorded 0 warmed allocation events in every path:

| Comparison | Paired median | 2026-09-24 interleaved candidate runs |
| --- | ---: | ---: |
| Onity checked / Zenject | 1.48x | 1.43x and 1.57x |
| Onity default / Zenject | 1.17x | — |
| Onity checked / Unity checked + atomics | 1.66x | — |

This is the known weak case. A single repeatedly reused item in Editor/Mono is slower in Onity than in Zenject and Unity, and nothing here changes that.

Raw reports: `di-remeasure-*-2026-10-01.json`, `pool-remeasure-*-2026-10-01.json`, and `pool-remeasure-editor-2026-10-01.xml` in this directory.
