# Two-input OnityTask WhenAll scheduling and output lifecycle

- Unity: 2022.3.62f3; Mono
- UniTask: 2e993ff18f28c931602a07292df0b0804eebef99
- OnityAsync SHA-256: 971222c94b83220e9225a3a9696a4a21c496ed26647236f54f12c00ae183d465
- Runner SHA-256: be348c45ec810bf236c396367e5fd116e48a6334681a04d5d9a9571f76e5ad5a
- Samples: 8; operations/sample: 2048 timing for success/completed, 256 timing for fault/cancel, 128 allocation; warmup batches: 10
- Allocation counter: Unity Profiler GC.Alloc metadata; 65,568/0-byte controls passed.
- Controls: 65568 B positive; 0 B empty.
- Worker controls: 0 B positive; 0 B empty.
- Worker allocation unavailable: InvalidDataException: Worker allocation controls failed.
- Cold Onity pending schedule with tracker off: unavailable, one process sample; controls 0/0 B.

Same two-argument WhenAll call per operation. Fresh unresolved inputs are prepared outside each slice. Scheduling scenarios include WhenAll and storage; lifecycle scenarios include scheduling, input completion, output GetResult, and cleanup. Input construction is outside both slices. Fault and cancel cases use precreated exception/token. Tracker-on matches Onity default; tracker-off changes only Onity. UniTask stays at its default. Samples include harness cost without subtraction. Editor/Mono only; no Player, frame-time, or IL2CPP result.

| Scenario | Library | Mean ns/op | Median ns/op | Range ns/op | SD ns/op | Main B/op | Worker B/op |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Pending success schedule; tracker on | OnityTask | 4263.836669921875 | 4284.765625 | 4070.361328125–4415.33203125 | 114.56405923675096 | 1408 | -1 |
| Pending success schedule; tracker on | UniTask | 751.30615234375 | 750.6103515625 | 727.294921875–782.958984375 | 14.811143700357215 | 160 | -1 |
| Pending success schedule; tracker off | OnityTask | 2288.6474609375 | 2206.298828125 | 2040.966796875–3037.744140625 | 303.31184586635806 | 544 | -1 |
| Pending success schedule; tracker off | UniTask | 779.888916015625 | 746.2158203125 | 738.623046875–1004.19921875 | 85.252894016474656 | 160 | -1 |
| Pending success lifecycle; tracker on | OnityTask | 15247.686767578123 | 15503.662109375 | 12362.158203125–17650.9765625 | 1402.2075293507671 | 1488.5625 | -1 |
| Pending success lifecycle; tracker on | UniTask | 1677.423095703125 | 1557.2998046875 | 1476.708984375–2590.72265625 | 350.20333492070392 | 160 | -1 |
| Pending success lifecycle; tracker off | OnityTask | 14280.938720703123 | 13734.1796875 | 12909.765625–16298.046875 | 1243.4790265615502 | 624.5625 | -1 |
| Pending success lifecycle; tracker off | UniTask | 1528.3935546875 | 1504.638671875 | 1450–1624.365234375 | 61.5211026187648 | 160 | -1 |
| Pending fault lifecycle; tracker on | OnityTask | 58392.041015625 | 55516.9921875 | 52911.328125–76323.4375 | 7069.1993494667458 | 54352.0625 | -1 |
| Pending fault lifecycle; tracker on | UniTask | 30116.650390625 | 28200.5859375 | 26625–40456.640625 | 4193.7448558406295 | 52438.1953125 | -1 |
| Pending fault lifecycle; tracker off | OnityTask | 71261.279296875 | 70347.265625 | 60621.09375–82139.0625 | 8303.5194344921656 | 70134.5625 | -1 |
| Pending fault lifecycle; tracker off | UniTask | 35639.35546875 | 33266.6015625 | 30385.546875–49492.578125 | 6450.5128043017867 | 69078.1953125 | -1 |
| Pending cancel lifecycle; tracker on | OnityTask | 23843.359375 | 22645.8984375 | 17850–29942.1875 | 3916.5181401004438 | 1904.5625 | -1 |
| Pending cancel lifecycle; tracker on | UniTask | 7772.021484375 | 7667.578125 | 7566.015625–8382.8125 | 260.43345028516461 | 600 | -1 |
| Pending cancel lifecycle; tracker off | OnityTask | 20977.392578125 | 21159.375 | 17947.65625–23494.921875 | 1839.4958393923964 | 1040.9296875 | -1 |
| Pending cancel lifecycle; tracker off | UniTask | 8055.859375 | 7838.8671875 | 7662.109375–9531.640625 | 574.49401485821431 | 600 | -1 |
| Both completed schedule; tracker on | OnityTask | 114.459228515625 | 114.4775390625 | 111.279296875–119.189453125 | 2.3740008285243026 | 0 | -1 |
| Both completed schedule; tracker on | UniTask | 326.01318359375 | 327.3681640625 | 315.33203125–338.818359375 | 7.5435008686586293 | 128 | -1 |
