# Two-input OnityTask WhenAll scheduling

- Unity: 2022.3.62f3; Mono
- UniTask: 2e993ff18f28c931602a07292df0b0804eebef99
- OnityAsync SHA-256: c0c463bb5801c6d0d417e23dd6cbe94ce2cca8b43d9db0c41a780e5115921565
- Runner SHA-256: 5b748a81eebe2307f844746c22aa55faf79b700acd31dd1650ef9f079950e7f9
- Samples: 8; operations/sample: 2048 timing, 128 allocation; warmup batches: 10
- Allocation counter: Unity Profiler GC.Alloc metadata and pending Onity callstacks; 65,568/0-byte controls passed.
- Controls: 65568 B positive; 0 B empty.

Same two-argument WhenAll call per operation. Each pending case has two fresh unresolved completion-source inputs prepared outside the measured slice. The marker includes WhenAll scheduling and storing the returned value; input completion, continuation dispatch, and GetResult occur afterward. The completed control uses both libraries' completed values. Tracker-on matches Onity's default; tracker-off changes only Onity's tracker. UniTask uses its default tracker setting. Samples include harness loop cost without subtraction. These Editor/Mono slices do not measure full lifecycle, frame time, or IL2CPP.

| Scenario | Library | Mean ns/op | Median ns/op | Range ns/op | SD ns/op | B/op |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Pending; Onity tracker on | OnityTask | 9575.8361816406232 | 9236.9140625 | 7016.259765625–12617.96875 | 2027.0223727300556 | 1556 |
| Pending; Onity tracker on | UniTask | 2174.21875 | 2078.857421875 | 1569.3359375–2992.578125 | 490.34210723510358 | 160 |
| Pending; Onity tracker off | OnityTask | 6383.14208984375 | 5821.337890625 | 4015.4296875–8789.35546875 | 1655.3586871494806 | 544 |
| Pending; Onity tracker off | UniTask | 2745.21484375 | 2457.8857421875 | 1322.412109375–4761.03515625 | 1034.271773866386 | 160 |
| Both completed; Onity tracker on | OnityTask | 167.059326171875 | 168.1640625 | 113.37890625–224.51171875 | 50.156871384785255 | 0 |
| Both completed; Onity tracker on | UniTask | 1212.408447265625 | 1132.2998046875 | 356.884765625–2072.75390625 | 676.83781906428248 | 128 |
