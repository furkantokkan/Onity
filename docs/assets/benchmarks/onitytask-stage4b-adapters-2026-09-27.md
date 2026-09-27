# OnityTask Stage 4b: Update streams and BCL adapters

Date: 2026-09-27. Uncommitted candidate based on `3f098937` (package version
still 0.3.14), Unity 2022.3.62f2. Functional verification passed.

## Change and ownership

- Added pull-based `OnityAsyncEnumerable.EveryUpdate()` and BCL/native
  `AsOnityAsyncEnumerable` / `AsAsyncEnumerable` adapters in the existing Unity
  assembly. No new runtime assembly or package dependency. The existing benchmark
  assembly references Onity.Core directly to compile the public Unit result.
- Kept the verified finite enumerator base, operators and consumers unchanged.
  BCL import independently serializes move consumption before upstream cleanup.
- Private token ownership pins initialization and cancellation calls, detaches
  forwarding outside locks, then waits for observation and cleanup before CTS
  disposal. Caller token sources remain caller-owned.
- Native exports preserve actual fault/cancellation status through explicit
  bridges. Faulted OCE stays faulted. Ordinary BCL caller await capture remains
  available; internal observation adds no creator-context dispatch.

## Review corrections before execution

- A pending-status recheck could overlap the sole completion callback and lose
  that notification. The observer now records an overlapping request and drains
  it before releasing its active-reader pin, retaining single consumption.
- Export bridge creation now uses the existing execution-context suppression
  helper, including Mono's TaskCompletionSource construction behavior.
- These were source-review findings corrected before the first Unity run;
  they were not measured test failures. Independent race coverage passes.

## Verification

| Check | Result |
| --- | --- |
| Roslyn semantic compilation | 72 runtime sources, 210 references, zero errors |
| Existing finite stream and async builder tests, Release EditMode | 79/79 passed, zero skipped, CLI exit 0 |
| Initial adapter EditMode run, Release | 26/28 passed; two fixture failures retained; CLI exit 1, Unity exit 2 |
| Corrected adapter EditMode run, Release | 28/28 passed, zero skipped, CLI exit 0 |
| New adapter PlayMode run, Release | 4/4 passed, zero skipped, CLI exit 0 |
| Full EditMode, normal and Release | 879/879 in each mode; zero skipped; both CLI exits 0 |
| Full PlayMode, normal and Release | 90/90 in each mode; zero skipped; both CLI exits 0 |
| Release Mono / IL2CPP smoke | 31/31 per backend; both CLI exits 0 |

The initial Unity result is
`TestResults/plan13-stage4b-existing-editmode-release.xml` on the isolated
verification host, `C:/Users/e-fur/.codex/worktrees/onity-builder-benchmark-4be50dc/Onity`.
Eight initially staged source/metadata hashes matched the frozen implementation.
All 509 runtime/test hashes matched before and after the full Editor runs.
The final benchmark integration snapshot contains 552 matching source, assembly
and metadata files; it adds only benchmark changes after those Editor runs.

The initial adapter run (`plan13-stage4b-new-editmode-release.xml`) exposed two
fixture expectations. Actual cancellation reconstructs an OCE with the stored
token; only a faulted OCE promises exception identity. The session probe also
attempted loop repair from OnDestroy during an already closing Play session.
That guard correctly rejected reinitialization. The correction retains token,
fault identity and session retirement assertions while removing repair during
normal teardown. Initial fixture/probe snapshots remain beside the host results.
Review also requires observing worker Task failures after bounded joins, so a
worker invocation failure cannot leave a default result that falsely passes.

## Player and revision evidence

| Backend | Build GUID | Raw result |
| --- | --- | --- |
| Mono | `f519b73276814a03abceeaa68d25d582` | [31 passing cases](onitytask-stage4b-adapters-2026-09-27/mono-smoke.json) |
| IL2CPP | `538dd737a4784d41af1140ed1587ea4b` | [31 passing cases](onitytask-stage4b-adapters-2026-09-27/il2cpp-smoke.json) |

Both are fresh non-development Release Players. IL2CPP uses Release compiler
configuration and Minimal stripping. Flow remains enabled, runner capacity is
128, and tracking/stack traces are disabled. Both report the internal execution
context path. No default performance setting changed.

- [Eight test runs, including the retained failed fixture run](onitytask-stage4b-adapters-2026-09-27/test-results-summary.json).
- [509 runtime/test hashes](onitytask-stage4b-adapters-2026-09-27/editor-source-hashes.json)
  and [552 complete build hashes](onitytask-stage4b-adapters-2026-09-27/source-hashes.json).
- [Build settings, environments, log hashes and initial fixture snapshots](onitytask-stage4b-adapters-2026-09-27/provenance.json).

All 552 hashes still matched both checkouts after execution. Project settings,
Editor settings and build settings returned to their saved hashes; the temporary
benchmark scene was removed. New cases cover real compiler async iterators,
reusable ValueTask versions, status-before-consumption, cancellation reentrancy,
held registration, cleanup precedence, both Update session transitions, and
internal versus ordinary BCL caller context behavior.

## Limits

- Enumerator, owned cancellation, observer and pending bridge state may allocate.
  No allocation quantity, zero-allocation or comparative speed claim is made.
- A BCL move that ignores cancellation can keep disposal pending. A malformed
  source that rejects registration still owns its unconsumed ValueTask; there
  is no polling, re-registration or fabricated completion to force shutdown.
- EveryUpdate has no idle subscription or buffered ticks. Active moves require
  main-thread Play/player acceptance; worker disposal still waits for actual
  Unity wait observation during cleanup.
- No commit, push or release was performed for this stage.
