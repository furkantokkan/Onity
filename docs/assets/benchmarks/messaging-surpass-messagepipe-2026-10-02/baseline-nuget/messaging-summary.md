# Messaging comparison summary

- Evidence root: `<host>\BenchmarkResults\messaging-baseline-2026-10-02`
- Generated (UTC): 2026-10-02T17:40:09.905632+00:00
- Reports: 6 (3 per backend), measured samples per process: 15, warmups: 2
- Libraries: Onity 0.7.0, MessagePipe 1.8.1
- MessagePipe dll SHA-256: `cf8a702ab31bbb7da9ea6de4349f6dec9e214be396234f7e527d6ebe06a94f1b`
- Rule (pre-registered): ratio = Onity process median / MessagePipe process median (time per operation, so below 1 means Onity is faster); faster when the median ratio over processes <= 0.95 and the worst process ratio <= 1.00; slower when the median ratio >= 1.05; otherwise on par.
- Gate rule (pre-registered): IL2CPP, 3 processes, every row faster (9 rows, or the 7 synchronous rows if the async rows were dropped); Mono is classified the same way and reported, not gated.
- Gate (IL2CPP): **FAIL**; not faster: AsyncPublish1 (slower), AsyncPublish8 (slower)

## IL2CPP (gated)

- Build GUID `372b4fe8484c4f8b95c2ceb95a01dde8`, Unity 2022.3.62f2, Onity 0.7.0, MessagePipe 1.8.1+e901746b927bcd84ad94d642512a7a7a7600d15b, source 441c44129a2cc4becc16c2849b2b8cb5fa0f4e82
- CPU: AMD Ryzen 9 5900X 12-Core Processor ; GC mode Enabled
- Processes: il2cpp-messaging-p1, il2cpp-messaging-p2, il2cpp-messaging-p3

| Scenario | Ops/sample | Onity ns/op | MessagePipe ns/op | Onity/MessagePipe per process | Median ratio | Worst ratio | Class |
| --- | ---: | ---: | ---: | --- | ---: | ---: | --- |
| PublishNoSubscribers | 200000 | 3.98 | 6.06 | 0.656, 0.656, 0.662 | 0.656 | 0.662 | faster |
| Publish1 | 200000 | 5.59 | 10.37 | 0.573, 0.536, 0.539 | 0.539 | 0.573 | faster |
| Publish8 | 50000 | 16.73 | 39.93 | 0.424, 0.436, 0.367 | 0.424 | 0.436 | faster |
| SubscribeDispose | 20000 | 314.05 | 416.02 | 0.839, 0.748, 0.429 | 0.748 | 0.839 | faster |
| SubscribeDispose64 | 20000 | 356.03 | 411.58 | 0.964, 0.865, 0.580 | 0.865 | 0.964 | faster |
| KeyedPublish | 200000 | 24.11 | 54.56 | 0.440, 0.442, 0.439 | 0.440 | 0.442 | faster |
| KeyedSubscribeDispose | 20000 | 336.88 | 483.35 | 0.710, 0.697, 0.447 | 0.697 | 0.710 | faster |
| AsyncPublish1 | 100000 | 85.37 | 74.71 | 1.151, 1.122, 1.138 | 1.138 | 1.151 | slower |
| AsyncPublish8 | 25000 | 210.44 | 163.26 | 1.301, 1.190, 1.305 | 1.301 | 1.305 | slower |

Process medians (ns/op) and gen-0 collections during the measured samples (sum over processes):

| Scenario | Onity per process | MessagePipe per process | Onity GCs | MessagePipe GCs |
| --- | --- | --- | ---: | ---: |
| PublishNoSubscribers | 3.947, 3.977, 4.095 | 6.016, 6.058, 6.182 | 0 | 0 |
| Publish1 | 5.603, 5.558, 5.588 | 9.780, 10.366, 10.377 | 0 | 0 |
| Publish8 | 16.706, 17.392, 16.728 | 39.420, 39.930, 45.590 | 0 | 0 |
| SubscribeDispose | 349.010, 314.050, 155.465 | 416.025, 419.715, 362.055 | 429 | 162 |
| SubscribeDispose64 | 448.365, 356.030, 206.995 | 465.185, 411.580, 356.995 | 426 | 154 |
| KeyedPublish | 23.980, 24.110, 24.262 | 54.457, 54.565, 55.234 | 0 | 0 |
| KeyedSubscribeDispose | 344.705, 336.880, 200.370 | 485.770, 483.345, 448.000 | 430 | 155 |
| AsyncPublish1 | 85.367, 86.542, 85.047 | 74.149, 77.129, 74.708 | 0 | 0 |
| AsyncPublish8 | 210.436, 206.776, 213.128 | 161.732, 173.696, 163.264 | 0 | 0 |

