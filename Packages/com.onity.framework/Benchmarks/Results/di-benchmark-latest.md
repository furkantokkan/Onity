# Onity DI Benchmark

- Generated (UTC): `2026-05-24T16:36:16Z`
- Unity: `2022.3.62f3`
- Platform: `WindowsEditor`
- Samples per case: `8`
- Warmup iterations: `512`

| Scenario | Container | Mean (ms) | ns/op | Alloc/sample (B) | Alloc/op (B) |
|---|---|---:|---:|---:|---:|
| Resolve (Singleton) | Onity | 1.5222 | 152.22 | 0.00 | 0.000000 |
| Resolve (Singleton) | VContainer | 1.9520 | 195.20 | 0.00 | 0.000000 |
| Resolve (Singleton) | Zenject | 23.2601 | 2326.01 | 0.00 | 0.000000 |
| Resolve (Transient) | Onity | 9.9570 | 995.70 | 0.00 | 0.000000 |
| Resolve (Transient) | VContainer | 14.2108 | 1421.08 | 0.00 | 0.000000 |
| Resolve (Transient) | Zenject | 126.7015 | 12670.15 | 0.00 | 0.000000 |
| Resolve (Combined) | Onity | 18.8285 | 1882.85 | 0.00 | 0.000000 |
| Resolve (Combined) | VContainer | 24.6200 | 2462.00 | 0.00 | 0.000000 |
| Resolve (Combined) | Zenject | 203.9153 | 20391.53 | 0.00 | 0.000000 |
| Resolve (Complex) | Onity | 378.9463 | 37894.63 | 0.00 | 0.000000 |
| Resolve (Complex) | VContainer | 471.1739 | 47117.39 | 0.00 | 0.000000 |
| Resolve (Complex) | Zenject | 3023.8274 | 302382.74 | 0.00 | 0.000000 |
| Prepare & Register (Complex) | Onity | 300.8530 | 30085.30 | 0.00 | 0.000000 |
| Prepare & Register (Complex) | VContainer | 1459.5271 | 145952.71 | 0.00 | 0.000000 |
| Prepare & Register (Complex) | Zenject | 1912.9742 | 191297.42 | 0.00 | 0.000000 |
