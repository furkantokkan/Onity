# Reactive comparison summary

- Evidence root: `<host>\BenchmarkResults\reactive-run1-20261002`
- Generated (UTC): 2026-10-02T12:04:52.707039+00:00
- Reports: 6 (3 per backend), measured samples per process: 15, warmups: 2
- Libraries: Onity 0.6.0, R3 1.3.0, UniRx 7.1.0
- Rule (pre-registered): ratio = Onity process median / other library process median (time per operation, so below 1 means Onity is faster); faster when the median ratio over processes <= 0.95 and the worst process ratio <= 1.00; slower when the median ratio >= 1.05; otherwise on par.
- IL2CPP is the headline; Mono is reported the same way.

## IL2CPP (headline)

- Build GUID `9ebeccd9b06a406eb4a00c27287f2146`, Unity 2022.3.62f2, Onity 0.6.0, source 4e92db959023180fa6530e5d0867153b9ff6047e
- CPU: AMD Ryzen 9 5900X 12-Core Processor 
- Processes: il2cpp-reactive-p1, il2cpp-reactive-p2, il2cpp-reactive-p3

| Scenario | Ops/sample | Onity ns/op | R3 ns/op | UniRx ns/op | Onity/R3 median (worst) | vs R3 | Onity/UniRx median (worst) | vs UniRx |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: | --- |
| SubjectOnNext1 | 200000 | 5.38 | 4.74 | 3.83 | 1.162 (1.253) | slower | 1.405 (1.500) | slower |
| SubjectOnNext8 | 50000 | 22.24 | 37.85 | 30.16 | 0.680 (0.824) | faster | 0.680 (0.737) | faster |
| SubjectSubscribeDispose | 20000 | 490.74 | 352.85 | 637.44 | 1.658 (1.755) | slower | 0.840 (0.908) | faster |
| PropertySetChanged | 200000 | 10.95 | 13.00 | 12.71 | 0.869 (0.957) | faster | 0.877 (0.966) | faster |
| PropertySetSame | 200000 | 5.33 | 6.89 | 8.18 | 0.773 (0.781) | faster | 0.671 (0.732) | faster |
| PropertySubscribeDispose | 20000 | 525.26 | 330.00 | 207.41 | 1.523 (1.645) | slower | 2.497 (2.617) | slower |
| WhereSelectOnNext | 200000 | 10.25 | 10.73 | 10.36 | 0.956 (0.984) | on par | 0.969 (0.990) | on par |
| ChainSubscribeDispose | 10000 | 1737.04 | 675.80 | 1001.46 | 2.570 (2.582) | slower | 1.700 (1.735) | slower |
| CombineLatestOnNext | 100000 | 8.17 | 44.17 | 51.13 | 0.190 (0.199) | faster | 0.167 (0.185) | faster |

Allocation: unavailable on this backend (Unavailable on IL2CPP: the counter is not called because the DI harness observed IL2CPP crashes with it.).

Per-process ratios:

| Scenario | Onity/R3 per process | Onity/UniRx per process |
| --- | --- | --- |
| SubjectOnNext1 | 1.253, 1.136, 1.162 | 1.500, 1.405, 1.390 |
| SubjectOnNext8 | 0.680, 0.824, 0.534 | 0.616, 0.737, 0.680 |
| SubjectSubscribeDispose | 1.182, 1.658, 1.755 | 0.654, 0.840, 0.908 |
| PropertySetChanged | 0.957, 0.842, 0.869 | 0.966, 0.861, 0.877 |
| PropertySetSame | 0.703, 0.773, 0.781 | 0.732, 0.651, 0.671 |
| PropertySubscribeDispose | 1.155, 1.523, 1.645 | 2.033, 2.497, 2.617 |
| WhereSelectOnNext | 0.984, 0.956, 0.947 | 0.896, 0.990, 0.969 |
| ChainSubscribeDispose | 1.837, 2.582, 2.570 | 1.280, 1.700, 1.735 |
| CombineLatestOnNext | 0.199, 0.182, 0.190 | 0.185, 0.157, 0.167 |

## Mono

- Build GUID `b2689d46c2bf4c8ebeacf8db9cb58bc9`, Unity 2022.3.62f2, Onity 0.6.0, source 4e92db959023180fa6530e5d0867153b9ff6047e
- CPU: AMD Ryzen 9 5900X 12-Core Processor 
- Processes: mono-reactive-p1, mono-reactive-p2, mono-reactive-p3

| Scenario | Ops/sample | Onity ns/op | R3 ns/op | UniRx ns/op | Onity/R3 median (worst) | vs R3 | Onity/UniRx median (worst) | vs UniRx |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: | --- |
| SubjectOnNext1 | 200000 | 42.98 | 9.43 | 5.57 | 4.503 (4.557) | slower | 7.719 (7.748) | slower |
| SubjectOnNext8 | 50000 | 279.06 | 54.72 | 37.88 | 4.993 (5.279) | slower | 7.032 (7.626) | slower |
| SubjectSubscribeDispose | 20000 | 397.40 | 283.77 | 507.22 | 1.362 (1.429) | slower | 0.777 (0.783) | faster |
| PropertySetChanged | 200000 | 57.34 | 17.35 | 13.37 | 3.351 (3.436) | slower | 4.284 (4.287) | slower |
| PropertySetSame | 200000 | 7.01 | 4.92 | 4.87 | 1.476 (1.508) | slower | 1.551 (1.570) | slower |
| PropertySubscribeDispose | 20000 | 432.06 | 281.87 | 208.27 | 1.575 (1.645) | slower | 2.149 (2.284) | slower |
| WhereSelectOnNext | 200000 | 35.08 | 13.12 | 8.98 | 2.673 (2.866) | slower | 3.907 (4.149) | slower |
| ChainSubscribeDispose | 10000 | 1186.26 | 568.34 | 718.28 | 2.087 (2.097) | slower | 1.641 (1.652) | slower |
| CombineLatestOnNext | 100000 | 52.43 | 33.89 | 31.03 | 1.484 (1.641) | slower | 1.709 (1.756) | slower |

Allocation: unavailable on this backend (Unavailable: the positive control read 0 bytes for a 64 KiB array and the empty control read 0 bytes.).

Per-process ratios:

| Scenario | Onity/R3 per process | Onity/UniRx per process |
| --- | --- | --- |
| SubjectOnNext1 | 4.462, 4.557, 4.503 | 7.510, 7.719, 7.748 |
| SubjectOnNext8 | 4.962, 5.279, 4.993 | 6.498, 7.626, 7.032 |
| SubjectSubscribeDispose | 1.429, 1.362, 1.318 | 0.783, 0.777, 0.758 |
| PropertySetChanged | 3.436, 3.010, 3.351 | 4.181, 4.287, 4.284 |
| PropertySetSame | 1.407, 1.476, 1.508 | 1.198, 1.551, 1.570 |
| PropertySubscribeDispose | 1.645, 1.575, 1.521 | 2.149, 2.284, 2.058 |
| WhereSelectOnNext | 2.866, 2.673, 2.673 | 3.762, 3.907, 4.149 |
| ChainSubscribeDispose | 2.097, 2.087, 2.034 | 1.652, 1.641, 1.610 |
| CombineLatestOnNext | 1.420, 1.641, 1.484 | 1.709, 1.756, 1.621 |

