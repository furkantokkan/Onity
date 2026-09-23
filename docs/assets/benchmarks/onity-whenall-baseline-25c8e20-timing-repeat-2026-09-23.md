# Two-input OnityTask WhenAll scheduling

- Unity: 2022.3.62f3; Mono
- UniTask: 2e993ff18f28c931602a07292df0b0804eebef99
- OnityAsync SHA-256: 6b7f0c5b79474e2b6be242365af3f18a7a9c241297be352ab99b5267807a6865
- Runner SHA-256: 9548bd0584cebf8b9b399b7d2663f99a7c10f6289953ad9d546534bd83444611
- Samples: 8; operations/sample: 2048 timing, 128 allocation; warmup batches: 10
- Allocation counter: Unavailable: separate Profiler pass not run.
- Controls: 0 B positive; 0 B empty.

Same two-argument WhenAll call per operation. Each pending case has two fresh unresolved completion-source inputs prepared outside the measured slice. The marker includes WhenAll scheduling and storing the returned value; input completion, continuation dispatch, and GetResult occur afterward. The completed control uses both libraries' completed values. Tracker-on matches Onity's default; tracker-off changes only Onity's tracker. UniTask uses its default tracker setting. Samples include harness loop cost without subtraction. These Editor/Mono slices do not measure full lifecycle, frame time, or IL2CPP.

| Scenario | Library | Mean ns/op | Median ns/op | Range ns/op | SD ns/op | B/op |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Pending; Onity tracker on | OnityTask | 4179.50439453125 | 4115.576171875 | 3999.90234375–4584.5703125 | 183.50519317104883 | -1 |
| Pending; Onity tracker on | UniTask | 728.5400390625 | 720.458984375 | 693.798828125–806.15234375 | 33.854374029062448 | -1 |
| Pending; Onity tracker off | OnityTask | 2030.43212890625 | 1965.2099609375 | 1917.529296875–2281.0546875 | 134.24938840300555 | -1 |
| Pending; Onity tracker off | UniTask | 712.36572265625 | 709.1796875 | 705.37109375–732.421875 | 8.4919798965135289 | -1 |
| Both completed; Onity tracker on | OnityTask | 1469.10400390625 | 1470.7275390625 | 1443.5546875–1484.08203125 | 10.825062889903148 | -1 |
| Both completed; Onity tracker on | UniTask | 321.173095703125 | 317.7734375 | 310.44921875–337.744140625 | 9.127886691144715 | -1 |
