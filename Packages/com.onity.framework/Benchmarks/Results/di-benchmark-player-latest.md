# Onity DI Player Benchmark

- Generated (UTC): `2026-05-30T23:02:24Z`
- Unity: `2022.3.62f3`
- Platform: `WindowsPlayer`
- Scripting backend: `IL2CPP`
- Compiled activation supported: `True`
- Generated activators registered: `19`
- Allocation measurement available: `False`
- Samples per case: `3`
- Warmup iterations: `128`

| Scenario | Container | Mean (ms) | ns/op | Alloc/sample (B) | Alloc/op (B) |
|---|---|---:|---:|---:|---:|
| Resolve (Singleton) | Onity (Reflection) | 0.1380 | 138.00 | n/a | n/a |
| Resolve (Singleton) | Onity (Baked) | 0.0184 | 18.43 | n/a | n/a |
| Resolve (Singleton) | VContainer | 0.0878 | 87.77 | n/a | n/a |
| Resolve (Singleton) | Zenject | 0.5994 | 599.37 | n/a | n/a |
| Resolve (Transient) | Onity (Reflection) | 0.3704 | 370.43 | n/a | n/a |
| Resolve (Transient) | Onity (Baked) | 0.1786 | 178.63 | n/a | n/a |
| Resolve (Transient) | VContainer | 0.5073 | 507.33 | n/a | n/a |
| Resolve (Transient) | Zenject | 2.2388 | 2238.83 | n/a | n/a |
| Resolve (Combined) | Onity (Reflection) | 0.4512 | 451.17 | n/a | n/a |
| Resolve (Combined) | Onity (Baked) | 0.1779 | 177.93 | n/a | n/a |
| Resolve (Combined) | VContainer | 0.6298 | 629.77 | n/a | n/a |
| Resolve (Combined) | Zenject | 2.8711 | 2871.07 | n/a | n/a |
| Resolve (Complex) | Onity (Reflection) | 6.1721 | 6172.07 | n/a | n/a |
| Resolve (Complex) | Onity (Baked) | 5.1145 | 5114.50 | n/a | n/a |
| Resolve (Complex) | VContainer | 12.0920 | 12092.00 | n/a | n/a |
| Resolve (Complex) | Zenject | 61.9443 | 61944.33 | n/a | n/a |
| Prepare & Register (Complex) | Onity (Reflection) | 30.5506 | 30550.57 | n/a | n/a |
| Prepare & Register (Complex) | Onity (Baked) | 35.3453 | 35345.30 | n/a | n/a |
| Prepare & Register (Complex) | VContainer | 46.0527 | 46052.67 | n/a | n/a |
| Prepare & Register (Complex) | Zenject | 72.1731 | 72173.10 | n/a | n/a |
