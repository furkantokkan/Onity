# Completion-source benchmark

- Unity: 2022.3.62f3; Editor Mono
- UniTask commit: 2e993ff18f28c931602a07292df0b0804eebef99
- Onity runtime commit: a7c7c9c07867a0f2a20a0b817ca0482e75b77795
- Timing operations/sample: 4096; allocation operations/sample: 256; samples: 8
- Allocation counter: Unavailable: separate Unity Profiler pass has not passed calibration.
- Empty harness: 9.47265625 ns/op; 0 B/op

Synchronous main-thread slices only. Construction, pending registration, completion with callback dispatch, late registration, and pending AsTask conversion are isolated. Source preparation and cleanup are outside each slice. Samples retain harness overhead; no baseline subtraction or overall winner is inferred.

| Stage | Type | Consumers | Library | ns/op | B/op |
| --- | --- | ---: | --- | ---: | ---: |
| Construction | untyped | 1 | OnityTask | 307.50732421875 | -1 |
| Construction | untyped | 1 | UniTask | 130.5999755859375 | -1 |
| PendingRegistration | untyped | 1 | OnityTask | 219.384765625 | -1 |
| PendingRegistration | untyped | 1 | UniTask | 339.16015625 | -1 |
| PendingRegistration | untyped | 4 | OnityTask | 1259.75341796875 | -1 |
| PendingRegistration | untyped | 4 | UniTask | 1619.610595703125 | -1 |
| CompletionDispatch | untyped | 1 | OnityTask | 119.37255859375 | -1 |
| CompletionDispatch | untyped | 1 | UniTask | 117.034912109375 | -1 |
| CompletionDispatch | untyped | 4 | OnityTask | 205.7708740234375 | -1 |
| CompletionDispatch | untyped | 4 | UniTask | 500.2685546875 | -1 |
| LateRegistration | untyped | 1 | OnityTask | 155.8807373046875 | -1 |
| LateRegistration | untyped | 1 | UniTask | 166.290283203125 | -1 |
| LateRegistration | untyped | 4 | OnityTask | 709.0484619140625 | -1 |
| LateRegistration | untyped | 4 | UniTask | 670.5841064453125 | -1 |
| AsTask | untyped | 1 | OnityTask | 1446.8658447265625 | -1 |
| AsTask | untyped | 1 | UniTask | 1157.958984375 | -1 |
| Construction | typed | 1 | OnityTask | 307.696533203125 | -1 |
| Construction | typed | 1 | UniTask | 116.2506103515625 | -1 |
| PendingRegistration | typed | 1 | OnityTask | 144.7174072265625 | -1 |
| PendingRegistration | typed | 1 | UniTask | 224.6978759765625 | -1 |
| PendingRegistration | typed | 4 | OnityTask | 795.7855224609375 | -1 |
| PendingRegistration | typed | 4 | UniTask | 1008.3343505859375 | -1 |
| CompletionDispatch | typed | 1 | OnityTask | 89.581298828125 | -1 |
| CompletionDispatch | typed | 1 | UniTask | 86.737060546875 | -1 |
| CompletionDispatch | typed | 4 | OnityTask | 131.475830078125 | -1 |
| CompletionDispatch | typed | 4 | UniTask | 365.1123046875 | -1 |
| LateRegistration | typed | 1 | OnityTask | 143.5577392578125 | -1 |
| LateRegistration | typed | 1 | UniTask | 144.9554443359375 | -1 |
| LateRegistration | typed | 4 | OnityTask | 540.057373046875 | -1 |
| LateRegistration | typed | 4 | UniTask | 550.9307861328125 | -1 |
| AsTask | typed | 1 | OnityTask | 769.5953369140625 | -1 |
| AsTask | typed | 1 | UniTask | 464.501953125 | -1 |
