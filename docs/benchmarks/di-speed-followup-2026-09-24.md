# DI speed follow-up (2026-09-24)

- Unity 2022.3.62f3, Windows, one machine. The fresh IL2CPP full comparison was built with a non-development Release setting and used 512 warmups and eight samples of 10,000 operations per case. The timing JSON identifies the IL2CPP backend but does not record the build configuration. The Onity path was baked and registered 19 generated activators. Resolve timing excludes registration and first activation; the prepare case includes registration and build.
- Full Release IL2CPP timing (ns/op):

| Scenario | Onity | VContainer | Zenject |
| --- | ---: | ---: | ---: |
| Singleton | 21.5 | 78.9 | 418.1 |
| Transient | 109.3 | 467.6 | 2,196.3 |
| Combined | 129.5 | 566.9 | 2,813.0 |
| Complex graph | 3,904.2 | 12,099.3 | 57,739.1 |
| Prepare and register complex graph | 28,775.5 | 37,517.9 | 67,447.7 |
| Keyed singleton | 37.0 | 93.2 | 544.1 |
| Scoped singleton | 50.7 | 116.7 | 552.5 |

- The separate September 23 Development profiler pass measured Onity baked allocations of 0 B/op for singleton, 16 B/op for transient and combined, and 384 B/op for the complex graph. Its empty control recorded zero, while nominal 1 MiB and 2 MiB controls recorded 1,053,728 and 2,102,304 bytes. The fresh timing report marks allocation unavailable; it does not establish zero allocation.
- Two unchanged-runtime full Editor runs were too variable for a small optimization decision: baked complex graph was 21,794 and 11,832 ns/op. A new focused harness reuses one warmed container per scenario, rotates four baked Onity cases through 16 rounds, records raw times, median, quartiles, and GC collection counts, and leaves the full comparison path unchanged.
- Focused Editor complex-graph medians were 12,710 and 12,268 ns/op; IQR/median was 5.8% and 5.6%. Focused IL2CPP complex-graph medians were 3,971 and 3,959 ns/op; IQR/median was 6.0% and 5.6%. The transient and combined IL2CPP cases, which allocated in the separate Development profile, had IQR/median between 5.7% and 8.9%. This misses the preselected 5% measurement-stability gate for deciding on a micro optimization.
- In both focused IL2CPP runs, singleton had no collections; every transient, combined, and complex sample had collections. Transient and combined samples had 2–3 collections, complex samples 5–6. Collection count alone did not consistently predict complex-graph time. GC was not disabled and samples were not discarded. No DI runtime optimization was retained from this experiment.
- The temporary Standalone `ONITY_BENCHMARKS` define was removed. `ProjectSettings.asset` returned to its original SHA-256 `93CC8177FCACAC2BF530346EF05658BF568483C0214EA9446AFA99C2E07E99F8` and has no Git diff.

Raw reports: [full IL2CPP](di-speed-il2cpp-full-2026-09-24.json), [IL2CPP hot run 1](di-speed-il2cpp-hot-1-2026-09-24.json), [IL2CPP hot run 2](di-speed-il2cpp-hot-2-2026-09-24.json), [Editor full run 1](di-speed-editor-full-1-2026-09-24.json), [Editor full run 2](di-speed-editor-full-2-2026-09-24.json), [Editor hot run 1](di-speed-editor-hot-1-2026-09-24.json), [Editor hot run 2](di-speed-editor-hot-2-2026-09-24.json), and [separate IL2CPP allocation profile](advanced-di-il2cpp-allocation.json).

These are local, self-run comparisons. The focused reports compare Onity before and after potential future changes; they do not compare competitors. The full runner measures frameworks in blocks, so external machine load can affect their relative timings. Pooling speed requires a separate matched Zenject comparison; VContainer has no built-in equivalent pool in this repository.
