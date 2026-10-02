# Messaging comparison summary

- Evidence root: `<host>\BenchmarkResults\messaging-attempt-1-2026-10-02`
- Generated (UTC): 2026-10-02T18:47:15.860577+00:00
- Reports: 6 (3 per backend), measured samples per process: 15, warmups: 2
- Libraries: Onity 0.7.0, MessagePipe 1.8.1
- MessagePipe dll SHA-256: `cf8a702ab31bbb7da9ea6de4349f6dec9e214be396234f7e527d6ebe06a94f1b`
- Rule (pre-registered): ratio = Onity process median / MessagePipe process median (time per operation, so below 1 means Onity is faster); faster when the median ratio over processes <= 0.95 and the worst process ratio <= 1.00; slower when the median ratio >= 1.05; otherwise on par.
- Gate rule (pre-registered): IL2CPP, 3 processes, every row faster (9 rows, or the 7 synchronous rows if the async rows were dropped); Mono is classified the same way and reported, not gated.
- Gate (IL2CPP): **PASS**

## IL2CPP (gated)

- Build GUID `0cb887bb53e644c1b40849bfbcfe9cfc`, Unity 2022.3.62f2, Onity 0.7.0, MessagePipe 1.8.1+e901746b927bcd84ad94d642512a7a7a7600d15b, source 4cc5763dcd8512cf4336aa6e31c8baf0c4fbd40d
- CPU: AMD Ryzen 9 5900X 12-Core Processor ; GC mode Enabled
- Processes: il2cpp-messaging-p1, il2cpp-messaging-p2, il2cpp-messaging-p3

| Scenario | Ops/sample | Onity ns/op | MessagePipe ns/op | Onity/MessagePipe per process | Median ratio | Worst ratio | Class |
| --- | ---: | ---: | ---: | --- | ---: | ---: | --- |
| PublishNoSubscribers | 200000 | 2.80 | 7.16 | 0.391, 0.459, 0.493 | 0.459 | 0.493 | faster |
| Publish1 | 200000 | 9.22 | 9.47 | 0.974, 0.660, 0.807 | 0.807 | 0.974 | faster |
| Publish8 | 50000 | 17.98 | 35.14 | 0.513, 0.554, 0.512 | 0.513 | 0.554 | faster |
| SubscribeDispose | 20000 | 99.34 | 484.50 | 0.205, 0.197, 0.137 | 0.197 | 0.205 | faster |
| SubscribeDispose64 | 20000 | 114.17 | 495.46 | 0.230, 0.218, 0.146 | 0.218 | 0.230 | faster |
| KeyedPublish | 200000 | 26.43 | 59.43 | 0.482, 0.389, 0.427 | 0.427 | 0.482 | faster |
| KeyedSubscribeDispose | 20000 | 118.67 | 529.00 | 0.224, 0.243, 0.163 | 0.224 | 0.243 | faster |
| AsyncPublish1 | 100000 | 24.95 | 78.49 | 0.273, 0.334, 0.313 | 0.313 | 0.334 | faster |
| AsyncPublish8 | 25000 | 95.31 | 167.42 | 0.569, 0.532, 0.604 | 0.569 | 0.604 | faster |

Process medians (ns/op) and gen-0 collections during the measured samples (sum over processes):

| Scenario | Onity per process | MessagePipe per process | Onity GCs | MessagePipe GCs |
| --- | --- | --- | ---: | ---: |
| PublishNoSubscribers | 2.800, 4.398, 2.555 | 7.158, 9.585, 5.181 | 0 | 0 |
| Publish1 | 9.223, 10.185, 6.902 | 9.475, 15.428, 8.556 | 0 | 0 |
| Publish8 | 17.734, 29.248, 17.980 | 34.586, 52.832, 35.138 | 0 | 0 |
| SubscribeDispose | 99.340, 135.420, 55.545 | 484.495, 685.940, 406.915 | 93 | 156 |
| SubscribeDispose64 | 114.170, 132.465, 55.330 | 495.465, 606.660, 379.390 | 92 | 154 |
| KeyedPublish | 28.627, 26.427, 25.275 | 59.428, 67.966, 59.129 | 0 | 0 |
| KeyedSubscribeDispose | 118.670, 141.520, 70.725 | 528.995, 583.010, 433.530 | 92 | 154 |
| AsyncPublish1 | 28.219, 24.948, 24.551 | 103.427, 74.763, 78.489 | 0 | 0 |
| AsyncPublish8 | 95.308, 89.416, 100.728 | 167.424, 168.148, 166.876 | 0 | 0 |

Allocation: unavailable on this backend (Unavailable on IL2CPP: the counter is not called because the DI harness observed IL2CPP crashes with it.).

## Mono (reported)

