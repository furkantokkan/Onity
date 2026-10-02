# PAR-C2a: async LINQ, filtering, paging, aggregates, materialization (note)

Branch `parity/async-linq-filtering` (Codex worktree), base dc50c97. The Codex work was uncommitted and had no note; it was
landed on `perf/surpass-unitask` through the salvage branch `parity/salvage-triggers-arp-linq`: files copied byte-for-byte,
then finished there (see "Salvage changes"). Pinned reference: UniTask 2.5.11 `Runtime/Linq/*`.

## Changed files

New, `Packages/com.onity.framework/Runtime/Unity/Scripts/Async/` (namespace `Onity.Unity.Async`, each with `.meta`):
- `OnityAsyncEnumerableOperators.Filtering.cs`: lazy stream operators (public partial static class `OnityAsyncEnumerableLinq`).
- `OnityAsyncEnumerableOperators.Aggregates.cs`: terminal counting, testing, element, aggregate and generic Min/Max operators.
- `OnityAsyncEnumerableOperators.Aggregates.Generated.cs`: numeric Sum/Average/Min/Max, 160 overloads. Generated, do not edit.
- `OnityAsyncEnumerableOperators.Materialize.cs`: ToList, ToHashSet, ToDictionary, ToLookup, ForEach, plus the internal lookup.
- `IOnityLookup.cs`, `IOnityGrouping.cs`: the Onity-owned lookup contract (added during the salvage, see below).

New tests, `Tests/EditMode/Scripts/` (each with `.meta`):
- `OnityAsyncEnumerableLinqFilteringEditModeTests.cs` (56 cases; also defines the `LinqProbe<T>` and `LinqTestSupport` helpers),
  `OnityAsyncEnumerableLinqAggregatesEditModeTests.cs` (49), `OnityAsyncEnumerableLinqMaterializeEditModeTests.cs` (30).

New tool: `tools/async-linq-codegen/generate_numeric_aggregates.py`.

Edited existing files, test-only (5 call sites, see "null literal"): `OnityAsyncEnumerableAwaitEditModeTests.cs` (3 casts) and
`OnityAsyncEnumerableEditModeTests.cs` (2 casts). Packet-rule exception: the rule allows one existing file per packet; two
existing test files had to change because the new overloads make a `null` literal argument ambiguous. No performance-lane
file is touched.

## API (extension methods of `OnityAsyncEnumerableLinq` on `IOnityAsyncEnumerable<T>`, 294 public overloads)

- Lazy operators: `Skip`, `SkipLast`, `TakeLast`; `SkipWhile` and `TakeWhile` each as plain, indexed, `Await`, indexed `Await`,
  `AwaitWithCancellation`, indexed `AwaitWithCancellation`; indexed `Select`; `SelectAwait` (no token) and
  `SelectAwaitWithCancellation` (token; index + token); indexed `Where`; `WhereAwait` (no token) and
  `WhereAwaitWithCancellation` (token; index + token); `Distinct` (plain, comparer, key selector, key selector + comparer),
  `DistinctAwait` (2), `DistinctAwaitWithCancellation` (2); `DistinctUntilChanged` (the same 4 shapes), `...Await` (2),
  `...AwaitWithCancellation` (2).
- Terminal operators: `CountAsync`, `LongCountAsync`, `AnyAsync`, `AllAsync` (predicate forms with `Await` and
  `AwaitWithCancellation`), `ContainsAsync` and `SequenceEqualAsync` (each with a comparer variant), `AggregateAsync` (3 shapes,
  each sync / `Await` / `AwaitWithCancellation`), `FirstAsync`, `FirstOrDefaultAsync`, `LastAsync`, `LastOrDefaultAsync`,
  `SingleAsync`, `SingleOrDefaultAsync` (predicate, `Await`, `AwaitWithCancellation` forms), `ElementAtAsync`,
  `ElementAtOrDefaultAsync`; generic comparer-based `MinAsync` and `MaxAsync`; generated `SumAsync`, `AverageAsync`, `MinAsync`,
  `MaxAsync` for int, long, float, double, decimal and their nullable forms (plain, selector, `Await`, `AwaitWithCancellation`).
- Materialization: `ToListAsync`, `ToHashSetAsync` (+ comparer), `ToDictionaryAsync`, `ToLookupAsync` (each with `Await` and
  `AwaitWithCancellation` forms, comparer and element-selector variants), `ForEachAsync` (`Action<T>`, `Action<T,int>`,
  `Func<T,OnityTask>`), `ForEachAwaitAsync` (2), `ForEachAwaitWithCancellationAsync` (2).
