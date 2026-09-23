# Two-input OnityTask WhenAll scheduling

- Unity: 2022.3.62f3; Mono
- UniTask: 2e993ff18f28c931602a07292df0b0804eebef99
- OnityAsync SHA-256: 810a3bb53a8a1bcb16a357eb87873338058dbf81e5397052ac0992f16affeb72
- Runner SHA-256: 3f1ad4d0d4ecd58998b3fa5d3b76ecd252c8aa97e4ddcf066902b4f9443cbb0b
- Samples: 8; operations/sample: 2048 timing, 128 allocation; warmup batches: 10
- Allocation counter: Unity Profiler GC.Alloc metadata; 65,568/0-byte controls passed.
- Controls: 65568 B positive; 0 B empty.

Same two-argument WhenAll call per operation. Each pending case has two fresh unresolved completion-source inputs prepared outside the measured slice. The marker includes WhenAll scheduling and storing the returned value; input completion, continuation dispatch, and GetResult occur afterward. The completed control uses both libraries' completed values. Tracker-on matches Onity's default; tracker-off changes only Onity's tracker. UniTask uses its default tracker setting. Samples include harness loop cost without subtraction. These Editor/Mono slices do not measure full lifecycle, frame time, or IL2CPP.

| Scenario | Library | Mean ns/op | Median ns/op | Range ns/op | SD ns/op | B/op |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Pending; Onity tracker on | OnityTask | 11473.944091796877 | 11651.7578125 | 7372.36328125–15920.654296875 | 3016.9413625651996 | 2260 |
| Pending; Onity tracker on | UniTask | 2396.905517578125 | 2042.822265625 | 970.41015625–4454.296875 | 1139.1649749398866 | 160 |
| Pending; Onity tracker off | OnityTask | 7622.4609375 | 7715.6982421875 | 5042.919921875–9772.94921875 | 1555.3226856452318 | 1248 |
| Pending; Onity tracker off | UniTask | 2911.99951171875 | 2767.041015625 | 2000.09765625–4222.8515625 | 749.00956311363052 | 160 |
| Both completed; Onity tracker on | OnityTask | 6968.450927734375 | 7034.86328125 | 4785.15625–9639.55078125 | 1667.6765109835344 | 1268 |
| Both completed; Onity tracker on | UniTask | 1139.6728515625 | 1286.1328125 | 461.669921875–1641.455078125 | 415.92402779580812 | 128 |
