# Onity DI Player Benchmark

- Generated (UTC): `2026-09-23T18:32:58Z`
- Unity: `2022.3.62f3`
- Platform: `WindowsPlayer`
- Scripting backend: `IL2CPP`
- Compiled activation supported: `True`
- Generated activators registered: `19`
- Allocation measurement available: `False`
- Samples per case: `8`
- Warmup iterations: `512`

| Scenario | Container | Mean (ms) | ns/op | Alloc/sample (B) | Alloc/op (B) |
|---|---|---:|---:|---:|---:|
| Resolve (Singleton) | Onity (Reflection) | 1.3766 | 137.66 | n/a | n/a |
| Resolve (Singleton) | Onity (Baked) | 0.2099 | 20.99 | n/a | n/a |
| Resolve (Singleton) | VContainer | 0.9089 | 90.89 | n/a | n/a |
| Resolve (Singleton) | Zenject | 4.6312 | 463.12 | n/a | n/a |
| Resolve (Transient) | Onity (Reflection) | 2.5662 | 256.62 | n/a | n/a |
| Resolve (Transient) | Onity (Baked) | 1.2886 | 128.86 | n/a | n/a |
| Resolve (Transient) | VContainer | 5.2902 | 529.02 | n/a | n/a |
| Resolve (Transient) | Zenject | 23.3341 | 2333.41 | n/a | n/a |
| Resolve (Combined) | Onity (Reflection) | 3.9461 | 394.61 | n/a | n/a |
| Resolve (Combined) | Onity (Baked) | 1.2616 | 126.16 | n/a | n/a |
| Resolve (Combined) | VContainer | 5.8825 | 588.25 | n/a | n/a |
| Resolve (Combined) | Zenject | 29.0093 | 2900.93 | n/a | n/a |
| Resolve (Complex) | Onity (Reflection) | 43.2135 | 4321.35 | n/a | n/a |
| Resolve (Complex) | Onity (Baked) | 39.3567 | 3935.67 | n/a | n/a |
| Resolve (Complex) | VContainer | 130.7804 | 13078.04 | n/a | n/a |
| Resolve (Complex) | Zenject | 618.8282 | 61882.82 | n/a | n/a |
| Prepare & Register (Complex) | Onity (Reflection) | 173.7508 | 17375.08 | n/a | n/a |
| Prepare & Register (Complex) | Onity (Baked) | 225.7508 | 22575.08 | n/a | n/a |
| Prepare & Register (Complex) | VContainer | 409.1892 | 40918.92 | n/a | n/a |
| Prepare & Register (Complex) | Zenject | 636.2254 | 63622.54 | n/a | n/a |
| Resolve (Keyed Singleton) | Onity (Reflection) | 0.3212 | 32.12 | n/a | n/a |
| Resolve (Keyed Singleton) | Onity (Baked) | 0.3112 | 31.12 | n/a | n/a |
| Resolve (Keyed Singleton) | VContainer | 0.8576 | 85.76 | n/a | n/a |
| Resolve (Keyed Singleton) | Zenject | 5.7389 | 573.89 | n/a | n/a |
| Resolve (Scoped Singleton) | Onity (Reflection) | 2.5284 | 252.84 | n/a | n/a |
| Resolve (Scoped Singleton) | Onity (Baked) | 0.4719 | 47.19 | n/a | n/a |
| Resolve (Scoped Singleton) | VContainer | 1.0943 | 109.43 | n/a | n/a |
| Resolve (Scoped Singleton) | Zenject | 5.6253 | 562.53 | n/a | n/a |
