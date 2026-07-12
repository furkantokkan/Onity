# Onity DI Player Benchmark

- Generated (UTC): `2026-07-12T13:34:55Z`
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
| Resolve (Singleton) | Onity (Reflection) | 0.1754 | 17.54 | n/a | n/a |
| Resolve (Singleton) | Onity (Baked) | 0.1833 | 18.33 | n/a | n/a |
| Resolve (Singleton) | VContainer | 0.9509 | 95.09 | n/a | n/a |
| Resolve (Singleton) | Zenject | 4.4934 | 449.34 | n/a | n/a |
| Resolve (Transient) | Onity (Reflection) | 1.5896 | 158.96 | n/a | n/a |
| Resolve (Transient) | Onity (Baked) | 1.9083 | 190.83 | n/a | n/a |
| Resolve (Transient) | VContainer | 5.4091 | 540.91 | n/a | n/a |
| Resolve (Transient) | Zenject | 24.4802 | 2448.02 | n/a | n/a |
| Resolve (Combined) | Onity (Reflection) | 1.7583 | 175.83 | n/a | n/a |
| Resolve (Combined) | Onity (Baked) | 1.9562 | 195.62 | n/a | n/a |
| Resolve (Combined) | VContainer | 6.1187 | 611.87 | n/a | n/a |
| Resolve (Combined) | Zenject | 30.8003 | 3080.03 | n/a | n/a |
| Resolve (Complex) | Onity (Reflection) | 51.0662 | 5106.62 | n/a | n/a |
| Resolve (Complex) | Onity (Baked) | 50.7112 | 5071.12 | n/a | n/a |
| Resolve (Complex) | VContainer | 124.7524 | 12475.24 | n/a | n/a |
| Resolve (Complex) | Zenject | 593.2716 | 59327.16 | n/a | n/a |
| Prepare & Register (Complex) | Onity (Reflection) | 211.2797 | 21127.97 | n/a | n/a |
| Prepare & Register (Complex) | Onity (Baked) | 244.8955 | 24489.55 | n/a | n/a |
| Prepare & Register (Complex) | VContainer | 348.8821 | 34888.21 | n/a | n/a |
| Prepare & Register (Complex) | Zenject | 595.6747 | 59567.47 | n/a | n/a |
