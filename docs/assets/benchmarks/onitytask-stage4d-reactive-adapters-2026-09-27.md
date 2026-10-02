# OnityTask Stage 4d: reactive stream adapters

Date: 2026-09-27. Candidate based on `3f098937`, package version 0.3.14,
Unity 2022.3.62f2. Implementation and verification are in progress.

## Scope

- Convert IOnityObservable<T> to a lazy native async stream with an explicit
  positive buffer capacity. Reject overflow, detach, drain accepted values,
  then fault. Source errors, including OCE, remain faults.
- Convert a native async stream to an observable with one sequential enumerator
  per subscription. Observe accepted native work and dispose upstream before
  natural terminal notification. Explicit disposal stops future notifications.
- Preserve cancellation ownership, exact-once cleanup and callback reentrancy.
  No implicit thread hop or added thread-safety guarantee for reactive sources.
- Keep existing reactive primitives, finite-stream base, token helper, channels,
  assemblies and dependencies unchanged. Pending adapter state starts unpooled.

## Required verification

- Lazy subscription, pre-cancellation, synchronous emission/termination before
  subscription-handle assignment, null handles and thrown Subscribe calls.
- FIFO drain, overflow, faulted OCE, cancellation/disposal races, held callbacks,
  exactly-once unsubscribe and cleanup-failure precedence.
- Independent reverse subscriptions, native pending moves settled by disposal,
  observer exceptions, value-only error reporting and observer disposal ownership.
- Focused and full Editor suites, both Release Player backends, and a bounded
  allocation workload with calibrated controls and explicit measurement limits.

## Planned bounded measurement

Use two synchronous pending-delivery rows, 4096 items/window, three warmups
and two runs of eight measured windows:

- Real Subject<int> to a capacity-one stream: prime lazy subscription with a
  sentinel outside, then measure pending Move, publish, consume and Current.
- Native source to observable: prepare public completion-source slots and
  subscription outside, prime one sentinel, then measure slot completion,
  observer delivery and registration of the next pending move.

Each slice includes public dispatch and scalar count/checksum accounting;
producer-slot allocation, initial subscription and cleanup are excluded.
Per-window delivery count must be 4096 and checksum 8386560, excluding the
sentinel. Restore GC before teardown and settle any remaining accepted work.

Use the existing calibrated counter, empty and held-64-KiB controls, retaining
all raw samples and selection/rejection metadata. HeapDelta measures heap
growth; it cannot prove exact allocated bytes or zero GC. There is no speed
comparison or allocation-free expectation. Each fresh Player also runs smoke.

New adapter tests and performance results remain pending. No commit, push or
release was performed.

## Implementation handoff

The initial candidate is frozen for independent review and verification:

- Adapter SHA256: `5465373F5216E936C1E7614B9EA77318B56988AEA656509EEDFA86FAFCAF4390`.
- Extension SHA256: `E08111D27C4FE50BAA51965111C0703F0CE4806778A19E48183766CF887F519A`.
- New metadata GUID: `b86ecf2359964aaf933bc8d6b7d859b9`.
- Roslyn semantic compilation: 79 runtime sources, 210 references, zero errors.
  This is not a Unity execution result. The three files matched the verification
  host after staging; existing runtime helpers remain unchanged.
- Independent runtime review found no blocking issue in the frozen candidate;
  functional and allocation verification remain required.
- Unity Release existing builder/finite-stream/BCL/channel/awaitable-operator
  regression passed 175/175, zero skipped, CLI exit 0. Host XML/log stem:
  `TestResults/plan13-stage4d-reactive-existing-editmode-release`.
