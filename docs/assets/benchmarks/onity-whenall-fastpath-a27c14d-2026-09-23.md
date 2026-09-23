# Two-input OnityTask WhenAll scheduling

- Unity: 2022.3.62f3; Mono
- UniTask: 2e993ff18f28c931602a07292df0b0804eebef99
- OnityAsync SHA-256: c0c463bb5801c6d0d417e23dd6cbe94ce2cca8b43d9db0c41a780e5115921565
- Runner SHA-256: 3f1ad4d0d4ecd58998b3fa5d3b76ecd252c8aa97e4ddcf066902b4f9443cbb0b
- Samples: 8; operations/sample: 2048 timing, 128 allocation; warmup batches: 10
- Allocation counter: Unity Profiler GC.Alloc metadata; 65,568/0-byte controls passed.
- Controls: 65568 B positive; 0 B empty.

Same two-argument WhenAll call per operation. Each pending case has two fresh unresolved completion-source inputs prepared outside the measured slice. The marker includes WhenAll scheduling and storing the returned value; input completion, continuation dispatch, and GetResult occur afterward. The completed control uses both libraries' completed values. Tracker-on matches Onity's default; tracker-off changes only Onity's tracker. UniTask uses its default tracker setting. Samples include harness loop cost without subtraction. These Editor/Mono slices do not measure full lifecycle, frame time, or IL2CPP.

| Scenario | Library | Mean ns/op | Median ns/op | Range ns/op | SD ns/op | B/op |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Pending; Onity tracker on | OnityTask | 4216.80908203125 | 4186.4501953125 | 4020.654296875–4543.896484375 | 162.91776975232256 | 1556 |
| Pending; Onity tracker on | UniTask | 751.64794921875 | 734.033203125 | 691.69921875–848.388671875 | 48.035911922027999 | 160 |
| Pending; Onity tracker off | OnityTask | 2014.41650390625 | 2008.2275390625 | 1966.552734375–2066.30859375 | 33.08434952096885 | 544 |
| Pending; Onity tracker off | UniTask | 714.935302734375 | 710.1318359375 | 701.513671875–737.548828125 | 13.221932226556229 | 160 |
| Both completed; Onity tracker on | OnityTask | 116.961669921875 | 116.3330078125 | 114.208984375–122.8515625 | 2.6937394458360426 | 0 |
| Both completed; Onity tracker on | UniTask | 316.387939453125 | 313.5009765625 | 306.689453125–339.501953125 | 9.8832918887665873 | 128 |
