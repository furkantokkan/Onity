# Two-input OnityTask WhenAll scheduling

- Unity: 2022.3.62f3; Mono
- UniTask: 2e993ff18f28c931602a07292df0b0804eebef99
- OnityAsync SHA-256: 6b7f0c5b79474e2b6be242365af3f18a7a9c241297be352ab99b5267807a6865
- Runner SHA-256: 9548bd0584cebf8b9b399b7d2663f99a7c10f6289953ad9d546534bd83444611
- Samples: 8; operations/sample: 2048 timing, 128 allocation; warmup batches: 10
- Allocation counter: Unity Profiler GC.Alloc metadata; 65,568/0-byte controls passed.
- Controls: 65568 B positive; 0 B empty.

Same two-argument WhenAll call per operation. Each pending case has two fresh unresolved completion-source inputs prepared outside the measured slice. The marker includes WhenAll scheduling and storing the returned value; input completion, continuation dispatch, and GetResult occur afterward. The completed control uses both libraries' completed values. Tracker-on matches Onity's default; tracker-off changes only Onity's tracker. UniTask uses its default tracker setting. Samples include harness loop cost without subtraction. These Editor/Mono slices do not measure full lifecycle, frame time, or IL2CPP.

| Scenario | Library | Mean ns/op | Median ns/op | Range ns/op | SD ns/op | B/op |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Pending; Onity tracker on | OnityTask | 4124.169921875 | 4115.8935546875 | 4028.955078125–4260.986328125 | 73.445133056031864 | 1620 |
| Pending; Onity tracker on | UniTask | 747.44873046875 | 722.4853515625 | 716.30859375–922.36328125 | 66.285397007338787 | 160 |
| Pending; Onity tracker off | OnityTask | 2027.288818359375 | 1998.3154296875 | 1930.126953125–2216.11328125 | 85.235441421494812 | 608 |
| Pending; Onity tracker off | UniTask | 721.8994140625 | 720.361328125 | 705.37109375–737.20703125 | 9.4431940042558775 | 160 |
| Both completed; Onity tracker on | OnityTask | 1497.20458984375 | 1494.1650390625 | 1476.953125–1534.521484375 | 16.803824295723061 | 276 |
| Both completed; Onity tracker on | UniTask | 345.086669921875 | 324.51171875 | 312.98828125–508.935546875 | 62.094358812235917 | 128 |
