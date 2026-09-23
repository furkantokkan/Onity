# Completion-source benchmark

- Unity: 2022.3.62f3; Editor Mono
- UniTask commit: 2e993ff18f28c931602a07292df0b0804eebef99
- Onity runtime commit: a7c7c9c07867a0f2a20a0b817ca0482e75b77795
- Timing operations/sample: 4096; allocation operations/sample: 256; samples: 8
- Allocation counter: Unity Profiler GC.Alloc metadata; 64 KiB and empty controls passed.
- Empty harness: 4.9224853515625 ns/op; 0 B/op

Synchronous main-thread slices only. Construction, pending registration, completion with callback dispatch, late registration, and pending AsTask conversion are isolated. Source preparation and cleanup are outside each slice. Samples retain harness overhead; no baseline subtraction or overall winner is inferred.

| Stage | Type | Consumers | Library | ns/op | B/op |
| --- | --- | ---: | --- | ---: | ---: |
| Construction | untyped | 1 | OnityTask | 482.4462890625 | 120 |
| Construction | untyped | 1 | UniTask | 215.8660888671875 | 72 |
| PendingRegistration | untyped | 1 | OnityTask | 178.857421875 | 0 |
| PendingRegistration | untyped | 1 | UniTask | 448.3795166015625 | 16 |
| PendingRegistration | untyped | 4 | OnityTask | 1311.822509765625 | 104 |
| PendingRegistration | untyped | 4 | UniTask | 1552.1484375 | 152 |
| CompletionDispatch | untyped | 1 | OnityTask | 110.076904296875 | 0 |
| CompletionDispatch | untyped | 1 | UniTask | 97.2503662109375 | 0 |
| CompletionDispatch | untyped | 4 | OnityTask | 176.129150390625 | 0 |
| CompletionDispatch | untyped | 4 | UniTask | 402.2705078125 | 0 |
| LateRegistration | untyped | 1 | OnityTask | 154.7943115234375 | 0 |
| LateRegistration | untyped | 1 | UniTask | 144.83642578125 | 0 |
| LateRegistration | untyped | 4 | OnityTask | 544.8394775390625 | 0 |
| LateRegistration | untyped | 4 | UniTask | 506.8756103515625 | 0 |
| AsTask | untyped | 1 | OnityTask | 1011.6485595703124 | 848 |
| AsTask | untyped | 1 | UniTask | 447.576904296875 | 120 |
| Construction | typed | 1 | OnityTask | 209.6221923828125 | 120 |
| Construction | typed | 1 | UniTask | 81.1553955078125 | 80 |
| PendingRegistration | typed | 1 | OnityTask | 138.1591796875 | 0 |
| PendingRegistration | typed | 1 | UniTask | 220.2667236328125 | 16 |
| PendingRegistration | typed | 4 | OnityTask | 785.552978515625 | 104 |
| PendingRegistration | typed | 4 | UniTask | 993.743896484375 | 152 |
| CompletionDispatch | typed | 1 | OnityTask | 87.841796875 | 0 |
| CompletionDispatch | typed | 1 | UniTask | 77.679443359375 | 0 |
| CompletionDispatch | typed | 4 | OnityTask | 148.4100341796875 | 0 |
| CompletionDispatch | typed | 4 | UniTask | 358.3953857421875 | 0 |
| LateRegistration | typed | 1 | OnityTask | 160.7025146484375 | 0 |
| LateRegistration | typed | 1 | UniTask | 147.0245361328125 | 0 |
| LateRegistration | typed | 4 | OnityTask | 602.984619140625 | 0 |
| LateRegistration | typed | 4 | UniTask | 558.3465576171875 | 0 |
| AsTask | typed | 1 | OnityTask | 1102.69775390625 | 848 |
| AsTask | typed | 1 | UniTask | 497.039794921875 | 120 |
