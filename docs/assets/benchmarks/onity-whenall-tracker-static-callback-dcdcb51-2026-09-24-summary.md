# Pending WhenAll tracker static-callback comparison

- Baseline product: `ba2c72cc00880ca63645c726f8c1ea965c895404`; static-callback candidate: `dcdcb510b32c86c6d5d4d99cc898a56193aa431a`.
- Unity 2022.3.62f3 Editor/Mono; UniTask pinned to `2e993ff18f28c931602a07292df0b0804eebef99`.
- The benchmark runner and editor menu Git blobs are identical in both hosts (`0e6db342404d6a74c86cf9ed27b7a9cca6932c45` and `6cb9d93b70f4517675805a23a41816597170efaa`). Working-file SHA-256 values differ because the two checkouts use different line endings.
- The marker includes scheduling `OnityTask.WhenAll(first, second)` and storing its result for two pending native completion-source inputs. Input preparation, completion, continuation dispatch, and consumption occur outside it.
- Eight allocation samples of 128 operations per scenario passed the 65,568 B positive and 0 B empty controls. Pending Onity marker and callstack bytes reconciled in all 16 samples. All eight samples within each Onity pending case reported the same bytes.

| Pending case | Baseline B/op | Candidate B/op | Change |
| --- | ---: | ---: | ---: |
| Onity tracker on | 1,556 | 1,408 | -148 |
| Onity tracker off | 544 | 544 | 0 |
| UniTask (both matched scenarios) | 160 | 160 | 0 |

The baseline's direct `OnityTaskTracker.TrackInternal` callstack group (148 B/op, two allocations/op) is absent from the candidate. The only product change replaces its capturing `ContinueWith` lambda with a cached static `Action<Task>` that reads `completedTask.Id`. This measured change removes exactly that group. The tracker-on minus tracker-off difference is now 864 B/op, all still in `Task.ContinueWith`, `ExecutionContext.Capture`, and Unity synchronization-context callstacks:

| Remaining tracker-only allocation callstack leaf | B/op | Allocations/op |
| --- | ---: | ---: |
| `UnitySynchronizationContext..ctor` | 552 | 2 |
| `Task.ContinueWith` | 80 | 1 |
| `ExecutionContext.Capture` | 72 | 1 |
| `Task.EnsureContingentPropertiesInitializedCore` | 72 | 1 |
| `UnitySynchronizationContext.CreateCopy` | 48 | 1 |
| `Task.ContinueWithCore` | 40 | 1 |
| **Total** | **864** | **7** |

The common 544 B/op tracker-off path and the completed-input Onity control (0 B/op) did not change. UniTask's pending path remained 160 B/op. The static callback is a narrow improvement; it does not make Onity's pending scheduling allocation competitive in this Editor/Mono slice. The remaining context capture should not be suppressed without preserving `ExecutionContext` behavior and separate tests. These measurements do not establish full lifecycle, Player/IL2CPP, or timing superiority.

See the [candidate raw JSON and per-sample callstacks](onity-whenall-tracker-static-callback-dcdcb51-2026-09-24.json), [baseline summary at pinned evidence commit](https://github.com/furkantokkan/Onity/blob/d585251/docs/assets/benchmarks/onity-whenall-tracker-attribution-ba2c72c-2026-09-24-summary.md), and [provenance](onity-whenall-tracker-static-callback-dcdcb51-2026-09-24.provenance.json).
