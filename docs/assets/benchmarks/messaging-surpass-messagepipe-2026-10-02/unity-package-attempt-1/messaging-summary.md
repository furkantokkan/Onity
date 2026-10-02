# Messaging comparison summary

- Evidence root: `<host>\BenchmarkResults\messaging-unitypkg-attempt-1-2026-10-02`
- Generated (UTC): 2026-10-02T19:45:48.344043+00:00
- Reports: 6 (3 per backend), measured samples per process: 15, warmups: 2
- Libraries: Onity 0.7.0, MessagePipe 1.8.1
- MessagePipe flavor: `unity-package`; .unitypackage SHA-256 `36ff7aa0611272e22c0c596616f2b01dfb42507a206194d9da2a11d8976544b0`; 54 package entries verified in the project; UniTask 2.5.10 (tree SHA-256 `0fa45e04f49edd24b0e1981fdb8b894cd4fd660a0e2cc47c9cdb865f05d820bd`)
- Rule (pre-registered): ratio = Onity process median / MessagePipe process median (time per operation, so below 1 means Onity is faster); faster when the median ratio over processes <= 0.95 and the worst process ratio <= 1.00; slower when the median ratio >= 1.05; otherwise on par.
- Gate rule (pre-registered): IL2CPP, 3 processes, every row faster (9 rows, or the 7 synchronous rows if the async rows were dropped); Mono is classified the same way and reported, not gated.
- Gate (IL2CPP): **PASS**

## IL2CPP (gated)

- Build GUID `da5c33248906489dae7aed44387f988a`, Unity 2022.3.62f2, Onity 0.7.0, MessagePipe 1.8.1 (unity-package), source 52e209a5fbe757ef3b8702b4c1724feae3e391bd
- CPU: AMD Ryzen 9 5900X 12-Core Processor ; GC mode Enabled
- Processes: il2cpp-messaging-p1, il2cpp-messaging-p2, il2cpp-messaging-p3

| Scenario | Ops/sample | Onity ns/op | MessagePipe ns/op | Onity/MessagePipe per process | Median ratio | Worst ratio | Class |
| --- | ---: | ---: | ---: | --- | ---: | ---: | --- |
| PublishNoSubscribers | 200000 | 2.25 | 5.32 | 0.423, 0.376, 0.416 | 0.416 | 0.423 | faster |
| Publish1 | 200000 | 6.21 | 9.16 | 0.767, 0.651, 0.726 | 0.726 | 0.767 | faster |
| Publish8 | 50000 | 20.97 | 39.40 | 0.532, 0.483, 0.526 | 0.526 | 0.532 | faster |
| SubscribeDispose | 20000 | 63.49 | 377.52 | 0.193, 0.164, 0.168 | 0.168 | 0.193 | faster |
| SubscribeDispose64 | 20000 | 60.13 | 417.30 | 0.216, 0.129, 0.150 | 0.150 | 0.216 | faster |
| KeyedPublish | 200000 | 28.35 | 67.83 | 0.418, 0.346, 0.436 | 0.418 | 0.436 | faster |
| KeyedSubscribeDispose | 20000 | 104.37 | 552.26 | 0.189, 0.172, 0.164 | 0.172 | 0.189 | faster |
| AsyncPublish1 | 100000 | 27.32 | 43.86 | 0.623, 0.740, 0.621 | 0.623 | 0.740 | faster |
| AsyncPublish8 | 25000 | 103.19 | 126.20 | 0.818, 0.846, 0.895 | 0.846 | 0.895 | faster |

Process medians (ns/op) and gen-0 collections during the measured samples (sum over processes):

| Scenario | Onity per process | MessagePipe per process | Onity GCs | MessagePipe GCs |
| --- | --- | --- | ---: | ---: |
| PublishNoSubscribers | 2.247, 2.344, 2.129 | 5.316, 6.237, 5.118 | 0 | 0 |
| Publish1 | 9.256, 5.963, 6.214 | 12.074, 9.155, 8.559 | 0 | 0 |
| Publish8 | 20.966, 17.154, 27.298 | 39.398, 35.522, 51.906 | 0 | 0 |
| SubscribeDispose | 80.240, 57.575, 63.490 | 416.435, 350.040, 377.525 | 93 | 156 |
| SubscribeDispose64 | 90.090, 55.590, 60.135 | 417.295, 430.085, 401.810 | 92 | 153 |
| KeyedPublish | 28.354, 29.181, 26.508 | 67.833, 84.407, 60.768 | 0 | 0 |
| KeyedSubscribeDispose | 104.370, 107.035, 74.020 | 552.265, 622.130, 451.135 | 90 | 155 |
| AsyncPublish1 | 27.318, 36.674, 26.839 | 43.858, 49.544, 43.238 | 0 | 0 |
| AsyncPublish8 | 103.192, 151.472, 102.252 | 126.200, 179.096, 114.268 | 0 | 0 |

