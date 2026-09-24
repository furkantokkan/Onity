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
- Pending success lifecycle; tracker on, OnityTask: all-thread raw B 137604,140704,137472,139592,137472,139592,137472,139592; other-thread raw B 4352,5088,4352,4352,4352,4352,4352,4352.
- Pending success lifecycle; tracker on, UniTask: all-thread raw B 20480,20480,20480,20480,20480,20480,20480,20480; other-thread raw B 0,0,0,0,0,0,0,0.
- Pending fault lifecycle; tracker on, OnityTask: all-thread raw B 462336,464456,462336,464456,462336,464456,462336,464456; other-thread raw B 40192,40192,40192,40192,40192,40192,40192,40192.
- Pending fault lifecycle; tracker on, UniTask: all-thread raw B 213248,213248,213248,213248,213248,213248,213248,213248; other-thread raw B 0,0,0,0,0,0,0,0.
- Pending cancel lifecycle; tracker on, OnityTask: all-thread raw B 236800,238920,236800,238920,236800,238920,236800,238920; other-thread raw B 4352,4352,4352,4352,4352,4352,4352,4352.
- Pending cancel lifecycle; tracker on, UniTask: all-thread raw B 76800,76800,76800,76800,76800,76800,76800,76800; other-thread raw B 0,0,0,0,0,0,0,0.

Same two-argument WhenAll call per operation. Fresh unresolved inputs are prepared outside each slice. Scheduling scenarios include WhenAll and storage; lifecycle scenarios include scheduling, input completion, output GetResult, and cleanup. Input construction is outside both slices. Fault cases use a fresh exception per operation, prepared outside the slice; cancel cases use a precreated token. Tracker-on matches Onity default; tracker-off changes only Onity. UniTask stays at its default. Profiler lifecycle bytes cover only the main-thread marker; tracker continuations may allocate later on worker threads. Samples include harness cost without subtraction. Editor/Mono only; no Player, frame-time, or IL2CPP result.

| Scenario | Library | Mean ns/op | Median ns/op | Range ns/op | SD ns/op | Main-marker B/op | Worker B/op |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Pending success lifecycle; tracker on | OnityTask | 8361.688232421875 | 8055.56640625 | 7843.017578125–9548.73046875 | 563.40428459017846 | 1048.28125 | -1 |
| Pending success lifecycle; tracker on | UniTask | 1496.0693359375 | 1483.154296875 | 1447.0703125–1561.767578125 | 37.836370263626733 | 160 | -1 |
| Pending success lifecycle; tracker off | OnityTask | 2344.927978515625 | 2275.1220703125 | 2238.96484375–2730.322265625 | 156.81761911379053 | 176 | -1 |
| Pending success lifecycle; tracker off | UniTask | 1494.54345703125 | 1493.505859375 | 1459.130859375–1527.63671875 | 21.36980546329012 | 160 | -1 |
| Pending fault lifecycle; tracker on | OnityTask | 34410.693359375 | 32244.140625 | 30823.828125–42153.90625 | 3910.6997894334459 | 3306.28125 | -1 |
| Pending fault lifecycle; tracker on | UniTask | 17047.65625 | 16676.171875 | 15988.671875–19393.75 | 1088.9637183949098 | 1666 | -1 |
| Pending fault lifecycle; tracker off | OnityTask | 26403.662109375 | 25514.453125 | 24860.9375–32652.34375 | 2421.512754359851 | 2434 | -1 |
| Pending fault lifecycle; tracker off | UniTask | 17254.00390625 | 16179.8828125 | 15774.609375–25558.984375 | 3145.4565660482222 | 1666 | -1 |
| Pending cancel lifecycle; tracker on | OnityTask | 17629.833984375 | 17415.4296875 | 16512.5–19073.828125 | 819.27578038013201 | 1824.28125 | -1 |
| Pending cancel lifecycle; tracker on | UniTask | 8459.86328125 | 7691.40625 | 7549.21875–13924.21875 | 2067.2699188848669 | 600 | -1 |
| Pending cancel lifecycle; tracker off | OnityTask | 12539.16015625 | 12246.484375 | 11957.03125–14245.703125 | 701.15154778813564 | 952 | -1 |
| Pending cancel lifecycle; tracker off | UniTask | 8506.787109375 | 7763.671875 | 7480.46875–13674.21875 | 1964.8962947425152 | 600 | -1 |
