# Typed params-array OnityTask WhenAll scheduling

- Unity: 2022.3.62f3; Mono
- UniTask: 2e993ff18f28c931602a07292df0b0804eebef99
- OnityAsync SHA-256: c0c463bb5801c6d0d417e23dd6cbe94ce2cca8b43d9db0c41a780e5115921565
- Runner SHA-256: 67e84073edc33d4613135d5e8b6ae3c126a962973e0085919e29be26e288f008
- Samples: 8; operations/sample: 2048 timing, 128 allocation; warmup batches: 10
- Allocation counter: Unavailable: separate Profiler pass not run.
- Controls: 0 B positive; 0 B empty.

Same typed params-array WhenAll<int> call per operation. Two- and four-input arrays are created before the measured slice and reused. Each pending case has fresh unresolved completion-source inputs prepared outside the slice. The marker includes WhenAll scheduling and storing the returned value; input completion, continuation dispatch, ordered-result validation, and GetResult occur afterward. Completed controls use both libraries' FromResult values. Onity tracker-on matches its default; UniTask uses its default tracker setting. Samples include harness loop cost without subtraction. These Editor/Mono slices do not measure full lifecycle, frame time, or IL2CPP.

| Scenario | Library | Mean ns/op | Median ns/op | Range ns/op | SD ns/op | B/op |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Pending two inputs; Onity tracker on | OnityTask | 5058.4228515625 | 5021.7041015625 | 4051.513671875–6336.5234375 | 788.15100381169316 | -1 |
| Pending two inputs; Onity tracker on | UniTask | 1176.605224609375 | 907.470703125 | 757.861328125–2263.28125 | 506.51538653170559 | -1 |
| Completed two inputs; Onity tracker on | OnityTask | 2139.19677734375 | 1984.5947265625 | 1799.658203125–2851.513671875 | 376.42777098355123 | -1 |
| Completed two inputs; Onity tracker on | UniTask | 409.38720703125 | 375.1953125 | 346.142578125–608.935546875 | 81.868279559837418 | -1 |
| Completed four inputs; Onity tracker on | OnityTask | 3334.503173828125 | 3241.5283203125 | 2273.193359375–4528.564453125 | 678.12450643490706 | -1 |
| Completed four inputs; Onity tracker on | UniTask | 639.471435546875 | 594.287109375 | 492.138671875–886.328125 | 139.35218799153628 | -1 |
