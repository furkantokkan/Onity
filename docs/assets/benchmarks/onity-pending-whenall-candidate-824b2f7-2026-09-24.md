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
| Pending success schedule; tracker on | OnityTask | 9021.7712402343768 | 9199.0478515625 | 7637.939453125–10327.83203125 | 804.68183476111165 | 1040 | -1 |
| Pending success schedule; tracker on | UniTask | 2571.929931640625 | 2754.4189453125 | 1447.4609375–3729.150390625 | 792.35628404277895 | 160 | -1 |
| Pending success schedule; tracker off | OnityTask | 3188.079833984375 | 3105.4931640625 | 2649.70703125–3767.138671875 | 406.17796658157624 | 176 | -1 |
| Pending success schedule; tracker off | UniTask | 2824.176025390625 | 2797.0458984375 | 1990.283203125–3628.955078125 | 638.95136626434874 | 160 | -1 |
| Pending success lifecycle; tracker on | OnityTask | 13080.55419921875 | 13101.2451171875 | 11376.5625–14745.8984375 | 1068.5969487825796 | 1048.28125 | -1 |
| Pending success lifecycle; tracker on | UniTask | 2635.003662109375 | 2454.58984375 | 2094.04296875–3607.71484375 | 488.2889772566665 | 160 | -1 |
| Pending success lifecycle; tracker off | OnityTask | 4612.4755859375 | 4684.912109375 | 3947.4609375–5517.87109375 | 469.66440086898899 | 176 | -1 |
| Pending success lifecycle; tracker off | UniTask | 4854.1259765625 | 4645.361328125 | 4229.638671875–5531.73828125 | 493.78639340575216 | 160 | -1 |
| Pending fault lifecycle; tracker on | OnityTask | 143273.876953125 | 138697.4609375 | 121048.828125–195831.25 | 22730.675784366362 | 103730.513671875 | -1 |
| Pending fault lifecycle; tracker on | UniTask | 99395.458984375 | 94998.2421875 | 81646.484375–127626.953125 | 14267.45190515896 | 101590.1953125 | -1 |
| Pending fault lifecycle; tracker off | OnityTask | 134413.037109375 | 136167.3828125 | 116616.015625–151005.859375 | 12865.677360759832 | 135882.2578125 | -1 |
| Pending fault lifecycle; tracker off | UniTask | 118048.388671875 | 115523.4375 | 92890.234375–149097.265625 | 17871.024495508635 | 134614.1953125 | -1 |
| Pending cancel lifecycle; tracker on | OnityTask | 33624.90234375 | 33682.2265625 | 25001.171875–45855.46875 | 6068.0805430275323 | 1824.6484375 | -1 |
| Pending cancel lifecycle; tracker on | UniTask | 13190.234375 | 12333.59375 | 9936.328125–16828.125 | 2472.1152631312607 | 600 | -1 |
| Pending cancel lifecycle; tracker off | OnityTask | 18903.7109375 | 18691.6015625 | 14590.234375–23180.859375 | 2775.9983775549113 | 952 | -1 |
| Pending cancel lifecycle; tracker off | UniTask | 13357.91015625 | 13110.3515625 | 10523.828125–16250.78125 | 1990.9769774082529 | 600 | -1 |
| Both completed schedule; tracker on | OnityTask | 187.63427734375 | 204.443359375 | 126.3671875–236.23046875 | 46.095791464388363 | 0 | -1 |
| Both completed schedule; tracker on | UniTask | 1111.97509765625 | 1102.34375 | 701.3671875–1479.98046875 | 214.51631563497671 | 128 | -1 |
