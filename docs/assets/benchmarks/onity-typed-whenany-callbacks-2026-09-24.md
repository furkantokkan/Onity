# Typed WhenAny callback benchmark — 2026-09-24

## Method and provenance

- Baseline product commit: `37c309a950b2142c91eeb62ad12f0a8a585b1d76`; tested benchmark checkout: `03a9b5c6baa94876de43ae672d9d5e565599f993`.
- Candidate product commit: `d66946ffd663fe2adbcb7d9b6040a2df55410542`; tested benchmark checkout: `10b48d262bad6e77a4d2c00e76d312bf008ab364`.
- UniTask pin: `2e993ff18f28c931602a07292df0b0804eebef99`. The two benchmark checkouts contain the same runner, menu, completion source, asmdefs, manifest, lock, and main ProjectSettings file. Only the measured `OnityAsync.cs` source differs.
- Windows Unity `2022.3.62f3` Editor/Mono; separate batch-mode Play Mode processes, no `-quit`. Each typed invocation supplied `-onityTaskProductCommit` with its full product SHA. Timing ran B1, C1, C2, B2; calibrated Profiler allocation passes ran C1, B1, B2, C2 against their respective timing reports. After those allocation gates passed, a third timing pair B3, C3 was run to resolve the small, overlapping pending-success scheduling difference in B2/C2. No additional samples were taken.
- Every timing report contains 12 scenarios (six cases with Onity tracking on/off), three routes, and eight samples per route/case. Each scenario/route gets 10 warmup batches; each timing sample contains 16 success or two failure batches of 128 operations. The native pending gate passed in every report. Native Onity versus UniTask's two-element `params` route is the primary comparison; the same-revision Task bridge is migration evidence.
- The Profiler measured main-thread `GC.Alloc` in independent processes. All four passes had a **65,568 B** positive control, **0 B** empty control, eight **0 B** empty-harness samples, and eight allocation samples per route/case; each allocation sample measures 128 operations. Deep Profiling and allocation callstacks were off. Each Profiler pass preserved its paired timing samples and passed the runner's product, harness, configuration, marker, and main-thread validation.

| SHA-256 input | Baseline | Candidate |
|---|---|---|
| `OnityAsync.cs` | `bff09f17014f807f8ea6fb97422a74a0e9694acfb5c7177116152e6977707bfa` | `f276cc8e2205384ffd96551007cd3de26263ad394cee5847308d3fb25d679432` |
| `OnityTaskCompletionSource.cs` | `7f07b40fce20fa18774e3f23a1a8936a4a96fa1e5c97f5212523c36ddddf334e` | Same |
| Benchmark runner / menu | `8accd2814e4fe64bb5041299a9f6093e7e2a103f24fa88c73318cb2543da5591` / `ca3a4addb2dfcc6e6c72b522215df6c7bf266eb5b145111cbde0221ca0193d28` | Same |
| Runtime / editor benchmark asmdefs | `e9b85c980c080723b2b996ecc4bbe523ecdc98c15dff990a21aba5c080aede67` / `3d0c84d3ce30ef56c787d2afe1d03d2d75c65b5176daf02d865f9f914bc3642` | Same |
| Manifest / lock / `ProjectSettings.asset` | `d9acf452b0c43b069cf445ad53f306231e6b9aad0742e7f2920d93fa2eba39ee` / `fc664130b065de661089068029aeba62148cfd8ccbb8b78efb59f97de5ddcc9b` / `50cc5d857d7009a3f3597f20a012ac28ff8f9a7b0cdcd2137f9418e407330582` | Same |

## Results

Native Onity bytes per operation were identical in both Profiler repetitions and with tracking on/off. Timing values below are median ns per operation from the two prepared-host timing pairs; the first-import pair remains in the raw evidence.

