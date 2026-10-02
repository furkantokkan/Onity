# PAR-C3: OnityAsyncReactiveProperty (note)

Branch `parity/async-reactive-property` (Codex worktree), base dc50c97. The Codex work was uncommitted; it was landed on
`perf/surpass-unitask` through the salvage branch `parity/salvage-triggers-arp-linq`: files copied byte-for-byte, then
finished there (re-entrancy rule, lazy publication state, a faulting-source test, this note). See "Salvage changes".

## Changed files (all new)

- `Packages/com.onity.framework/Runtime/Unity/Scripts/Async/OnityAsyncReactiveProperty.cs` (+ `.meta`)
- `Packages/com.onity.framework/Tests/EditMode/Scripts/OnityAsyncReactivePropertyEditModeTests.cs` (+ `.meta`)
- `docs/Plan/notes/par-c3-async-reactive-property.md` (this note)

No existing file was edited.

## Salvage changes (on top of the Codex copy)

- Re-entrant set now throws. Setting `Value` while the same property is still publishing (from a `WaitAsync`
  continuation, an enumerator continuation or a synchronous callback that the publication resumed) throws
  `InvalidOperationException`, as UniTask does ("Can not trigger itself in iterating"). The check runs first, under the
  core gate, before the value is stored: a rejected set leaves `Value` and the running publication unchanged, so no
  subscriber can observe 2 after 1 and a waiter never completes with a stale value. The flag is cleared in a `finally`.
  `OnityAsyncReactiveCore<T>.Publish` takes the value field by `ref` and writes it inside that check; the read-only pump
  uses the same call. Unlike UniTask, the rejected value is not stored first (UniTask stores, then throws).
- Lazy publication state. The `Value` setter and `Dispose()` use the core only when it already exists; with no
  subscriber the setter just stores the value and creates nothing. `OnityAsyncReactiveCore<T>` is created by the first
  `WaitAsync`, `GetAsyncEnumerator` or `WithoutCurrent` call.
- Read-only property and re-entry: the pump publishes through the same call, so a source that yields an item from inside
  a continuation of the same property faults the pump (logged, last value kept), exactly like UniTask. Documented in the
  class remarks.
- Docs: `Value` remarks state the equality-semantics difference from `ReactiveProperty<T>` (Onity.Reactive skips equal
  values; this type never does) and the re-entry rule.
- Tests: the old test that asserted the permissive nested publish was replaced by three re-entrancy tests (synchronous
  callback with ordering for a later waiter and an enumerator, awaited waiter continuation, awaited enumerator
  continuation); added `SetValue_WithNothingObserving_CreatesNoPublicationState` and
  `ReadOnly_FaultingSource_LogsFault_EndsPump_KeepsLastValue_AndLeavesSubscribersPending`. The fixture has 32 tests
  (Codex: 28).

## API added (namespace `Onity.Unity.Async`, UniTask 2.5.11 `AsyncReactiveProperty.cs` parity)

- `IOnityReadOnlyAsyncReactiveProperty<T> : IOnityAsyncEnumerable<T>`: `Value`, `WithoutCurrent()`, `WaitAsync(ct)`.
- `IOnityAsyncReactiveProperty<T>`: adds `new T Value { get; set; }`.
- `OnityAsyncReactiveProperty<T>` (sealed, `[Serializable]`, `[SerializeField] m_latestValue`): ctor(T), `Value`,
  `WithoutCurrent()`, `GetAsyncEnumerator(ct)`, `WaitAsync(ct)`, `Dispose()`, implicit `operator T`, `ToString()`.
- `OnityReadOnlyAsyncReactiveProperty<T>` (sealed): ctors `(T initial, source, ct)` and `(source, ct)`, `Value`,
  `WithoutCurrent()`, `GetAsyncEnumerator`, `WaitAsync`, `Dispose()`, implicit `operator T`, `ToString()`.
- `OnityAsyncReactivePropertyExtensions.ToReadOnlyAsyncReactiveProperty<T>(this IOnityAsyncEnumerable<T>, ct)` and
  `(..., T initialValue, ct)`. (UniTask names the holder class `StateExtensions`.)
- Internal: `OnityAsyncReactiveCore<T>` (shared engine), `IOnityAsyncReactiveValueReader<T>`.

## UniTask behavior mirrored

- No equality skip: every `Value` set publishes, equal or not (UniTask has no equality check).
- A publication completes all pending waiters once and wakes enumerators that await a move; a value published while an
  enumerator has no pending move is dropped (UniTask resets its source on the next move).
- Enumerator first move yields the current value (read at the first move); `WithoutCurrent()` skips it.
- `Dispose()` completes registered enumerators (pending move false) and cancels pending waiters with
  `CancellationToken.None`. The property stays usable afterwards (UniTask does not mark it disposed).
- Read-only: pump started in the constructor; OCE ends it silently, other faults are logged; `Dispose()` disposes the pump.

## Deliberate differences

- Enumerator contract is Onity's: `Current` throws `InvalidOperationException` without a valid item; a second
  outstanding move throws; disposing an enumerator ends a pending move with `false`; after dispose or property
  completion further moves return `false`; a canceled enumeration token cancels the pending move and later moves.
- A rejected re-entrant set does not store its value (UniTask stores the value and then throws), so the value always
  matches what the running publication delivers.
- Thread affinity: documented as Unity main thread; the handler list is synchronized so token cancellation from any
  thread is safe, continuations run inline on the publishing/canceling thread.

