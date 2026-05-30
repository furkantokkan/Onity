# DI Benchmark Summary

| Scenario | Container | Mean (ms) | Alloc/sample (B) |
|---|---|---:|---:|
| Resolve (Singleton) | Onity (Baked) | 0.6313 | 0.00 |
| Resolve (Singleton) | Onity (Reflection) | 1.6397 | 0.00 |
| Resolve (Singleton) | VContainer | 2.1413 | 0.00 |
| Resolve (Singleton) | Zenject | 28.6625 | 0.00 |
| Resolve (Transient) | Onity (Baked) | 10.8281 | 0.00 |
| Resolve (Transient) | Onity (Reflection) | 9.4297 | 0.00 |
| Resolve (Transient) | VContainer | 18.7881 | 0.00 |
| Resolve (Transient) | Zenject | 123.5602 | 0.00 |
| Resolve (Combined) | Onity (Baked) | 9.7196 | 0.00 |
| Resolve (Combined) | Onity (Reflection) | 12.3304 | 0.00 |
| Resolve (Combined) | VContainer | 20.7888 | 0.00 |
| Resolve (Combined) | Zenject | 172.4822 | 0.00 |
| Resolve (Complex) | Onity (Baked) | 229.0521 | 0.00 |
| Resolve (Complex) | Onity (Reflection) | 259.4010 | 0.00 |
| Resolve (Complex) | VContainer | 421.5842 | 0.00 |
| Resolve (Complex) | Zenject | 2898.2338 | 0.00 |
| Prepare & Register (Complex) | Onity (Baked) | 610.4435 | 0.00 |
| Prepare & Register (Complex) | Onity (Reflection) | 429.2859 | 0.00 |
| Prepare & Register (Complex) | VContainer | 1507.3011 | 0.00 |
| Prepare & Register (Complex) | Zenject | 2155.3699 | 0.00 |

## Relative Speedup vs VContainer

| Scenario | Onity (Baked) speedup | Onity (Reflection) speedup |
|---|---:|---:|
| Resolve (Singleton) | +70.52% | +23.42% |
| Resolve (Transient) | +42.37% | +49.81% |
| Resolve (Combined) | +53.25% | +40.69% |
| Resolve (Complex) | +45.67% | +38.47% |
| Prepare & Register (Complex) | +59.50% | +71.52% |