| Tracker | Scenario | Baseline B/op → candidate B/op | B2 → C2 ns/op | B3 → C3 ns/op |
|---|---|---:|---:|---:|
| On | Pending success schedule | 424 → 408 | 725.0 → 739.0 | 702.8 → 696.6 |
| On | Pending success lifecycle | 424 → 408 | 2024.5 → 2042.8 | 1985.5 → 2116.7 |
| On | Pending fault lifecycle | 936 → 920 | 11857.8 → 12135.0 | 11757.4 → 13070.3 |
| On | Pending cancel lifecycle | 936 → 920 | 9877.1 → 10862.5 | 10325.2 → 10299.6 |
| On | Both completed, first wins | 424 → 152 | 1626.3 → 1349.3 | 1512.5 → 1313.6 |
| On | Second completed first | 424 → 280 | 1806.3 → 1743.0 | 1786.9 → 1686.6 |
| Off | Pending success schedule | 424 → 408 | 719.2 → 736.0 | 717.3 → 670.7 |
| Off | Pending success lifecycle | 424 → 408 | 2043.1 → 2036.5 | 1977.6 → 1969.4 |
| Off | Pending fault lifecycle | 936 → 920 | 11654.1 → 12528.9 | 12113.1 → 11952.1 |
| Off | Pending cancel lifecycle | 936 → 920 | 10820.5 → 11080.9 | 10060.0 → 10269.7 |
| Off | Both completed, first wins | 424 → 152 | 1609.9 → 1398.1 | 1464.2 → 1315.0 |
| Off | Second completed first | 424 → 280 | 1880.1 → 1818.2 | 1829.4 → 1669.3 |

The candidate passes the allocation gates: both-completed inputs are below 318 B/op, second-completed inputs allocate less than baseline, and pending success, fault, and cancellation do not increase. B2/C2 pending-success scheduling moved 725.0 → 739.0 ns/op with tracking on and 719.2 → 736.0 off; B3/C3 moved 702.8 → 696.6 on and 717.3 → 670.7 off. UniTask scheduling controls moved 833.0 → 823.2 and 806.2 → 802.5 on; 826.4 → 834.5 and 805.3 → 814.2 off. Sample ranges overlap, so there is no convincing repeatable scheduling regression relative to that control. Completed-input timing improves in both prepared-host pairs. This is a scoped callback comparison, not evidence of overall superiority over UniTask.

## Interpretation and raw reports

- B1/C1 were preserved as the first-import pair. Their timing medians vary by several-fold, including pending-success schedule with tracking on (B1 1318.6, C1 5511.3 ns/op); Unity rebuilt each worktree's Library. They were not rerun for a favorable result. The later B2/C2 and predeclared B3/C3 pairs used prepared hosts. A separate Coin Unity Editor and asset workers were present; host CPU load was approximately 10% before B3 and 23% between B3/C3. Absolute timing remains environment-sensitive.
- B1 and C1 logged successful benchmark completion and normal Unity shutdown, but their initial launch method did not retain an OS exit-code handle. B2, C2, B3, C3 and all four Profiler processes returned exit code 0. No run log contained compilation, test, or benchmark failure signatures.
- Pending scheduling excludes completion and consumption from its timing marker; lifecycle cases include winner/loser completion and observation. Completed-input controls include their documented preparation/loser boundaries. These Editor/Mono and main-thread measurements do not establish Player, IL2CPP, or worker-allocation behavior.
- [B1 timing](onity-typed-whenany-callbacks-2026-09-24/B1-timing.json), [C1 timing](onity-typed-whenany-callbacks-2026-09-24/C1-timing.json), [C2 timing](onity-typed-whenany-callbacks-2026-09-24/C2-timing.json), [B2 timing](onity-typed-whenany-callbacks-2026-09-24/B2-timing.json), [B3 timing](onity-typed-whenany-callbacks-2026-09-24/B3-timing.json), [C3 timing](onity-typed-whenany-callbacks-2026-09-24/C3-timing.json).
- [C1 Profiler](onity-typed-whenany-callbacks-2026-09-24/C1-profiler.json), [B1 Profiler](onity-typed-whenany-callbacks-2026-09-24/B1-profiler.json), [B2 Profiler](onity-typed-whenany-callbacks-2026-09-24/B2-profiler.json), [C2 Profiler](onity-typed-whenany-callbacks-2026-09-24/C2-profiler.json). Each Profiler report contains its original timing data plus the independent allocation pass; the timing-only files above preserve the pre-Profiler versions.
