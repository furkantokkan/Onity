# Reactive comparison summary

- Evidence root: `<host>\BenchmarkResults\reactive-attempt2-9563453`
- Generated (UTC): 2026-10-02T14:06:30.005549+00:00
- Reports: 6 (3 per backend), measured samples per process: 15, warmups: 2
- Libraries: Onity 0.6.0, R3 1.3.0, UniRx 7.1.0
- Rule (pre-registered): ratio = Onity process median / other library process median (time per operation, so below 1 means Onity is faster); faster when the median ratio over processes <= 0.95 and the worst process ratio <= 1.00; slower when the median ratio >= 1.05; otherwise on par.
- IL2CPP is the headline; Mono is reported the same way.

## IL2CPP (headline)

- Build GUID `76bffacfc72746dbad4a05b3890997b5`, Unity 2022.3.62f2, Onity 0.6.0, source 9563453f5f46bd79dad9f7d4204a9d4f6f6be7eb
- CPU: AMD Ryzen 9 5900X 12-Core Processor 
- Processes: il2cpp-reactive-p1, il2cpp-reactive-p2, il2cpp-reactive-p3

| Scenario | Ops/sample | Onity ns/op | R3 ns/op | UniRx ns/op | Onity/R3 median (worst) | vs R3 | Onity/UniRx median (worst) | vs UniRx |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: | --- |
| SubjectOnNext1 | 200000 | 4.25 | 4.57 | 4.07 | 0.930 (0.941) | faster | 1.044 (1.075) | on par |
| SubjectOnNext8 | 50000 | 15.97 | 24.71 | 29.58 | 0.634 (0.646) | faster | 0.540 (0.543) | faster |
| SubjectSubscribeDispose | 20000 | 118.44 | 335.62 | 607.36 | 0.353 (0.354) | faster | 0.178 (0.195) | faster |
| PropertySetChanged | 200000 | 7.01 | 12.91 | 12.89 | 0.552 (0.572) | faster | 0.543 (0.543) | faster |
| PropertySetSame | 200000 | 4.94 | 6.90 | 7.55 | 0.717 (0.724) | faster | 0.652 (0.667) | faster |
| PropertySubscribeDispose | 20000 | 112.46 | 333.62 | 202.38 | 0.337 (0.359) | faster | 0.559 (0.582) | faster |
| WhereSelectOnNext | 200000 | 7.96 | 9.70 | 10.14 | 0.801 (0.824) | faster | 0.785 (0.799) | faster |
| ChainSubscribeDispose | 10000 | 365.18 | 625.06 | 1023.87 | 0.584 (0.598) | faster | 0.365 (0.370) | faster |
| CombineLatestOnNext | 100000 | 7.06 | 45.22 | 52.56 | 0.153 (0.158) | faster | 0.135 (0.143) | faster |

Allocation: unavailable on this backend (Unavailable on IL2CPP: the counter is not called because the DI harness observed IL2CPP crashes with it.).

Per-process ratios:

| Scenario | Onity/R3 per process | Onity/UniRx per process |
| --- | --- | --- |
| SubjectOnNext1 | 0.917, 0.930, 0.941 | 1.075, 1.044, 1.016 |
| SubjectOnNext8 | 0.646, 0.575, 0.634 | 0.540, 0.539, 0.543 |
| SubjectSubscribeDispose | 0.353, 0.354, 0.339 | 0.195, 0.165, 0.178 |
| PropertySetChanged | 0.539, 0.572, 0.552 | 0.543, 0.533, 0.543 |
| PropertySetSame | 0.716, 0.724, 0.717 | 0.667, 0.537, 0.652 |
| PropertySubscribeDispose | 0.359, 0.337, 0.333 | 0.559, 0.556, 0.582 |
| WhereSelectOnNext | 0.777, 0.824, 0.801 | 0.775, 0.785, 0.799 |
| ChainSubscribeDispose | 0.556, 0.584, 0.598 | 0.365, 0.357, 0.370 |
| CombineLatestOnNext | 0.146, 0.158, 0.153 | 0.135, 0.133, 0.143 |

## Mono

- Build GUID `57eb3ef763c74b598735d77694b0093b`, Unity 2022.3.62f2, Onity 0.6.0, source 9563453f5f46bd79dad9f7d4204a9d4f6f6be7eb
- CPU: AMD Ryzen 9 5900X 12-Core Processor 
- Processes: mono-reactive-p1, mono-reactive-p2, mono-reactive-p3

| Scenario | Ops/sample | Onity ns/op | R3 ns/op | UniRx ns/op | Onity/R3 median (worst) | vs R3 | Onity/UniRx median (worst) | vs UniRx |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: | --- |
| SubjectOnNext1 | 200000 | 9.18 | 9.94 | 5.14 | 0.926 (1.112) | on par | 1.793 (1.952) | slower |
| SubjectOnNext8 | 50000 | 25.98 | 54.42 | 34.89 | 0.472 (0.477) | faster | 0.736 (0.748) | faster |
| SubjectSubscribeDispose | 20000 | 90.38 | 318.39 | 526.15 | 0.282 (0.307) | faster | 0.172 (0.179) | faster |
| PropertySetChanged | 200000 | 11.21 | 19.54 | 13.56 | 0.580 (0.603) | faster | 0.834 (0.869) | faster |
| PropertySetSame | 200000 | 5.21 | 5.32 | 5.63 | 0.961 (0.994) | on par | 0.936 (0.938) | faster |
| PropertySubscribeDispose | 20000 | 99.06 | 284.69 | 199.91 | 0.338 (0.352) | faster | 0.492 (0.501) | faster |
| WhereSelectOnNext | 200000 | 11.89 | 12.54 | 9.02 | 0.948 (0.955) | faster | 1.318 (1.382) | slower |
| ChainSubscribeDispose | 10000 | 342.06 | 593.95 | 749.41 | 0.581 (0.610) | faster | 0.461 (0.462) | faster |
| CombineLatestOnNext | 100000 | 56.80 | 38.14 | 32.63 | 1.489 (1.543) | slower | 1.741 (1.781) | slower |

Allocation: unavailable on this backend (Unavailable: the positive control read 0 bytes for a 64 KiB array and the empty control read 0 bytes.).

Per-process ratios:

| Scenario | Onity/R3 per process | Onity/UniRx per process |
| --- | --- | --- |
| SubjectOnNext1 | 1.112, 0.922, 0.926 | 1.952, 1.785, 1.793 |
| SubjectOnNext8 | 0.477, 0.472, 0.466 | 0.748, 0.732, 0.736 |
| SubjectSubscribeDispose | 0.282, 0.273, 0.307 | 0.172, 0.165, 0.179 |
| PropertySetChanged | 0.580, 0.603, 0.563 | 0.834, 0.869, 0.799 |
| PropertySetSame | 0.905, 0.994, 0.961 | 0.936, 0.833, 0.938 |
| PropertySubscribeDispose | 0.352, 0.316, 0.338 | 0.501, 0.463, 0.492 |
| WhereSelectOnNext | 0.933, 0.955, 0.948 | 1.382, 1.299, 1.318 |
| ChainSubscribeDispose | 0.610, 0.581, 0.560 | 0.462, 0.461, 0.451 |
| CombineLatestOnNext | 1.543, 1.448, 1.489 | 1.781, 1.715, 1.741 |

