# Typed `WhenAll<int>(params)` comparison

The baseline is product commit `c9ba3548dbf7432d88801739da0d93fc908a3d6c`.
The candidate is `970a4ae77d1fed16a83b2930fff4fdbb24da320d`. Both hosts use
the same benchmark runner Git blob `8233d48b0ebe3f79653c6062475d23af9bae5e3e`
and UniTask 2.5.11 pinned at `2e993ff18f28c931602a07292df0b0804eebef99`.
The working-file SHA-256 values differ only because the hosts checked out
different line endings; their normalized source text is identical.

| Inputs | Library | Baseline B/op | Candidate B/op | Baseline ns/op, runs 1 / 2 | Candidate ns/op, runs 1 / 2 |
| --- | --- | ---: | ---: | ---: | ---: |
| Pending 2; Onity tracker on | OnityTask | 1,408 | 1,408 | 4,907.6 / 5,058.4 | 14,037.2 / 4,648.7 |
| Pending 2; Onity tracker on | UniTask | 144 | 144 | 903.9 / 1,176.6 | 2,932.7 / 848.1 |
| Completed 2; Onity tracker on | OnityTask | 392 | **40** | 2,201.6 / 2,139.2 | **622.4 / 341.2** |
| Completed 2; Onity tracker on | UniTask | 112 | 112 | 462.4 / 409.4 | 1,391.0 / 432.8 |
| Completed 4; Onity tracker on | OnityTask | 592 | **48** | 2,374.7 / 3,334.5 | **986.1 / 408.1** |
| Completed 4; Onity tracker on | UniTask | 120 | 120 | 653.4 / 639.5 | 1,640.4 / 593.4 |

The candidate completed path allocates 72 B/op less than UniTask for both
input counts. It is faster than UniTask within each of the two candidate timing
processes. The first candidate process was slower for both libraries after its
fresh Unity Library import; the separate repeat is closer to baseline UniTask
timing. This is a scheduling-slice result, not an end-to-end task benchmark.
The pending control did not change in allocation and remains substantially
slower and more allocating than UniTask with default Onity tracking enabled.

Each input array was prepared before the marker and reused. Pending inputs
were freshly created before each slice. The marker includes the typed
`WhenAll<int>(params)` call and storage of its return value. Input completion,
continuation dispatch, ordered-result validation, and `GetResult` occurred
afterward. Each host ran two independent timing processes with eight samples
of 2,048 operations per case, plus eight Profiler allocation samples of 128
operations per case. There were ten 128-operation warmup batches per case.
Both Profiler passes measured exactly 65,568 B for the positive control and
0 B for the empty control, including all eight empty-harness samples. Every
per-case allocation sample agreed within its case.

Raw [baseline JSON](onity-typed-whenall-baseline-c9ba354-2026-09-24.json),
[baseline timing repeat](onity-typed-whenall-baseline-c9ba354-timing-repeat-2026-09-24.json),
[candidate JSON](onity-typed-whenall-candidate-970a4ae-2026-09-24.json), and
[candidate timing repeat](onity-typed-whenall-candidate-970a4ae-timing-repeat-2026-09-24.json)
are accompanied by [provenance](onity-typed-whenall-comparison-2026-09-24.provenance.json).
The runner also generated CSV and Markdown forms of each raw report.

This Unity 2022.3.62f3 Editor/Mono measurement does not establish Player,
IL2CPP, frame-time, completion-callback, or full-lifecycle superiority.
The empty timing loop was measured but not subtracted. A larger completed
input-count study remains useful for choosing a linear general-purpose path.
