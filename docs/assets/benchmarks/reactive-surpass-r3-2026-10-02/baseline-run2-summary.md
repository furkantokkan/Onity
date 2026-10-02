# Reactive comparison summary

- Evidence root: `<host>\BenchmarkResults\reactive-run2-20261002`
- Generated (UTC): 2026-10-02T12:06:29.465012+00:00
- Reports: 10 (5 per backend), measured samples per process: 15, warmups: 2
- Libraries: Onity 0.6.0, R3 1.3.0, UniRx 7.1.0
- Rule (pre-registered): ratio = Onity process median / other library process median (time per operation, so below 1 means Onity is faster); faster when the median ratio over processes <= 0.95 and the worst process ratio <= 1.00; slower when the median ratio >= 1.05; otherwise on par.
- IL2CPP is the headline; Mono is reported the same way.

## IL2CPP (headline)

- Build GUID `9ebeccd9b06a406eb4a00c27287f2146`, Unity 2022.3.62f2, Onity 0.6.0, source 4e92db959023180fa6530e5d0867153b9ff6047e
- CPU: AMD Ryzen 9 5900X 12-Core Processor 
- Processes: il2cpp-reactive-p1, il2cpp-reactive-p2, il2cpp-reactive-p3, il2cpp-reactive-p4, il2cpp-reactive-p5

| Scenario | Ops/sample | Onity ns/op | R3 ns/op | UniRx ns/op | Onity/R3 median (worst) | vs R3 | Onity/UniRx median (worst) | vs UniRx |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: | --- |
| SubjectOnNext1 | 200000 | 5.36 | 4.48 | 3.81 | 1.198 (1.289) | slower | 1.415 (1.425) | slower |
| SubjectOnNext8 | 50000 | 21.27 | 25.66 | 30.99 | 0.817 (1.492) | on par | 0.675 (1.219) | on par |
| SubjectSubscribeDispose | 20000 | 544.70 | 330.58 | 613.38 | 1.664 (1.745) | slower | 0.903 (0.939) | faster |
| PropertySetChanged | 200000 | 10.97 | 12.44 | 12.72 | 0.894 (1.012) | on par | 0.881 (0.955) | faster |
| PropertySetSame | 200000 | 5.40 | 6.77 | 7.76 | 0.778 (0.837) | faster | 0.695 (0.743) | faster |
| PropertySubscribeDispose | 20000 | 558.32 | 330.71 | 213.93 | 1.644 (1.744) | slower | 2.368 (2.677) | slower |
| WhereSelectOnNext | 200000 | 8.90 | 9.51 | 9.67 | 0.938 (0.941) | faster | 0.924 (0.963) | faster |
| ChainSubscribeDispose | 10000 | 1877.50 | 680.53 | 1109.19 | 2.521 (3.008) | slower | 1.693 (1.855) | slower |
| CombineLatestOnNext | 100000 | 8.29 | 47.31 | 52.75 | 0.178 (0.245) | faster | 0.159 (0.174) | faster |

Allocation: unavailable on this backend (Unavailable on IL2CPP: the counter is not called because the DI harness observed IL2CPP crashes with it.).

Per-process ratios:

| Scenario | Onity/R3 per process | Onity/UniRx per process |
| --- | --- | --- |
| SubjectOnNext1 | 1.164, 1.195, 1.205, 1.289, 1.198 | 1.368, 1.425, 1.415, 1.386, 1.422 |
| SubjectOnNext8 | 0.725, 1.492, 0.791, 0.882, 0.817 | 0.675, 1.219, 0.657, 0.722, 0.670 |
| SubjectSubscribeDispose | 1.664, 1.745, 1.405, 1.692, 1.617 | 0.939, 0.903, 0.778, 0.907, 0.783 |
| PropertySetChanged | 0.894, 0.859, 0.900, 1.012, 0.844 | 0.881, 0.886, 0.863, 0.955, 0.862 |
| PropertySetSame | 0.837, 0.775, 0.809, 0.778, 0.731 | 0.743, 0.695, 0.685, 0.667, 0.706 |
| PropertySubscribeDispose | 1.678, 1.744, 1.443, 1.644, 1.347 | 2.610, 2.677, 2.368, 2.331, 2.293 |
| WhereSelectOnNext | 0.932, 0.938, 0.940, 0.941, 0.926 | 0.916, 0.963, 0.956, 0.894, 0.924 |
| ChainSubscribeDispose | 2.759, 2.521, 2.401, 3.008, 2.321 | 1.693, 1.791, 1.510, 1.855, 1.464 |
| CombineLatestOnNext | 0.178, 0.192, 0.245, 0.151, 0.178 | 0.161, 0.152, 0.174, 0.152, 0.159 |

