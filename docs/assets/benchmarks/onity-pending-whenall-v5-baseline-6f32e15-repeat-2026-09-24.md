# Two-input OnityTask WhenAll corrected pending lifecycle

- Unity: 2022.3.62f3; Mono
- UniTask: 2e993ff18f28c931602a07292df0b0804eebef99
- OnityAsync SHA-256: 971222c94b83220e9225a3a9696a4a21c496ed26647236f54f12c00ae183d465
- Runner SHA-256: 57434edc0d21f4385f5ccba1d72a0753b34287684cd4814dab09d614c425aef1
- Samples: 8; operations/sample: 2048 timing for success, 256 timing for fault/cancel, 128 allocation; warmup batches: 10
- Allocation counter: Unity Profiler GC.Alloc metadata; 65,568/0-byte controls passed.
- Controls: 65568 B positive; 0 B empty.
- Worker controls: 0 B positive; 0 B empty.
- Worker allocation unavailable: InvalidDataException: Worker allocation controls failed.
- All-thread window controls: main positive 65568/0 B (main/other), empty 0/0 B, ThreadPool positive 88/65602 B.
- All-thread lifecycle allocation unavailable: ThreadPool positive control was not 65,568 worker bytes.
- First pending schedule; tracker off, OnityTask: 792 raw B; first call in a warmed Editor process, one sample per process.
- 384 outstanding pending schedule; tracker off, OnityTask: 208896,208896,208896,208896,208896,208896,208896,208896 raw B for 384 simultaneous outputs per sample.
- First pending schedule; tracker off, UniTask: 2032 raw B; first call in a warmed Editor process, one sample per process.
- 384 outstanding pending schedule; tracker off, UniTask: 81920,61440,61440,61440,61440,61440,61440,61440 raw B for 384 simultaneous outputs per sample.


Same two-argument WhenAll call per operation. Fresh unresolved inputs are prepared outside each slice. Scheduling scenarios include WhenAll and storage; lifecycle scenarios include scheduling, input completion, output GetResult, and cleanup. Input construction is outside both slices. Fault cases use a fresh exception per operation, prepared outside the slice; cancel cases use a precreated token. Tracker-on matches Onity default; tracker-off changes only Onity. UniTask stays at its default. Profiler lifecycle bytes cover only the main-thread marker; tracker continuations may allocate later on worker threads. Samples include harness cost without subtraction. Editor/Mono only; no Player, frame-time, or IL2CPP result.

| Scenario | Library | Mean ns/op | Median ns/op | Range ns/op | SD ns/op | Main-marker B/op | Worker B/op |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Pending success lifecycle; tracker on | OnityTask | 15922.564697265623 | 16279.0283203125 | 13645.41015625–17649.365234375 | 1446.8029740529073 | 1488.5625 | -1 |
| Pending success lifecycle; tracker on | UniTask | 1509.27734375 | 1498.9013671875 | 1489.84375–1567.87109375 | 25.371920224913072 | 160 | -1 |
| Pending success lifecycle; tracker off | OnityTask | 13480.987548828123 | 13095.8740234375 | 12252.83203125–15430.078125 | 1127.4855510483396 | 624.5625 | -1 |
| Pending success lifecycle; tracker off | UniTask | 1648.42529296875 | 1503.4423828125 | 1491.015625–2105.615234375 | 242.72976096500713 | 160 | -1 |
| Pending fault lifecycle; tracker on | OnityTask | 29641.69921875 | 28552.34375 | 27242.1875–33279.296875 | 2004.3315211624708 | 2152.5625 | -1 |
| Pending fault lifecycle; tracker on | UniTask | 15888.037109375 | 15861.5234375 | 15596.875–16230.46875 | 228.23633586349115 | 1666 | -1 |
| Pending fault lifecycle; tracker off | OnityTask | 29750.927734375 | 29632.6171875 | 26135.15625–35622.265625 | 2821.5738902599855 | 1288.5625 | -1 |
| Pending fault lifecycle; tracker off | UniTask | 16112.060546875 | 16113.671875 | 15342.96875–16727.34375 | 401.71809466088581 | 1666 | -1 |
| Pending cancel lifecycle; tracker on | OnityTask | 22656.15234375 | 21460.546875 | 18329.6875–30998.828125 | 4083.8342765470015 | 1904.5625 | -1 |
| Pending cancel lifecycle; tracker on | UniTask | 8626.123046875 | 8221.484375 | 7874.609375–11250.78125 | 1039.4612434927433 | 600 | -1 |
| Pending cancel lifecycle; tracker off | OnityTask | 21831.884765625 | 21938.28125 | 16480.859375–30332.8125 | 4618.180731301446 | 1040.5625 | -1 |
| Pending cancel lifecycle; tracker off | UniTask | 8263.37890625 | 7888.28125 | 7587.890625–9657.8125 | 790.25148155413842 | 600 | -1 |
