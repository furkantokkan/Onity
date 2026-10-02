using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    public sealed class OnityAsyncEnumerableGroupingEditModeTests
    {
        private static T[] Items<T>(IEnumerable<T> source)
        {
            var list = new List<T>();
            foreach (T item in source)
            {
                list.Add(item);
            }
            return list.ToArray();
        }

        private static int Sum(IEnumerable<int> source)
        {
            int sum = 0;
            foreach (int item in source)
            {
                sum += item;
            }
            return sum;
        }

        private static int Count<T>(IEnumerable<T> source) => Items(source).Length;

        private sealed class ReverseComparer : IComparer<int>
        {
            public int Compare(int x, int y) => y.CompareTo(x);
        }

        // ---- GroupBy ----------------------------------------------------------------------------

        [Test]
        public void GroupBy_YieldsGroupsInFirstSeenKeyOrder_AfterDisposingTheSource()
        {
            var log = new List<string>();
            var source = MultiTest.Ints("source", log, 1, 2, 3, 4, 5, 6);
            IOnityAsyncEnumerator<IOnityGrouping<int, int>> enumerator = source.GroupBy(x => x % 3).GetAsyncEnumerator();
            MultiTestResult<bool> first = MultiTest.Move(enumerator, log, "first");
            Assert.That(first.Value, Is.True);
            Assert.That(log, Is.EqualTo(new[] { "source.acquire", "source.dispose", "first" }));
            Assert.That(enumerator.Current.Key, Is.EqualTo(1));
            Assert.That(Items(enumerator.Current), Is.EqualTo(new[] { 1, 4 }));
            MultiTestResult<bool> second = MultiTest.Move(enumerator);
            Assert.That(enumerator.Current.Key, Is.EqualTo(2));
            Assert.That(Items(enumerator.Current), Is.EqualTo(new[] { 2, 5 }));
            MultiTest.Move(enumerator);
            Assert.That(enumerator.Current.Key, Is.EqualTo(0));
            Assert.That(Items(enumerator.Current), Is.EqualTo(new[] { 3, 6 }));
            MultiTest.ExpectEnd(enumerator);
            Assert.That(second.Value, Is.True);
            MultiTest.Dispose(enumerator);
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void GroupBy_ElementAndResultSelectors_AndComparer()
        {
            IOnityGrouping<int, int>[] groups = MultiTest.Drain(MultiTest.Ints("s", null, 1, 2, 3, 4)
                .GroupBy(x => x % 2, x => x * 10));
            Assert.That(groups.Length, Is.EqualTo(2));
            Assert.That(Items(groups[0]), Is.EqualTo(new[] { 10, 30 }));
            Assert.That(Items(groups[1]), Is.EqualTo(new[] { 20, 40 }));

            Assert.That(MultiTest.Drain(MultiTest.Ints("s", null, 1, 2, 3, 4)
                .GroupBy(x => x % 2, (key, items) => key * 100 + Sum(items))), Is.EqualTo(new[] { 104, 6 }));
            Assert.That(MultiTest.Drain(MultiTest.Ints("s", null, 1, 2, 3)
                .GroupBy(x => x % 2, x => x + 1, (key, items) => Sum(items))), Is.EqualTo(new[] { 6, 3 }));

            var words = new MultiTestStream<string>("w", null, "a", "A", "b");
            IOnityGrouping<string, string>[] folded = MultiTest.Drain(words.GroupBy(x => x, StringComparer.OrdinalIgnoreCase));
            Assert.That(folded.Length, Is.EqualTo(2));
            Assert.That(Items(folded[0]), Is.EqualTo(new[] { "a", "A" }));
        }

        [Test]
        public void GroupBy_SupportsNullKeys()
        {
            var words = new MultiTestStream<string>("w", null, null, "x", null);
            IOnityGrouping<string, string>[] groups = MultiTest.Drain(words.GroupBy(x => x));
            Assert.That(groups.Length, Is.EqualTo(2));
            Assert.That(groups[0].Key, Is.Null);
            Assert.That(Count(groups[0]), Is.EqualTo(2));
            Assert.That(groups[1].Key, Is.EqualTo("x"));
        }

        [Test]
        public void GroupByAwait_AwaitsKeysOneAtATime()
        {
            var keys = new Queue<OnityTaskCompletionSource<int>>();
            IOnityAsyncEnumerator<IOnityGrouping<int, int>> enumerator = MultiTest.Ints("s", null, 5, 6)
                .GroupByAwait(x =>
                {
                    var key = new OnityTaskCompletionSource<int>();
                    keys.Enqueue(key);
                    return key.Task;
                }).GetAsyncEnumerator();
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            Assert.That(keys.Count, Is.EqualTo(1));
            keys.Dequeue().TrySetResult(1);
            Assert.That(keys.Count, Is.EqualTo(1));
            keys.Dequeue().TrySetResult(1);
            Assert.That(move.Value, Is.True);
            Assert.That(Items(enumerator.Current), Is.EqualTo(new[] { 5, 6 }));
            MultiTest.ExpectEnd(enumerator);
            MultiTest.Dispose(enumerator);

            Assert.That(MultiTest.Drain(MultiTest.Ints("s", null, 1, 2, 3).GroupByAwait(
                x => OnityTask.FromResult(x % 2), x => OnityTask.FromResult(x * 2),
                (key, items) => OnityTask.FromResult(Sum(items)))), Is.EqualTo(new[] { 8, 4 }));
        }

        [Test]
        public void GroupByAwaitWithCancellation_DisposalCancelsAPendingKey_AndWaitsForIt()
        {
            var key = new OnityTaskCompletionSource<int>();
            CancellationToken received = default;
            var source = MultiTest.Ints("s", null, 1);
            IOnityAsyncEnumerator<IOnityGrouping<int, int>> enumerator = source.GroupByAwaitWithCancellation(
                (x, token) =>
                {
                    received = token;
                    return key.Task;
                }).GetAsyncEnumerator();
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            MultiTestResult disposal = MultiTestResult.Watch(enumerator.DisposeAsync());
            Assert.That(move.Value, Is.False);
            Assert.That(received.IsCancellationRequested, Is.True);
            Assert.That(source.Disposals, Is.EqualTo(1));
            Assert.That(disposal.Completed, Is.False);
            key.TrySetCanceled(received);
            Assert.That(disposal.Completed, Is.True);
            Assert.That(disposal.Error, Is.Null);
        }

        [Test]
        public void GroupBy_SourceOrSelectorFault_FaultsAfterDisposingTheSource()
        {
            var source = MultiTest.Ints("s", null, 1, 2);
            source.MoveFailureAt = 1;
            source.MoveFailure = new FormatException();
            Assert.That(MultiTest.Catch(() => MultiTest.Drain(source.GroupBy(x => x))), Is.TypeOf<FormatException>());
            Assert.That(source.Disposals, Is.EqualTo(1));

            var other = MultiTest.Ints("o", null, 1);
            Exception selector = MultiTest.Catch(() => MultiTest.Drain(
                other.GroupBy<int, int>(x => throw new OperationCanceledException())));
            Assert.That(selector, Is.InstanceOf<OperationCanceledException>());
            Assert.That(other.Disposals, Is.EqualTo(1));
            Assert.That(MultiTest.Drain(MultiTest.Ints("e", null).GroupBy(x => x)), Is.Empty);
        }

        // ---- Join / GroupJoin -----------------------------------------------------------------------

        [Test]
        public void Join_ReadsTheInnerSourceFirst_AndYieldsMatchesInOrder()
        {
            var log = new List<string>();
            var outer = MultiTest.Ints("outer", log, 1, 2, 3);
            var inner = new MultiTestStream<string>("inner", log, "a1", "b2", "c2", "d4");
            Assert.That(MultiTest.Drain(outer.Join(inner, o => o, i => i[1] - '0', (o, i) => o + i)),
                Is.EqualTo(new[] { "1a1", "2b2", "2c2" }));
            Assert.That(log, Is.EqualTo(new[] { "inner.acquire", "inner.dispose", "outer.acquire", "outer.dispose" }));
        }

        [Test]
        public void Join_NullKeysNeverMatch_AndComparerIsUsed()
        {
            var outer = new MultiTestStream<string>("o", null, null, "x", "Y");
            var inner = new MultiTestStream<string>("i", null, null, "X", "y");
            Assert.That(MultiTest.Drain(outer.Join(inner, o => o, i => i, (o, i) => o + i,
                StringComparer.OrdinalIgnoreCase)), Is.EqualTo(new[] { "xX", "Yy" }));
            Assert.That(MultiTest.Drain(new MultiTestStream<string>("o", null, null, "x")
                .Join(new MultiTestStream<string>("i", null, null, "x"), o => o, i => i, (o, i) => o + i)),
                Is.EqualTo(new[] { "xx" }));
        }

        [Test]
        public void JoinAwait_AndJoinAwaitWithCancellation_AwaitEveryDelegate()
        {
            var outer = MultiTest.Ints("o", null, 1, 2);
            var inner = MultiTest.Ints("i", null, 2, 1, 2);
            Assert.That(MultiTest.Drain(outer.JoinAwait(inner, o => OnityTask.FromResult(o), i => OnityTask.FromResult(i),
                (o, i) => OnityTask.FromResult(o * 10 + i))), Is.EqualTo(new[] { 11, 22, 22 }));
            CancellationToken received = default;
            Assert.That(MultiTest.Drain(MultiTest.Ints("o", null, 1).JoinAwaitWithCancellation(
                MultiTest.Ints("i", null, 1), (o, token) => OnityTask.FromResult(o),
                (i, token) => OnityTask.FromResult(i), (o, i, token) =>
                {
                    received = token;
                    return OnityTask.FromResult(o + i);
                })), Is.EqualTo(new[] { 2 }));
            Assert.That(received.CanBeCanceled, Is.True);
        }

        [Test]
        public void Join_InnerFault_NeverAcquiresTheOuterSource()
        {
            var outer = MultiTest.Ints("o", null, 1);
            var inner = MultiTest.Ints("i", null, 1);
            inner.MoveFailureAt = 0;
            inner.MoveFailure = new FormatException();
            Assert.That(MultiTest.Catch(() => MultiTest.Drain(outer.Join(inner, o => o, i => i, (o, i) => o))),
                Is.TypeOf<FormatException>());
            Assert.That(inner.Disposals, Is.EqualTo(1));
            Assert.That(outer.Acquires, Is.Zero);
        }

        [Test]
        public void GroupJoin_YieldsOneResultPerOuterItem_WithEmptyGroups()
        {
            var outer = new MultiTestStream<string>("o", null, "a", null, "b", "c");
            var inner = new MultiTestStream<string>("i", null, "a", "a", "b", null);
            Assert.That(MultiTest.Drain(outer.GroupJoin(inner, o => o, i => i, (o, group) => (o ?? "-") + Count(group))),
                Is.EqualTo(new[] { "a2", "-0", "b1", "c0" }));
            Assert.That(MultiTest.Drain(MultiTest.Ints("o", null, 1, 2).GroupJoinAwait(MultiTest.Ints("i", null, 1, 1),
                o => OnityTask.FromResult(o), i => OnityTask.FromResult(i),
                (o, group) => OnityTask.FromResult(o * 10 + Count(group)))), Is.EqualTo(new[] { 12, 20 }));
            Assert.That(MultiTest.Drain(MultiTest.Ints("o", null, 3).GroupJoinAwaitWithCancellation(
                MultiTest.Ints("i", null, 3), (o, token) => OnityTask.FromResult(o), (i, token) => OnityTask.FromResult(i),
                (o, group, token) => OnityTask.FromResult(Sum(group)))), Is.EqualTo(new[] { 3 }));
        }

        // ---- OrderBy / ThenBy -------------------------------------------------------------------

        [Test]
        public void OrderBy_IsStable_AndThenByRefinesWithoutChangingTheOriginal()
        {
            var source = new MultiTestStream<(int Key, string Id)>("s", null, (2, "a"), (1, "b"), (2, "c"), (1, "d"));
            IOnityOrderedAsyncEnumerable<(int Key, string Id)> ordered = source.OrderBy(x => x.Key);
            Assert.That(Ids(MultiTest.Drain(ordered)), Is.EqualTo(new[] { "b", "d", "a", "c" }));
            Assert.That(Ids(MultiTest.Drain(source.OrderByDescending(x => x.Key))), Is.EqualTo(new[] { "a", "c", "b", "d" }));
            Assert.That(Ids(MultiTest.Drain(ordered.ThenByDescending(x => x.Id))), Is.EqualTo(new[] { "d", "b", "c", "a" }));
            Assert.That(Ids(MultiTest.Drain(ordered)), Is.EqualTo(new[] { "b", "d", "a", "c" }));
            Assert.That(MultiTest.Drain(MultiTest.Ints("s", null, 3, 1, 2).OrderBy(x => x, new ReverseComparer())),
                Is.EqualTo(new[] { 3, 2, 1 }));
            Assert.That(MultiTest.Drain(MultiTest.Ints("s", null, 3, 1, 2).OrderBy(x => x).ThenBy(x => -x)),
                Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(MultiTest.Drain(MultiTest.Ints("e", null).OrderBy(x => x)), Is.Empty);
        }

        private static string[] Ids((int Key, string Id)[] items)
        {
            var ids = new string[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                ids[i] = items[i].Id;
            }
            return ids;
        }

        [Test]
        public void OrderBy_ReadsAndDisposesTheSourceBeforeTheFirstItem()
        {
            var log = new List<string>();
            var source = MultiTest.Ints("source", log, 2, 1);
            IOnityAsyncEnumerator<int> enumerator = source.OrderBy(x => x).GetAsyncEnumerator();
            MultiTestResult<bool> first = MultiTest.Move(enumerator, log, "first");
            Assert.That(first.Value, Is.True);
            Assert.That(enumerator.Current, Is.EqualTo(1));
            Assert.That(log, Is.EqualTo(new[] { "source.acquire", "source.dispose", "first" }));
            MultiTest.ExpectItem(enumerator, 2);
            MultiTest.ExpectEnd(enumerator);
            MultiTest.Dispose(enumerator);
        }

        [Test]
        public void OrderByAwait_ComputesKeysOneAtATime_AndAllKeyForms()
        {
            var keys = new Queue<OnityTaskCompletionSource<int>>();
            IOnityAsyncEnumerator<int> enumerator = MultiTest.Ints("s", null, 10, 20, 30)
                .OrderByAwait(x =>
                {
                    var key = new OnityTaskCompletionSource<int>();
                    keys.Enqueue(key);
                    return key.Task;
                }).GetAsyncEnumerator();
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            Assert.That(keys.Count, Is.EqualTo(1));
            keys.Dequeue().TrySetResult(3);
            keys.Dequeue().TrySetResult(1);
            keys.Dequeue().TrySetResult(2);
            Assert.That(move.Value, Is.True);
            Assert.That(enumerator.Current, Is.EqualTo(20));
            MultiTest.ExpectItem(enumerator, 30);
            MultiTest.ExpectItem(enumerator, 10);
            MultiTest.ExpectEnd(enumerator);
            MultiTest.Dispose(enumerator);

            Assert.That(MultiTest.Drain(MultiTest.Ints("s", null, 1, 2, 3).OrderByDescendingAwait(x => OnityTask.FromResult(x))),
                Is.EqualTo(new[] { 3, 2, 1 }));
            Assert.That(MultiTest.Drain(MultiTest.Ints("s", null, 1, 2, 3, 4).OrderByAwaitWithCancellation(
                    (x, token) => OnityTask.FromResult(x % 2))
                .ThenByDescendingAwait(x => OnityTask.FromResult(x))), Is.EqualTo(new[] { 4, 2, 3, 1 }));
            Assert.That(MultiTest.Drain(MultiTest.Ints("s", null, 2, 1).OrderByDescendingAwaitWithCancellation(
                    (x, token) => OnityTask.FromResult(0))
                .ThenByAwaitWithCancellation((x, token) => OnityTask.FromResult(x))), Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void OrderByAwaitWithCancellation_DisposalCancelsAPendingKey_AndWaitsForIt()
        {
            var key = new OnityTaskCompletionSource<int>();
            CancellationToken received = default;
            var source = MultiTest.Ints("s", null, 1, 2);
            IOnityAsyncEnumerator<int> enumerator = source.OrderByAwaitWithCancellation((x, token) =>
            {
                received = token;
                return key.Task;
            }).GetAsyncEnumerator();
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            Assert.That(source.Disposals, Is.EqualTo(1), "the source is disposed before keys are computed");
            MultiTestResult disposal = MultiTestResult.Watch(enumerator.DisposeAsync());
            Assert.That(move.Value, Is.False);
            Assert.That(received.IsCancellationRequested, Is.True);
            Assert.That(disposal.Completed, Is.False);
            key.TrySetCanceled(received);
            Assert.That(disposal.Completed, Is.True);
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void OrderBy_KeyOrComparerFault_Faults()
        {
            Exception key = MultiTest.Catch(() => MultiTest.Drain(MultiTest.Ints("s", null, 1, 2)
                .OrderBy<int, int>(x => throw new FormatException())));
            Assert.That(key, Is.TypeOf<FormatException>());
            Assert.That(MultiTest.Catch(() => MultiTest.Drain(MultiTest.Ints("s", null, 1, 2)
                .OrderBy(x => x, Comparer<int>.Create((x, y) => throw new FormatException())))), Is.Not.Null);
        }

        // ---- Union / Intersect / Except -------------------------------------------------------------

        [Test]
        public void Union_IsConcatThenDistinct()
        {
            var log = new List<string>();
            var first = MultiTest.Ints("first", log, 1, 2, 2, 3);
            var second = MultiTest.Ints("second", log, 3, 4, 1);
            Assert.That(MultiTest.Drain(first.Union(second)), Is.EqualTo(new[] { 1, 2, 3, 4 }));
            Assert.That(log, Is.EqualTo(new[] { "first.acquire", "first.dispose", "second.acquire", "second.dispose" }));
            Assert.That(MultiTest.Drain(new MultiTestStream<string>("a", null, "a").Union(
                new MultiTestStream<string>("b", null, "A", "b"), StringComparer.OrdinalIgnoreCase)),
                Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public void IntersectAndExcept_ReadTheSecondSourceFirst_AndYieldDistinctItems()
        {
            var log = new List<string>();
            var first = MultiTest.Ints("first", log, 1, 2, 2, 3, 4);
            var second = MultiTest.Ints("second", log, 2, 4, 4, 5);
            Assert.That(MultiTest.Drain(first.Intersect(second)), Is.EqualTo(new[] { 2, 4 }));
            Assert.That(log, Is.EqualTo(new[] { "second.acquire", "second.dispose", "first.acquire", "first.dispose" }));
            Assert.That(MultiTest.Drain(first.Except(second)), Is.EqualTo(new[] { 1, 3 }));
            Assert.That(MultiTest.Drain(new MultiTestStream<string>("a", null, "A", "b", "a").Intersect(
                new MultiTestStream<string>("b", null, "a"), StringComparer.OrdinalIgnoreCase)),
                Is.EqualTo(new[] { "A" }));
            Assert.That(MultiTest.Drain(new MultiTestStream<string>("a", null, "A", "b", "B").Except(
                new MultiTestStream<string>("b", null, "a"), StringComparer.OrdinalIgnoreCase)),
                Is.EqualTo(new[] { "b" }));
        }

        [Test]
        public void SetOperations_SecondFault_NeverAcquiresTheFirstSource()
        {
            var first = MultiTest.Ints("first", null, 1);
            var second = MultiTest.Ints("second", null, 1);
            second.MoveFailureAt = 0;
            second.MoveFailure = new FormatException();
            Assert.That(MultiTest.Catch(() => MultiTest.Drain(first.Except(second))), Is.TypeOf<FormatException>());
            Assert.That(first.Acquires, Is.Zero);
            Assert.That(second.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void Arguments_AreValidatedSynchronously()
        {
            IOnityAsyncEnumerable<int> missing = null;
            var source = MultiTest.Ints("s", null, 1);
            Func<int, int> key = null;
            Assert.Throws<ArgumentNullException>(() => missing.GroupBy(x => x));
            Assert.Throws<ArgumentNullException>(() => source.GroupBy(key));
            Assert.Throws<ArgumentNullException>(() => source.GroupBy(x => x, key));
            Assert.Throws<ArgumentNullException>(() => missing.Join(source, x => x, x => x, (x, y) => x));
            Assert.Throws<ArgumentNullException>(() => source.Join(missing, x => x, x => x, (x, y) => x));
            Assert.Throws<ArgumentNullException>(() => source.GroupJoin(source, key, x => x, (x, y) => x));
            Assert.Throws<ArgumentNullException>(() => missing.OrderBy(x => x));
            Assert.Throws<ArgumentNullException>(() => source.OrderBy(key));
            Assert.Throws<ArgumentNullException>(() => ((IOnityOrderedAsyncEnumerable<int>)null).ThenBy(x => x));
            Assert.Throws<ArgumentNullException>(() => source.OrderBy(x => x).ThenBy(key));
            Assert.Throws<ArgumentNullException>(() => missing.Union(source));
            Assert.Throws<ArgumentNullException>(() => source.Intersect(missing));
            Assert.Throws<ArgumentNullException>(() => missing.Except(source));
            Assert.That(source.Acquires, Is.Zero);
        }
    }
}