## Allocation notes

- `WaitAsync` uses a pooled waiter (`OnityRunnerPool`, cap 256, generation-claimed) plus the pooled
  `OnityAutoResetTaskCompletionSource<T>`; a cancelable token adds only the registration. Publish snapshots through
  `ArrayPool`. Enumerator moves use the pooled bool source; first move and terminal moves are inline results.
  Creating an enumerator allocates (as in UniTask). No zero-allocation claim made (no Release Player measurement).
- Tracker integration (`TaskTracker.TrackActiveTask` in UniTask) is deferred to PAR-B8.

## BindTo (out of scope for PAR-C3; moves to the UI event packets)

`BindTo` is not part of this packet. The roadmap C3 row lists it ("UI Toolkit; uGUI under A4b"), but UniTask `BindTo`
exists only for uGUI `Text`, `Selectable` (bool interactable), `TMP_Text`, and a generic
`BindTo<TSource,TObject>(source, monoBehaviour, Action)` (UnityBindingExtensions.cs, TextMeshProAsyncExtensions.cs);
there is no UI Toolkit equivalent in the pinned UniTask. It moves to the UI packets: PAR-A4a (UI Toolkit, beyond
UniTask) and PAR-A4b (uGUI/TMP parity, including the `AsyncReactiveProperty<T>.BindTo(Text|TMP_Text)` overloads that
take the concrete property type). Drop `BindTo` from the roadmap C3 row when those packets are specified.

## Verification

Codex worktree `C:/Users/e-fur/.codex/worktrees/onity-async-reactive-property/Onity` at base dc50c97, original fixture
(28 tests, before the salvage edits):

- Roslyn: `unity_compile_check.py --project <wt> --files <runtime + test> --dependents` -> COMPILE_OK (Onity.Unity,
  Onity.Editor, Onity.Tests.EditMode, Onity.Tests.PlayMode).
- EditMode (Release): `Unity.exe -batchmode -nographics -runTests -projectPath <wt> -testPlatform EditMode
  -testFilter "Onity.Tests.EditMode.OnityAsyncReactivePropertyEditModeTests" -releaseCodeOptimization` -> exit 0,
  28/28 passed (first run 26/28: two tests wrongly expected the exact `OperationCanceledException` type where Onity
  yields `TaskCanceledException`; fixed to `Assert.Catch`). Artifacts (gitignored, in that worktree):
  `Logs/PAR-C3/editmode-results.xml`, `Logs/PAR-C3/editmode.log`.
- Covered there: all-waiters-once, no late waiter, equal-value publish, pre-cancel, cancel one waiter, cancel after
  value, cross-thread cancel, 1000 pooled cycles, enumeration current-then-next, `WithoutCurrent`, dropped value without
  pending move, multiple enumerators, Current/second-move errors, token cancel (pre and pending), property/enumerator
  dispose and idempotence, `ToString`, interface views, read-only from `OnityChannel` source (initial/no initial,
  cancel, dispose, null source).

Salvage branch `parity/salvage-triggers-arp-linq` (integration base 3a9022c):

- Roslyn on the branch with `--dependents` (all engine-free assemblies, Onity.Unity, Onity.Editor and both test
  assemblies): COMPILE_OK.
- The edited fixture (32 tests) has NOT been run: the 28-test result above belongs to the Codex copy and does not cover
  the re-entrancy, lazy-state and faulting-source tests or the changed `Publish`/`Value`/`Dispose` code. Run
  `Onity.Tests.EditMode.OnityAsyncReactivePropertyEditModeTests` once in the final test stage.

## Lines for the integrator

- CHANGELOG: "Added `OnityAsyncReactiveProperty<T>`, `OnityReadOnlyAsyncReactiveProperty<T>`, their interfaces and
  `ToReadOnlyAsyncReactiveProperty` (UniTask `AsyncReactiveProperty` parity). Setting the value from a continuation of
  its own publication throws `InvalidOperationException`; like UniTask it never skips equal values, unlike
  `ReactiveProperty<T>`."
- Parity matrix: `AsyncReactiveProperty`, `ReadOnlyAsyncReactiveProperty`, `ToReadOnlyAsyncReactiveProperty` ->
  implemented (`OnityAsyncReactivePropertyEditModeTests`); `BindTo` rows -> PAR-A4a (UI Toolkit) and PAR-A4b (uGUI/TMP).

## Decisions closed

- Nested publish: throw `InvalidOperationException` (UniTask parity), rejected before the value is stored.
- Extension holder name stays `OnityAsyncReactivePropertyExtensions` (not UniTask's `StateExtensions`).

## Known limits and optional follow-ups

- `lock (this)` is used on the nested `Enumerator` (and on the internal `Waiter`). The enumerator instance is handed to
  callers, so a private gate object would be safer against foreign locking; not changed here.
- `Value` sets from several threads are not supported (an overlapping set throws); only token cancellation is
  thread-safe. The read-only pump publishes wherever its source delivers.
- `OnityReadOnlyAsyncReactiveProperty<T>` depends on the performance-lane internals `OnityRunnerPool<T>` and
  `IOnityPooledRunner<T>` (stable on the integration base); owners of the pool packets should know a parity type uses
  them.
- Any `OperationCanceledException` thrown by the source, not only the constructor token's, ends the pump silently.
- Optional: a Unity serialization round-trip test of `m_latestValue`.
