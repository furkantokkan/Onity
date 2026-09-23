# Two-input OnityTask WhenAll scheduling

- Unity: 2022.3.62f3; Mono
- UniTask: 2e993ff18f28c931602a07292df0b0804eebef99
- OnityAsync SHA-256: 810a3bb53a8a1bcb16a357eb87873338058dbf81e5397052ac0992f16affeb72
- Runner SHA-256: 3f1ad4d0d4ecd58998b3fa5d3b76ecd252c8aa97e4ddcf066902b4f9443cbb0b
- Samples: 8; operations/sample: 2048 timing, 128 allocation; warmup batches: 10
- Allocation counter: Unavailable: separate Profiler pass not run.
- Controls: 0 B positive; 0 B empty.

Same two-argument WhenAll call per operation. Each pending case has two fresh unresolved completion-source inputs prepared outside the measured slice. The marker includes WhenAll scheduling and storing the returned value; input completion, continuation dispatch, and GetResult occur afterward. The completed control uses both libraries' completed values. Tracker-on matches Onity's default; tracker-off changes only Onity's tracker. UniTask uses its default tracker setting. Samples include harness loop cost without subtraction. These Editor/Mono slices do not measure full lifecycle, frame time, or IL2CPP.

| Scenario | Library | Mean ns/op | Median ns/op | Range ns/op | SD ns/op | B/op |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Pending; Onity tracker on | OnityTask | 3717.596435546875 | 3628.90625 | 3482.373046875–4518.310546875 | 310.11058936095384 | -1 |
| Pending; Onity tracker on | UniTask | 714.697265625 | 711.865234375 | 682.32421875–769.482421875 | 26.641403142130685 | -1 |
| Pending; Onity tracker off | OnityTask | 1595.782470703125 | 1593.359375 | 1585.498046875–1608.59375 | 8.2985886314562904 | -1 |
| Pending; Onity tracker off | UniTask | 694.317626953125 | 695.7763671875 | 673.73046875–712.109375 | 12.222449674290095 | -1 |
| Both completed; Onity tracker on | OnityTask | 2799.7802734375 | 2768.7744140625 | 2720.60546875–3026.123046875 | 93.063962890317555 | -1 |
| Both completed; Onity tracker on | UniTask | 322.283935546875 | 320.60546875 | 304.58984375–344.189453125 | 12.166116429595817 | -1 |
