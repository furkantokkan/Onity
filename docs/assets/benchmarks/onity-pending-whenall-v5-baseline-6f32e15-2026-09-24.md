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
- All-thread window controls: main positive 65568/0 B (main/other), empty 0/0 B, ThreadPool positive 40/65568 B.
- First pending schedule; tracker off, OnityTask: 792 raw B; first call in a warmed Editor process, one sample per process.
- 384 outstanding pending schedule; tracker off, OnityTask: 208896,208896,208896,208896,208896,208896,208896,208896 raw B for 384 simultaneous outputs per sample.
- First pending schedule; tracker off, UniTask: 2032 raw B; first call in a warmed Editor process, one sample per process.
- 384 outstanding pending schedule; tracker off, UniTask: 81920,61440,61440,61440,61440,61440,61440,61440 raw B for 384 simultaneous outputs per sample.

All-thread lifecycle samples include a tracked-batch completion barrier. Raw totals include benchmark barrier overhead; they are reported separately from the main-thread marker slices.
- Pending success lifecycle; tracker on, OnityTask: all-thread raw B 199412,199240,199240,199240,199240,199240,199240,199240; other-thread raw B 8744,8704,8704,8704,8704,8704,8704,8704.
- Pending success lifecycle; tracker on, UniTask: all-thread raw B 20480,20480,20480,20480,20480,20480,20480,20480; other-thread raw B 0,0,0,0,0,0,0,0.
- Pending fault lifecycle; tracker on, OnityTask: all-thread raw B 371272,371272,371272,371272,371272,371272,371272,371272; other-thread raw B 95744,95744,95744,95744,95744,95744,95744,95744.
- Pending fault lifecycle; tracker on, UniTask: all-thread raw B 213248,213248,213248,213248,213248,213248,213248,213248; other-thread raw B 0,0,0,0,0,0,0,0.
- Pending cancel lifecycle; tracker on, OnityTask: all-thread raw B 261704,261704,261704,261704,261704,261704,261704,261704; other-thread raw B 17920,17920,17920,17920,17920,17920,17920,17920.
- Pending cancel lifecycle; tracker on, UniTask: all-thread raw B 76800,76800,76800,76800,76800,76800,76800,76800; other-thread raw B 0,0,0,0,0,0,0,0.

Same two-argument WhenAll call per operation. Fresh unresolved inputs are prepared outside each slice. Scheduling scenarios include WhenAll and storage; lifecycle scenarios include scheduling, input completion, output GetResult, and cleanup. Input construction is outside both slices. Fault cases use a fresh exception per operation, prepared outside the slice; cancel cases use a precreated token. Tracker-on matches Onity default; tracker-off changes only Onity. UniTask stays at its default. Profiler lifecycle bytes cover only the main-thread marker; tracker continuations may allocate later on worker threads. Samples include harness cost without subtraction. Editor/Mono only; no Player, frame-time, or IL2CPP result.

| Scenario | Library | Mean ns/op | Median ns/op | Range ns/op | SD ns/op | Main-marker B/op | Worker B/op |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Pending success lifecycle; tracker on | OnityTask | 14460.980224609377 | 14424.7314453125 | 12285.64453125–16307.6171875 | 1277.9122013252393 | 1488.5625 | -1 |
| Pending success lifecycle; tracker on | UniTask | 1489.35546875 | 1496.38671875 | 1425.5859375–1526.85546875 | 31.833999668424028 | 160 | -1 |
| Pending success lifecycle; tracker off | OnityTask | 12271.83837890625 | 12249.560546875 | 10867.138671875–14360.791015625 | 1090.1027849467791 | 624.5625 | -1 |
| Pending success lifecycle; tracker off | UniTask | 1532.14111328125 | 1512.59765625 | 1483.056640625–1684.9609375 | 60.151809677130252 | 160 | -1 |
| Pending fault lifecycle; tracker on | OnityTask | 30808.935546875 | 31452.34375 | 25277.734375–36347.265625 | 3263.7959822064008 | 2152.5625 | -1 |
| Pending fault lifecycle; tracker on | UniTask | 15929.39453125 | 15930.2734375 | 15746.09375–16119.53125 | 107.57974018111648 | 1666 | -1 |
| Pending fault lifecycle; tracker off | OnityTask | 27689.990234375 | 27513.4765625 | 21566.40625–33487.109375 | 3676.2270581885991 | 1288.5625 | -1 |
| Pending fault lifecycle; tracker off | UniTask | 16412.451171875 | 15945.5078125 | 15470.3125–17953.515625 | 984.89518121018284 | 1666 | -1 |
| Pending cancel lifecycle; tracker on | OnityTask | 18598.33984375 | 18410.15625 | 17189.0625–21234.765625 | 1224.1253864105029 | 1904.5625 | -1 |
| Pending cancel lifecycle; tracker on | UniTask | 7592.822265625 | 7580.2734375 | 7496.484375–7709.765625 | 69.587992970889402 | 600 | -1 |
| Pending cancel lifecycle; tracker off | OnityTask | 18369.53125 | 17232.03125 | 14979.296875–23059.765625 | 2809.1374213017439 | 1040.5625 | -1 |
| Pending cancel lifecycle; tracker off | UniTask | 7910.009765625 | 7736.1328125 | 7551.953125–8889.0625 | 412.01776869428602 | 600 | -1 |
