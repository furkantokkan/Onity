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
| Pending success schedule; tracker on | OnityTask | 4024.822998046875 | 3952.734375 | 3891.552734375–4299.31640625 | 151.06782893530203 | 1408 | -1 |
| Pending success schedule; tracker on | UniTask | 729.486083984375 | 731.8359375 | 718.505859375–738.18359375 | 5.910292957285499 | 160 | -1 |
| Pending success schedule; tracker off | OnityTask | 2019.07958984375 | 2019.384765625 | 1923.779296875–2099.90234375 | 57.285961531744945 | 544 | -1 |
| Pending success schedule; tracker off | UniTask | 764.29443359375 | 749.2431640625 | 728.271484375–822.509765625 | 34.057973770954746 | 160 | -1 |
| Pending success lifecycle; tracker on | OnityTask | 14474.615478515623 | 14605.5908203125 | 12079.443359375–16537.109375 | 1185.4609632550575 | 1488.5625 | -1 |
| Pending success lifecycle; tracker on | UniTask | 1536.993408203125 | 1526.6357421875 | 1464.6484375–1676.416015625 | 64.965297061801323 | 160 | -1 |
| Pending success lifecycle; tracker off | OnityTask | 12007.489013671877 | 11546.7041015625 | 9796.142578125–14186.572265625 | 1404.4096172077689 | 624.5625 | -1 |
| Pending success lifecycle; tracker off | UniTask | 1491.064453125 | 1476.85546875 | 1451.318359375–1568.1640625 | 38.522489605306284 | 160 | -1 |
| Pending fault lifecycle; tracker on | OnityTask | 74145.849609375 | 62183.203125 | 50737.5–127186.328125 | 25236.32233254052 | 54352.0625 | -1 |
| Pending fault lifecycle; tracker on | UniTask | 33569.384765625 | 31231.8359375 | 26251.953125–43465.234375 | 5898.7551359836134 | 52438.1953125 | -1 |
| Pending fault lifecycle; tracker off | OnityTask | 83072.705078125 | 84938.4765625 | 58428.515625–103147.265625 | 15614.547654387479 | 70134.5625 | -1 |
| Pending fault lifecycle; tracker off | UniTask | 51491.943359375 | 47579.8828125 | 34004.296875–77191.015625 | 14837.273009877021 | 69078.1953125 | -1 |
| Pending cancel lifecycle; tracker on | OnityTask | 23264.990234375 | 23515.234375 | 17319.53125–29943.359375 | 4207.1176433281862 | 1904.5625 | -1 |
| Pending cancel lifecycle; tracker on | UniTask | 8771.77734375 | 7987.890625 | 7568.359375–13437.890625 | 1821.7276904584239 | 600 | -1 |
| Pending cancel lifecycle; tracker off | OnityTask | 23758.7890625 | 23351.7578125 | 20516.40625–31290.234375 | 3283.6695355335787 | 1040.9296875 | -1 |
| Pending cancel lifecycle; tracker off | UniTask | 8127.880859375 | 8107.03125 | 7545.3125–8694.53125 | 396.95481526541056 | 600 | -1 |
| Both completed schedule; tracker on | OnityTask | 126.708984375 | 126.7822265625 | 124.12109375–129.78515625 | 1.7813902682572949 | 0 | -1 |
| Both completed schedule; tracker on | UniTask | 335.68115234375 | 328.41796875 | 319.23828125–367.041015625 | 18.226037892273656 | 128 | -1 |
