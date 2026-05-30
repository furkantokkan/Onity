# DI Benchmark Summary

| Scenario | Container | Mean (ms) | Alloc/sample (B) |
|---|---|---:|---:|
| Resolve (Singleton) | Onity | 1.7540 | 0.00 |
| Resolve (Singleton) | VContainer | 1.8708 | 0.00 |
| Resolve (Singleton) | Zenject | 26.8265 | 0.00 |
| Resolve (Transient) | Onity | 20.8758 | 0.00 |
| Resolve (Transient) | VContainer | 18.0105 | 0.00 |
| Resolve (Transient) | Zenject | 113.6180 | 0.00 |
| Resolve (Combined) | Onity | 21.6289 | 0.00 |
| Resolve (Combined) | VContainer | 18.3269 | 0.00 |
| Resolve (Combined) | Zenject | 142.6761 | 0.00 |
| Resolve (Complex) | Onity | 523.8602 | 0.00 |
| Resolve (Complex) | VContainer | 409.1614 | 0.00 |
| Resolve (Complex) | Zenject | 2650.5408 | 0.00 |
| Prepare & Register (Complex) | Onity | 124.9193 | 0.00 |
| Prepare & Register (Complex) | VContainer | 1431.2299 | 0.00 |
| Prepare & Register (Complex) | Zenject | 1793.4749 | 0.00 |

## Relative Speedup vs VContainer

| Scenario | Onity speedup |
|---|---:|
| Resolve (Singleton) | +6.24% |
| Resolve (Transient) | -15.91% |
| Resolve (Combined) | -18.02% |
| Resolve (Complex) | -28.03% |
| Prepare & Register (Complex) | +91.27% |