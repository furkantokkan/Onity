# Typed params-array OnityTask WhenAll scheduling

- Unity: 2022.3.62f3; Mono
- UniTask: 2e993ff18f28c931602a07292df0b0804eebef99
- OnityAsync SHA-256: 971222c94b83220e9225a3a9696a4a21c496ed26647236f54f12c00ae183d465
- Runner SHA-256: 0249bdf33302114de936f3749299161cfc47c4061d8edc96b44d56359b756f63
- Samples: 8; operations/sample: 2048 timing, 128 allocation; warmup batches: 10
- Allocation counter: Unavailable: separate Profiler pass not run.
- Controls: 0 B positive; 0 B empty.

Same typed params-array WhenAll<int> call per operation. Two- and four-input arrays are created before the measured slice and reused. Each pending case has fresh unresolved completion-source inputs prepared outside the slice. The marker includes WhenAll scheduling and storing the returned value; input completion, continuation dispatch, ordered-result validation, and GetResult occur afterward. Completed controls use both libraries' FromResult values. Onity tracker-on matches its default; UniTask uses its default tracker setting. Samples include harness loop cost without subtraction. These Editor/Mono slices do not measure full lifecycle, frame time, or IL2CPP.

| Scenario | Library | Mean ns/op | Median ns/op | Range ns/op | SD ns/op | B/op |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Pending two inputs; Onity tracker on | OnityTask | 4648.74267578125 | 4696.2158203125 | 4240.8203125–5047.607421875 | 282.87095551163765 | -1 |
| Pending two inputs; Onity tracker on | UniTask | 848.05908203125 | 784.716796875 | 748.388671875–1204.248046875 | 148.45707563518297 | -1 |
| Completed two inputs; Onity tracker on | OnityTask | 341.162109375 | 298.3154296875 | 286.71875–549.90234375 | 86.31513333844029 | -1 |
| Completed two inputs; Onity tracker on | UniTask | 432.781982421875 | 399.365234375 | 369.921875–590.52734375 | 73.792815216504067 | -1 |
| Completed four inputs; Onity tracker on | OnityTask | 408.148193359375 | 405.419921875 | 394.384765625–422.021484375 | 10.633646798458711 | -1 |
| Completed four inputs; Onity tracker on | UniTask | 593.37158203125 | 543.212890625 | 496.58203125–885.009765625 | 127.3030801199294 | -1 |
