# Two-input OnityTask WhenAll scheduling

- Unity: 2022.3.62f3; Mono
- UniTask: 2e993ff18f28c931602a07292df0b0804eebef99
- OnityAsync SHA-256: c0c463bb5801c6d0d417e23dd6cbe94ce2cca8b43d9db0c41a780e5115921565
- Runner SHA-256: 7239ef6da5cc3366f866f2ce64f260d0b12d5e99b7852a7e3d86361cfedf6f9b
- Samples: 8; operations/sample: 2048 timing, 128 allocation; warmup batches: 10
- Allocation counter: Unity Profiler GC.Alloc metadata and pending Onity callstacks; 65,568/0-byte controls passed.
- Controls: 65568 B positive; 0 B empty.

Same two-argument WhenAll call per operation. Each pending case has two fresh unresolved completion-source inputs prepared outside the measured slice. The marker includes WhenAll scheduling and storing the returned value; input completion, continuation dispatch, and GetResult occur afterward. The completed control uses both libraries' completed values. Tracker-on matches Onity's default; tracker-off changes only Onity's tracker. UniTask uses its default tracker setting. Samples include harness loop cost without subtraction. These Editor/Mono slices do not measure full lifecycle, frame time, or IL2CPP.

| Scenario | Library | Mean ns/op | Median ns/op | Range ns/op | SD ns/op | B/op |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Pending; Onity tracker on | OnityTask | 12817.169189453123 | 12692.333984375 | 10293.84765625–14804.78515625 | 1460.2157986669456 | 1408 |
| Pending; Onity tracker on | UniTask | 2930.072021484375 | 2721.09375 | 2275.537109375–4084.228515625 | 645.43268060824221 | 160 |
| Pending; Onity tracker off | OnityTask | 6231.781005859375 | 6416.6015625 | 3010.888671875–8282.2265625 | 1734.1081117397157 | 544 |
| Pending; Onity tracker off | UniTask | 2854.052734375 | 2828.662109375 | 2008.642578125–3839.697265625 | 529.44006527885017 | 160 |
| Both completed; Onity tracker on | OnityTask | 183.85009765625 | 165.6982421875 | 113.18359375–398.2421875 | 88.100382261226329 | 0 |
| Both completed; Onity tracker on | UniTask | 842.5048828125 | 919.970703125 | 460.44921875–1054.8828125 | 217.9811344251751 | 128 |
