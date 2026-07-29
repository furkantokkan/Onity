# Onity DI Player Benchmark

- Generated (UTC): `2026-07-12T13:30:25Z`
- Unity: `2022.3.62f3`
- Platform: `WindowsPlayer`
- Scripting backend: `IL2CPP`
- Compiled activation supported: `True`
- Generated activators registered: `19`
- Allocation measurement available: `False`
- Samples per case: `1000`
- Warmup iterations: `512`

| Scenario | Container | Mean (ms) | ns/op | Alloc/sample (B) | Alloc/op (B) |
|---|---|---:|---:|---:|---:|
| Resolve (Singleton) | Onity (Reflection) | 0.1880 | 18.80 | n/a | n/a |
| Resolve (Singleton) | Onity (Baked) | 0.1740 | 17.40 | n/a | n/a |
| Resolve (Singleton) | VContainer | 0.9439 | 94.39 | n/a | n/a |
| Resolve (Singleton) | Zenject | 4.3524 | 435.24 | n/a | n/a |