- Public types: `IOnityLookup<TKey,TElement>`, `IOnityGrouping<TKey,TElement>`.
- Internal: `OnityLinqDescription` (lazy description, one outstanding move) and 15 enumerators built on the existing
  `OnitySourceAsyncEnumerator` / `OnityAwaitAsyncEnumerator` bases; `OnityLinqFunc`, `OnityLinqFunc2` (readonly delegate
  unions), `OnityLinqIdentity`, `OnityLinqCore`, `OnityLookup`, `OnityGrouping`.
- Terminal operators are `async OnityTask` methods that always await `DisposeAsync` in a `finally`; a delegate that throws
  `OperationCanceledException` is a fault; a cleanup failure takes precedence. No thread hop is added (class remarks).

## Deliberate deviations from UniTask 2.5.11

Numeric aggregates follow System.Linq where UniTask's template is accidentally wrong (also stated in the generator header):
- A nullable `Sum` starts at zero. UniTask starts at null, so its nullable `Sum` is always null.
- `Average` of an empty stream faults with `InvalidOperationException` for non-nullable types and yields null for nullable types
  (UniTask divides by zero: NaN or `DivideByZeroException`).
- `Average` accumulates int and long in long, and float in double (UniTask overflows at int range).

Kept as UniTask does it (and not as System.Linq does):
- The generic comparer-based `MinAsync` / `MaxAsync` (and their selector forms) return `default` for an empty stream. System.Linq
  throws for value types. The numeric overloads (int, long, float, double, decimal and nullable) throw `InvalidOperationException`
  on an empty stream, in UniTask and System.Linq alike. A selector that returns `int` binds to the numeric overload, not the
  generic one.
- Float and double `Min`/`Max` compare with plain `<` / `>` like the UniTask template, so a NaN in the stream is not propagated
  the way System.Linq propagates it.

Other:
- `ForEachAsync(Func<T,OnityTask>)` is an extra overload that UniTask does not have (see below).
- `ToLookupAsync` returns Onity's own `IOnityLookup<TKey,TElement>`, not `System.Linq.ILookup` (see below).

## Naming compromise: SelectAwait, WhereAwait, ForEachAsync

Onity's existing `OnityAsyncEnumerableExtensions.SelectAwait`, `WhereAwait` and `ForEachAsync` already take a token
(`Func<T, CancellationToken, OnityTask<...>>`). UniTask names those shapes `*AwaitWithCancellation` and gives `SelectAwait` and
`WhereAwait` no token. This packet adds the UniTask names (`SelectAwaitWithCancellation`, `WhereAwaitWithCancellation`, the
no-token `SelectAwait` and `WhereAwait`, `ForEachAwaitWithCancellationAsync`) and keeps the existing token-aware methods, so
existing callers compile and `SelectAwait` accepts both shapes.

## Parity gap: index-only SelectAwait / WhereAwait

UniTask has `SelectAwait(Func<T,int,UniTask<R>>)` and `WhereAwait(Func<T,int,UniTask<bool>>)`. They are not provided. An
overload with an `int` second parameter is ambiguous (CS0121) with the existing token-aware overload whenever the lambda ignores
its second parameter, which was confirmed with an in-memory C# 9 overload-resolution probe. The indexed async forms exist only with a
token: `SelectAwaitWithCancellation(Func<T,int,CancellationToken,OnityTask<R>>)` and the `Where` counterpart. Record this row as a
parity gap in the matrix.

## Extra overload: ForEachAsync(Func<T, OnityTask>)

The probe showed that an `async` lambda passed to `ForEachAsync` binds to the `Func` overloads and not to `Action<T>` or
`Action<T,int>`. Without a `Func<T,OnityTask>` overload an `async item => ...` lambda would silently become an `async void`
`Action<T>`, whose exceptions nobody can observe. The extra overload closes that hole; it goes beyond UniTask on purpose and
delegates to the token-aware `ForEachAsync`.

## Lookup type: IOnityLookup / IOnityGrouping

The first Codex copy returned `System.Linq.ILookup` and used `System.Linq.IGrouping` (16 references; no `using System.Linq`, no
LINQ operators). Onity runtime does not use System.Linq (repository rule), so the salvage replaced them with Onity-owned
interfaces. `IOnityLookup<TKey,TElement>` has `Count` (number of distinct keys), an indexer that returns `IEnumerable<TElement>`
(empty for an absent key), `Contains(TKey)`, and enumerates `IOnityGrouping<TKey,TElement>` in first-seen key order.
`IOnityGrouping<out TKey,out TElement>` is an `IEnumerable<TElement>` with `Key`. A null key is supported. The internal
`OnityLookup` / `OnityGrouping` implement them. Consequence: the result is not assignable to `System.Linq.ILookup` (UniTask
returns `ILookup`); callers that need BCL interop copy it themselves.

## Source break: null literal

Because `Select`, `Where`, `SelectAwait`, `WhereAwait` and `ForEachAsync` now have several delegate overloads, a `null` literal
argument no longer compiles (ambiguous). Cast it to the intended delegate type. The two existing test files were edited at exactly
these 5 call sites (`Select<int,int>`, `Where`, `SelectAwait<int,int>`, `WhereAwait`, `ForEachAsync`).

