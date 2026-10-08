using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    // Deterministic synchronous source with call counters and fault/cancel/pending injection.
    internal sealed class LinqProbe<T> : IOnityAsyncEnumerable<T>
    {
        private readonly T[] m_items;
        internal int Acquires;
        internal int Moves;
        internal int Disposals;
        internal CancellationToken Token;
        internal Action<int> OnMove;
        internal Exception MoveFailure;
        internal int MoveFailureAt = -1;
        internal Exception DisposeFailure;
        internal int PendingAt = -1;
        internal OnityTaskCompletionSource<bool> Pending;

        internal LinqProbe(params T[] items)
        {
            m_items = items;
        }

        public IOnityAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            Acquires++;
            Token = cancellationToken;
            return new Enumerator(this, cancellationToken);
        }

        private sealed class Enumerator : IOnityAsyncEnumerator<T>
        {
            private readonly LinqProbe<T> m_owner;
            private readonly CancellationToken m_token;
            private int m_position;
            private T m_current;

            internal Enumerator(LinqProbe<T> owner, CancellationToken token)
            {
                m_owner = owner;
                m_token = token;
            }

            public T Current => m_current;

            public OnityTask<bool> MoveNextAsync()
            {
                int move = m_owner.Moves++;
                m_owner.OnMove?.Invoke(move);
                if (move == m_owner.MoveFailureAt)
                {
                    return OnityTask<bool>.FromException(m_owner.MoveFailure);
                }
                if (m_token.IsCancellationRequested)
                {
                    return OnityTask<bool>.FromCanceled(m_token);
                }
                if (m_position < m_owner.m_items.Length)
                {
                    m_current = m_owner.m_items[m_position++];
                    return move == m_owner.PendingAt ? m_owner.Pending.Task : OnityTask<bool>.FromResult(true);
                }
                m_current = default;
                return OnityTask<bool>.FromResult(false);
            }

            public OnityTask DisposeAsync()
            {
                m_owner.Disposals++;
                return m_owner.DisposeFailure != null
                    ? OnityTask.FromException(m_owner.DisposeFailure) : OnityTask.Completed;
            }
        }
    }

    internal static class LinqTestSupport
    {
        internal static LinqProbe<int> Items(params int[] items) => new LinqProbe<int>(items);

        internal static T Read<T>(OnityTask<T> task)
        {
            Assert.That(task.IsCompleted, Is.True);
            return task.GetAwaiter().GetResult();
        }

        internal static void Read(OnityTask task)
        {
            Assert.That(task.IsCompleted, Is.True);
            task.GetAwaiter().GetResult();
        }

        internal static Exception Catch(Action action)
        {
            try
            {
                action();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        internal static void Wait(Func<bool> done)
        {
            Assert.That(SpinWait.SpinUntil(done, TimeSpan.FromSeconds(5)), Is.True);
        }

        internal static T[] ToArray<T>(IOnityAsyncEnumerable<T> source) => Read(source.ToArrayAsync());
    }

    public sealed class OnityAsyncEnumerableLinqFilteringEditModeTests
    {
        private static LinqProbe<int> Items(params int[] items) => LinqTestSupport.Items(items);
        private static T Read<T>(OnityTask<T> task) => LinqTestSupport.Read(task);
        private static void Read(OnityTask task) => LinqTestSupport.Read(task);
        private static T[] ToArray<T>(IOnityAsyncEnumerable<T> source) => LinqTestSupport.ToArray(source);
        private static Exception Catch(Action action) => LinqTestSupport.Catch(action);
        private static OnityTask<bool> Await(bool value) => OnityTask<bool>.FromResult(value);

        // ---- admission -------------------------------------------------------------------

        [Test]
        public void Arguments_ValidateSynchronously_AndDescriptionsAreLazy()
        {
            IOnityAsyncEnumerable<int> missing = null;
            var source = Items(1, 2, 3);
            Func<int, bool> sync = null;
            Func<int, int, bool> syncIndexed = null;
            Func<int, OnityTask<bool>> awaiting = null;
            Func<int, int, OnityTask<bool>> awaitingIndexed = null;
            Func<int, CancellationToken, OnityTask<bool>> cancel = null;
            Func<int, int, CancellationToken, OnityTask<bool>> cancelIndexed = null;
            Func<int, int> key = null;
            Func<int, OnityTask<int>> keyAwait = null;
            Func<int, CancellationToken, OnityTask<int>> keyCancel = null;

            Assert.Throws<ArgumentNullException>(() => missing.Skip(1));
            Assert.Throws<ArgumentNullException>(() => missing.SkipLast(1));
            Assert.Throws<ArgumentNullException>(() => missing.TakeLast(1));
            Assert.Throws<ArgumentNullException>(() => missing.SkipWhile(value => true));
            Assert.Throws<ArgumentNullException>(() => missing.TakeWhile(value => true));
            Assert.Throws<ArgumentNullException>(() => missing.Distinct());
            Assert.Throws<ArgumentNullException>(() => missing.DistinctUntilChanged());
            Assert.Throws<ArgumentNullException>(() => missing.Where((value, index) => true));
            Assert.Throws<ArgumentNullException>(() => missing.Select((value, index) => value));
            Assert.Throws<ArgumentNullException>(() => source.SkipWhile(sync));
            Assert.Throws<ArgumentNullException>(() => source.SkipWhile(syncIndexed));
            Assert.Throws<ArgumentNullException>(() => source.SkipWhileAwait(awaiting));
            Assert.Throws<ArgumentNullException>(() => source.SkipWhileAwait(awaitingIndexed));
            Assert.Throws<ArgumentNullException>(() => source.SkipWhileAwaitWithCancellation(cancel));
            Assert.Throws<ArgumentNullException>(() => source.SkipWhileAwaitWithCancellation(cancelIndexed));
            Assert.Throws<ArgumentNullException>(() => source.TakeWhile(sync));
            Assert.Throws<ArgumentNullException>(() => source.TakeWhile(syncIndexed));
            Assert.Throws<ArgumentNullException>(() => source.TakeWhileAwait(awaiting));
            Assert.Throws<ArgumentNullException>(() => source.TakeWhileAwait(awaitingIndexed));
            Assert.Throws<ArgumentNullException>(() => source.TakeWhileAwaitWithCancellation(cancel));
            Assert.Throws<ArgumentNullException>(() => source.TakeWhileAwaitWithCancellation(cancelIndexed));
            Assert.Throws<ArgumentNullException>(() => source.Where(syncIndexed));
            Assert.Throws<ArgumentNullException>(() => source.WhereAwait(awaiting));
            Assert.Throws<ArgumentNullException>(() => source.WhereAwaitWithCancellation(cancel));
            Assert.Throws<ArgumentNullException>(() => source.WhereAwaitWithCancellation(cancelIndexed));
            Assert.Throws<ArgumentNullException>(() => source.Select((Func<int, int, int>)null));
            Assert.Throws<ArgumentNullException>(() => source.SelectAwait((Func<int, OnityTask<int>>)null));
            Assert.Throws<ArgumentNullException>(() => source.SelectAwaitWithCancellation(
                (Func<int, CancellationToken, OnityTask<int>>)null));
            Assert.Throws<ArgumentNullException>(() => source.SelectAwaitWithCancellation(
                (Func<int, int, CancellationToken, OnityTask<int>>)null));
            Assert.Throws<ArgumentNullException>(() => source.Distinct(key));
            Assert.Throws<ArgumentNullException>(() => source.Distinct(key, EqualityComparer<int>.Default));
            Assert.Throws<ArgumentNullException>(() => source.DistinctAwait(keyAwait));
            Assert.Throws<ArgumentNullException>(() => source.DistinctAwaitWithCancellation(keyCancel));
            Assert.Throws<ArgumentNullException>(() => source.DistinctUntilChanged(key));
            Assert.Throws<ArgumentNullException>(() => source.DistinctUntilChanged(key, EqualityComparer<int>.Default));
            Assert.Throws<ArgumentNullException>(() => source.DistinctUntilChangedAwait(keyAwait));
            Assert.Throws<ArgumentNullException>(() => source.DistinctUntilChangedAwaitWithCancellation(keyCancel));

            source.Skip(1).GetAsyncEnumerator().DisposeAsync();
            source.SkipLast(1).GetAsyncEnumerator().DisposeAsync();
            source.TakeLast(1).GetAsyncEnumerator().DisposeAsync();
            source.SkipWhile(value => true).GetAsyncEnumerator().DisposeAsync();
            source.TakeWhileAwait(value => Await(true)).GetAsyncEnumerator().DisposeAsync();
            source.Distinct().GetAsyncEnumerator().DisposeAsync();
            source.DistinctUntilChanged().GetAsyncEnumerator().DisposeAsync();
            Assert.That(source.Acquires, Is.Zero);
            Assert.That(source.Moves, Is.Zero);
        }

        // ---- Skip / SkipLast / TakeLast --------------------------------------------------

        [TestCase(0, new[] { 1, 2, 3, 4, 5 })]
        [TestCase(-3, new[] { 1, 2, 3, 4, 5 })]
        [TestCase(2, new[] { 3, 4, 5 })]
        [TestCase(5, new int[0])]
        [TestCase(9, new int[0])]
        public void Skip_DropsLeadingItems(int count, int[] expected)
        {
            var source = Items(1, 2, 3, 4, 5);
            Assert.That(ToArray(source.Skip(count)), Is.EqualTo(expected));
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void Skip_NonPositiveCountReturnsSource_AndEmptySourceStaysEmpty()
        {
            var source = Items(1);
            Assert.That(source.Skip(0), Is.SameAs(source));
            Assert.That(source.SkipLast(-1), Is.SameAs(source));
            Assert.That(ToArray(Items().Skip(3)), Is.Empty);
            Assert.That(ToArray(Items().SkipLast(3)), Is.Empty);
            Assert.That(ToArray(Items().TakeLast(3)), Is.Empty);
        }

        [TestCase(0, new[] { 1, 2, 3, 4, 5 })]
        [TestCase(2, new[] { 1, 2, 3 })]
        [TestCase(5, new int[0])]
        [TestCase(8, new int[0])]
        public void SkipLast_DropsTrailingItems(int count, int[] expected)
        {
            var source = Items(1, 2, 3, 4, 5);
            Assert.That(ToArray(source.SkipLast(count)), Is.EqualTo(expected));
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [TestCase(2, new[] { 4, 5 })]
        [TestCase(5, new[] { 1, 2, 3, 4, 5 })]
        [TestCase(9, new[] { 1, 2, 3, 4, 5 })]
        [TestCase(1, new[] { 5 })]
        public void TakeLast_KeepsTrailingItems(int count, int[] expected)
        {
            var source = Items(1, 2, 3, 4, 5);
            Assert.That(ToArray(source.TakeLast(count)), Is.EqualTo(expected));
            Assert.That(source.Moves, Is.EqualTo(6));
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void TakeLast_NonPositiveCountIsEmptyWithoutAcquiringUpstream()
        {
            var source = Items(1, 2, 3);
            Assert.That(ToArray(source.TakeLast(0)), Is.Empty);
            Assert.That(ToArray(source.TakeLast(-2)), Is.Empty);
            Assert.That(source.Acquires, Is.Zero);
        }

        [Test]
        public void TakeLast_DisposesUpstreamBeforeThePublishedLastItem()
        {
            var source = Items(1, 2, 3, 4, 5);
            var iterator = source.TakeLast(2).GetAsyncEnumerator();
            Assert.That(Read(iterator.MoveNextAsync()), Is.True);
            Assert.That(iterator.Current, Is.EqualTo(4));
            Assert.That(source.Disposals, Is.Zero);
            Assert.That(Read(iterator.MoveNextAsync()), Is.True);
            Assert.That(iterator.Current, Is.EqualTo(5));
            Assert.That(source.Disposals, Is.EqualTo(1));
            Assert.That(Read(iterator.MoveNextAsync()), Is.False);
            Read(iterator.DisposeAsync());
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void SkipAndTakeLast_AreReusableDescriptionsWithIndependentState()
        {
            var source = Items(1, 2, 3, 4);
            var skip = source.Skip(1);
            var tail = source.TakeLast(2);
            var last = source.SkipLast(1);
            for (int round = 0; round < 2; round++)
            {
                Assert.That(ToArray(skip), Is.EqualTo(new[] { 2, 3, 4 }));
                Assert.That(ToArray(tail), Is.EqualTo(new[] { 3, 4 }));
                Assert.That(ToArray(last), Is.EqualTo(new[] { 1, 2, 3 }));
            }
            Assert.That(source.Disposals, Is.EqualTo(6));
        }

        [Test]
        public void SkipPagingOverLargeRun_DoesNotRecurse()
        {
            Assert.That(ToArray(OnityAsyncEnumerable.Range(0, 100000).Skip(99999)), Is.EqualTo(new[] { 99999 }));
            Assert.That(ToArray(OnityAsyncEnumerable.Range(0, 100000).SkipLast(99999)), Is.EqualTo(new[] { 0 }));
            Assert.That(ToArray(OnityAsyncEnumerable.Range(0, 100000).TakeLast(1)), Is.EqualTo(new[] { 99999 }));
            Assert.That(ToArray(OnityAsyncEnumerable.Range(0, 100000).SkipWhile(value => true)), Is.Empty);
        }

        // ---- SkipWhile / TakeWhile -------------------------------------------------------

        [Test]
        public void SkipWhile_AllShapes_SkipOnlyTheLeadingRun()
        {
            var source = Items(1, 2, 3, 1, 2);
            int calls = 0;
            Assert.That(ToArray(source.SkipWhile(value => { calls++; return value < 3; })),
                Is.EqualTo(new[] { 3, 1, 2 }));
            Assert.That(calls, Is.EqualTo(3));
            Assert.That(ToArray(source.SkipWhile((value, index) => index < 2)), Is.EqualTo(new[] { 3, 1, 2 }));
            Assert.That(ToArray(source.SkipWhileAwait(value => Await(value < 3))), Is.EqualTo(new[] { 3, 1, 2 }));
            Assert.That(ToArray(source.SkipWhileAwait((value, index) => Await(index < 2))),
                Is.EqualTo(new[] { 3, 1, 2 }));
            Assert.That(ToArray(source.SkipWhileAwaitWithCancellation((value, token) => Await(value < 3))),
                Is.EqualTo(new[] { 3, 1, 2 }));
            Assert.That(ToArray(source.SkipWhileAwaitWithCancellation((value, index, token) => Await(index < 2))),
                Is.EqualTo(new[] { 3, 1, 2 }));
            Assert.That(ToArray(source.SkipWhile(value => false)), Is.EqualTo(new[] { 1, 2, 3, 1, 2 }));
            Assert.That(ToArray(source.SkipWhileAwait(value => Await(true))), Is.Empty);
            Assert.That(ToArray(Items().SkipWhile(value => true)), Is.Empty);
            Assert.That(source.Disposals, Is.EqualTo(8));
        }

        [Test]
        public void TakeWhile_AllShapes_EndAtTheFirstFailureWithoutPullingMore()
        {
            var source = Items(1, 2, 3, 1, 2);
            Assert.That(ToArray(source.TakeWhile(value => value < 3)), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(source.Moves, Is.EqualTo(3));
            Assert.That(source.Disposals, Is.EqualTo(1));
            Assert.That(ToArray(source.TakeWhile((value, index) => index < 4)), Is.EqualTo(new[] { 1, 2, 3, 1 }));
            Assert.That(ToArray(source.TakeWhileAwait(value => Await(value < 3))), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(ToArray(source.TakeWhileAwait((value, index) => Await(index < 1))), Is.EqualTo(new[] { 1 }));
            Assert.That(ToArray(source.TakeWhileAwaitWithCancellation((value, token) => Await(value < 3))),
                Is.EqualTo(new[] { 1, 2 }));
            Assert.That(ToArray(source.TakeWhileAwaitWithCancellation((value, index, token) => Await(index < 3))),
                Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(ToArray(source.TakeWhile(value => false)), Is.Empty);
            Assert.That(ToArray(Items().TakeWhile(value => true)), Is.Empty);
            Assert.That(source.Disposals, Is.EqualTo(source.Acquires));
        }

        [Test]
        public void TakeWhile_DisposesUpstreamBeforeThePublishedEnd_AndStaysTerminal()
        {
            var source = Items(1, 2, 3, 4);
            var iterator = source.TakeWhile(value => value < 2).GetAsyncEnumerator();
            Assert.That(Read(iterator.MoveNextAsync()), Is.True);
            Assert.That(iterator.Current, Is.EqualTo(1));
            Assert.That(source.Disposals, Is.Zero);
            Assert.That(Read(iterator.MoveNextAsync()), Is.False);
            Assert.That(source.Disposals, Is.EqualTo(1));
            Assert.That(Read(iterator.MoveNextAsync()), Is.False);
            Assert.That(source.Moves, Is.EqualTo(2));
            Read(iterator.DisposeAsync());
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void TakeWhileAwaitWithCancellation_PassesACancelableOwnedToken_AndPendingPredicateWaits()
        {
            var source = Items(1, 2, 3);
            var pending = new OnityTaskCompletionSource<bool>();
            CancellationToken seen = default;
            int calls = 0;
            var iterator = source.TakeWhileAwaitWithCancellation((value, token) =>
            {
                seen = token;
                calls++;
                return calls == 2 ? pending.Task : Await(true);
            }).GetAsyncEnumerator();
            Assert.That(Read(iterator.MoveNextAsync()), Is.True);
            Assert.That(iterator.Current, Is.EqualTo(1));
            Assert.That(seen.CanBeCanceled, Is.True);
            var second = iterator.MoveNextAsync();
            Assert.That(second.IsCompleted, Is.False);
            Assert.That(source.Moves, Is.EqualTo(2));
            pending.TrySetResult(false);
            LinqTestSupport.Wait(() => second.IsCompleted);
            Assert.That(Read(second), Is.False);
            Assert.That(source.Moves, Is.EqualTo(2));
            Read(iterator.DisposeAsync());
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        // ---- indexed Select / Where and await shapes -------------------------------------

        [Test]
        public void IndexedSelectAndWhere_PassZeroBasedIndexes()
        {
            Assert.That(ToArray(OnityAsyncEnumerable.Range(10, 3).Select((value, index) => value * index)),
                Is.EqualTo(new[] { 0, 11, 24 }));
            Assert.That(ToArray(OnityAsyncEnumerable.Range(10, 5).Where((value, index) => index % 2 == 0)),
                Is.EqualTo(new[] { 10, 12, 14 }));
            Assert.That(ToArray(OnityAsyncEnumerable.Range(10, 3).SelectAwaitWithCancellation(
                (value, index, token) => OnityTask<int>.FromResult(value + index))), Is.EqualTo(new[] { 10, 12, 14 }));
            Assert.That(ToArray(OnityAsyncEnumerable.Range(10, 5).WhereAwaitWithCancellation(
                (value, index, token) => Await(index > 2))), Is.EqualTo(new[] { 13, 14 }));
        }

        [Test]
        public void AwaitShapesWithoutIndex_MapAndFilter()
        {
            Assert.That(ToArray(Items(1, 2, 3).SelectAwait(value => OnityTask<int>.FromResult(value * 2))),
                Is.EqualTo(new[] { 2, 4, 6 }));
            Assert.That(ToArray(Items(1, 2, 3).SelectAwaitWithCancellation(
                (value, token) => OnityTask<int>.FromResult(value * 3))), Is.EqualTo(new[] { 3, 6, 9 }));
            Assert.That(ToArray(Items(1, 2, 3, 4).WhereAwait(value => Await(value % 2 == 0))),
                Is.EqualTo(new[] { 2, 4 }));
            Assert.That(ToArray(Items(1, 2, 3, 4).WhereAwaitWithCancellation((value, token) => Await(value > 2))),
                Is.EqualTo(new[] { 3, 4 }));
        }

        [Test]
        public void IndexedAwaitOperators_OrderResultsAcrossPendingDelegates()
        {
            var source = Items(1, 2, 3);
            var gate = new OnityTaskCompletionSource<int>();
            int calls = 0;
            var task = source.SelectAwaitWithCancellation((value, index, token) =>
            {
                calls++;
                return calls == 1 ? gate.Task : OnityTask<int>.FromResult(value * 10 + index);
            }).ToArrayAsync();
            Assert.That(task.IsCompleted, Is.False);
            Assert.That(source.Moves, Is.EqualTo(1));
            gate.TrySetResult(99);
            LinqTestSupport.Wait(() => task.IsCompleted);
            Assert.That(Read(task), Is.EqualTo(new[] { 99, 21, 32 }));
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        // ---- Distinct / DistinctUntilChanged ---------------------------------------------

        [Test]
        public void Distinct_AllShapes()
        {
            var source = Items(1, 2, 1, 3, 2, 4);
            Assert.That(ToArray(source.Distinct()), Is.EqualTo(new[] { 1, 2, 3, 4 }));
            Assert.That(ToArray(source.Distinct(EqualityComparer<int>.Default)), Is.EqualTo(new[] { 1, 2, 3, 4 }));
            Assert.That(ToArray(source.Distinct(value => value % 2)), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(ToArray(source.Distinct(value => value % 3, EqualityComparer<int>.Default)),
                Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(ToArray(source.DistinctAwait(value => OnityTask<int>.FromResult(value % 2))),
                Is.EqualTo(new[] { 1, 2 }));
            Assert.That(ToArray(source.DistinctAwait(value => OnityTask<int>.FromResult(value % 3),
                EqualityComparer<int>.Default)), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(ToArray(source.DistinctAwaitWithCancellation(
                (value, token) => OnityTask<int>.FromResult(value % 2))), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(ToArray(source.DistinctAwaitWithCancellation(
                (value, token) => OnityTask<int>.FromResult(value % 3), EqualityComparer<int>.Default)),
                Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(ToArray(Items().Distinct()), Is.Empty);
            Assert.That(source.Disposals, Is.EqualTo(source.Acquires));
        }

        [Test]
        public void Distinct_UsesTheSuppliedComparer_AndNullComparerSelectsDefault()
        {
            var source = new LinqProbe<string>("a", "A", "b", "B", "a");
            Assert.That(ToArray(source.Distinct(StringComparer.OrdinalIgnoreCase)), Is.EqualTo(new[] { "a", "b" }));
            Assert.That(ToArray(source.Distinct((IEqualityComparer<string>)null)),
                Is.EqualTo(new[] { "a", "A", "b", "B" }));
            Assert.That(ToArray(source.Distinct(value => value, StringComparer.OrdinalIgnoreCase)),
                Is.EqualTo(new[] { "a", "b" }));
            Assert.That(ToArray(source.DistinctAwait(value => OnityTask<string>.FromResult(value),
                StringComparer.OrdinalIgnoreCase)), Is.EqualTo(new[] { "a", "b" }));
            Assert.That(ToArray(new LinqProbe<string>(null, "x", null).Distinct()), Is.EqualTo(new string[] { null, "x" }));
        }

        [Test]
        public void Distinct_EachEnumerationKeepsItsOwnSeenSet()
        {
            var distinct = Items(1, 1, 2).Distinct();
            Assert.That(ToArray(distinct), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(ToArray(distinct), Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void DistinctUntilChanged_AllShapes()
        {
            var source = Items(1, 1, 2, 2, 1, 3, 3);
            Assert.That(ToArray(source.DistinctUntilChanged()), Is.EqualTo(new[] { 1, 2, 1, 3 }));
            Assert.That(ToArray(source.DistinctUntilChanged(EqualityComparer<int>.Default)),
                Is.EqualTo(new[] { 1, 2, 1, 3 }));
            Assert.That(ToArray(source.DistinctUntilChanged(value => value / 2)), Is.EqualTo(new[] { 1, 2, 1, 3 }));
            Assert.That(ToArray(source.DistinctUntilChanged(value => value % 2, EqualityComparer<int>.Default)),
                Is.EqualTo(new[] { 1, 2, 1 }));
            Assert.That(ToArray(source.DistinctUntilChangedAwait(value => OnityTask<int>.FromResult(value))),
                Is.EqualTo(new[] { 1, 2, 1, 3 }));
            Assert.That(ToArray(source.DistinctUntilChangedAwait(
                value => OnityTask<int>.FromResult(value % 2), EqualityComparer<int>.Default)),
                Is.EqualTo(new[] { 1, 2, 1 }));
            Assert.That(ToArray(source.DistinctUntilChangedAwaitWithCancellation(
                (value, token) => OnityTask<int>.FromResult(value))), Is.EqualTo(new[] { 1, 2, 1, 3 }));
            Assert.That(ToArray(source.DistinctUntilChangedAwaitWithCancellation(
                (value, token) => OnityTask<int>.FromResult(value % 2), EqualityComparer<int>.Default)),
                Is.EqualTo(new[] { 1, 2, 1 }));
            Assert.That(ToArray(Items().DistinctUntilChanged()), Is.Empty);
            Assert.That(ToArray(Items(7).DistinctUntilChanged()), Is.EqualTo(new[] { 7 }));
        }

        [Test]
        public void DistinctUntilChanged_ComparerAndNullKeys()
        {
            var source = new LinqProbe<string>("a", "A", "b", "B", "a");
            Assert.That(ToArray(source.DistinctUntilChanged(StringComparer.OrdinalIgnoreCase)),
                Is.EqualTo(new[] { "a", "b", "a" }));
            Assert.That(ToArray(source.DistinctUntilChanged((IEqualityComparer<string>)null)).Length, Is.EqualTo(5));
            Assert.That(ToArray(new LinqProbe<string>(null, null, "x", null).DistinctUntilChanged()),
                Is.EqualTo(new string[] { null, "x", null }));
        }

        // ---- cancellation, faults, disposal ----------------------------------------------

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        public void CancellationMidEnumeration_IsCanceled_AndUpstreamIsDisposedOnce(int shape)
        {
            using (var cts = new CancellationTokenSource())
            {
                var source = Items(1, 2, 3, 4, 5, 6);
                source.OnMove = move =>
                {
                    if (move == 2)
                    {
                        cts.Cancel();
                    }
                };
                IOnityAsyncEnumerable<int> stream;
                switch (shape)
                {
                    case 0: stream = source.Skip(1); break;
                    case 1: stream = source.SkipLast(1); break;
                    case 2: stream = source.TakeLast(2); break;
                    case 3: stream = source.SkipWhile(value => true); break;
                    case 4: stream = source.TakeWhileAwait(value => Await(true)); break;
                    case 5: stream = source.Distinct(); break;
                    case 6: stream = source.DistinctUntilChangedAwaitWithCancellation(
                        (value, token) => OnityTask<int>.FromResult(value)); break;
                    default: stream = source.SkipWhileAwaitWithCancellation((value, index, token) => Await(true)); break;
                }
                var task = stream.ToArrayAsync(cts.Token);
                Assert.That(task.IsCompleted, Is.True);
                Assert.That(task.IsCanceled, Is.True);
                Assert.That(source.Disposals, Is.EqualTo(1));
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void UpstreamFault_PropagatesThroughOperators_AndUpstreamIsDisposedOnce(int shape)
        {
            var fault = new InvalidOperationException("source");
            var source = Items(1, 2, 3, 4);
            source.MoveFailureAt = 2;
            source.MoveFailure = fault;
            IOnityAsyncEnumerable<int> stream;
            switch (shape)
            {
                case 0: stream = source.Skip(1); break;
                case 1: stream = source.SkipLast(1); break;
                case 2: stream = source.TakeLast(2); break;
                case 3: stream = source.TakeWhile(value => true); break;
                case 4: stream = source.Distinct(); break;
                default: stream = source.SkipWhileAwait(value => Await(true)); break;
            }
            var task = stream.ToArrayAsync();
            Assert.That(task.IsFaulted, Is.True);
            Assert.That(Catch(() => Read(task)), Is.SameAs(fault));
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void DelegateException_FaultsTheStream_AndUpstreamIsDisposedOnce(int shape)
        {
            var fault = new InvalidOperationException("delegate");
            var source = Items(1, 2, 3);
            IOnityAsyncEnumerable<int> stream;
            switch (shape)
            {
                case 0: stream = source.SkipWhile((Func<int, bool>)(value => throw fault)); break;
                case 1: stream = source.TakeWhile((Func<int, bool>)(value => throw fault)); break;
                case 2: stream = source.Where((Func<int, int, bool>)((value, index) => throw fault)); break;
                case 3: stream = source.Select((Func<int, int, int>)((value, index) => throw fault)); break;
                case 4: stream = source.Distinct((Func<int, int>)(value => throw fault)); break;
                case 5: stream = source.DistinctUntilChanged((Func<int, int>)(value => throw fault)); break;
                default: stream = source.TakeWhileAwait((Func<int, OnityTask<bool>>)(value => throw fault)); break;
            }
            var task = stream.ToArrayAsync();
            Assert.That(task.IsFaulted, Is.True);
            Assert.That(Catch(() => Read(task)), Is.SameAs(fault));
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void ReturnedFaultedOrCanceledDelegateTask_KeepsItsStatus()
        {
            var fault = new InvalidOperationException("task");
            var faulted = Items(1, 2).SkipWhileAwait(value => OnityTask<bool>.FromException(fault)).ToArrayAsync();
            Assert.That(faulted.IsFaulted, Is.True);
            Assert.That(Catch(() => Read(faulted)), Is.SameAs(fault));
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                var canceled = Items(1, 2).DistinctAwait(
                    value => OnityTask<int>.FromCanceled(cts.Token)).ToArrayAsync();
                Assert.That(canceled.IsCanceled, Is.True);
            }
        }

        [Test]
        public void EarlyConsumerExit_DisposesUpstreamOnce()
        {
            var source = Items(1, 2, 3, 4, 5);
            Assert.That(Read(source.Skip(1).FirstAsync()), Is.EqualTo(2));
            Assert.That(source.Moves, Is.EqualTo(2));
            Assert.That(source.Disposals, Is.EqualTo(1));
            Assert.That(Read(source.Distinct().Take(2).ToArrayAsync()), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(source.Disposals, Is.EqualTo(2));
            Assert.That(Read(source.TakeWhile(value => value < 4).Skip(2).FirstAsync()), Is.EqualTo(3));
            Assert.That(source.Disposals, Is.EqualTo(3));
        }

        [Test]
        public void ExplicitDisposeWhilePending_DisposesUpstreamOnce()
        {
            var source = Items(1, 2, 3);
            source.PendingAt = 0;
            source.Pending = new OnityTaskCompletionSource<bool>();
            var iterator = source.TakeLast(2).GetAsyncEnumerator();
            var move = iterator.MoveNextAsync();
            Assert.That(move.IsCompleted, Is.False);
            var dispose = iterator.DisposeAsync();
            source.Pending.TrySetResult(false);
            LinqTestSupport.Wait(() => dispose.IsCompleted && move.IsCompleted);
            Read(dispose);
            Assert.That(Read(move), Is.False);
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void CleanupFailure_TakesPrecedenceOverLateItems()
        {
            var cleanup = new InvalidOperationException("cleanup");
            var source = Items(1, 2, 3);
            source.DisposeFailure = cleanup;
            var iterator = source.TakeWhile(value => value < 3).GetAsyncEnumerator();
            Assert.That(Read(iterator.MoveNextAsync()), Is.True);
            Assert.That(Read(iterator.MoveNextAsync()), Is.True);
            var last = iterator.MoveNextAsync();
            Assert.That(last.IsFaulted, Is.True);
            Assert.That(Catch(() => Read(last)), Is.SameAs(cleanup));
        }
    }
}
