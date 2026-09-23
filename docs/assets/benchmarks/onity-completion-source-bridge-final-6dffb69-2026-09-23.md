# Completion-source benchmark

- Unity: 2022.3.62f3; Editor Mono
- UniTask commit: 2e993ff18f28c931602a07292df0b0804eebef99
- Onity runtime commit: a7c7c9c07867a0f2a20a0b817ca0482e75b77795
- Timing operations/sample: 4096; allocation operations/sample: 256; samples: 8
- Allocation counter: Unity Profiler GC.Alloc metadata; 64 KiB and empty controls passed.
- Empty harness: 5.6488037109375 ns/op; 0 B/op

Synchronous main-thread slices only. Construction, pending registration, completion with callback dispatch, late registration, and pending AsTask conversion are isolated. Source preparation and cleanup are outside each slice. Samples retain harness overhead; no baseline subtraction or overall winner is inferred.

| Stage | Type | Consumers | Library | ns/op | B/op |
| --- | --- | ---: | --- | ---: | ---: |
| Construction | untyped | 1 | OnityTask | 235.6597900390625 | 120 |
| Construction | untyped | 1 | UniTask | 105.743408203125 | 72 |
| PendingRegistration | untyped | 1 | OnityTask | 142.4896240234375 | 0 |
| PendingRegistration | untyped | 1 | UniTask | 223.236083984375 | 16 |
| PendingRegistration | untyped | 4 | OnityTask | 927.06604003906239 | 104 |
| PendingRegistration | untyped | 4 | UniTask | 1156.1126708984375 | 152 |
| CompletionDispatch | untyped | 1 | OnityTask | 104.78515625 | 0 |
| CompletionDispatch | untyped | 1 | UniTask | 95.5596923828125 | 0 |
| CompletionDispatch | untyped | 4 | OnityTask | 196.331787109375 | 0 |
| CompletionDispatch | untyped | 4 | UniTask | 429.4769287109375 | 0 |
| LateRegistration | untyped | 1 | OnityTask | 154.4952392578125 | 0 |
| LateRegistration | untyped | 1 | UniTask | 152.7191162109375 | 0 |
| LateRegistration | untyped | 4 | OnityTask | 577.6641845703125 | 0 |
| LateRegistration | untyped | 4 | UniTask | 561.4227294921875 | 0 |
| AsTask | untyped | 1 | OnityTask | 1014.892578125 | 176 |
| AsTask | untyped | 1 | UniTask | 592.7581787109375 | 120 |
| Construction | typed | 1 | OnityTask | 284.320068359375 | 120 |
| Construction | typed | 1 | UniTask | 135.1593017578125 | 80 |
| PendingRegistration | typed | 1 | OnityTask | 164.19677734375 | 0 |
| PendingRegistration | typed | 1 | UniTask | 271.649169921875 | 16 |
| PendingRegistration | typed | 4 | OnityTask | 908.746337890625 | 104 |
| PendingRegistration | typed | 4 | UniTask | 1158.1207275390625 | 152 |
| CompletionDispatch | typed | 1 | OnityTask | 97.979736328125 | 0 |
| CompletionDispatch | typed | 1 | UniTask | 110.5987548828125 | 0 |
| CompletionDispatch | typed | 4 | OnityTask | 167.2760009765625 | 0 |
| CompletionDispatch | typed | 4 | UniTask | 418.5211181640625 | 0 |
| LateRegistration | typed | 1 | OnityTask | 186.7462158203125 | 0 |
| LateRegistration | typed | 1 | UniTask | 192.7947998046875 | 0 |
| LateRegistration | typed | 4 | OnityTask | 635.8612060546875 | 0 |
| LateRegistration | typed | 4 | UniTask | 634.1552734375 | 0 |
| AsTask | typed | 1 | OnityTask | 1094.5068359375 | 176 |
| AsTask | typed | 1 | UniTask | 755.7159423828125 | 120 |
