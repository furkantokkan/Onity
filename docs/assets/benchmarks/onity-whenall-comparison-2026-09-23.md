# Two-input WhenAll comparison — Unity 2022.3.62f3 Editor/Mono

The `a75793f` candidate passed the product correctness suite, but the
calibrated scheduling allocation comparison rejects it as a performance
improvement over `25c8e20`. Both benchmark hosts used the same runner and menu
Git blobs, pinned UniTask `2.5.11` (`2e993ff18f28c931602a07292df0b0804eebef99`),
128 operations per batch, ten warmup batches per scenario and library, eight
timing samples, and eight Profiler allocation samples. The 65,568 B positive
and 0 B empty allocation controls passed in both hosts.

| Scheduling case | Baseline Onity B/op | Candidate Onity B/op | UniTask B/op | Baseline Onity/UniTask ns/op, timing repeat | Candidate Onity/UniTask ns/op, timing repeat |
| --- | ---: | ---: | ---: | ---: | ---: |
| Pending, Onity tracker default-on | 1,620 | 2,260 | 160 | 4,179.5 / 728.5 | 3,717.6 / 714.7 |
| Pending, Onity tracker off | 608 | 1,248 | 160 | 2,030.4 / 712.4 | 1,595.8 / 694.3 |
| Both completed, Onity tracker default-on | 276 | 1,268 | 128 | 1,469.1 / 321.2 | 2,799.8 / 322.3 |

Every allocation sample for a given case and library had the same byte total;
the full raw arrays are in the JSON reports. The first candidate timing process
was about three times slower for **both** libraries, so the table uses the
separate timing repeat, in which UniTask controls returned near the baseline
range. Absolute timing comparisons across separate Editor processes are
suggestive and do not establish a causal speed change. The completed-input
case regressed in the repeat, while pending timings moved in the opposite
direction. The stable allocation increase is sufficient to reject the candidate
for a goal that requires lower hot-path allocation.

The measured slice contains the two-argument `WhenAll` call and storing the
returned task. Input creation, completion, continuation dispatch, result
consumption, and full task lifetime are outside the marker. These results do
not describe Player/IL2CPP behavior.

- Candidate [combined raw JSON](onity-whenall-candidate-a75793f-2026-09-23.json),
  [timing repeat JSON](onity-whenall-candidate-a75793f-timing-repeat-2026-09-23.json),
  and [provenance](onity-whenall-candidate-a75793f-2026-09-23.provenance.json).
- Baseline raw reports and provenance are on branch
  `benchmark/onitytask-whenall-baseline` at the corresponding
  `docs/assets/benchmarks/onity-whenall-baseline-25c8e20-2026-09-23.*` paths.
