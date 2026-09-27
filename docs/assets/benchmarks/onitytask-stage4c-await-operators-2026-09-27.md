# OnityTask Stage 4c: sequential awaitable operators

Date: 2026-09-27. Candidate based on `3f098937`, package version 0.3.14,
Unity 2022.3.62f2. Functional verification and independent review passed.

## Scope

- Add SelectAwait and WhereAwait descriptions plus the ForEachAsync terminal
  consumer, with cancellation-aware OnityTask delegates.
- Process one upstream move and one delegate at a time. Preserve actual task
  fault/cancellation status, including faulted OCE, and consume accepted work
  exactly once. No implicit Unity or synchronization-context dispatch.
- Pass the enumeration token upstream and a lazily owned token to delegates.
  Disposal signals that token, starts eligible native upstream cleanup and
  waits for accepted observations, cleanup and cancellation quiescence.
- Preserve the frozen finite-stream base, consumers, token helper, channels,
  existing assemblies and dependencies. Private coordinator state starts
  unpooled; no zero-allocation or comparative speed claim.

## Required verification

- Inline, suspended and Task-backed delegates; sequential ordering, exact
  consumption, cancellation tokens and synchronous thrown exceptions.
- Long synchronous rejection without recursion, empty/pre-canceled sources,
  reentrant disposal and callbacks started after the initial MoveCore returns.
- Shared disposal, pending delegate observation, upstream cleanup ordering,
  cancellation-callback quiescence and cleanup-failure precedence.
- Focused and full Unity suites, plus Release Mono and IL2CPP smoke.

## Verification

- Runtime source review passed for implementation SHA256
  `459E41F166582074BD7FE046707F8727D0D6D426AFFDE66A1CC5FC940E636847`
  and extension SHA256
  `99A7840530C4ACB0C0FFDBA4BE670FC9468637373604025478B7248B468AFF29`.
- Roslyn semantic compilation: 78 runtime sources, 210 references, zero errors.
- Unity Release EditMode existing builder/stream/channel regression: 141/141
  passed, zero skipped, CLI exit 0. Host XML/log stem:
  `TestResults/plan13-stage4c-await-existing-editmode-release`.
- Initial focused Release EditMode: 33/34 passed; Unity exit 2, CLI exit 8.
  One cleanup-fault test asserted the decorated task's fault before its
  Task-backed continuation had propagated. The runtime did not change.
- Corrected focused Release EditMode: 34/34 passed, zero skipped, CLI exit 0.
  The test now waits with the existing deadline before status/result checks
  and final cleanup consumption. A second assertion verifies upstream disposal
  starts while the independently released cancellation callback is still held.
- The initial fixture SHA256 was
  `ED61D834F4592510B1087C7A09F3C2684C09B8C7770CFBB379E7B981BE209746`;
  the corrected fixture is
  `03221595977C0D40C46AD2B45D85221144A539746A3AFE46087DDC48C5CA3349`.
  Initial source is retained in the host's
  `TestResults/plan13-stage4c-await-initial-fixture/` directory.
- New Release PlayMode: 2/2 passed. Full suites in both normal and Release
  optimizations: 947/947 EditMode and 94/94 PlayMode per optimization, zero
  skipped and CLI exit 0 throughout.
- All 574 runtime/test/benchmark files matched source and host before the
  focused PlayMode/full-suite gates; the runtime/test subset contains 529 files.
  These runs all use the corrected fixture and frozen runtime.

[Eight runs, including the initial failure](onitytask-stage4c-await-operators-2026-09-27/test-results-summary.json),
[529-file Editor manifest](onitytask-stage4c-await-operators-2026-09-27/editor-source-hashes.json)
and [574-file Player manifest](onitytask-stage4c-await-operators-2026-09-27/source-hashes.json)
retain test counts, CLI codes and XML/log hashes.

## Player harness correction

The initial Mono Player passed 34/35 cases, then failed in the new disposal
smoke. Its CriticalGate is deliberately single-use, but the new case completed
it in a branch, again after the branch, and again in finally. The cleanup error
could also mask the first failure. Source review had incorrectly assumed this
Player gate was idempotent like the separate EditMode test gate.

The correction adds local completion ownership, set before the first completion
attempt, and guards every call site. Both completion-order assertions remain;
exceptions are not swallowed. Runtime and the shared CriticalGate did not change.
The initial smoke source SHA256 is
`5F25326098F4E910460B39404F76BD362238C2555A6A7BE71BD31B7A21001061`;
the corrected source is
`314EC272357B301DD52A1A3D3FDBDC484CF4393C0CF1DD7F8B1BC6C66C4E4A2C`.
The initial snapshot remains in the host's
`TestResults/plan13-stage4c-await-initial-smoke/` directory.

Retained [initial Mono failure](onitytask-stage4c-await-operators-2026-09-27/mono-smoke-initial.json)
and [initial Player source manifest](onitytask-stage4c-await-operators-2026-09-27/initial-player-source-hashes.json)
identify GUID `3442cb9cd1414b57b10267ad306d3665`, Unity exit 1 and CLI exit 6.
The final manifest changes only this smoke file after the full Editor suites;
their 529 runtime/test files remain identical.

## Final Player results and restoration

Fresh non-development Release Players pass 35/35 cases with CLI exit 0:

| Backend | Build GUID | Result |
| --- | --- | --- |
| Mono | `727cf6b1e791428b99ca5767c9ab7638` | 35/35 |
| IL2CPP | `c440c6fbdcb84058bfabfd1c690b3a73` | 35/35 |

Both report InternalPair execution-context support, flow enabled, runner pool
capacity 128 and tracking disabled. IL2CPP uses Release/Minimal stripping.
The two new groups exercise actual suspended delegates and both cleanup orders,
in addition to sequential operators, fault/cancellation identity and long filters.

All 574 source/host hashes matched after execution. After the failed initial
run and corrected builds, the host's Standalone backend remained Mono instead
of the original IL2CPP setting. With this host Editor closed, that single field
was restored; the complete original ProjectSettings.asset hash matches again.
The other two settings files were already restored, and the temporary scene
was removed. This restoration affected only the isolated verification host.

[Final Mono smoke](onitytask-stage4c-await-operators-2026-09-27/mono-smoke.json),
[final IL2CPP smoke](onitytask-stage4c-await-operators-2026-09-27/il2cpp-smoke.json)
and [provenance](onitytask-stage4c-await-operators-2026-09-27/provenance.json)
retain final build metadata, report/log hashes, snapshots and restoration evidence.

This is a functional checkpoint. Descriptions, owned tokens and pending
observation state allocate; allocation quantity, zero allocation and comparative
speed were not measured. The approved reactive-adapter packet follows next.

No commit, push or release was performed.
