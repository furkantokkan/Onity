# Reactive comparison summary

- Evidence root: `<host path>`
- Generated (UTC): 2026-10-02T15:44:57.487546+00:00
- Reports: 6 (3 per backend), measured samples per process: 15, warmups: 2
- Libraries: Onity 0.6.0, R3 1.3.0, UniRx 7.1.0
- Rule (pre-registered): ratio = Onity process median / other library process median (time per operation, so below 1 means Onity is faster); faster when the median ratio over processes <= 0.95 and the worst process ratio <= 1.00; slower when the median ratio >= 1.05; otherwise on par.
- IL2CPP is the headline; Mono is reported the same way.

## IL2CPP (headline)

- Build GUID `ac2bdd1c782842fc8d27e90da330fd26`, Unity 2022.3.62f2, Onity 0.6.0, source 36c68c580b7ce622a0132cf952336b6aa93c64c7
- CPU: AMD Ryzen 9 5900X 12-Core Processor 
- Processes: il2cpp-reactive-p1, il2cpp-reactive-p2, il2cpp-reactive-p3

| Scenario | Ops/sample | Onity ns/op | R3 ns/op | UniRx ns/op | Onity/R3 median (worst) | vs R3 | Onity/UniRx median (worst) | vs UniRx |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: | --- |
| SubjectOnNext1 | 200000 | 3.23 | 4.39 | 3.70 | 0.737 (0.739) | faster | 0.872 (0.874) | faster |
| SubjectOnNext8 | 50000 | 15.45 | 23.84 | 29.68 | 0.634 (0.666) | faster | 0.521 (0.524) | faster |
| SubjectSubscribeDispose | 20000 | 90.34 | 265.85 | 513.75 | 0.340 (0.365) | faster | 0.183 (0.196) | faster |
| PropertySetChanged | 200000 | 6.56 | 11.76 | 12.26 | 0.558 (0.558) | faster | 0.535 (0.541) | faster |
| PropertySetSame | 200000 | 4.69 | 6.59 | 7.10 | 0.713 (0.716) | faster | 0.659 (0.660) | faster |
| PropertySubscribeDispose | 20000 | 103.69 | 289.68 | 180.90 | 0.368 (0.370) | faster | 0.566 (0.589) | faster |
| WhereSelectOnNext | 200000 | 7.33 | 9.10 | 9.86 | 0.805 (0.811) | faster | 0.758 (0.812) | faster |
| ChainSubscribeDispose | 10000 | 307.18 | 541.54 | 871.02 | 0.567 (0.593) | faster | 0.353 (0.396) | faster |
| CombineLatestOnNext | 100000 | 6.28 | 43.47 | 48.51 | 0.145 (0.146) | faster | 0.129 (0.131) | faster |

Allocation: unavailable on this backend (Unavailable on IL2CPP: the counter is not called because the DI harness observed IL2CPP crashes with it.).

Per-process ratios:

| Scenario | Onity/R3 per process | Onity/UniRx per process |
| --- | --- | --- |
| SubjectOnNext1 | 0.737, 0.736, 0.739 | 0.872, 0.872, 0.874 |
| SubjectOnNext8 | 0.634, 0.630, 0.666 | 0.521, 0.515, 0.524 |
| SubjectSubscribeDispose | 0.365, 0.340, 0.340 | 0.196, 0.183, 0.176 |
| PropertySetChanged | 0.558, 0.558, 0.556 | 0.535, 0.532, 0.541 |
| PropertySetSame | 0.713, 0.711, 0.716 | 0.655, 0.660, 0.659 |
| PropertySubscribeDispose | 0.350, 0.370, 0.368 | 0.566, 0.565, 0.589 |
| WhereSelectOnNext | 0.805, 0.811, 0.772 | 0.720, 0.812, 0.758 |
| ChainSubscribeDispose | 0.593, 0.567, 0.561 | 0.396, 0.353, 0.338 |
| CombineLatestOnNext | 0.145, 0.143, 0.146 | 0.129, 0.128, 0.131 |

## Mono

- Build GUID `a75b0ac2272f4f6584fc5535fefb8a38`, Unity 2022.3.62f2, Onity 0.6.0, source 36c68c580b7ce622a0132cf952336b6aa93c64c7
- CPU: AMD Ryzen 9 5900X 12-Core Processor 
- Processes: mono-reactive-p1, mono-reactive-p2, mono-reactive-p3

| Scenario | Ops/sample | Onity ns/op | R3 ns/op | UniRx ns/op | Onity/R3 median (worst) | vs R3 | Onity/UniRx median (worst) | vs UniRx |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: | --- |
| SubjectOnNext1 | 200000 | 5.26 | 8.73 | 4.79 | 0.603 (0.605) | faster | 1.098 (1.099) | slower |
| SubjectOnNext8 | 50000 | 24.85 | 52.85 | 33.68 | 0.470 (0.471) | faster | 0.738 (0.744) | faster |
| SubjectSubscribeDispose | 20000 | 94.94 | 284.32 | 471.77 | 0.330 (0.357) | faster | 0.201 (0.215) | faster |
| PropertySetChanged | 200000 | 8.62 | 19.05 | 13.23 | 0.452 (0.452) | faster | 0.644 (0.654) | faster |
| PropertySetSame | 200000 | 5.37 | 5.15 | 5.32 | 1.040 (1.043) | on par | 1.001 (1.016) | on par |
| PropertySubscribeDispose | 20000 | 104.77 | 258.94 | 186.87 | 0.405 (0.419) | faster | 0.561 (0.568) | faster |
| WhereSelectOnNext | 200000 | 8.83 | 12.16 | 8.51 | 0.719 (0.744) | faster | 1.015 (1.104) | on par |
| ChainSubscribeDispose | 10000 | 329.70 | 539.45 | 708.26 | 0.614 (0.615) | faster | 0.466 (0.483) | faster |
| CombineLatestOnNext | 100000 | 45.70 | 33.50 | 29.92 | 1.378 (1.390) | slower | 1.527 (1.551) | slower |

Allocation: unavailable on this backend (Unavailable: the positive control read 0 bytes for a 64 KiB array and the empty control read 0 bytes.).

Per-process ratios:

| Scenario | Onity/R3 per process | Onity/UniRx per process |
| --- | --- | --- |
| SubjectOnNext1 | 0.605, 0.597, 0.603 | 1.099, 1.097, 1.098 |
| SubjectOnNext8 | 0.471, 0.470, 0.464 | 0.733, 0.738, 0.744 |
| SubjectSubscribeDispose | 0.330, 0.312, 0.357 | 0.201, 0.183, 0.215 |
| PropertySetChanged | 0.447, 0.452, 0.452 | 0.644, 0.654, 0.597 |
| PropertySetSame | 1.043, 1.040, 0.828 | 1.000, 1.001, 1.016 |
| PropertySubscribeDispose | 0.371, 0.405, 0.419 | 0.534, 0.561, 0.568 |
| WhereSelectOnNext | 0.719, 0.713, 0.744 | 1.015, 0.999, 1.104 |
| ChainSubscribeDispose | 0.614, 0.610, 0.615 | 0.466, 0.458, 0.483 |
| CombineLatestOnNext | 1.390, 1.378, 1.350 | 1.551, 1.527, 1.527 |

