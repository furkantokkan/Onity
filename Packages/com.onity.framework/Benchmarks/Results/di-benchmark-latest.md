# Onity DI Benchmark

- Generated (UTC): `2026-05-30T18:32:48Z`
- Unity: `2022.3.62f3`
- Platform: `WindowsEditor`
- Samples per case: `8`
- Warmup iterations: `512`

| Scenario | Container | Mean (ms) | ns/op | Alloc/sample (B) | Alloc/op (B) |
|---|---|---:|---:|---:|---:|
| Resolve (Singleton) | Onity (Reflection) | 2.1494 | 214.94 | 0.00 | 0.000000 |
| Resolve (Singleton) | Onity (Baked) | 0.9412 | 94.12 | 0.00 | 0.000000 |
| Resolve (Singleton) | VContainer | 2.0212 | 202.12 | 0.00 | 0.000000 |
| Resolve (Singleton) | Zenject | 31.3679 | 3136.79 | 0.00 | 0.000000 |
| Resolve (Transient) | Onity (Reflection) | 17.2534 | 1725.34 | 0.00 | 0.000000 |
| Resolve (Transient) | Onity (Baked) | 7.7509 | 775.09 | 0.00 | 0.000000 |
| Resolve (Transient) | VContainer | 16.9687 | 1696.87 | 0.00 | 0.000000 |
| Resolve (Transient) | Zenject | 116.8100 | 11681.00 | 0.00 | 0.000000 |
| Resolve (Combined) | Onity (Reflection) | 10.5864 | 1058.64 | 0.00 | 0.000000 |
| Resolve (Combined) | Onity (Baked) | 8.9640 | 896.40 | 0.00 | 0.000000 |
| Resolve (Combined) | VContainer | 17.1201 | 1712.01 | 0.00 | 0.000000 |
| Resolve (Combined) | Zenject | 153.9986 | 15399.86 | 0.00 | 0.000000 |
| Resolve (Complex) | Onity (Reflection) | 265.0232 | 26502.32 | 0.00 | 0.000000 |
| Resolve (Complex) | Onity (Baked) | 227.8668 | 22786.68 | 0.00 | 0.000000 |
| Resolve (Complex) | VContainer | 579.9535 | 57995.35 | 0.00 | 0.000000 |
| Resolve (Complex) | Zenject | 2853.9380 | 285393.80 | 0.00 | 0.000000 |
| Prepare & Register (Complex) | Onity (Reflection) | 358.3679 | 35836.79 | 0.00 | 0.000000 |
| Prepare & Register (Complex) | Onity (Baked) | 472.4312 | 47243.12 | 0.00 | 0.000000 |
| Prepare & Register (Complex) | VContainer | 1351.4018 | 135140.18 | 0.00 | 0.000000 |
| Prepare & Register (Complex) | Zenject | 1971.3162 | 197131.62 | 0.00 | 0.000000 |
