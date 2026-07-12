# DI Benchmark Summary

| Scenario | Container | Mean (ms) | Alloc/sample (B) |
|---|---|---:|---:|
| Resolve (Singleton) | Onity (Baked) | 0.7796 | n/a |
| Resolve (Singleton) | Onity (Reflection) | 0.6906 | n/a |
| Resolve (Singleton) | VContainer | 2.1728 | n/a |
| Resolve (Singleton) | Zenject | 27.7802 | n/a |
| Resolve (Transient) | Onity (Baked) | 13.6585 | n/a |
| Resolve (Transient) | Onity (Reflection) | 10.3020 | n/a |
| Resolve (Transient) | VContainer | 23.5158 | n/a |
| Resolve (Transient) | Zenject | 125.6114 | n/a |
| Resolve (Combined) | Onity (Baked) | 8.7471 | n/a |
| Resolve (Combined) | Onity (Reflection) | 9.7991 | n/a |
| Resolve (Combined) | VContainer | 19.0485 | n/a |
| Resolve (Combined) | Zenject | 143.8201 | n/a |
| Resolve (Complex) | Onity (Baked) | 208.2796 | n/a |
| Resolve (Complex) | Onity (Reflection) | 208.7402 | n/a |
| Resolve (Complex) | VContainer | 402.6962 | n/a |
| Resolve (Complex) | Zenject | 2818.1413 | n/a |
| Prepare & Register (Complex) | Onity (Baked) | 549.9569 | n/a |
| Prepare & Register (Complex) | Onity (Reflection) | 406.1332 | n/a |
| Prepare & Register (Complex) | VContainer | 1392.4649 | n/a |
| Prepare & Register (Complex) | Zenject | 1888.6533 | n/a |

## Relative Speedup vs VContainer

| Scenario | Onity (Baked) speedup | Onity (Reflection) speedup |
|---|---:|---:|
| Resolve (Singleton) | +64.12% | +68.22% |
| Resolve (Transient) | +41.92% | +56.19% |
| Resolve (Combined) | +54.08% | +48.56% |
| Resolve (Complex) | +48.28% | +48.16% |
| Prepare & Register (Complex) | +60.50% | +70.83% |