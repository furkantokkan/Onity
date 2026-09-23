# OnityTask Preserve vs UniTask sharing

- Unity: 2022.3.62f3; Editor Mono
- UniTask commit: 2e993ff18f28c931602a07292df0b0804eebef99
- Onity runtime commit: 83308ebb20cfbc4100d43b9c18521356292158c9
- Timing operations/sample: 4096; allocation operations/sample: 256; samples: 8
- Allocation counter: Unity Profiler GC.Alloc metadata; 64 KiB and empty controls passed.
- Empty harness: 11.419677734375 ns/op; 0 B/op

Pending native WhenAny<int> sources with two unresolved completion-source inputs. Only conversion or two late result reads are measured; input creation, pending callback registration, source completion, and first consumption are outside each slice. One-consumer cases compare Preserve with Preserve. Four-consumer cases compare OnityTask.Preserve with UniTask.AsTask, which returns a .NET Task. The pending callbacks and repeated results are checked after each batch. Samples retain harness overhead; no baseline subtraction or overall winner is inferred.

| Stage | Type | Consumers | Library | ns/op | B/op |
| --- | --- | ---: | --- | ---: | ---: |
| PreserveConversion | typed | 1 | OnityTask.Preserve | 1402.6214599609375 | 264 |
| PreserveConversion | typed | 1 | UniTask.Preserve | 443.7286376953125 | 40 |
| PreserveTwoLateReads | typed | 1 | OnityTask.Preserve | 257.55615234375 | 0 |
| PreserveTwoLateReads | typed | 1 | UniTask.Preserve | 129.150390625 | 0 |
| PreserveConversion | typed | 4 | OnityTask.Preserve | 1654.9652099609375 | 264 |
| PreserveConversion | typed | 4 | UniTask.AsTask | 1257.452392578125 | 104 |
| PreserveTwoLateReads | typed | 4 | OnityTask.Preserve | 246.2890625 | 0 |
| PreserveTwoLateReads | typed | 4 | UniTask.AsTask | 126.300048828125 | 0 |
