# Pending two-input WhenAll tracker allocation attribution

- Source: Onity product commit `ba2c72cc00880ca63645c726f8c1ea965c895404`.
- Runtime: Unity 2022.3.62f3 Editor, Mono; pinned UniTask `2e993ff18f28c931602a07292df0b0804eebef99`.
- Slice: `OnityTask.WhenAll(first, second)` scheduling and returned-value storage for two unresolved native completion-source inputs. Preparation, completion, continuation dispatch, and consumption are outside the marker.
- Profiler: eight samples of 128 operations for each case. Every pending Onity sample's resolved `GC.Alloc` callstack total matched its marker total (tracker on 8/8; tracker off 8/8). The positive control was 65,568 B and the empty control was 0 B.
- Source SHA-256: `c0c463bb5801c6d0d417e23dd6cbe94ce2cca8b43d9db0c41a780e5115921565`; runner SHA-256: `5b748a81eebe2307f844746c22aa55faf79b700acd31dd1650ef9f079950e7f9`.

| Pending case | Onity B/op | UniTask B/op |
| --- | ---: | ---: |
| Onity tracker on | 1,556 | 160 |
| Onity tracker off | 544 | 160 |

The 1,012 B/op difference between the two Onity cases is present in callstacks through `OnityTaskTracker.TrackInternal` and its `Task.ContinueWith` registration. The following groups are exclusive to the tracker-on marker. Values are the mean of 1,024 measured operations; all eight samples were identical.

| Allocation callstack leaf | B/op | Allocations/op |
| --- | ---: | ---: |
| `UnitySynchronizationContext..ctor` via `ExecutionContext.Capture` and `Task.ContinueWith` | 552 | 2 |
| `OnityTaskTracker.TrackInternal` | 148 | 2 |
| `Task.ContinueWith` | 80 | 1 |
| `ExecutionContext.Capture` | 72 | 1 |
| `Task.EnsureContingentPropertiesInitializedCore` via `ContinueWith` | 72 | 1 |
| `UnitySynchronizationContext.CreateCopy` | 48 | 1 |
| `Task.ContinueWithCore` | 40 | 1 |
| **Total** | **1,012** | **9** |

The direct `TrackInternal` group is consistent with its captured lambda and delegate at the `task.ContinueWith(completedTask => CompleteTrackedTask(taskId, completedTask), ...)` call. Profiler callstacks identify methods and allocation counts, not managed object types, so object identity is an inference. A cached noncapturing `Action<Task>` that reads `completedTask.Id` is a focused candidate for this 148 B/op group. The remaining `ContinueWith` groups include `ExecutionContext` and Unity synchronization-context capture; removing those requires separate semantic and performance verification. This attribution does not treat the full 1,012 B/op as closure cost.

The common 544 B/op tracker-off path includes two native-to-Task completion-source bridges and the `Task.WhenAll` path. The already-completed control produced 0 B/op for Onity and 128 B/op for UniTask in this run. These results cover Editor/Mono scheduling only; they do not measure full lifecycle, Player/IL2CPP, or behavior under other synchronization contexts.

Raw per-sample callstacks and timing are in [the JSON report](onity-whenall-tracker-attribution-ba2c72c-2026-09-24.json). [Provenance](onity-whenall-tracker-attribution-ba2c72c-2026-09-24.provenance.json) records the temporary benchmark setup and restored configuration hashes.
