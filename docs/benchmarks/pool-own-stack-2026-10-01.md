# OnityObjectPool own-stack change (2026-10-01)

- **Change:** `OnityObjectPool<T>` now keeps inactive items in its own array stack, with source in commit `e1d1b36`. Before, it wrapped Unity's `ObjectPool<T>`. In checked mode, the top of the stack serves as the duplicate-return fast slot. Repeated one-item rent/return therefore needs one reference comparison, with no hash-set work and no release-callback delegate.
- **Environment:** Unity 2022.3.62f2 on Windows. Other Unity Editors were open on the machine, so absolute times are noisy. The ratios below are paired within one process.
- **Correctness:** EditMode 481 passed (3 explicit benchmarks skipped) and PlayMode 4 passed. A new test covers the changed `Clear` accounting: only destroyed inactive items are subtracted from `CountAll`.

## Editor/Mono, single reused item (explicit EditMode benchmarks, 11 interleaved samples)

| Paired median | Before (2026-10-01, same day) | After, run 1 | After, run 2 |
| --- | ---: | ---: | ---: |
| Onity checked / Zenject `MemoryPool` | 1.483x | **0.590x** | **0.579x** |
| Onity default / Zenject `MemoryPool` | 1.170x | **0.674x** | **0.559x** |
| Onity checked / Unity checked + atomics | 1.661x | **0.806x** | **0.717x** |
| Onity / raw Unity pool | 1.30x | **0.83x** | **0.81x** |

Every path recorded zero warmed `GC.Alloc` events per 64 pairs. The positive allocation control was valid.

## IL2CPP Release, 32-item two-parameter burst vs Zenject

The same 4-arm scenario and gates as the 2026-09-24 report: 2,000 warmup bursts and 12 rotated samples of 20,000 bursts. The run order was old binary, new binary, new binary, old binary.

| Run | Factory/Zenject median (Q1–Q3) | Direct/Zenject median |
| --- | --- | ---: |
| old 1 | 0.796 (0.759–0.903) | 0.868 |
| new 1 | **0.738** (0.692–0.775) | 0.834 |
| new 2 | **0.729** (0.667–0.783) | 0.679 |
| old 2 | 0.856 (0.822–0.880) | 0.859 |

All four runs passed the distinctness, parameter, capacity, duplicate-return, reset, checksum, and final-state checks. GameAssembly SHA-256 prefixes: old `e86359c174e1a22d`, new `68c6c900901e5073`.

Raw reports: `pool-own-stack-editor-{1,2}-2026-10-01.xml` and `pool-own-stack-il2cpp-release-{old,new}-{1,2}-2026-10-01.json`.
