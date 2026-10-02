using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    public sealed class OnityAsyncEnumerableLinqMaterializeEditModeTests
    {
        private static LinqProbe<int> Items(params int[] items) => LinqTestSupport.Items(items);
        private static T Read<T>(OnityTask<T> task) => LinqTestSupport.Read(task);
        private static void Read(OnityTask task) => LinqTestSupport.Read(task);
        private static Exception Catch(Action action) => LinqTestSupport.Catch(action);
        private static OnityTask<T> Result<T>(T value) => OnityTask<T>.FromResult(value);

        // ---- admission -------------------------------------------------------------------

        [Test]
        public void Arguments_ValidateSynchronously()
        {
            IOnityAsyncEnumerable<int> missing = null;
            var source = Items(1, 2);
            Assert.Throws<ArgumentNullException>(() => missing.ToListAsync());
            Assert.Throws<ArgumentNullException>(() => missing.ToHashSetAsync());
            Assert.Throws<ArgumentNullException>(() => missing.ToHashSetAsync(EqualityComparer<int>.Default));
            Assert.Throws<ArgumentNullException>(() => missing.ToDictionaryAsync(value => value));
            Assert.Throws<ArgumentNullException>(() => source.ToDictionaryAsync((Func<int, int>)null));
            Assert.Throws<ArgumentNullException>(() => source.ToDictionaryAsync(value => value, (Func<int, int>)null));
            Assert.Throws<ArgumentNullException>(() => source.ToDictionaryAwaitAsync((Func<int, OnityTask<int>>)null));
            Assert.Throws<ArgumentNullException>(() => source.ToDictionaryAwaitAsync(
                value => Result(value), (Func<int, OnityTask<int>>)null));
            Assert.Throws<ArgumentNullException>(() => source.ToDictionaryAwaitWithCancellationAsync(
                (Func<int, CancellationToken, OnityTask<int>>)null));
            Assert.Throws<ArgumentNullException>(() => missing.ToLookupAsync(value => value));
            Assert.Throws<ArgumentNullException>(() => source.ToLookupAsync((Func<int, int>)null));
            Assert.Throws<ArgumentNullException>(() => source.ToLookupAsync(value => value, (Func<int, int>)null));
            Assert.Throws<ArgumentNullException>(() => source.ToLookupAwaitAsync((Func<int, OnityTask<int>>)null));
            Assert.Throws<ArgumentNullException>(() => source.ToLookupAwaitWithCancellationAsync(
                (value, token) => Result(value), (Func<int, CancellationToken, OnityTask<int>>)null));
            Assert.Throws<ArgumentNullException>(() => missing.ForEachAsync(value => { }));
            Assert.Throws<ArgumentNullException>(() => source.ForEachAsync((Action<int>)null));
            Assert.Throws<ArgumentNullException>(() => source.ForEachAsync((Action<int, int>)null));
            Assert.Throws<ArgumentNullException>(() => source.ForEachAsync((Func<int, OnityTask>)null));
            Assert.Throws<ArgumentNullException>(() => missing.ForEachAwaitAsync(value => OnityTask.Completed));
            Assert.Throws<ArgumentNullException>(() => source.ForEachAwaitAsync((Func<int, OnityTask>)null));
            Assert.Throws<ArgumentNullException>(() => source.ForEachAwaitAsync((Func<int, int, OnityTask>)null));
            Assert.Throws<ArgumentNullException>(() => source.ForEachAwaitWithCancellationAsync(
                (Func<int, CancellationToken, OnityTask>)null));
            Assert.Throws<ArgumentNullException>(() => source.ForEachAwaitWithCancellationAsync(
                (Func<int, int, CancellationToken, OnityTask>)null));
            Assert.That(source.Acquires, Is.Zero);
        }

        // ---- ToList / ToHashSet ----------------------------------------------------------

        [Test]
        public void ToList_CollectsInOrder_AndEmptyIsEmpty()
        {
            var source = Items(3, 1, 2);
            Assert.That(Read(source.ToListAsync()), Is.EqualTo(new List<int> { 3, 1, 2 }));
            Assert.That(Read(Items().ToListAsync()), Is.Empty);
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void ToHashSet_DeduplicatesWithDefaultOrSuppliedComparer()
        {
            var source = Items(1, 2, 1, 3, 2);
            Assert.That(Read(source.ToHashSetAsync()), Is.EquivalentTo(new[] { 1, 2, 3 }));
            var strings = new LinqProbe<string>("a", "A", "b");
            Assert.That(Read(strings.ToHashSetAsync()).Count, Is.EqualTo(3));
            Assert.That(Read(strings.ToHashSetAsync(StringComparer.OrdinalIgnoreCase)).Count, Is.EqualTo(2));
            Assert.That(Read(strings.ToHashSetAsync((IEqualityComparer<string>)null)).Count, Is.EqualTo(3));
            Assert.That(Read(Items().ToHashSetAsync()), Is.Empty);
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        // ---- ToDictionary ----------------------------------------------------------------

        [Test]
        public void ToDictionary_AllShapes()
        {
            var source = Items(1, 2, 3);
            var items = new Dictionary<int, int> { { 10, 1 }, { 20, 2 }, { 30, 3 } };
            var elements = new Dictionary<int, string> { { 10, "1" }, { 20, "2" }, { 30, "3" } };
            var comparer = EqualityComparer<int>.Default;
            Assert.That(Read(source.ToDictionaryAsync(value => value * 10)), Is.EqualTo(items));
            Assert.That(Read(source.ToDictionaryAsync(value => value * 10, comparer)), Is.EqualTo(items));
            Assert.That(Read(source.ToDictionaryAsync(value => value * 10, value => value.ToString())), Is.EqualTo(elements));
            Assert.That(Read(source.ToDictionaryAsync(value => value * 10, value => value.ToString(), comparer)),
                Is.EqualTo(elements));
            Assert.That(Read(source.ToDictionaryAwaitAsync(value => Result(value * 10))), Is.EqualTo(items));
            Assert.That(Read(source.ToDictionaryAwaitAsync(value => Result(value * 10), comparer)), Is.EqualTo(items));
            Assert.That(Read(source.ToDictionaryAwaitAsync(value => Result(value * 10), value => Result(value.ToString()))),
                Is.EqualTo(elements));
            Assert.That(Read(source.ToDictionaryAwaitAsync(value => Result(value * 10),
                value => Result(value.ToString()), comparer)), Is.EqualTo(elements));
            Assert.That(Read(source.ToDictionaryAwaitWithCancellationAsync((value, token) => Result(value * 10))),
                Is.EqualTo(items));
            Assert.That(Read(source.ToDictionaryAwaitWithCancellationAsync(
                (value, token) => Result(value * 10), comparer)), Is.EqualTo(items));
            Assert.That(Read(source.ToDictionaryAwaitWithCancellationAsync(
                (value, token) => Result(value * 10), (value, token) => Result(value.ToString()))), Is.EqualTo(elements));
            Assert.That(Read(source.ToDictionaryAwaitWithCancellationAsync((value, token) => Result(value * 10),
                (value, token) => Result(value.ToString()), comparer)), Is.EqualTo(elements));
            Assert.That(Read(Items().ToDictionaryAsync(value => value)), Is.Empty);
            Assert.That(source.Disposals, Is.EqualTo(source.Acquires));
        }

        [Test]
        public void ToDictionary_UsesTheComparer_AndFaultsOnDuplicateOrNullKeys()
        {
            var strings = new LinqProbe<string>("a", "B");
            var dictionary = Read(strings.ToDictionaryAsync(value => value, StringComparer.OrdinalIgnoreCase));
            Assert.That(dictionary["A"], Is.EqualTo("a"));
            Assert.That(dictionary["b"], Is.EqualTo("B"));

            var duplicate = Items(1, 2, 1).ToDictionaryAsync(value => value);
            Assert.That(duplicate.IsFaulted, Is.True);
            Assert.That(Catch(() => Read(duplicate)), Is.InstanceOf<ArgumentException>());
            var duplicateCase = new LinqProbe<string>("a", "A").ToDictionaryAsync(
                value => value, StringComparer.OrdinalIgnoreCase);
            Assert.That(Catch(() => Read(duplicateCase)), Is.InstanceOf<ArgumentException>());
            var nullKey = new LinqProbe<string>("a", null).ToDictionaryAsync(value => value);
            Assert.That(Catch(() => Read(nullKey)), Is.InstanceOf<ArgumentNullException>());
        }

        [Test]
        public void ToDictionary_EvaluatesKeyBeforeElementPerItem_AndPassesTheToken()
        {
            var order = new List<string>();
            using (var cts = new CancellationTokenSource())
            {
                CancellationToken seen = default;
                var dictionary = Read(Items(1, 2).ToDictionaryAwaitWithCancellationAsync(
                    (value, token) =>
                    {
                        order.Add("k" + value);
                        seen = token;
                        return Result(value);
                    },
                    (value, token) =>
                    {
                        order.Add("e" + value);
                        return Result(value * 2);
                    }, cts.Token));
                Assert.That(order, Is.EqualTo(new[] { "k1", "e1", "k2", "e2" }));
                Assert.That(seen, Is.EqualTo(cts.Token));
                Assert.That(dictionary[2], Is.EqualTo(4));
            }
        }

        // ---- ToLookup --------------------------------------------------------------------

        [Test]
        public void ToLookup_GroupsInFirstSeenOrder_WithAllShapes()
        {
            var source = Items(1, 2, 3, 4, 5);
            var comparer = EqualityComparer<int>.Default;
            var plain = Read(source.ToLookupAsync(value => value % 2));
            AssertItemLookup(plain);
            AssertItemLookup(Read(source.ToLookupAsync(value => value % 2, comparer)));
            AssertItemLookup(Read(source.ToLookupAwaitAsync(value => Result(value % 2))));
            AssertItemLookup(Read(source.ToLookupAwaitAsync(value => Result(value % 2), comparer)));
            AssertItemLookup(Read(source.ToLookupAwaitWithCancellationAsync((value, token) => Result(value % 2))));
            AssertItemLookup(Read(source.ToLookupAwaitWithCancellationAsync(
                (value, token) => Result(value % 2), comparer)));
            AssertElementLookup(Read(source.ToLookupAsync(value => value % 2, value => value * 10)));
            AssertElementLookup(Read(source.ToLookupAsync(value => value % 2, value => value * 10, comparer)));
            AssertElementLookup(Read(source.ToLookupAwaitAsync(value => Result(value % 2), value => Result(value * 10))));
            AssertElementLookup(Read(source.ToLookupAwaitAsync(
                value => Result(value % 2), value => Result(value * 10), comparer)));
            AssertElementLookup(Read(source.ToLookupAwaitWithCancellationAsync(
                (value, token) => Result(value % 2), (value, token) => Result(value * 10))));
            AssertElementLookup(Read(source.ToLookupAwaitWithCancellationAsync(
                (value, token) => Result(value % 2), (value, token) => Result(value * 10), comparer)));
            var keys = new List<int>();
            foreach (var group in plain)
            {
                keys.Add(group.Key);
            }
            Assert.That(keys, Is.EqualTo(new[] { 1, 0 }));
            Assert.That(Read(Items().ToLookupAsync(value => value)).Count, Is.Zero);
            Assert.That(source.Disposals, Is.EqualTo(source.Acquires));
        }

        private static void AssertItemLookup(IOnityLookup<int, int> lookup)
        {
            Assert.That(lookup.Count, Is.EqualTo(2));
            Assert.That(lookup[1], Is.EqualTo(new[] { 1, 3, 5 }));
            Assert.That(lookup[0], Is.EqualTo(new[] { 2, 4 }));
            Assert.That(lookup[7], Is.Empty);
            Assert.That(lookup.Contains(1), Is.True);
            Assert.That(lookup.Contains(7), Is.False);
        }

        private static void AssertElementLookup(IOnityLookup<int, int> lookup)
        {
            Assert.That(lookup.Count, Is.EqualTo(2));
            Assert.That(lookup[1], Is.EqualTo(new[] { 10, 30, 50 }));
            Assert.That(lookup[0], Is.EqualTo(new[] { 20, 40 }));
        }

        [Test]
        public void ToLookup_GroupingsExposeTheirKeyAndElementsInOrder_AndAbsentKeysAreEmpty()
        {
            IOnityLookup<int, int> lookup = Read(Items(1, 2, 3, 4, 5).ToLookupAsync(value => value % 2));

            var rendered = new List<string>();
            foreach (IOnityGrouping<int, int> group in lookup)
            {
                rendered.Add(group.Key + ":" + string.Join(",", group));
            }

            Assert.That(rendered, Is.EqualTo(new[] { "1:1,3,5", "0:2,4" }));
            Assert.That(lookup.Count, Is.EqualTo(2));
            Assert.That(lookup[9], Is.Empty);
            Assert.That(lookup.Contains(9), Is.False);
        }

        [Test]
        public void ToLookup_SupportsNullKeysAndKeyComparers()
        {
            var strings = new LinqProbe<string>("a", null, "A", null);
            var lookup = Read(strings.ToLookupAsync(value => value, StringComparer.OrdinalIgnoreCase));
            Assert.That(lookup.Count, Is.EqualTo(2));
            Assert.That(lookup["a"], Is.EqualTo(new[] { "a", "A" }));
            Assert.That(lookup[null], Is.EqualTo(new string[] { null, null }));
            Assert.That(lookup.Contains(null), Is.True);
            var keys = new List<string>();
            foreach (var group in lookup)
            {
                keys.Add(group.Key);
            }
            Assert.That(keys, Is.EqualTo(new string[] { "a", null }));
            var noNull = Read(new LinqProbe<string>("x").ToLookupAsync(value => value));
            Assert.That(noNull.Contains(null), Is.False);
            Assert.That(noNull[null], Is.Empty);
        }

        // ---- ForEach ---------------------------------------------------------------------

        [Test]
        public void ForEach_SynchronousShapes_VisitInOrderWithIndexes()
        {
            var seen = new List<int>();
            var source = Items(5, 6, 7);
            Read(source.ForEachAsync(value => seen.Add(value)));
            Assert.That(seen, Is.EqualTo(new[] { 5, 6, 7 }));
            seen.Clear();
            Read(source.ForEachAsync((value, index) => seen.Add(value * 10 + index)));
            Assert.That(seen, Is.EqualTo(new[] { 50, 61, 72 }));
            Read(Items().ForEachAsync(value => seen.Add(-1)));
            Assert.That(seen.Count, Is.EqualTo(3));
            Assert.That(source.Disposals, Is.EqualTo(2));
        }

        [Test]
        public void ForEach_AwaitShapes_VisitInOrderWithIndexesAndTokens()
        {
            var seen = new List<int>();
            var tokens = new List<bool>();
            var source = Items(5, 6, 7);
            Read(source.ForEachAsync(value =>
            {
                seen.Add(value);
                return OnityTask.Completed;
            }));
            Read(source.ForEachAwaitAsync(value =>
            {
                seen.Add(value + 100);
                return OnityTask.Completed;
            }));
            Read(source.ForEachAwaitAsync((value, index) =>
            {
                seen.Add(value * 10 + index);
                return OnityTask.Completed;
            }));
            Read(source.ForEachAwaitWithCancellationAsync((value, token) =>
            {
                seen.Add(value + 200);
                tokens.Add(token.CanBeCanceled);
                return OnityTask.Completed;
            }));
            Read(source.ForEachAwaitWithCancellationAsync((value, index, token) =>
            {
                seen.Add(value * 10 + index + 1000);
                tokens.Add(token.CanBeCanceled);
                return OnityTask.Completed;
            }));
            Assert.That(seen, Is.EqualTo(new[]
            {
                5, 6, 7, 105, 106, 107, 50, 61, 72, 205, 206, 207, 1050, 1061, 1072
            }));
            Assert.That(tokens, Is.EqualTo(new[] { true, true, true, true, true, true }));
            Assert.That(source.Disposals, Is.EqualTo(5));
            Read(Items().ForEachAwaitAsync(value => OnityTask.Completed));
        }

        [Test]
        public void ForEach_PendingActionKeepsMovesSequential()
        {
            var source = Items(1, 2, 3);
            var gate = new OnityTaskCompletionSource();
            int calls = 0;
            var task = source.ForEachAwaitAsync(value =>
            {
                calls++;
                return calls == 1 ? gate.Task : OnityTask.Completed;
            });
            Assert.That(task.IsCompleted, Is.False);
            Assert.That(source.Moves, Is.EqualTo(1));
            Assert.That(calls, Is.EqualTo(1));
            gate.TrySetResult();
            LinqTestSupport.Wait(() => task.IsCompleted);
            Read(task);
            Assert.That(calls, Is.EqualTo(3));
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void ForEach_ActionException_FaultsAndUpstreamIsDisposedOnce(int shape)
        {
            var fault = new InvalidOperationException("action");
            var source = Items(1, 2, 3);
            OnityTask task;
            switch (shape)
            {
                case 0: task = source.ForEachAsync((Action<int>)(value => throw fault)); break;
                case 1: task = source.ForEachAsync((Action<int, int>)((value, index) => throw fault)); break;
                case 2: task = source.ForEachAwaitAsync((Func<int, OnityTask>)(value => throw fault)); break;
                default: task = source.ForEachAwaitWithCancellationAsync(
                    (Func<int, CancellationToken, OnityTask>)((value, token) => throw fault)); break;
            }
            Assert.That(task.IsFaulted, Is.True);
            Assert.That(Catch(() => Read(task)), Is.SameAs(fault));
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        // ---- cancellation, faults, cleanup -----------------------------------------------

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void CancellationMidEnumeration_IsCanceled_AndUpstreamIsDisposedOnce(int shape)
        {
            using (var cts = new CancellationTokenSource())
            {
                var source = Items(1, 2, 3, 4, 5);
                source.OnMove = move =>
                {
                    if (move == 2)
                    {
                        cts.Cancel();
                    }
                };
                bool canceled;
                switch (shape)
                {
                    case 0: canceled = source.ToListAsync(cts.Token).IsCanceled; break;
                    case 1: canceled = source.ToHashSetAsync(cts.Token).IsCanceled; break;
                    case 2: canceled = source.ToDictionaryAsync(value => value, cts.Token).IsCanceled; break;
                    case 3: canceled = source.ToLookupAwaitAsync(value => Result(value), cts.Token).IsCanceled; break;
                    case 4: canceled = source.ForEachAsync(value => { }, cts.Token).IsCanceled; break;
                    case 5: canceled = source.ForEachAwaitAsync(value => OnityTask.Completed, cts.Token).IsCanceled; break;
                    default: canceled = source.ToDictionaryAwaitWithCancellationAsync(
                        (value, token) => Result(value), cts.Token).IsCanceled; break;
                }
                Assert.That(canceled, Is.True);
                Assert.That(source.Disposals, Is.EqualTo(1));
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void UpstreamFault_Propagates_AndUpstreamIsDisposedOnce(int shape)
        {
            var fault = new InvalidOperationException("source");
            var source = Items(1, 2, 3, 4);
            source.MoveFailureAt = 2;
            source.MoveFailure = fault;
            Exception error;
            switch (shape)
            {
                case 0: error = Catch(() => Read(source.ToListAsync())); break;
                case 1: error = Catch(() => Read(source.ToHashSetAsync())); break;
                case 2: error = Catch(() => Read(source.ToDictionaryAsync(value => value))); break;
                case 3: error = Catch(() => Read(source.ToLookupAsync(value => value))); break;
                default: error = Catch(() => Read(source.ForEachAsync(value => { }))); break;
            }
            Assert.That(error, Is.SameAs(fault));
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void SelectorException_Faults_AndCleanupFailureTakesPrecedence()
        {
            var fault = new InvalidOperationException("selector");
            var cleanup = new InvalidOperationException("cleanup");
            var source = Items(1, 2);
            var faulted = source.ToLookupAsync((Func<int, int>)(value => throw fault));
            Assert.That(Catch(() => Read(faulted)), Is.SameAs(fault));
            Assert.That(source.Disposals, Is.EqualTo(1));

            var failing = Items(1, 2);
            failing.DisposeFailure = cleanup;
            Assert.That(Catch(() => Read(failing.ToListAsync())), Is.SameAs(cleanup));
            var failingSelector = failing.ToDictionaryAsync((Func<int, int>)(value => throw fault));
            Assert.That(Catch(() => Read(failingSelector)), Is.SameAs(cleanup));
            Assert.That(failing.Disposals, Is.EqualTo(2));
        }

        [Test]
        public void PendingUpstreamMove_ResumesMaterialization()
        {
            var source = Items(1, 2, 3);
            source.PendingAt = 1;
            source.Pending = new OnityTaskCompletionSource<bool>();
            var task = source.ToDictionaryAsync(value => value);
            Assert.That(task.IsCompleted, Is.False);
            source.Pending.TrySetResult(true);
            LinqTestSupport.Wait(() => task.IsCompleted);
            Assert.That(Read(task).Count, Is.EqualTo(3));
            Assert.That(source.Disposals, Is.EqualTo(1));
        }
    }
}
