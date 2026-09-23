# Two-input `WhenAll` fast-path comparison

The candidate is the exact runtime source from product commit `a27c14d` (Git
blob `726369708ce7e32f046b36632ec2c64e67b038cf`). The prior product
commit `d0ff06d` has the same runtime Git blob as the measured baseline
`25c8e20`: `1dcb59297c21ec72f8c6512e3a64549c5e307074`. Both benchmark
hosts use the same runner Git blob `8a43d9d6bfab86c8f8e1c6b8d4beb482f1f840d3`
and official UniTask 2.5.11 at `2e993ff18f28c931602a07292df0b0804eebef99`.
The candidate's runner working-file SHA-256 is
`3f1ad4d0d4ecd58998b3fa5d3b76ecd252c8aa97e4ddcf066902b4f9443cbb0b`;
the baseline checkout has different line endings but the same runner Git blob.

| Inputs; Onity tracker | Library | Prior B/op | Candidate B/op | Prior ns/op | Candidate ns/op | Prior repeat ns/op | Candidate repeat ns/op |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Pending; on (default) | OnityTask | 1,620 | 1,556 | 4,124.2 | 4,216.8 | 4,179.5 | 4,322.7 |
| Pending; on (default) | UniTask | 160 | 160 | 747.4 | 751.6 | 728.5 | 733.3 |
| Pending; off | OnityTask | 608 | 544 | 2,027.3 | 2,014.4 | 2,030.4 | 2,087.2 |
| Pending; off | UniTask | 160 | 160 | 721.9 | 714.9 | 712.4 | 742.1 |
| Both completed; on (default) | OnityTask | 276 | 0 | 1,497.2 | 117.0 | 1,469.1 | 115.5 |
| Both completed; on (default) | UniTask | 128 | 128 | 345.1 | 316.4 | 321.2 | 330.2 |

Each B/op value was identical across eight raw allocation samples. The
Profiler positive and empty controls measured exactly 65,568 B and 0 B. The
completed-input path became allocation-free in this slice and was about 2.7×
faster than UniTask in the two candidate timing runs. Pending OnityTask still
allocates more and schedules slower than UniTask, including with the Onity
tracker off. These timing runs are separate Editor processes; small timing
differences in pending cases are not established product improvements.

The marker measures only the user-level two-argument `WhenAll` call and storage
of its return value. Inputs are prepared before the marker, then completed and
consumed after it. There are ten 128-operation warmup batches, eight timing
samples of 2,048 operations, and eight Profiler samples of 128 operations per
scenario and library. This Unity 2022.3.62f3 Editor/Mono result does not cover
completion callbacks, continuation dispatch, the full task lifecycle, frame
time, Player builds, or IL2CPP. Timing includes loop cost without subtraction.

Raw candidate [JSON](onity-whenall-fastpath-a27c14d-2026-09-23.json),
[CSV](onity-whenall-fastpath-a27c14d-2026-09-23.csv), and
[timing repeat](onity-whenall-fastpath-a27c14d-timing-repeat-2026-09-23.json)
are paired with [provenance](onity-whenall-fastpath-a27c14d-2026-09-23.provenance.json).
The same branch contains the [baseline JSON](onity-whenall-baseline-25c8e20-2026-09-23.json)
and [baseline repeat](onity-whenall-baseline-25c8e20-timing-repeat-2026-09-23.json).