## Mono

- Build GUID `b2689d46c2bf4c8ebeacf8db9cb58bc9`, Unity 2022.3.62f2, Onity 0.6.0, source 4e92db959023180fa6530e5d0867153b9ff6047e
- CPU: AMD Ryzen 9 5900X 12-Core Processor 
- Processes: mono-reactive-p1, mono-reactive-p2, mono-reactive-p3, mono-reactive-p4, mono-reactive-p5

| Scenario | Ops/sample | Onity ns/op | R3 ns/op | UniRx ns/op | Onity/R3 median (worst) | vs R3 | Onity/UniRx median (worst) | vs UniRx |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: | --- |
| SubjectOnNext1 | 200000 | 41.83 | 9.00 | 5.25 | 4.574 (4.730) | slower | 8.092 (8.443) | slower |
| SubjectOnNext8 | 50000 | 255.08 | 51.47 | 36.35 | 5.023 (5.377) | slower | 7.101 (7.628) | slower |
| SubjectSubscribeDispose | 20000 | 392.00 | 290.25 | 510.24 | 1.362 (1.391) | slower | 0.766 (0.789) | faster |
| PropertySetChanged | 200000 | 55.61 | 18.61 | 13.49 | 2.966 (3.553) | slower | 4.182 (4.322) | slower |
| PropertySetSame | 200000 | 7.16 | 4.75 | 5.10 | 1.519 (1.551) | slower | 1.396 (1.572) | slower |
| PropertySubscribeDispose | 20000 | 435.83 | 272.14 | 193.40 | 1.581 (1.773) | slower | 2.355 (2.375) | slower |
| WhereSelectOnNext | 200000 | 35.36 | 13.07 | 8.73 | 2.651 (2.800) | slower | 4.045 (4.320) | slower |
| ChainSubscribeDispose | 10000 | 1205.68 | 569.46 | 746.52 | 2.143 (2.286) | slower | 1.648 (1.703) | slower |
| CombineLatestOnNext | 100000 | 50.79 | 35.15 | 30.99 | 1.445 (1.516) | slower | 1.652 (1.663) | slower |

Allocation: unavailable on this backend (Unavailable: the positive control read 0 bytes for a 64 KiB array and the empty control read 0 bytes.).

Per-process ratios:

| Scenario | Onity/R3 per process | Onity/UniRx per process |
| --- | --- | --- |
| SubjectOnNext1 | 4.522, 4.730, 4.483, 4.574, 4.654 | 7.613, 8.092, 8.143, 8.443, 7.966 |
| SubjectOnNext8 | 4.964, 4.891, 5.244, 5.377, 5.023 | 7.017, 7.018, 7.273, 7.628, 7.101 |
| SubjectSubscribeDispose | 1.350, 1.284, 1.391, 1.362, 1.387 | 0.777, 0.754, 0.766, 0.766, 0.789 |
| PropertySetChanged | 2.909, 2.958, 3.422, 3.553, 2.966 | 4.161, 4.246, 4.123, 4.322, 4.182 |
| PropertySetSame | 1.519, 1.523, 1.429, 1.366, 1.551 | 1.396, 1.572, 1.356, 1.352, 1.438 |
| PropertySubscribeDispose | 1.581, 1.742, 1.521, 1.773, 1.565 | 2.225, 2.375, 2.355, 2.126, 2.358 |
| WhereSelectOnNext | 2.610, 2.599, 2.720, 2.800, 2.651 | 4.320, 3.936, 3.978, 4.045, 4.050 |
| ChainSubscribeDispose | 2.178, 2.070, 2.143, 2.286, 2.087 | 1.615, 1.663, 1.703, 1.648, 1.543 |
| CombineLatestOnNext | 1.445, 1.499, 1.399, 1.443, 1.516 | 1.618, 1.565, 1.656, 1.652, 1.663 |

