# Two-input OnityTask WhenAll corrected pending lifecycle

- Unity: 2022.3.62f3; Mono
- UniTask: 2e993ff18f28c931602a07292df0b0804eebef99
- OnityAsync SHA-256: 00fe9203a9b32e389525873f649841c44671fcc87134b5984eb4d9c6aa9d0007
- Runner SHA-256: a696143fd85b99a51653a8fb67e603e07f04a5ea5c38efa19f67cf95a99a754d
- Samples: 8; operations/sample: 2048 timing for success, 256 timing for fault/cancel, 128 allocation; warmup batches: 10
- Allocation counter: Unity Profiler GC.Alloc metadata; 65,568/0-byte controls passed.
- Controls: 65568 B positive; 0 B empty.
- Worker controls: 0 B positive; 0 B empty.
- Worker allocation unavailable: InvalidDataException: Worker allocation controls failed.
- All-thread window controls: main positive 65568/0 B (main/other), empty 0/0 B, ThreadPool positive 40/65568 B.
- First pending schedule; tracker off, OnityTask: 1226 raw B; first call in a warmed Editor process, one sample per process.
- 384 outstanding pending schedule; tracker off, OnityTask: 163840,115712,115712,115712,115712,115712,115712,115712 raw B for 384 simultaneous outputs per sample.
- First pending schedule; tracker off, UniTask: 2032 raw B; first call in a warmed Editor process, one sample per process.
- 384 outstanding pending schedule; tracker off, UniTask: 81920,61440,61440,61440,61440,61440,61440,61440 raw B for 384 simultaneous outputs per sample.

All-thread lifecycle samples include a tracked-batch completion barrier. Raw totals include benchmark barrier overhead; they are reported separately from the main-thread marker slices.
- Pending success lifecycle; tracker on, OnityTask: all-thread raw B 137604,139592,137472,139592,137472,139592,137472,139592; other-thread raw B 4352,4352,4352,4352,4352,4352,4352,4352.
- Pending success lifecycle; tracker on, UniTask: all-thread raw B 20480,20480,20480,20480,20480,20480,20480,20480; other-thread raw B 0,0,0,0,0,0,0,0.
- Pending fault lifecycle; tracker on, OnityTask: all-thread raw B 462336,464456,462336,464456,462336,464456,462336,464456; other-thread raw B 40192,40192,40192,40192,40192,40192,40192,40192.
- Pending fault lifecycle; tracker on, UniTask: all-thread raw B 213248,213248,213248,213248,213248,213248,213248,213248; other-thread raw B 0,0,0,0,0,0,0,0.
- Pending cancel lifecycle; tracker on, OnityTask: all-thread raw B 236800,238920,236800,238920,236800,238920,236800,238920; other-thread raw B 4352,4352,4352,4352,4352,4352,4352,4352.
- Pending cancel lifecycle; tracker on, UniTask: all-thread raw B 76800,76800,76800,76800,76800,76800,76800,76800; other-thread raw B 0,0,0,0,0,0,0,0.

Same two-argument WhenAll call per operation. Fresh unresolved inputs are prepared outside each slice. Scheduling scenarios include WhenAll and storage; lifecycle scenarios include scheduling, input completion, output GetResult, and cleanup. Input construction is outside both slices. Fault cases use a fresh exception per operation, prepared outside the slice; cancel cases use a precreated token. Tracker-on matches Onity default; tracker-off changes only Onity. UniTask stays at its default. Profiler lifecycle bytes cover only the main-thread marker; tracker continuations may allocate later on worker threads. Samples include harness cost without subtraction. Editor/Mono only; no Player, frame-time, or IL2CPP result.

| Scenario | Library | Mean ns/op | Median ns/op | Range ns/op | SD ns/op | Main-marker B/op | Worker B/op |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Pending success lifecycle; tracker on | OnityTask | 7981.072998046875 | 7962.6953125 | 7458.544921875–8862.451171875 | 411.53502875697745 | 1048.28125 | -1 |
| Pending success lifecycle; tracker on | UniTask | 1521.484375 | 1493.5546875 | 1444.482421875–1805.517578125 | 109.668783998389 | 160 | -1 |
| Pending success lifecycle; tracker off | OnityTask | 2258.319091796875 | 2264.9658203125 | 2219.580078125–2291.9921875 | 23.192163536965591 | 176 | -1 |
| Pending success lifecycle; tracker off | UniTask | 1656.036376953125 | 1542.4560546875 | 1432.666015625–2296.826171875 | 287.03814180798111 | 160 | -1 |
| Pending fault lifecycle; tracker on | OnityTask | 31892.822265625 | 31297.65625 | 30003.125–33944.140625 | 1454.7508406242516 | 3306.28125 | -1 |
| Pending fault lifecycle; tracker on | UniTask | 16309.1796875 | 15853.515625 | 15385.9375–18682.03125 | 1014.360503388692 | 1666 | -1 |
| Pending fault lifecycle; tracker off | OnityTask | 26530.95703125 | 26131.25 | 24116.015625–29773.828125 | 1617.3482444596809 | 2434 | -1 |
| Pending fault lifecycle; tracker off | UniTask | 15996.77734375 | 15910.9375 | 15434.375–16625 | 381.22952515701144 | 1666 | -1 |
| Pending cancel lifecycle; tracker on | OnityTask | 19143.5546875 | 18224.4140625 | 17777.34375–25782.8125 | 2520.5267458606982 | 1824.28125 | -1 |
| Pending cancel lifecycle; tracker on | UniTask | 7709.765625 | 7723.6328125 | 7526.953125–7861.71875 | 107.90233462886415 | 600 | -1 |
| Pending cancel lifecycle; tracker off | OnityTask | 12283.984375 | 12277.734375 | 12082.03125–12508.59375 | 128.5488653820625 | 952 | -1 |
| Pending cancel lifecycle; tracker off | UniTask | 7909.423828125 | 7863.28125 | 7654.6875–8572.265625 | 272.3644148514752 | 600 | -1 |
