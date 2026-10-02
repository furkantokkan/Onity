# Reactive comparison summary

- Evidence root: `<host>\BenchmarkResults\reactive-attempt1-b73c20e`
- Generated (UTC): 2026-10-02T13:35:46.549527+00:00
- Reports: 6 (3 per backend), measured samples per process: 15, warmups: 2
- Libraries: Onity 0.6.0, R3 1.3.0, UniRx 7.1.0
- Rule (pre-registered): ratio = Onity process median / other library process median (time per operation, so below 1 means Onity is faster); faster when the median ratio over processes <= 0.95 and the worst process ratio <= 1.00; slower when the median ratio >= 1.05; otherwise on par.
- IL2CPP is the headline; Mono is reported the same way.

## IL2CPP (headline)

- Build GUID `d3ced29dbec449d98d1a120372dd55d8`, Unity 2022.3.62f2, Onity 0.6.0, source b73c20ecce1aa7290e35a32a6d7de9cce2e0228b
- CPU: AMD Ryzen 9 5900X 12-Core Processor 
- Processes: il2cpp-reactive-p1, il2cpp-reactive-p2, il2cpp-reactive-p3

| Scenario | Ops/sample | Onity ns/op | R3 ns/op | UniRx ns/op | Onity/R3 median (worst) | vs R3 | Onity/UniRx median (worst) | vs UniRx |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: | --- |
| SubjectOnNext1 | 200000 | 4.68 | 4.39 | 3.71 | 1.061 (1.082) | slower | 1.262 (1.286) | slower |
| SubjectOnNext8 | 50000 | 17.53 | 23.79 | 28.46 | 0.732 (0.738) | faster | 0.614 (0.616) | faster |
| SubjectSubscribeDispose | 20000 | 90.30 | 271.52 | 504.83 | 0.361 (0.382) | faster | 0.181 (0.204) | faster |
| PropertySetChanged | 200000 | 6.95 | 12.14 | 14.33 | 0.578 (0.580) | faster | 0.488 (0.490) | faster |
| PropertySetSame | 200000 | 4.81 | 6.67 | 7.59 | 0.722 (0.723) | faster | 0.633 (0.635) | faster |
| PropertySubscribeDispose | 20000 | 100.01 | 282.89 | 176.86 | 0.349 (0.385) | faster | 0.571 (0.617) | faster |
| WhereSelectOnNext | 200000 | 7.54 | 9.30 | 9.95 | 0.812 (0.814) | faster | 0.753 (0.762) | faster |
| ChainSubscribeDispose | 10000 | 317.63 | 576.68 | 863.69 | 0.553 (0.598) | faster | 0.368 (0.385) | faster |
| CombineLatestOnNext | 100000 | 7.68 | 43.07 | 50.03 | 0.178 (0.182) | faster | 0.153 (0.153) | faster |

Allocation: unavailable on this backend (Unavailable on IL2CPP: the counter is not called because the DI harness observed IL2CPP crashes with it.).

Per-process ratios:

| Scenario | Onity/R3 per process | Onity/UniRx per process |
| --- | --- | --- |
| SubjectOnNext1 | 1.059, 1.082, 1.061 | 1.246, 1.286, 1.262 |
| SubjectOnNext8 | 0.730, 0.738, 0.732 | 0.611, 0.616, 0.614 |
| SubjectSubscribeDispose | 0.382, 0.361, 0.326 | 0.204, 0.179, 0.181 |
| PropertySetChanged | 0.578, 0.572, 0.580 | 0.490, 0.471, 0.488 |
| PropertySetSame | 0.723, 0.721, 0.722 | 0.632, 0.633, 0.635 |
| PropertySubscribeDispose | 0.385, 0.349, 0.338 | 0.617, 0.571, 0.548 |
| WhereSelectOnNext | 0.812, 0.814, 0.796 | 0.762, 0.753, 0.753 |
| ChainSubscribeDispose | 0.530, 0.598, 0.553 | 0.355, 0.385, 0.368 |
| CombineLatestOnNext | 0.182, 0.177, 0.178 | 0.151, 0.153, 0.153 |

## Mono

- Build GUID `d0c5043e8e6242bd818bd80d01d06d36`, Unity 2022.3.62f2, Onity 0.6.0, source b73c20ecce1aa7290e35a32a6d7de9cce2e0228b
- CPU: AMD Ryzen 9 5900X 12-Core Processor 
- Processes: mono-reactive-p1, mono-reactive-p2, mono-reactive-p3

| Scenario | Ops/sample | Onity ns/op | R3 ns/op | UniRx ns/op | Onity/R3 median (worst) | vs R3 | Onity/UniRx median (worst) | vs UniRx |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: | --- |
| SubjectOnNext1 | 200000 | 8.29 | 8.84 | 4.75 | 0.944 (0.952) | faster | 1.747 (1.759) | slower |
| SubjectOnNext8 | 50000 | 25.75 | 52.06 | 33.27 | 0.496 (0.497) | faster | 0.774 (0.776) | faster |
| SubjectSubscribeDispose | 20000 | 82.53 | 280.82 | 466.02 | 0.292 (0.319) | faster | 0.181 (0.192) | faster |
| PropertySetChanged | 200000 | 11.10 | 18.76 | 12.94 | 0.588 (0.592) | faster | 0.847 (0.858) | faster |
| PropertySetSame | 200000 | 4.87 | 5.75 | 5.91 | 0.864 (0.942) | faster | 0.838 (0.912) | faster |
| PropertySubscribeDispose | 20000 | 85.69 | 272.83 | 186.08 | 0.320 (0.330) | faster | 0.461 (0.494) | faster |
| WhereSelectOnNext | 200000 | 10.89 | 12.21 | 8.49 | 0.894 (0.900) | faster | 1.281 (1.283) | slower |
| ChainSubscribeDispose | 10000 | 309.73 | 536.58 | 668.23 | 0.579 (0.605) | faster | 0.467 (0.473) | faster |
| CombineLatestOnNext | 100000 | 52.44 | 32.59 | 29.88 | 1.607 (1.622) | slower | 1.754 (1.772) | slower |

Allocation: unavailable on this backend (Unavailable: the positive control read 0 bytes for a 64 KiB array and the empty control read 0 bytes.).

Per-process ratios:

| Scenario | Onity/R3 per process | Onity/UniRx per process |
| --- | --- | --- |
| SubjectOnNext1 | 0.938, 0.944, 0.952 | 1.747, 1.759, 1.721 |
| SubjectOnNext8 | 0.496, 0.497, 0.492 | 0.774, 0.776, 0.773 |
| SubjectSubscribeDispose | 0.287, 0.319, 0.292 | 0.169, 0.192, 0.181 |
| PropertySetChanged | 0.587, 0.592, 0.588 | 0.845, 0.858, 0.847 |
| PropertySetSame | 0.838, 0.942, 0.864 | 0.912, 0.808, 0.838 |
| PropertySubscribeDispose | 0.320, 0.330, 0.312 | 0.452, 0.494, 0.461 |
| WhereSelectOnNext | 0.900, 0.885, 0.894 | 1.276, 1.283, 1.281 |
| ChainSubscribeDispose | 0.579, 0.605, 0.561 | 0.467, 0.473, 0.451 |
| CombineLatestOnNext | 1.607, 1.622, 1.537 | 1.753, 1.772, 1.754 |

