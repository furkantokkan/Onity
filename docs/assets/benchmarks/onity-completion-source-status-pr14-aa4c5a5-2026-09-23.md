# Completion-source IsCompleted probe

- Unity: 2022.3.62f3; Editor Mono
- UniTask commit: 2e993ff18f28c931602a07292df0b0804eebef99
- Onity runtime commit: aa4c5a587245270e83bc19006a6cffc472ff660c
- Timing operations/sample: 4096; allocation operations/sample: 256; samples: 8
- Allocation counter: Unity Profiler GC.Alloc metadata; 64 KiB and empty controls passed.
- Empty harness: 5.4443359375 ns/op; 0 B/op

Pending and completed-success GetAwaiter().IsCompleted reads on fresh typed and untyped sources. Setup and completion stay outside each measured slice. Samples retain harness overhead; no baseline subtraction or overall winner is inferred.

| Stage | Type | Consumers | Library | ns/op | B/op |
| --- | --- | ---: | --- | ---: | ---: |
| PendingStatus | untyped | 1 | OnityTask | 127.8778076171875 | 0 |
| PendingStatus | untyped | 1 | UniTask | 90.6890869140625 | 0 |
| TerminalStatus | untyped | 1 | OnityTask | 85.693359375 | 0 |
| TerminalStatus | untyped | 1 | UniTask | 78.2806396484375 | 0 |
| PendingStatus | typed | 1 | OnityTask | 120.0714111328125 | 0 |
| PendingStatus | typed | 1 | UniTask | 82.354736328125 | 0 |
| TerminalStatus | typed | 1 | OnityTask | 98.162841796875 | 0 |
| TerminalStatus | typed | 1 | UniTask | 79.6661376953125 | 0 |
