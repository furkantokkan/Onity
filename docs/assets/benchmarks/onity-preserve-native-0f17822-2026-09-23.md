# OnityTask Preserve vs UniTask sharing

- Unity: 2022.3.62f3; Editor Mono
- UniTask commit: 2e993ff18f28c931602a07292df0b0804eebef99
- Onity runtime commit: 0f1782236c43fb89a4ba630f3c7e5a020f0be30e
- Timing operations/sample: 4096; allocation operations/sample: 256; samples: 8
- Allocation counter: Unity Profiler GC.Alloc metadata; 64 KiB and empty controls passed.
- Empty harness: 6.21337890625 ns/op; 0 B/op

Pending native WhenAny<int> sources with two unresolved completion-source inputs. Only conversion or two late result reads are measured; input creation, pending callback registration, source completion, and first consumption are outside each slice. One-consumer cases compare Preserve with Preserve. Four-consumer cases compare OnityTask.Preserve with UniTask.AsTask, which returns a .NET Task. The pending callbacks and repeated results are checked after each batch. Samples retain harness overhead; no baseline subtraction or overall winner is inferred.

| Stage | Type | Consumers | Library | ns/op | B/op |
| --- | --- | ---: | --- | ---: | ---: |
| PreserveConversion | typed | 1 | OnityTask.Preserve | 477.1331787109375 | 264 |
| PreserveConversion | typed | 1 | UniTask.Preserve | 119.3267822265625 | 40 |
| PreserveTwoLateReads | typed | 1 | OnityTask.Preserve | 161.6729736328125 | 0 |
| PreserveTwoLateReads | typed | 1 | UniTask.Preserve | 108.2275390625 | 0 |
| PreserveConversion | typed | 4 | OnityTask.Preserve | 453.2928466796875 | 264 |
| PreserveConversion | typed | 4 | UniTask.AsTask | 366.9036865234375 | 104 |
| PreserveTwoLateReads | typed | 4 | OnityTask.Preserve | 150.5157470703125 | 0 |
| PreserveTwoLateReads | typed | 4 | UniTask.AsTask | 83.1512451171875 | 0 |
