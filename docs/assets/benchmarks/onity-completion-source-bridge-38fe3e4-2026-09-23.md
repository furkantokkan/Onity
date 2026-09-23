# Completion-source benchmark

- Unity: 2022.3.62f3; Editor Mono
- UniTask commit: 2e993ff18f28c931602a07292df0b0804eebef99
- Onity runtime commit: a7c7c9c07867a0f2a20a0b817ca0482e75b77795
- Timing operations/sample: 4096; allocation operations/sample: 256; samples: 8
- Allocation counter: Unity Profiler GC.Alloc metadata; 64 KiB and empty controls passed.
- Empty harness: 4.7760009765625 ns/op; 0 B/op

Synchronous main-thread slices only. Construction, pending registration, completion with callback dispatch, late registration, and pending AsTask conversion are isolated. Source preparation and cleanup are outside each slice. Samples retain harness overhead; no baseline subtraction or overall winner is inferred.

| Stage | Type | Consumers | Library | ns/op | B/op |
| --- | --- | ---: | --- | ---: | ---: |
| Construction | untyped | 1 | OnityTask | 265.8905029296875 | 120 |
| Construction | untyped | 1 | UniTask | 113.6199951171875 | 72 |
| PendingRegistration | untyped | 1 | OnityTask | 155.46875 | 0 |
| PendingRegistration | untyped | 1 | UniTask | 254.0740966796875 | 16 |
| PendingRegistration | untyped | 4 | OnityTask | 1590.75927734375 | 104 |
| PendingRegistration | untyped | 4 | UniTask | 2010.748291015625 | 152 |
| CompletionDispatch | untyped | 1 | OnityTask | 153.5980224609375 | 0 |
| CompletionDispatch | untyped | 1 | UniTask | 129.620361328125 | 0 |
| CompletionDispatch | untyped | 4 | OnityTask | 224.7283935546875 | 0 |
| CompletionDispatch | untyped | 4 | UniTask | 547.9217529296875 | 0 |
| LateRegistration | untyped | 1 | OnityTask | 146.6827392578125 | 0 |
| LateRegistration | untyped | 1 | UniTask | 151.8890380859375 | 0 |
| LateRegistration | untyped | 4 | OnityTask | 766.0980224609375 | 0 |
| LateRegistration | untyped | 4 | UniTask | 705.609130859375 | 0 |
| AsTask | untyped | 1 | OnityTask | 1124.530029296875 | 176 |
| AsTask | untyped | 1 | UniTask | 860.601806640625 | 120 |
| Construction | typed | 1 | OnityTask | 217.2332763671875 | 120 |
| Construction | typed | 1 | UniTask | 89.7979736328125 | 80 |
| PendingRegistration | typed | 1 | OnityTask | 149.5391845703125 | 0 |
| PendingRegistration | typed | 1 | UniTask | 223.4344482421875 | 16 |
| PendingRegistration | typed | 4 | OnityTask | 858.4747314453125 | 104 |
| PendingRegistration | typed | 4 | UniTask | 1044.5953369140625 | 152 |
| CompletionDispatch | typed | 1 | OnityTask | 90.4052734375 | 0 |
| CompletionDispatch | typed | 1 | UniTask | 87.255859375 | 0 |
| CompletionDispatch | typed | 4 | OnityTask | 141.595458984375 | 0 |
| CompletionDispatch | typed | 4 | UniTask | 374.078369140625 | 0 |
| LateRegistration | typed | 1 | OnityTask | 139.7552490234375 | 0 |
| LateRegistration | typed | 1 | UniTask | 143.4295654296875 | 0 |
| LateRegistration | typed | 4 | OnityTask | 536.5081787109375 | 0 |
| LateRegistration | typed | 4 | UniTask | 534.09423828125 | 0 |
| AsTask | typed | 1 | OnityTask | 807.9864501953125 | 176 |
| AsTask | typed | 1 | UniTask | 491.0003662109375 | 120 |
