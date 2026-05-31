# Onity DI Player Benchmark

- Generated (UTC): `2026-05-31T00:48:27Z`
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
| Resolve (Singleton) | Onity (Reflection) | 1.5717 | 157.17 | n/a | n/a |
| Resolve (Singleton) | Onity (Baked) | 0.1691 | 16.91 | n/a | n/a |
| Resolve (Singleton) | VContainer | 0.7875 | 78.75 | n/a | n/a |
| Resolve (Singleton) | Zenject | 5.4699 | 546.99 | n/a | n/a |
| Resolve (Transient) | Onity (Reflection) | 3.4832 | 348.32 | n/a | n/a |
| Resolve (Transient) | Onity (Baked) | 1.9137 | 191.37 | n/a | n/a |
| Resolve (Transient) | VContainer | 5.7608 | 576.08 | n/a | n/a |
| Resolve (Transient) | Zenject | 27.4241 | 2742.41 | n/a | n/a |
| Resolve (Combined) | Onity (Reflection) | 6.3396 | 633.96 | n/a | n/a |
| Resolve (Combined) | Onity (Baked) | 2.3205 | 232.05 | n/a | n/a |
| Resolve (Combined) | VContainer | 7.9405 | 794.05 | n/a | n/a |
| Resolve (Combined) | Zenject | 35.3076 | 3530.76 | n/a | n/a |
| Resolve (Complex) | Onity (Reflection) | 60.9520 | 6095.20 | n/a | n/a |
| Resolve (Complex) | Onity (Baked) | 53.9946 | 5399.46 | n/a | n/a |
| Resolve (Complex) | VContainer | 127.3993 | 12739.93 | n/a | n/a |
| Resolve (Complex) | Zenject | 610.7198 | 61071.98 | n/a | n/a |
| Prepare & Register (Complex) | Onity (Reflection) | 249.5759 | 24957.59 | n/a | n/a |
| Prepare & Register (Complex) | Onity (Baked) | 310.8390 | 31083.90 | n/a | n/a |
| Prepare & Register (Complex) | VContainer | 424.4649 | 42446.49 | n/a | n/a |
| Prepare & Register (Complex) | Zenject | 663.8635 | 66386.35 | n/a | n/a |