Allocation: unavailable on this backend (Unavailable on IL2CPP: the counter is not called because the DI harness observed IL2CPP crashes with it.).

## Mono (reported)

- Build GUID `4c7b337f8fb04fb3aa4c3b72f8b2f081`, Unity 2022.3.62f2, Onity 0.7.0, MessagePipe 1.8.1 (unity-package), source 52e209a5fbe757ef3b8702b4c1724feae3e391bd
- CPU: AMD Ryzen 9 5900X 12-Core Processor ; GC mode Enabled
- Processes: mono-messaging-p1, mono-messaging-p2, mono-messaging-p3

| Scenario | Ops/sample | Onity ns/op | MessagePipe ns/op | Onity/MessagePipe per process | Median ratio | Worst ratio | Class |
| --- | ---: | ---: | ---: | --- | ---: | ---: | --- |
| PublishNoSubscribers | 200000 | 5.72 | 7.95 | 0.720, 0.616, 0.706 | 0.706 | 0.720 | faster |
| Publish1 | 200000 | 8.09 | 14.37 | 0.563, 0.682, 0.559 | 0.563 | 0.682 | faster |
| Publish8 | 50000 | 25.43 | 33.92 | 0.786, 0.697, 0.756 | 0.756 | 0.786 | faster |
| SubscribeDispose | 20000 | 80.04 | 294.98 | 0.265, 0.334, 0.297 | 0.297 | 0.334 | faster |
| SubscribeDispose64 | 20000 | 82.89 | 296.37 | 0.287, 0.280, 0.253 | 0.280 | 0.287 | faster |
| KeyedPublish | 200000 | 26.04 | 44.47 | 0.586, 0.539, 0.529 | 0.539 | 0.586 | faster |
| KeyedSubscribeDispose | 20000 | 98.78 | 338.57 | 0.278, 0.292, 0.304 | 0.292 | 0.304 | faster |
| AsyncPublish1 | 100000 | 59.80 | 117.66 | 0.508, 0.514, 0.548 | 0.514 | 0.548 | faster |
| AsyncPublish8 | 25000 | 255.98 | 366.75 | 0.665, 0.662, 0.862 | 0.665 | 0.862 | faster |

Process medians (ns/op) and gen-0 collections during the measured samples (sum over processes):

| Scenario | Onity per process | MessagePipe per process | Onity GCs | MessagePipe GCs |
| --- | --- | --- | ---: | ---: |
| PublishNoSubscribers | 5.717, 6.513, 5.572 | 7.945, 10.568, 7.894 | 0 | 0 |
| Publish1 | 8.085, 10.905, 7.896 | 14.369, 15.992, 14.114 | 0 | 0 |
| Publish8 | 26.662, 25.428, 25.314 | 33.924, 36.500, 33.478 | 0 | 0 |
| SubscribeDispose | 78.245, 112.150, 80.040 | 294.975, 335.845, 269.740 | 69 | 141 |
| SubscribeDispose64 | 96.605, 82.885, 71.455 | 336.125, 296.370, 282.435 | 66 | 141 |
| KeyedPublish | 26.038, 29.831, 23.232 | 44.465, 55.392, 43.930 | 0 | 0 |
| KeyedSubscribeDispose | 104.795, 98.785, 97.755 | 376.825, 338.575, 321.610 | 71 | 174 |
| AsyncPublish1 | 59.795, 47.363, 73.844 | 117.655, 92.178, 134.768 | 0 | 0 |
| AsyncPublish8 | 242.404, 255.980, 316.076 | 364.700, 386.896, 366.752 | 0 | 0 |

Allocation: unavailable on this backend (Unavailable: the positive control read 0 bytes for a 64 KiB array and the empty control read 0 bytes.).

## Process observations (informational)

| Process | Exit | Wall s | Priority | Affinity | Other Unity CPU s | Fraction of wall | Noise |
| --- | ---: | ---: | --- | --- | ---: | ---: | --- |
| il2cpp-messaging-p1 | 0 | 2.5 | High | 0xfffffc | 0.80 | 0.325 | flagged |
| il2cpp-messaging-p2 | 0 | 2.4 | High | 0xfffffc | 0.36 | 0.147 | flagged |
| il2cpp-messaging-p3 | 0 | 2.2 | High | 0xfffffc | 0.78 | 0.360 | flagged |
| mono-messaging-p1 | 0 | 2.6 | High | 0xfffffc | 0.31 | 0.118 | unknown |
| mono-messaging-p2 | 0 | 2.6 | High | 0xfffffc | 1.80 | 0.699 | flagged |
| mono-messaging-p3 | 0 | 2.4 | High | 0xfffffc | 10.11 | 4.169 | flagged |

Noise rule: Other Unity Editor CPU <= 0.5 s and <= 5% of wall time; a missing, new or unreadable process is unknown. Informational.
