# Onity DI Benchmark

- Generated (UTC): `2026-09-23T18:05:18Z`
- Unity: `2022.3.62f3`
- Platform: `WindowsEditor`
- Samples per case: `8`
- Warmup iterations: `512`
- Allocation counter: `Unavailable`
- Allocation values: unavailable on this runtime; timing results only.

| Scenario | Container | Mean (ms) | ns/op | Alloc/sample (B) | Alloc/op (B) |
|---|---|---:|---:|---:|---:|
| Resolve (Singleton) | Onity (Reflection) | 2.2857 | 228.57 | N/A | N/A |
| Resolve (Singleton) | Onity (Baked) | 0.8561 | 85.61 | N/A | N/A |
| Resolve (Singleton) | VContainer | 1.8092 | 180.92 | N/A | N/A |
| Resolve (Singleton) | Zenject | 29.2205 | 2922.05 | N/A | N/A |
| Resolve (Transient) | Onity (Reflection) | 8.1245 | 812.45 | N/A | N/A |
| Resolve (Transient) | Onity (Baked) | 6.8678 | 686.78 | N/A | N/A |
| Resolve (Transient) | VContainer | 18.9831 | 1898.31 | N/A | N/A |
| Resolve (Transient) | Zenject | 126.6357 | 12663.57 | N/A | N/A |
| Resolve (Combined) | Onity (Reflection) | 9.5497 | 954.97 | N/A | N/A |
| Resolve (Combined) | Onity (Baked) | 6.4036 | 640.36 | N/A | N/A |
| Resolve (Combined) | VContainer | 17.4405 | 1744.05 | N/A | N/A |
| Resolve (Combined) | Zenject | 135.4141 | 13541.41 | N/A | N/A |
| Resolve (Complex) | Onity (Reflection) | 126.3023 | 12630.23 | N/A | N/A |
| Resolve (Complex) | Onity (Baked) | 122.9806 | 12298.06 | N/A | N/A |
| Resolve (Complex) | VContainer | 379.9098 | 37990.98 | N/A | N/A |
| Resolve (Complex) | Zenject | 2742.6226 | 274262.26 | N/A | N/A |
| Prepare & Register (Complex) | Onity (Reflection) | 358.7005 | 35870.05 | N/A | N/A |
| Prepare & Register (Complex) | Onity (Baked) | 475.3388 | 47533.88 | N/A | N/A |
| Prepare & Register (Complex) | VContainer | 1342.9872 | 134298.72 | N/A | N/A |
| Prepare & Register (Complex) | Zenject | 1733.8369 | 173383.69 | N/A | N/A |
| Resolve (Keyed Singleton) | Onity (Reflection) | 2.6580 | 265.80 | N/A | N/A |
| Resolve (Keyed Singleton) | Onity (Baked) | 2.4589 | 245.89 | N/A | N/A |
| Resolve (Keyed Singleton) | VContainer | 2.3617 | 236.17 | N/A | N/A |
| Resolve (Keyed Singleton) | Zenject | 28.6692 | 2866.92 | N/A | N/A |
| Resolve (Scoped Singleton) | Onity (Reflection) | 4.0757 | 407.57 | N/A | N/A |
| Resolve (Scoped Singleton) | Onity (Baked) | 1.5484 | 154.84 | N/A | N/A |
| Resolve (Scoped Singleton) | VContainer | 2.6489 | 264.89 | N/A | N/A |
| Resolve (Scoped Singleton) | Zenject | 28.2779 | 2827.79 | N/A | N/A |
