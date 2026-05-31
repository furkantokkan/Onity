# Onity DI Player Benchmark

- Generated (UTC): `2026-05-31T15:26:19Z`
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
| Resolve (Singleton) | Onity (Reflection) | 1.2559 | 125.59 | n/a | n/a |
| Resolve (Singleton) | Onity (Baked) | 0.1980 | 19.80 | n/a | n/a |
| Resolve (Singleton) | VContainer | 0.9790 | 97.90 | n/a | n/a |
| Resolve (Singleton) | Zenject | 4.8755 | 487.55 | n/a | n/a |
| Resolve (Transient) | Onity (Reflection) | 2.7586 | 275.86 | n/a | n/a |
| Resolve (Transient) | Onity (Baked) | 1.3307 | 133.07 | n/a | n/a |
| Resolve (Transient) | VContainer | 5.2792 | 527.92 | n/a | n/a |
| Resolve (Transient) | Zenject | 23.0164 | 2301.64 | n/a | n/a |
| Resolve (Combined) | Onity (Reflection) | 4.3052 | 430.52 | n/a | n/a |
| Resolve (Combined) | Onity (Baked) | 1.5853 | 158.53 | n/a | n/a |
| Resolve (Combined) | VContainer | 6.7852 | 678.52 | n/a | n/a |
| Resolve (Combined) | Zenject | 30.5241 | 3052.41 | n/a | n/a |
| Resolve (Complex) | Onity (Reflection) | 48.9044 | 4890.44 | n/a | n/a |
| Resolve (Complex) | Onity (Baked) | 47.8235 | 4782.35 | n/a | n/a |
| Resolve (Complex) | VContainer | 135.5241 | 13552.41 | n/a | n/a |
| Resolve (Complex) | Zenject | 619.9870 | 61998.70 | n/a | n/a |
| Prepare & Register (Complex) | Onity (Reflection) | 215.4434 | 21544.34 | n/a | n/a |
| Prepare & Register (Complex) | Onity (Baked) | 269.4386 | 26943.86 | n/a | n/a |
| Prepare & Register (Complex) | VContainer | 396.9432 | 39694.32 | n/a | n/a |
| Prepare & Register (Complex) | Zenject | 659.3654 | 65936.54 | n/a | n/a |
