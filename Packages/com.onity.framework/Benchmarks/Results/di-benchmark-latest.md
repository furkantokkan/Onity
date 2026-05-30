# Onity DI Benchmark

- Generated (UTC): `2026-05-30T19:38:06Z`
- Unity: `2022.3.62f3`
- Platform: `WindowsEditor`
- Samples per case: `8`
- Warmup iterations: `512`

| Scenario | Container | Mean (ms) | ns/op | Alloc/sample (B) | Alloc/op (B) |
|---|---|---:|---:|---:|---:|
| Resolve (Singleton) | Onity (Reflection) | 1.6397 | 163.97 | 0.00 | 0.000000 |
| Resolve (Singleton) | Onity (Baked) | 0.6313 | 63.13 | 0.00 | 0.000000 |
| Resolve (Singleton) | VContainer | 2.1413 | 214.13 | 0.00 | 0.000000 |
| Resolve (Singleton) | Zenject | 28.6625 | 2866.25 | 0.00 | 0.000000 |
| Resolve (Transient) | Onity (Reflection) | 9.4297 | 942.97 | 0.00 | 0.000000 |
| Resolve (Transient) | Onity (Baked) | 10.8281 | 1082.81 | 0.00 | 0.000000 |
| Resolve (Transient) | VContainer | 18.7881 | 1878.81 | 0.00 | 0.000000 |
| Resolve (Transient) | Zenject | 123.5602 | 12356.02 | 0.00 | 0.000000 |
| Resolve (Combined) | Onity (Reflection) | 12.3304 | 1233.04 | 0.00 | 0.000000 |
| Resolve (Combined) | Onity (Baked) | 9.7196 | 971.96 | 0.00 | 0.000000 |
| Resolve (Combined) | VContainer | 20.7888 | 2078.88 | 0.00 | 0.000000 |
| Resolve (Combined) | Zenject | 172.4822 | 17248.22 | 0.00 | 0.000000 |
| Resolve (Complex) | Onity (Reflection) | 259.4010 | 25940.10 | 0.00 | 0.000000 |
| Resolve (Complex) | Onity (Baked) | 229.0521 | 22905.21 | 0.00 | 0.000000 |
| Resolve (Complex) | VContainer | 421.5842 | 42158.42 | 0.00 | 0.000000 |
| Resolve (Complex) | Zenject | 2898.2338 | 289823.38 | 0.00 | 0.000000 |
| Prepare & Register (Complex) | Onity (Reflection) | 429.2859 | 42928.59 | 0.00 | 0.000000 |
| Prepare & Register (Complex) | Onity (Baked) | 610.4435 | 61044.35 | 0.00 | 0.000000 |
| Prepare & Register (Complex) | VContainer | 1507.3011 | 150730.11 | 0.00 | 0.000000 |
| Prepare & Register (Complex) | Zenject | 2155.3699 | 215536.99 | 0.00 | 0.000000 |