## Known limits (not changed here)

- `AggregateAsync` with a result selector runs the selector before the upstream is disposed.
- The `DistinctAwait` enumerator does not clear its seen set on cleanup (the sync `Distinct` does). Optional follow-up.
- Terminal operators rely on the upstream honoring the cancellation token.

## Generator

`python tools/async-linq-codegen/generate_numeric_aggregates.py` rewrites the generated file; `--check` exits 1 when it is stale
and prints `UP_TO_DATE` otherwise. Types: int, long, float, double, decimal and nullable forms; variants: plain, selector,
`Await`, `AwaitWithCancellation`.

## Salvage changes (on top of the Codex copy)

- `MinMax_Generic_AllShapes` expected 0 from `MaxAsync(value => value.Length)` on an empty string stream. That call binds to the
  numeric Int32 overload, which throws `InvalidOperationException` (System.Linq and UniTask too), so the runtime was right and
  the test was wrong. The test now asserts the exception, and adds empty-stream cases for the generic overloads with a
  `string`-returning selector (`MaxAsync`, `MinAwaitAsync`) that expect null.
- `System.Linq.ILookup` / `IGrouping` replaced by `IOnityLookup` / `IOnityGrouping` (new files with `.meta`); the two lookup test
  helpers take `IOnityLookup<int,int>`; new test `ToLookup_GroupingsExposeTheirKeyAndElementsInOrder_AndAbsentKeysAreEmpty`.
- This note.

## Verification

Codex worktree `C:/Users/e-fur/.codex/worktrees/onity-async-linq-filtering/Onity` at base dc50c97 (Unity 2022.3.62f2, EditMode,
`-releaseCodeOptimization`): `Logs/PAR-C2a/editmode-release.xml` and `editmode-release.log` (gitignored, in that worktree). 8
async-enumerable fixtures, 297/298 passed: Adapter 28, Await 34, Core (`OnityAsyncEnumerableEditModeTests`) 37, Reactive 31,
Channel 34, LinqFiltering 56, LinqMaterialize 29, LinqAggregates 48/49. The one failure is the wrong `MinMax_Generic_AllShapes`
expectation described above.

Salvage branch `parity/salvage-triggers-arp-linq` (integration base 3a9022c):
- Roslyn with `--dependents` (all engine-free assemblies, Onity.Unity, Onity.Editor, both test assemblies): COMPILE_OK.
- `generate_numeric_aggregates.py --check`: UP_TO_DATE.
- The corrected fixtures have NOT been run on this branch. Run the 8 fixtures above once in the final test stage; the Codex
  failure is fixed and one test was added (Materialize 30), so expect all of them to pass.

## Lines for the integrator

- CHANGELOG: "Added async LINQ operators on `IOnityAsyncEnumerable<T>` (class `OnityAsyncEnumerableLinq`, UniTask parity):
  `Skip`, `SkipLast`, `TakeLast`, `SkipWhile`, `TakeWhile`, indexed `Select` and `Where`, `Distinct`, `DistinctUntilChanged`,
  `CountAsync`, `LongCountAsync`, `AnyAsync`, `AllAsync`, `ContainsAsync`, `SequenceEqualAsync`, `AggregateAsync`, `First`, `Last`,
  `Single` (+ `OrDefault`), `ElementAt` (+ `OrDefault`), `Sum`, `Average`, `Min`, `Max` for int, long, float, double, decimal and
  their nullable forms, `ToListAsync`, `ToHashSetAsync`, `ToDictionaryAsync`, `ToLookupAsync`, `ForEachAsync` and `ForEachAwaitAsync`,
  each with `Await` and `AwaitWithCancellation` forms. `ToLookupAsync` returns Onity's `IOnityLookup` / `IOnityGrouping`, not
  `System.Linq.ILookup`."
- CHANGELOG (breaking, source): "A `null` literal passed to `Select`, `Where`, `SelectAwait`, `WhereAwait` or `ForEachAsync` is
  now ambiguous between the new delegate overloads; cast it to the delegate type."
- Parity matrix: every operator in the C2a row -> implemented (`OnityAsyncEnumerableLinq*EditModeTests`); index-only async
  `SelectAwait` / `WhereAwait` -> parity gap (CS0121 with the token-aware overload); `ForEachAsync(Func<T,OnityTask>)` -> beyond
  UniTask. Not covered by any C2 packet yet: `Cast`, `OfType`, `Do`, `Except`, `Intersect`, `Union`, `Join`, `GroupJoin`,
  `TakeUntil`, `TakeUntilCanceled`, `SkipUntil`, `SkipUntilCanceled` (assessment observation; check against the pinned UniTask
  Linq folder before declaring Area C complete).