Allocation: unavailable on this backend (Unavailable on IL2CPP: the counter is not called because the DI harness observed IL2CPP crashes with it.).

## Mono (reported)

- Build GUID `a26d274cbbf143c4b7ce628f032dae6d`, Unity 2022.3.62f2, Onity 0.7.0, MessagePipe 1.8.1+e901746b927bcd84ad94d642512a7a7a7600d15b, source 441c44129a2cc4becc16c2849b2b8cb5fa0f4e82
- CPU: AMD Ryzen 9 5900X 12-Core Processor ; GC mode Enabled
- Processes: mono-messaging-p1, mono-messaging-p2, mono-messaging-p3

| Scenario | Ops/sample | Onity ns/op | MessagePipe ns/op | Onity/MessagePipe per process | Median ratio | Worst ratio | Class |
| --- | ---: | ---: | ---: | --- | ---: | ---: | --- |
| PublishNoSubscribers | 200000 | 7.25 | 7.82 | 0.930, 0.920, 0.921 | 0.921 | 0.930 | faster |
| Publish1 | 200000 | 8.42 | 14.16 | 0.596, 0.608, 0.595 | 0.596 | 0.608 | faster |
| Publish8 | 50000 | 27.52 | 33.94 | 0.809, 0.824, 0.811 | 0.811 | 0.824 | faster |
| SubscribeDispose | 20000 | 256.85 | 273.05 | 0.889, 0.967, 0.959 | 0.959 | 0.967 | on par |
| SubscribeDispose64 | 20000 | 307.57 | 263.01 | 1.124, 1.243, 1.169 | 1.169 | 1.243 | slower |
| KeyedPublish | 200000 | 26.36 | 41.79 | 0.626, 0.640, 0.631 | 0.631 | 0.640 | faster |
| KeyedSubscribeDispose | 20000 | 280.64 | 324.24 | 0.798, 0.877, 0.861 | 0.861 | 0.877 | faster |
| AsyncPublish1 | 100000 | 177.15 | 152.40 | 1.173, 1.134, 1.151 | 1.151 | 1.173 | slower |
| AsyncPublish8 | 25000 | 501.12 | 402.19 | 1.276, 1.248, 1.246 | 1.248 | 1.276 | slower |

Process medians (ns/op) and gen-0 collections during the measured samples (sum over processes):

| Scenario | Onity per process | MessagePipe per process | Onity GCs | MessagePipe GCs |
| --- | --- | --- | ---: | ---: |
| PublishNoSubscribers | 7.276, 7.192, 7.248 | 7.822, 7.816, 7.867 | 0 | 0 |
| Publish1 | 8.406, 8.609, 8.425 | 14.101, 14.158, 14.164 | 0 | 0 |
| Publish8 | 27.370, 28.008, 27.516 | 33.836, 33.990, 33.940 | 0 | 0 |
| SubscribeDispose | 242.655, 270.625, 256.850 | 273.050, 279.965, 267.945 | 430 | 144 |
| SubscribeDispose64 | 290.340, 341.555, 307.570 | 258.235, 274.760, 263.015 | 426 | 135 |
| KeyedPublish | 26.229, 26.538, 26.360 | 41.914, 41.437, 41.788 | 0 | 0 |
| KeyedSubscribeDispose | 258.675, 280.780, 280.645 | 324.240, 320.050, 326.070 | 426 | 179 |
| AsyncPublish1 | 178.743, 177.151, 173.167 | 152.404, 156.204, 150.507 | 0 | 0 |
| AsyncPublish8 | 530.500, 495.856, 501.116 | 415.908, 397.188, 402.192 | 0 | 0 |

Allocation: unavailable on this backend (Unavailable: the positive control read 0 bytes for a 64 KiB array and the empty control read 0 bytes.).

## Process observations (informational)

| Process | Exit | Wall s | Priority | Affinity | Other Unity CPU s | Fraction of wall | Noise |
| --- | ---: | ---: | --- | --- | ---: | ---: | --- |
| il2cpp-messaging-p1 | 0 | 2.8 | High | 0xfffffc | 0.28 | 0.100 | flagged |
| il2cpp-messaging-p2 | 0 | 2.3 | High | 0xfffffc | 0.16 | 0.067 | flagged |
| il2cpp-messaging-p3 | 0 | 2.1 | High | 0xfffffc | 0.17 | 0.083 | flagged |
| mono-messaging-p1 | 0 | 4.9 | High | 0xfffffc | 0.98 | 0.200 | flagged |
| mono-messaging-p2 | 0 | 2.9 | High | 0xfffffc | 0.39 | 0.133 | flagged |
| mono-messaging-p3 | 0 | 2.9 | High | 0xfffffc | 0.31 | 0.109 | flagged |

Noise rule: Other Unity Editor CPU <= 0.5 s and <= 5% of wall time; a missing, new or unreadable process is unknown. Informational.
