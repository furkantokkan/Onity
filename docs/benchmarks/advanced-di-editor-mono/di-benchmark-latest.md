# Onity DI Benchmark

- Generated (UTC): `2026-09-23T17:56:24Z`
- Unity: `2022.3.62f3`
- Platform: `WindowsEditor`
- Samples per case: `8`
- Warmup iterations: `512`
- Allocation counter: `Unavailable`
- Allocation values: unavailable on this runtime; timing results only.

| Scenario | Container | Mean (ms) | ns/op | Alloc/sample (B) | Alloc/op (B) |
|---|---|---:|---:|---:|---:|
| Resolve (Singleton) | Onity (Reflection) | 2.3429 | 234.29 | N/A | N/A |
| Resolve (Singleton) | Onity (Baked) | 0.9385 | 93.85 | N/A | N/A |
| Resolve (Singleton) | VContainer | 2.1750 | 217.50 | N/A | N/A |
| Resolve (Singleton) | Zenject | 27.0088 | 2700.88 | N/A | N/A |
| Resolve (Transient) | Onity (Reflection) | 6.6180 | 661.80 | N/A | N/A |
| Resolve (Transient) | Onity (Baked) | 4.9770 | 497.70 | N/A | N/A |
| Resolve (Transient) | VContainer | 16.2019 | 1620.19 | N/A | N/A |
| Resolve (Transient) | Zenject | 117.0850 | 11708.50 | N/A | N/A |
| Resolve (Combined) | Onity (Reflection) | 8.7313 | 873.13 | N/A | N/A |
| Resolve (Combined) | Onity (Baked) | 6.0319 | 603.19 | N/A | N/A |
| Resolve (Combined) | VContainer | 21.3894 | 2138.94 | N/A | N/A |
| Resolve (Combined) | Zenject | 162.0224 | 16202.24 | N/A | N/A |
| Resolve (Complex) | Onity (Reflection) | 139.5982 | 13959.82 | N/A | N/A |
| Resolve (Complex) | Onity (Baked) | 143.5932 | 14359.32 | N/A | N/A |
| Resolve (Complex) | VContainer | 391.3610 | 39136.10 | N/A | N/A |
| Resolve (Complex) | Zenject | 3191.4807 | 319148.07 | N/A | N/A |
| Prepare & Register (Complex) | Onity (Reflection) | 486.8446 | 48684.46 | N/A | N/A |
| Prepare & Register (Complex) | Onity (Baked) | 604.2030 | 60420.30 | N/A | N/A |
| Prepare & Register (Complex) | VContainer | 1524.6161 | 152461.61 | N/A | N/A |
| Prepare & Register (Complex) | Zenject | 2004.9178 | 200491.78 | N/A | N/A |
| Resolve (Keyed Singleton) | Onity (Reflection) | 4.2072 | 420.72 | N/A | N/A |
| Resolve (Keyed Singleton) | Onity (Baked) | 4.0257 | 402.57 | N/A | N/A |
| Resolve (Keyed Singleton) | VContainer | 2.6923 | 269.23 | N/A | N/A |
| Resolve (Keyed Singleton) | Zenject | 31.0079 | 3100.79 | N/A | N/A |
| Resolve (Scoped Singleton) | Onity (Reflection) | 4.2239 | 422.39 | N/A | N/A |
| Resolve (Scoped Singleton) | Onity (Baked) | 6.6773 | 667.73 | N/A | N/A |
| Resolve (Scoped Singleton) | VContainer | 3.3687 | 336.87 | N/A | N/A |
| Resolve (Scoped Singleton) | Zenject | 32.3442 | 3234.42 | N/A | N/A |
