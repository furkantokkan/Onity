# Onity DI Benchmark

- Generated (UTC): `2026-07-12T13:31:37Z`
- Unity: `2022.3.62f3`
- Platform: `WindowsEditor`
- Allocation measurement available: `False`
- Samples per case: `8`
- Warmup iterations: `512`

| Scenario | Container | Mean (ms) | ns/op | Alloc/sample (B) | Alloc/op (B) |
|---|---|---:|---:|---:|---:|
| Resolve (Singleton) | Onity (Reflection) | 0.6906 | 69.06 | n/a | n/a |
| Resolve (Singleton) | Onity (Baked) | 0.7796 | 77.96 | n/a | n/a |
| Resolve (Singleton) | VContainer | 2.1729 | 217.29 | n/a | n/a |
| Resolve (Singleton) | Zenject | 27.7802 | 2778.02 | n/a | n/a |
| Resolve (Transient) | Onity (Reflection) | 10.3020 | 1030.20 | n/a | n/a |
| Resolve (Transient) | Onity (Baked) | 13.6585 | 1365.85 | n/a | n/a |
| Resolve (Transient) | VContainer | 23.5158 | 2351.58 | n/a | n/a |
| Resolve (Transient) | Zenject | 125.6114 | 12561.14 | n/a | n/a |
| Resolve (Combined) | Onity (Reflection) | 9.7991 | 979.91 | n/a | n/a |
| Resolve (Combined) | Onity (Baked) | 8.7471 | 874.71 | n/a | n/a |
| Resolve (Combined) | VContainer | 19.0485 | 1904.85 | n/a | n/a |
| Resolve (Combined) | Zenject | 143.8201 | 14382.01 | n/a | n/a |
| Resolve (Complex) | Onity (Reflection) | 208.7402 | 20874.02 | n/a | n/a |
| Resolve (Complex) | Onity (Baked) | 208.2796 | 20827.96 | n/a | n/a |
| Resolve (Complex) | VContainer | 402.6962 | 40269.62 | n/a | n/a |
| Resolve (Complex) | Zenject | 2818.1413 | 281814.13 | n/a | n/a |
| Prepare & Register (Complex) | Onity (Reflection) | 406.1332 | 40613.32 | n/a | n/a |
| Prepare & Register (Complex) | Onity (Baked) | 549.9569 | 54995.69 | n/a | n/a |
| Prepare & Register (Complex) | VContainer | 1392.4649 | 139246.49 | n/a | n/a |
| Prepare & Register (Complex) | Zenject | 1888.6533 | 188865.33 | n/a | n/a |
