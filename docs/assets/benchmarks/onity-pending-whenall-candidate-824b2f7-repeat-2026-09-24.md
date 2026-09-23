# Two-input OnityTask WhenAll scheduling and output lifecycle

- Unity: 2022.3.62f3; Mono
- UniTask: 2e993ff18f28c931602a07292df0b0804eebef99
- OnityAsync SHA-256: 00fe9203a9b32e389525873f649841c44671fcc87134b5984eb4d9c6aa9d0007
- Runner SHA-256: 414673af9f9e86317f3fd1f9c007d00240e2db6a9b4c206ee93c6114063059a1
- Samples: 8; operations/sample: 2048 timing for success/completed, 256 timing for fault/cancel, 128 allocation; warmup batches: 10
- Allocation counter: Unity Profiler GC.Alloc metadata; 65,568/0-byte controls passed.
- Controls: 65568 B positive; 0 B empty.
- Worker controls: 0 B positive; 0 B empty.
- Worker allocation unavailable: InvalidDataException: Worker allocation controls failed.
- Cold Onity pending schedule with tracker off: unavailable, one process sample; controls 0/0 B.

Same two-argument WhenAll call per operation. Fresh unresolved inputs are prepared outside each slice. Scheduling scenarios include WhenAll and storage; lifecycle scenarios include scheduling, input completion, output GetResult, and cleanup. Input construction is outside both slices. Fault and cancel cases use precreated exception/token. Tracker-on matches Onity default; tracker-off changes only Onity. UniTask stays at its default. Samples include harness cost without subtraction. Editor/Mono only; no Player, frame-time, or IL2CPP result.

| Scenario | Library | Mean ns/op | Median ns/op | Range ns/op | SD ns/op | Main B/op | Worker B/op |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Pending success schedule; tracker on | OnityTask | 3473.4619140625 | 3470.9716796875 | 3327.490234375–3612.353515625 | 98.607413165881127 | 1040 | -1 |
| Pending success schedule; tracker on | UniTask | 783.53271484375 | 772.0947265625 | 763.76953125–835.15625 | 24.632152733646443 | 160 | -1 |
| Pending success schedule; tracker off | OnityTask | 1362.82958984375 | 1359.6435546875 | 1332.666015625–1409.912109375 | 27.005515113689459 | 176 | -1 |
| Pending success schedule; tracker off | UniTask | 743.072509765625 | 738.9892578125 | 724.21875–799.169921875 | 22.126043897183617 | 160 | -1 |
| Pending success lifecycle; tracker on | OnityTask | 8057.3974609375 | 7934.4482421875 | 7492.7734375–8895.263671875 | 475.48719505112348 | 1048.6484375 | -1 |
| Pending success lifecycle; tracker on | UniTask | 1556.005859375 | 1533.6181640625 | 1497.36328125–1727.44140625 | 71.241066857873733 | 160 | -1 |
| Pending success lifecycle; tracker off | OnityTask | 2479.4189453125 | 2425.1953125 | 2289.208984375–2832.666015625 | 170.74990755360727 | 176 | -1 |
| Pending success lifecycle; tracker off | UniTask | 1576.1962890625 | 1540.7958984375 | 1491.552734375–1810.107421875 | 94.995048168023004 | 160 | -1 |
| Pending fault lifecycle; tracker on | OnityTask | 77110.595703125 | 76512.6953125 | 61970.703125–94304.296875 | 9089.7827543762141 | 103730.513671875 | -1 |
| Pending fault lifecycle; tracker on | UniTask | 56051.513671875 | 54865.8203125 | 42667.96875–68566.015625 | 8322.0012325750304 | 101590.1953125 | -1 |
| Pending fault lifecycle; tracker off | OnityTask | 83076.025390625 | 83130.46875 | 73851.171875–93079.296875 | 6611.7193988996705 | 135882.2578125 | -1 |
| Pending fault lifecycle; tracker off | UniTask | 70104.00390625 | 72580.859375 | 57746.484375–78896.09375 | 6817.0596893940719 | 134614.1953125 | -1 |
| Pending cancel lifecycle; tracker on | OnityTask | 18904.58984375 | 18684.375 | 17315.625–21366.796875 | 1177.3135479430653 | 1824.6484375 | -1 |
| Pending cancel lifecycle; tracker on | UniTask | 8128.662109375 | 7948.6328125 | 7758.203125–9429.6875 | 508.32891175686984 | 600 | -1 |
| Pending cancel lifecycle; tracker off | OnityTask | 12652.587890625 | 12457.6171875 | 12044.53125–13623.828125 | 547.70023316257732 | 952 | -1 |
| Pending cancel lifecycle; tracker off | UniTask | 7927.978515625 | 7894.3359375 | 7755.46875–8172.265625 | 136.81747795518427 | 600 | -1 |
| Both completed schedule; tracker on | OnityTask | 120.281982421875 | 118.5546875 | 113.818359375–139.306640625 | 7.553260262082703 | 0 | -1 |
| Both completed schedule; tracker on | UniTask | 319.5556640625 | 319.6044921875 | 308.3984375–333.7890625 | 9.089870556118484 | 128 | -1 |
