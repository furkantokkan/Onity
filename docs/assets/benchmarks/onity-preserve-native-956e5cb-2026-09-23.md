# OnityTask Preserve vs UniTask sharing

- Unity: 2022.3.62f3; Editor Mono
- UniTask commit: 2e993ff18f28c931602a07292df0b0804eebef99
- Onity runtime commit: 956e5cb356cdce0b324bfdad5242ac3c2b537a97
- Timing operations/sample: 4096; allocation operations/sample: 256; samples: 8
- Allocation counter: Unity Profiler GC.Alloc metadata; 64 KiB and empty controls passed.
- Empty harness: 5.55419921875 ns/op; 0 B/op

Pending native WhenAny<int> sources with two unresolved completion-source inputs. Only conversion or two late result reads are measured; input creation, pending callback registration, source completion, and first consumption are outside each slice. One-consumer cases compare Preserve with Preserve. Four-consumer cases compare OnityTask.Preserve with UniTask.AsTask, which returns a .NET Task. The pending callbacks and repeated results are checked after each batch. Samples retain harness overhead; no baseline subtraction or overall winner is inferred.

| Stage | Type | Consumers | Library | ns/op | B/op |
| --- | --- | ---: | --- | ---: | ---: |
| PreserveConversion | typed | 1 | OnityTask.Preserve | 362.9364013671875 | 248 |
| PreserveConversion | typed | 1 | UniTask.Preserve | 93.5028076171875 | 40 |
| PreserveTwoLateReads | typed | 1 | OnityTask.Preserve | 139.056396484375 | 0 |
| PreserveTwoLateReads | typed | 1 | UniTask.Preserve | 90.56396484375 | 0 |
| PreserveConversion | typed | 4 | OnityTask.Preserve | 381.597900390625 | 248 |
| PreserveConversion | typed | 4 | UniTask.AsTask | 387.6129150390625 | 104 |
| PreserveTwoLateReads | typed | 4 | OnityTask.Preserve | 143.9544677734375 | 0 |
| PreserveTwoLateReads | typed | 4 | UniTask.AsTask | 82.0281982421875 | 0 |