- Build GUID `0524d27810d248e289a02c5b18b279ac`, Unity 2022.3.62f2, Onity 0.7.0, MessagePipe 1.8.1+e901746b927bcd84ad94d642512a7a7a7600d15b, source 4cc5763dcd8512cf4336aa6e31c8baf0c4fbd40d
- CPU: AMD Ryzen 9 5900X 12-Core Processor ; GC mode Enabled
- Processes: mono-messaging-p1, mono-messaging-p2, mono-messaging-p3

| Scenario | Ops/sample | Onity ns/op | MessagePipe ns/op | Onity/MessagePipe per process | Median ratio | Worst ratio | Class |
| --- | ---: | ---: | ---: | --- | ---: | ---: | --- |
| PublishNoSubscribers | 200000 | 5.67 | 8.00 | 0.709, 0.697, 0.718 | 0.709 | 0.718 | faster |
| Publish1 | 200000 | 8.11 | 14.38 | 0.561, 0.570, 0.707 | 0.570 | 0.707 | faster |
| Publish8 | 50000 | 28.41 | 37.30 | 0.722, 0.830, 0.762 | 0.762 | 0.830 | faster |
| SubscribeDispose | 20000 | 102.46 | 322.32 | 0.268, 0.334, 0.319 | 0.319 | 0.334 | faster |
| SubscribeDispose64 | 20000 | 83.58 | 297.21 | 0.271, 0.280, 0.314 | 0.280 | 0.314 | faster |
| KeyedPublish | 200000 | 23.66 | 43.16 | 0.548, 0.563, 0.529 | 0.548 | 0.563 | faster |
| KeyedSubscribeDispose | 20000 | 92.61 | 374.80 | 0.240, 0.287, 0.341 | 0.287 | 0.341 | faster |
| AsyncPublish1 | 100000 | 46.78 | 162.69 | 0.286, 0.298, 0.287 | 0.287 | 0.298 | faster |
| AsyncPublish8 | 25000 | 214.63 | 426.96 | 0.522, 0.503, 0.543 | 0.522 | 0.543 | faster |

Process medians (ns/op) and gen-0 collections during the measured samples (sum over processes):

| Scenario | Onity per process | MessagePipe per process | Onity GCs | MessagePipe GCs |
| --- | --- | --- | ---: | ---: |
| PublishNoSubscribers | 5.668, 5.510, 6.223 | 7.997, 7.902, 8.669 | 0 | 0 |
| Publish1 | 8.060, 8.110, 10.688 | 14.377, 14.236, 15.108 | 0 | 0 |
| Publish8 | 23.552, 30.966, 28.412 | 32.616, 37.322, 37.298 | 0 | 0 |
| SubscribeDispose | 102.460, 88.145, 102.895 | 382.625, 264.125, 322.320 | 71 | 145 |
| SubscribeDispose64 | 83.575, 75.725, 93.405 | 308.655, 270.160, 297.215 | 72 | 140 |
| KeyedPublish | 23.664, 23.352, 32.083 | 43.155, 41.493, 60.621 | 0 | 0 |
| KeyedSubscribeDispose | 92.610, 91.665, 127.815 | 385.355, 318.905, 374.795 | 69 | 178 |
| AsyncPublish1 | 46.532, 46.779, 49.548 | 162.689, 156.894, 172.529 | 0 | 0 |
| AsyncPublish8 | 208.156, 214.632, 232.116 | 398.692, 426.964, 427.156 | 0 | 0 |

Allocation: unavailable on this backend (Unavailable: the positive control read 0 bytes for a 64 KiB array and the empty control read 0 bytes.).

## Process observations (informational)

| Process | Exit | Wall s | Priority | Affinity | Other Unity CPU s | Fraction of wall | Noise |
| --- | ---: | ---: | --- | --- | ---: | ---: | --- |
| il2cpp-messaging-p1 | 0 | 2.6 | High | 0xfffffc | 0.38 | 0.147 | flagged |
| il2cpp-messaging-p2 | 0 | 2.4 | High | 0xfffffc | 0.28 | 0.119 | flagged |
| il2cpp-messaging-p3 | 0 | 2.0 | High | 0xfffffc | 0.25 | 0.126 | flagged |
| mono-messaging-p1 | 0 | 5.4 | High | 0xfffffc | 0.67 | 0.125 | flagged |
| mono-messaging-p2 | 0 | 2.4 | High | 0xfffffc | 0.25 | 0.103 | flagged |
| mono-messaging-p3 | 0 | 2.9 | High | 0xfffffc | 2.44 | 0.841 | flagged |

Noise rule: Other Unity Editor CPU <= 0.5 s and <= 5% of wall time; a missing, new or unreadable process is unknown. Informational.
