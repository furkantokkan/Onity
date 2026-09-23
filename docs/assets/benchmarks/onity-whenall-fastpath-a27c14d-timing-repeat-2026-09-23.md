# Two-input OnityTask WhenAll scheduling

- Unity: 2022.3.62f3; Mono
- UniTask: 2e993ff18f28c931602a07292df0b0804eebef99
- OnityAsync SHA-256: c0c463bb5801c6d0d417e23dd6cbe94ce2cca8b43d9db0c41a780e5115921565
- Runner SHA-256: 3f1ad4d0d4ecd58998b3fa5d3b76ecd252c8aa97e4ddcf066902b4f9443cbb0b
- Samples: 8; operations/sample: 2048 timing, 128 allocation; warmup batches: 10
- Allocation counter: Unavailable: separate Profiler pass not run.
- Controls: 0 B positive; 0 B empty.

Same two-argument WhenAll call per operation. Each pending case has two fresh unresolved completion-source inputs prepared outside the measured slice. The marker includes WhenAll scheduling and storing the returned value; input completion, continuation dispatch, and GetResult occur afterward. The completed control uses both libraries' completed values. Tracker-on matches Onity's default; tracker-off changes only Onity's tracker. UniTask uses its default tracker setting. Samples include harness loop cost without subtraction. These Editor/Mono slices do not measure full lifecycle, frame time, or IL2CPP.

| Scenario | Library | Mean ns/op | Median ns/op | Range ns/op | SD ns/op | B/op |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Pending; Onity tracker on | OnityTask | 4322.705078125 | 4250.048828125 | 4105.810546875–4699.12109375 | 222.41216029345603 | -1 |
| Pending; Onity tracker on | UniTask | 733.319091796875 | 727.9052734375 | 710.44921875–773.2421875 | 20.331578281717945 | -1 |
| Pending; Onity tracker off | OnityTask | 2087.152099609375 | 2097.75390625 | 1950.5859375–2359.9609375 | 120.34533767797006 | -1 |
| Pending; Onity tracker off | UniTask | 742.10205078125 | 720.7275390625 | 717.48046875–861.669921875 | 46.833488839401511 | -1 |
| Both completed; Onity tracker on | OnityTask | 115.509033203125 | 114.3310546875 | 111.865234375–126.416015625 | 4.3658144776629513 | -1 |
| Both completed; Onity tracker on | UniTask | 330.194091796875 | 315.478515625 | 306.54296875–442.28515625 | 42.503711436704911 | -1 |
