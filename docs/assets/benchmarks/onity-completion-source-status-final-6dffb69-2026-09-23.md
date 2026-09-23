# Completion-source benchmark

- Unity: 2022.3.62f3; Editor Mono
- UniTask commit: 2e993ff18f28c931602a07292df0b0804eebef99
- Onity runtime commit: a7c7c9c07867a0f2a20a0b817ca0482e75b77795
- Timing operations/sample: 4096; allocation operations/sample: 256; samples: 8
- Allocation counter: Unity Profiler GC.Alloc metadata; 64 KiB and empty controls passed.
- Empty harness: 5.8319091796875 ns/op; 0 B/op

Pending and completed-success GetAwaiter().IsCompleted reads on fresh typed and untyped sources. Setup and completion stay outside each measured slice. Samples retain harness overhead; no baseline subtraction or overall winner is inferred.

| Stage | Type | Consumers | Library | ns/op | B/op |
| --- | --- | ---: | --- | ---: | ---: |
| PendingStatus | untyped | 1 | OnityTask | 125.8697509765625 | 0 |
| PendingStatus | untyped | 1 | UniTask | 92.535400390625 | 0 |
| TerminalStatus | untyped | 1 | OnityTask | 97.5616455078125 | 0 |
| TerminalStatus | untyped | 1 | UniTask | 85.07080078125 | 0 |
| PendingStatus | typed | 1 | OnityTask | 114.94140625 | 0 |
| PendingStatus | typed | 1 | UniTask | 80.0811767578125 | 0 |
| TerminalStatus | typed | 1 | OnityTask | 88.983154296875 | 0 |
| TerminalStatus | typed | 1 | UniTask | 76.373291015625 | 0 |
