# Onity DI Player Benchmark

- Generated (UTC): `2026-05-30T20:09:24Z`
- Unity: `2022.3.62f3`
- Platform: `WindowsPlayer`
- Scripting backend: `IL2CPP`
- Compiled activation supported: `True`
- Allocation measurement available: `False`
- Samples per case: `8`
- Warmup iterations: `512`

| Scenario | Container | Mean (ms) | ns/op | Alloc/sample (B) | Alloc/op (B) |
|---|---|---:|---:|---:|---:|
| Resolve (Singleton) | Onity (Reflection) | 1.0167 | 101.67 | n/a | n/a |
| Resolve (Singleton) | Onity (Baked) | 0.1731 | 17.31 | n/a | n/a |
| Resolve (Singleton) | VContainer | 0.8571 | 85.71 | n/a | n/a |
| Resolve (Singleton) | Zenject | 4.6865 | 468.65 | n/a | n/a |
| Resolve (Transient) | Onity (Reflection) | 15.8084 | 1580.84 | n/a | n/a |
| Resolve (Transient) | Onity (Baked) | 14.3130 | 1431.30 | n/a | n/a |
| Resolve (Transient) | VContainer | 5.7976 | 579.76 | n/a | n/a |
| Resolve (Transient) | Zenject | 24.5774 | 2457.74 | n/a | n/a |
| Resolve (Combined) | Onity (Reflection) | 15.0515 | 1505.15 | n/a | n/a |
| Resolve (Combined) | Onity (Baked) | 12.6254 | 1262.54 | n/a | n/a |
| Resolve (Combined) | VContainer | 6.0186 | 601.86 | n/a | n/a |
| Resolve (Combined) | Zenject | 35.2451 | 3524.51 | n/a | n/a |
| Resolve (Complex) | Onity (Reflection) | 373.7887 | 37378.87 | n/a | n/a |
| Resolve (Complex) | Onity (Baked) | 347.2856 | 34728.56 | n/a | n/a |
| Resolve (Complex) | VContainer | 129.1826 | 12918.26 | n/a | n/a |
| Resolve (Complex) | Zenject | 626.8852 | 62688.52 | n/a | n/a |
| Prepare & Register (Complex) | Onity (Reflection) | 209.3944 | 20939.44 | n/a | n/a |
| Prepare & Register (Complex) | Onity (Baked) | 238.7178 | 23871.78 | n/a | n/a |
| Prepare & Register (Complex) | VContainer | 384.6470 | 38464.70 | n/a | n/a |
| Prepare & Register (Complex) | Zenject | 610.6027 | 61060.27 | n/a | n/a |
