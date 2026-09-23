# OnityTask Preserve vs UniTask sharing

- Unity: 2022.3.62f3; Editor Mono
- UniTask commit: 2e993ff18f28c931602a07292df0b0804eebef99
- Onity runtime commit: 6a15305edfc0cf8e5cd1bba8afac4dc00fb5a8bc
- Timing operations/sample: 4096; allocation operations/sample: 256; samples: 8
- Allocation counter: Unity Profiler GC.Alloc metadata; 64 KiB and empty controls passed.
- Empty harness: 5.09033203125 ns/op; 0 B/op

Pending native WhenAny<int> sources with two unresolved completion-source inputs. Only conversion or two late result reads are measured; input creation, pending callback registration, source completion, and first consumption are outside each slice. One-consumer cases compare Preserve with Preserve. Four-consumer cases compare OnityTask.Preserve with UniTask.AsTask, which returns a .NET Task. The pending callbacks and repeated results are checked after each batch. Samples retain harness overhead; no baseline subtraction or overall winner is inferred.

| Stage | Type | Consumers | Library | ns/op | B/op |
| --- | --- | ---: | --- | ---: | ---: |
| PreserveConversion | typed | 1 | OnityTask.Preserve | 319.8944091796875 | 120 |
| PreserveConversion | typed | 1 | UniTask.Preserve | 99.6368408203125 | 40 |
| PreserveTwoLateReads | typed | 1 | OnityTask.Preserve | 148.5595703125 | 0 |
| PreserveTwoLateReads | typed | 1 | UniTask.Preserve | 98.699951171875 | 0 |
| PreserveConversion | typed | 4 | OnityTask.Preserve | 373.14453125 | 120 |
| PreserveConversion | typed | 4 | UniTask.AsTask | 420.7183837890625 | 104 |
| PreserveTwoLateReads | typed | 4 | OnityTask.Preserve | 153.497314453125 | 0 |
| PreserveTwoLateReads | typed | 4 | UniTask.AsTask | 90.8782958984375 | 0 |
