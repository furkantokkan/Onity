# OnityTask Preserve vs UniTask sharing

- Unity: 2022.3.62f3; Editor Mono
- UniTask commit: 2e993ff18f28c931602a07292df0b0804eebef99
- Onity runtime commit: c2f9358994b8198c12ab48e1569014787f65325e
- Timing operations/sample: 4096; allocation operations/sample: 256; samples: 8
- Allocation counter: Unity Profiler GC.Alloc metadata; 64 KiB and empty controls passed.
- Empty harness: 5.18798828125 ns/op; 0 B/op

Pending native WhenAny<int> sources with two unresolved completion-source inputs. Only conversion or two late result reads are measured; input creation, pending callback registration, source completion, and first consumption are outside each slice. One-consumer cases compare Preserve with Preserve. Four-consumer cases compare OnityTask.Preserve with UniTask.AsTask, which returns a .NET Task. The pending callbacks and repeated results are checked after each batch. Samples retain harness overhead; no baseline subtraction or overall winner is inferred.

| Stage | Type | Consumers | Library | ns/op | B/op |
| --- | --- | ---: | --- | ---: | ---: |
| PreserveConversion | typed | 1 | OnityTask.Preserve | 355.01708984375 | 120 |
| PreserveConversion | typed | 1 | UniTask.Preserve | 113.2293701171875 | 40 |
| PreserveTwoLateReads | typed | 1 | OnityTask.Preserve | 168.9300537109375 | 0 |
| PreserveTwoLateReads | typed | 1 | UniTask.Preserve | 106.4544677734375 | 0 |
| PreserveConversion | typed | 4 | OnityTask.Preserve | 400.1953125 | 120 |
| PreserveConversion | typed | 4 | UniTask.AsTask | 461.8743896484375 | 104 |
| PreserveTwoLateReads | typed | 4 | OnityTask.Preserve | 176.96533203125 | 0 |
| PreserveTwoLateReads | typed | 4 | UniTask.AsTask | 94.00634765625 | 0 |
