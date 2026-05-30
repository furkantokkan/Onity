# DI Benchmark Summary

| Scenario | Container | Mean (ms) | Alloc/sample (B) |
|---|---|---:|---:|
| Resolve (Singleton) | Onity (Baked) | 0.9412 | 0.00 |
| Resolve (Singleton) | Onity (Reflection) | 2.1494 | 0.00 |
| Resolve (Singleton) | VContainer | 2.0212 | 0.00 |
| Resolve (Singleton) | Zenject | 31.3679 | 0.00 |
| Resolve (Transient) | Onity (Baked) | 7.7509 | 0.00 |
| Resolve (Transient) | Onity (Reflection) | 17.2534 | 0.00 |
| Resolve (Transient) | VContainer | 16.9687 | 0.00 |
| Resolve (Transient) | Zenject | 116.8100 | 0.00 |
| Resolve (Combined) | Onity (Baked) | 8.9640 | 0.00 |
| Resolve (Combined) | Onity (Reflection) | 10.5864 | 0.00 |
| Resolve (Combined) | VContainer | 17.1201 | 0.00 |
| Resolve (Combined) | Zenject | 153.9986 | 0.00 |
| Resolve (Complex) | Onity (Baked) | 227.8668 | 0.00 |
| Resolve (Complex) | Onity (Reflection) | 265.0232 | 0.00 |
| Resolve (Complex) | VContainer | 579.9535 | 0.00 |
| Resolve (Complex) | Zenject | 2853.9380 | 0.00 |
| Prepare & Register (Complex) | Onity (Baked) | 472.4312 | 0.00 |
| Prepare & Register (Complex) | Onity (Reflection) | 358.3679 | 0.00 |
| Prepare & Register (Complex) | VContainer | 1351.4018 | 0.00 |
| Prepare & Register (Complex) | Zenject | 1971.3162 | 0.00 |

## Relative Speedup vs VContainer

| Scenario | Onity (Baked) speedup | Onity (Reflection) speedup |
|---|---:|---:|
| Resolve (Singleton) | +53.44% | -6.34% |
| Resolve (Transient) | +54.32% | -1.68% |
| Resolve (Combined) | +47.64% | +38.16% |
| Resolve (Complex) | +60.71% | +54.30% |
| Prepare & Register (Complex) | +65.04% | +73.48% |