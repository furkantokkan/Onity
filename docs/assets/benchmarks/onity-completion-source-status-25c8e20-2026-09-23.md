# Completion-source IsCompleted probe

- Unity: 2022.3.62f3; Editor Mono
- UniTask commit: 2e993ff18f28c931602a07292df0b0804eebef99
- Onity runtime commit: 25c8e202214a1b7b5fa258feb87a0f5d91750ad4
- Timing operations/sample: 4096; allocation operations/sample: 256; samples: 8
- Allocation counter: Unity Profiler GC.Alloc metadata; 64 KiB and empty controls passed.
- Empty harness: 7.9193115234375 ns/op; 0 B/op

Pending and completed-success GetAwaiter().IsCompleted reads on fresh typed and untyped sources. Setup and completion stay outside each measured slice. Samples retain harness overhead; no baseline subtraction or overall winner is inferred.

| Stage | Type | Consumers | Library | ns/op | B/op |
| --- | --- | ---: | --- | ---: | ---: |
| PendingStatus | untyped | 1 | OnityTask | 90.53955078125 | 0 |
| PendingStatus | untyped | 1 | UniTask | 77.5482177734375 | 0 |
| TerminalStatus | untyped | 1 | OnityTask | 96.051025390625 | 0 |
| TerminalStatus | untyped | 1 | UniTask | 78.82080078125 | 0 |
| PendingStatus | typed | 1 | OnityTask | 93.743896484375 | 0 |
| PendingStatus | typed | 1 | UniTask | 83.55712890625 | 0 |
| TerminalStatus | typed | 1 | OnityTask | 83.26416015625 | 0 |
| TerminalStatus | typed | 1 | UniTask | 78.0303955078125 | 0 |
