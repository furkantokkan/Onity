# Typed params-array OnityTask WhenAll scheduling

- Unity: 2022.3.62f3; Mono
- UniTask: 2e993ff18f28c931602a07292df0b0804eebef99
- OnityAsync SHA-256: c0c463bb5801c6d0d417e23dd6cbe94ce2cca8b43d9db0c41a780e5115921565
- Runner SHA-256: 67e84073edc33d4613135d5e8b6ae3c126a962973e0085919e29be26e288f008
- Samples: 8; operations/sample: 2048 timing, 128 allocation; warmup batches: 10
- Allocation counter: Unity Profiler GC.Alloc metadata; 65,568/0-byte controls passed.
- Controls: 65568 B positive; 0 B empty.

Same typed params-array WhenAll<int> call per operation. Two- and four-input arrays are created before the measured slice and reused. Each pending case has fresh unresolved completion-source inputs prepared outside the slice. The marker includes WhenAll scheduling and storing the returned value; input completion, continuation dispatch, ordered-result validation, and GetResult occur afterward. Completed controls use both libraries' FromResult values. Onity tracker-on matches its default; UniTask uses its default tracker setting. Samples include harness loop cost without subtraction. These Editor/Mono slices do not measure full lifecycle, frame time, or IL2CPP.

| Scenario | Library | Mean ns/op | Median ns/op | Range ns/op | SD ns/op | B/op |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Pending two inputs; Onity tracker on | OnityTask | 4907.62939453125 | 4808.251953125 | 4475.5859375–5802.978515625 | 380.98718445136319 | 1408 |
| Pending two inputs; Onity tracker on | UniTask | 903.875732421875 | 884.912109375 | 815.087890625–1085.791015625 | 88.993396845923982 | 144 |
| Completed two inputs; Onity tracker on | OnityTask | 2201.605224609375 | 2048.7548828125 | 1899.4140625–3139.16015625 | 381.7163307883871 | 392 |
| Completed two inputs; Onity tracker on | UniTask | 462.445068359375 | 434.6923828125 | 399.21875–674.169921875 | 84.192567755977763 | 112 |
| Completed four inputs; Onity tracker on | OnityTask | 2374.676513671875 | 2391.9677734375 | 2288.720703125–2438.720703125 | 53.320133292766862 | 592 |
| Completed four inputs; Onity tracker on | UniTask | 653.363037109375 | 647.607421875 | 534.1796875–936.42578125 | 119.18278792332885 | 120 |
