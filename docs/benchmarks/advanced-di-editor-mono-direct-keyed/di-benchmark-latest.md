# Onity DI Benchmark

- Generated (UTC): `2026-09-23T18:20:39Z`
- Unity: `2022.3.62f3`
- Platform: `WindowsEditor`
- Samples per case: `8`
- Warmup iterations: `512`
- Allocation counter: `Unavailable`
- Allocation values: unavailable on this runtime; timing results only.

| Scenario | Container | Mean (ms) | ns/op | Alloc/sample (B) | Alloc/op (B) |
|---|---|---:|---:|---:|---:|
| Resolve (Singleton) | Onity (Reflection) | 2.2163 | 221.63 | N/A | N/A |
| Resolve (Singleton) | Onity (Baked) | 1.0606 | 106.06 | N/A | N/A |
| Resolve (Singleton) | VContainer | 2.1046 | 210.46 | N/A | N/A |
| Resolve (Singleton) | Zenject | 27.7347 | 2773.47 | N/A | N/A |
| Resolve (Transient) | Onity (Reflection) | 7.6099 | 760.99 | N/A | N/A |
| Resolve (Transient) | Onity (Baked) | 6.8883 | 688.83 | N/A | N/A |
| Resolve (Transient) | VContainer | 18.7153 | 1871.53 | N/A | N/A |
| Resolve (Transient) | Zenject | 117.8463 | 11784.63 | N/A | N/A |
| Resolve (Combined) | Onity (Reflection) | 8.6764 | 867.64 | N/A | N/A |
| Resolve (Combined) | Onity (Baked) | 5.4720 | 547.20 | N/A | N/A |
| Resolve (Combined) | VContainer | 18.8586 | 1885.86 | N/A | N/A |
| Resolve (Combined) | Zenject | 139.0010 | 13900.10 | N/A | N/A |
| Resolve (Complex) | Onity (Reflection) | 124.6445 | 12464.45 | N/A | N/A |
| Resolve (Complex) | Onity (Baked) | 124.6522 | 12465.22 | N/A | N/A |
| Resolve (Complex) | VContainer | 376.3269 | 37632.69 | N/A | N/A |
| Resolve (Complex) | Zenject | 2724.1766 | 272417.66 | N/A | N/A |
| Prepare & Register (Complex) | Onity (Reflection) | 384.5108 | 38451.08 | N/A | N/A |
| Prepare & Register (Complex) | Onity (Baked) | 532.8353 | 53283.53 | N/A | N/A |
| Prepare & Register (Complex) | VContainer | 1531.3286 | 153132.86 | N/A | N/A |
| Prepare & Register (Complex) | Zenject | 1839.9596 | 183995.96 | N/A | N/A |
| Resolve (Keyed Singleton) | Onity (Reflection) | 1.3847 | 138.47 | N/A | N/A |
| Resolve (Keyed Singleton) | Onity (Baked) | 1.3955 | 139.55 | N/A | N/A |
| Resolve (Keyed Singleton) | VContainer | 2.5202 | 252.02 | N/A | N/A |
| Resolve (Keyed Singleton) | Zenject | 29.5108 | 2951.08 | N/A | N/A |
| Resolve (Scoped Singleton) | Onity (Reflection) | 4.3493 | 434.93 | N/A | N/A |
| Resolve (Scoped Singleton) | Onity (Baked) | 1.6632 | 166.32 | N/A | N/A |
| Resolve (Scoped Singleton) | VContainer | 2.9969 | 299.69 | N/A | N/A |
| Resolve (Scoped Singleton) | Zenject | 28.1916 | 2819.16 | N/A | N/A |
