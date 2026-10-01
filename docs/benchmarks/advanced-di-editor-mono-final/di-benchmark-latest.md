# Onity DI Benchmark

- Generated (UTC): `2026-09-23T18:12:47Z`
- Unity: `2022.3.62f3`
- Platform: `WindowsEditor`
- Samples per case: `8`
- Warmup iterations: `512`
- Allocation counter: `Unavailable`
- Allocation values: unavailable on this runtime; timing results only.

| Scenario | Container | Mean (ms) | ns/op | Alloc/sample (B) | Alloc/op (B) |
|---|---|---:|---:|---:|---:|
| Resolve (Singleton) | Onity (Reflection) | 2.5889 | 258.89 | N/A | N/A |
| Resolve (Singleton) | Onity (Baked) | 0.7828 | 78.28 | N/A | N/A |
| Resolve (Singleton) | VContainer | 1.6215 | 162.15 | N/A | N/A |
| Resolve (Singleton) | Zenject | 24.5526 | 2455.26 | N/A | N/A |
| Resolve (Transient) | Onity (Reflection) | 6.1592 | 615.92 | N/A | N/A |
| Resolve (Transient) | Onity (Baked) | 5.3817 | 538.17 | N/A | N/A |
| Resolve (Transient) | VContainer | 16.2872 | 1628.72 | N/A | N/A |
| Resolve (Transient) | Zenject | 118.2810 | 11828.10 | N/A | N/A |
| Resolve (Combined) | Onity (Reflection) | 9.0009 | 900.09 | N/A | N/A |
| Resolve (Combined) | Onity (Baked) | 8.0127 | 801.27 | N/A | N/A |
| Resolve (Combined) | VContainer | 17.8602 | 1786.02 | N/A | N/A |
| Resolve (Combined) | Zenject | 151.1993 | 15119.93 | N/A | N/A |
| Resolve (Complex) | Onity (Reflection) | 139.0873 | 13908.73 | N/A | N/A |
| Resolve (Complex) | Onity (Baked) | 124.6102 | 12461.02 | N/A | N/A |
| Resolve (Complex) | VContainer | 369.4559 | 36945.59 | N/A | N/A |
| Resolve (Complex) | Zenject | 2685.7602 | 268576.02 | N/A | N/A |
| Prepare & Register (Complex) | Onity (Reflection) | 383.6863 | 38368.63 | N/A | N/A |
| Prepare & Register (Complex) | Onity (Baked) | 521.6055 | 52160.55 | N/A | N/A |
| Prepare & Register (Complex) | VContainer | 1462.7992 | 146279.92 | N/A | N/A |
| Prepare & Register (Complex) | Zenject | 1905.2839 | 190528.39 | N/A | N/A |
| Resolve (Keyed Singleton) | Onity (Reflection) | 2.3248 | 232.48 | N/A | N/A |
| Resolve (Keyed Singleton) | Onity (Baked) | 2.8506 | 285.06 | N/A | N/A |
| Resolve (Keyed Singleton) | VContainer | 2.2869 | 228.69 | N/A | N/A |
| Resolve (Keyed Singleton) | Zenject | 29.7420 | 2974.20 | N/A | N/A |
| Resolve (Scoped Singleton) | Onity (Reflection) | 4.1914 | 419.14 | N/A | N/A |
| Resolve (Scoped Singleton) | Onity (Baked) | 1.6248 | 162.48 | N/A | N/A |
| Resolve (Scoped Singleton) | VContainer | 2.5333 | 253.33 | N/A | N/A |
| Resolve (Scoped Singleton) | Zenject | 33.2344 | 3323.44 | N/A | N/A |
