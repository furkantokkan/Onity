# Typed params-array OnityTask WhenAll scheduling

- Unity: 2022.3.62f3; Mono
- UniTask: 2e993ff18f28c931602a07292df0b0804eebef99
- OnityAsync SHA-256: 971222c94b83220e9225a3a9696a4a21c496ed26647236f54f12c00ae183d465
- Runner SHA-256: 0249bdf33302114de936f3749299161cfc47c4061d8edc96b44d56359b756f63
- Samples: 8; operations/sample: 2048 timing, 128 allocation; warmup batches: 10
- Allocation counter: Unity Profiler GC.Alloc metadata; 65,568/0-byte controls passed.
- Controls: 65568 B positive; 0 B empty.

Same typed params-array WhenAll<int> call per operation. Two- and four-input arrays are created before the measured slice and reused. Each pending case has fresh unresolved completion-source inputs prepared outside the slice. The marker includes WhenAll scheduling and storing the returned value; input completion, continuation dispatch, ordered-result validation, and GetResult occur afterward. Completed controls use both libraries' FromResult values. Onity tracker-on matches its default; UniTask uses its default tracker setting. Samples include harness loop cost without subtraction. These Editor/Mono slices do not measure full lifecycle, frame time, or IL2CPP.

| Scenario | Library | Mean ns/op | Median ns/op | Range ns/op | SD ns/op | B/op |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Pending two inputs; Onity tracker on | OnityTask | 14037.249755859377 | 13866.5283203125 | 9576.85546875–16938.037109375 | 2364.5680519755397 | 1408 |
| Pending two inputs; Onity tracker on | UniTask | 2932.684326171875 | 3049.70703125 | 1596.826171875–3852.099609375 | 806.51198127969678 | 144 |
| Completed two inputs; Onity tracker on | OnityTask | 622.406005859375 | 607.5439453125 | 343.84765625–1054.4921875 | 212.37152147826257 | 40 |
| Completed two inputs; Onity tracker on | UniTask | 1390.985107421875 | 1442.0166015625 | 863.916015625–2009.130859375 | 365.8108629573448 | 112 |
| Completed four inputs; Onity tracker on | OnityTask | 986.12060546875 | 968.7255859375 | 517.1875–1385.83984375 | 239.58350081368829 | 48 |
| Completed four inputs; Onity tracker on | UniTask | 1640.423583984375 | 1800.3173828125 | 765.771484375–2058.740234375 | 412.50427304388296 | 120 |
