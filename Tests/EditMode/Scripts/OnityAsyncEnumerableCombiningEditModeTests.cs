using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    public sealed class OnityAsyncEnumerableCombiningEditModeTests
    {
        [Test]
        public void Arguments_AreValidatedSynchronously()
        {
            IOnityAsyncEnumerable<int> missing = null;
            var source = MultiTest.Ints("s", null, 1);
            Func<int, int, int> selector = null;
            Func<int, int, OnityTask<int>> awaitSelector = null;
            Func<int, int, CancellationToken, OnityTask<int>> cancelSelector = null;
            Assert.Throws<ArgumentNullException>(() => missing.Append(1));
            Assert.Throws<ArgumentNullException>(() => missing.Prepend(1));
            Assert.Throws<ArgumentNullException>(() => missing.Concat(source));
            Assert.Throws<ArgumentNullException>(() => source.Concat(missing));
            Assert.Throws<ArgumentNullException>(() => missing.Merge(source));
            Assert.Throws<ArgumentNullException>(() => source.Merge(source, missing));
            Assert.Throws<ArgumentNullException>(() => ((IEnumerable<IOnityAsyncEnumerable<int>>)null).Merge());
            Assert.Throws<ArgumentNullException>(() => OnityAsyncEnumerable.Merge<int>(null));
            Assert.Throws<ArgumentException>(() => OnityAsyncEnumerable.Merge<int>());
            Assert.Throws<ArgumentException>(() => OnityAsyncEnumerable.Merge(source, missing));
            Assert.Throws<ArgumentException>(() => new List<IOnityAsyncEnumerable<int>>().Merge());
            Assert.Throws<ArgumentNullException>(() => missing.Zip(source));
            Assert.Throws<ArgumentNullException>(() => source.Zip(missing));
            Assert.Throws<ArgumentNullException>(() => source.Zip(source, selector));
            Assert.Throws<ArgumentNullException>(() => source.ZipAwait(source, awaitSelector));
            Assert.Throws<ArgumentNullException>(() => source.ZipAwaitWithCancellation(source, cancelSelector));
            Assert.That(source.Acquires, Is.Zero);
        }

        // ---- Append / Prepend -------------------------------------------------------------------

        [Test]
        public void Append_YieldsTheItemAfterTheSource_AfterDisposingIt()
        {
            var log = new List<string>();
            var source = MultiTest.Ints("source", log, 1, 2);
            IOnityAsyncEnumerator<int> enumerator = source.Append(3).GetAsyncEnumerator();
            MultiTest.ExpectItem(enumerator, 1);
            MultiTest.ExpectItem(enumerator, 2);
            MultiTestResult<bool> last = MultiTest.Move(enumerator, log, "appended");
            Assert.That(last.Value, Is.True);
            Assert.That(enumerator.Current, Is.EqualTo(3));
            Assert.That(log, Is.EqualTo(new[] { "source.acquire", "source.dispose", "appended" }));
            MultiTest.ExpectEnd(enumerator);
            MultiTest.Dispose(enumerator);
            Assert.That(source.Disposals, Is.EqualTo(1));
            Assert.That(MultiTest.Drain(MultiTest.Ints("e", null).Append(9)), Is.EqualTo(new[] { 9 }));
        }

        [Test]
        public void Append_SourceFault_FaultsWithoutTheAppendedItem()
        {
            var source = MultiTest.Ints("s", null, 1);
            source.MoveFailureAt = 1;
            source.MoveFailure = new FormatException();
            IOnityAsyncEnumerator<int> enumerator = source.Append(5).GetAsyncEnumerator();
            MultiTest.ExpectItem(enumerator, 1);
            Assert.That(MultiTest.Move(enumerator).Error, Is.TypeOf<FormatException>());
            MultiTest.Dispose(enumerator);
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void Prepend_YieldsTheItemFirst_AndAcquiresTheSourceLazily()
        {
            var source = MultiTest.Ints("s", null, 1, 2);
            IOnityAsyncEnumerator<int> enumerator = source.Prepend(0).GetAsyncEnumerator();
            MultiTest.ExpectItem(enumerator, 0);
            Assert.That(source.Acquires, Is.Zero);
            MultiTest.ExpectItem(enumerator, 1);
            MultiTest.ExpectItem(enumerator, 2);
            MultiTest.ExpectEnd(enumerator);
            MultiTest.Dispose(enumerator);
            Assert.That(source.Disposals, Is.EqualTo(1));

            var untouched = MultiTest.Ints("u", null, 1);
            Assert.That(MultiTest.Drain(untouched.Prepend(4).Take(1)), Is.EqualTo(new[] { 4 }));
            Assert.That(untouched.Acquires, Is.Zero);
        }

        [Test]
        public void Prepend_PrecanceledToken_CancelsTheFirstMove()
        {
            var token = new CancellationToken(true);
            IOnityAsyncEnumerator<int> enumerator = MultiTest.Ints("s", null, 1).Prepend(0).GetAsyncEnumerator(token);
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            Assert.That(move.Canceled, Is.True);
            Assert.That(move.CanceledToken, Is.EqualTo(token));
            MultiTest.Dispose(enumerator);
        }

        // ---- Concat -------------------------------------------------------------------------------

        [Test]
        public void Concat_DisposesTheFirstSourceBeforeAcquiringTheSecond()
        {
            var log = new List<string>();
            var first = MultiTest.Ints("first", log, 1, 2);
            var second = MultiTest.Ints("second", log, 3);
            Assert.That(MultiTest.Drain(first.Concat(second)), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(log, Is.EqualTo(new[] { "first.acquire", "first.dispose", "second.acquire", "second.dispose" }));
            Assert.That(MultiTest.Drain(MultiTest.Ints("a", null).Concat(MultiTest.Ints("b", null, 5))),
                Is.EqualTo(new[] { 5 }));
            Assert.That(MultiTest.Drain(MultiTest.Ints("a", null, 5).Concat(MultiTest.Ints("b", null))),
                Is.EqualTo(new[] { 5 }));
        }

        [Test]
        public void Concat_FirstFault_NeverAcquiresTheSecond()
        {
            var first = MultiTest.Ints("first", null, 1);
            first.MoveFailureAt = 1;
            first.MoveFailure = new FormatException();
            var second = MultiTest.Ints("second", null, 2);
            IOnityAsyncEnumerator<int> enumerator = first.Concat(second).GetAsyncEnumerator();
            MultiTest.ExpectItem(enumerator, 1);
            Assert.That(MultiTest.Move(enumerator).Error, Is.TypeOf<FormatException>());
            MultiTest.Dispose(enumerator);
            Assert.That(first.Disposals, Is.EqualTo(1));
            Assert.That(second.Acquires, Is.Zero);
        }

        [Test]
        public void Concat_FirstDisposalFailure_FaultsAndNeverAcquiresTheSecond()
        {
            var first = MultiTest.Ints("first", null, 1);
            first.DisposeFailure = new InvalidOperationException("dispose");
            var second = MultiTest.Ints("second", null, 2);
            IOnityAsyncEnumerator<int> enumerator = first.Concat(second).GetAsyncEnumerator();
            MultiTest.ExpectItem(enumerator, 1);
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            Assert.That(move.Error, Is.TypeOf<InvalidOperationException>().With.Message.EqualTo("dispose"));
            Assert.That(first.Disposals, Is.EqualTo(1));
            Assert.That(second.Acquires, Is.Zero);
        }

        [Test]
        public void Concat_DisposeDuringTheSecond_DisposesOnlyTheSecondAgain()
        {
            var first = MultiTest.Ints("first", null, 1);
            var second = MultiTest.Manual("second", null);
            IOnityAsyncEnumerator<int> enumerator = first.Concat(second).GetAsyncEnumerator();
            MultiTest.ExpectItem(enumerator, 1);
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            Assert.That(move.Completed, Is.False);
            MultiTest.Dispose(enumerator);
            Assert.That(move.Value, Is.False);
            Assert.That(first.Disposals, Is.EqualTo(1));
            Assert.That(second.Disposals, Is.EqualTo(1));
        }

        // ---- Merge --------------------------------------------------------------------------------

        [Test]
        public void Merge_SynchronousSources_InterleaveOneItemPerSourcePerRound()
        {
            var first = MultiTest.Ints("first", null, 1, 2, 3);
            var second = MultiTest.Ints("second", null, 10, 11, 12);
            Assert.That(MultiTest.Drain(first.Merge(second)), Is.EqualTo(new[] { 1, 10, 2, 11, 3, 12 }));
            Assert.That(first.Disposals + second.Disposals, Is.EqualTo(2));
            Assert.That(MultiTest.Drain(OnityAsyncEnumerable.Merge(MultiTest.Ints("a", null, 1),
                MultiTest.Ints("b", null), MultiTest.Ints("c", null, 2, 3))), Is.EqualTo(new[] { 1, 2, 3 }));
            var list = new List<IOnityAsyncEnumerable<int>> { MultiTest.Ints("a", null, 4), MultiTest.Ints("b", null, 5) };
            Assert.That(MultiTest.Drain(list.Merge()), Is.EqualTo(new[] { 4, 5 }));
        }

        [Test]
        public void Merge_SnapshotsTheSourceArray()
        {
            var sources = new IOnityAsyncEnumerable<int>[] { MultiTest.Ints("a", null, 1) };
            IOnityAsyncEnumerable<int> merged = OnityAsyncEnumerable.Merge(sources);
            sources[0] = MultiTest.Ints("b", null, 2);
            Assert.That(MultiTest.Drain(merged), Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void Merge_PendingSources_RelayInArrivalOrder_WithOneOutstandingMovePerSource()
        {
            var first = MultiTest.Manual("first", null);
            var second = MultiTest.Manual("second", null);
            IOnityAsyncEnumerator<int> enumerator = first.Merge(second).GetAsyncEnumerator();
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            Assert.That(first.Moves + second.Moves, Is.EqualTo(2));
            second.Push(20);
            Assert.That(move.Value, Is.True);
            Assert.That(enumerator.Current, Is.EqualTo(20));

            first.Push(1);
            Assert.That(first.Moves + second.Moves, Is.EqualTo(2));
            MultiTest.ExpectItem(enumerator, 1);
            Assert.That(first.Moves + second.Moves, Is.EqualTo(2), "a queued item is relayed without new moves");

            MultiTestResult<bool> pending = MultiTest.Move(enumerator);
            Assert.That(first.Moves, Is.EqualTo(2));
            Assert.That(second.Moves, Is.EqualTo(2));
            first.End();
            Assert.That(pending.Completed, Is.False);
            second.Push(21);
            Assert.That(pending.Value, Is.True);
            Assert.That(enumerator.Current, Is.EqualTo(21));
            MultiTestResult<bool> end = MultiTest.Move(enumerator);
            Assert.That(first.Moves, Is.EqualTo(2), "an ended source is never moved again");
            second.End();
            Assert.That(end.Completed, Is.True);
            Assert.That(end.Value, Is.False);
            Assert.That(first.Disposals + second.Disposals, Is.EqualTo(2));
            MultiTest.Dispose(enumerator);
        }

        [Test]
        public void Merge_Fault_IsRelayedAfterEarlierItems_AndDisposesEverySourceInOrderFirst()
        {
            var log = new List<string>();
            var a = MultiTest.Manual("a", log);
            var b = MultiTest.Manual("b", log);
            var c = MultiTest.Manual("c", log);
            IOnityAsyncEnumerator<int> enumerator = OnityAsyncEnumerable.Merge(a, b, c).GetAsyncEnumerator();
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            c.Push(9);
            Assert.That(move.Value, Is.True);
            a.Push(1);
            b.Fail(new FormatException("b"));
            MultiTest.ExpectItem(enumerator, 1);
            MultiTestResult<bool> fault = MultiTest.Move(enumerator, log, "fault");
            Assert.That(fault.Error, Is.TypeOf<FormatException>().With.Message.EqualTo("b"));
            Assert.That(log, Is.EqualTo(new[]
            {
                "a.acquire", "b.acquire", "c.acquire", "a.dispose", "b.dispose", "c.dispose", "fault"
            }));
            Assert.That(c.Moves, Is.EqualTo(1), "no source is moved after the fault arrived");
            MultiTest.Dispose(enumerator);
            Assert.That(a.Disposals + b.Disposals + c.Disposals, Is.EqualTo(3));
        }

        [Test]
        public void Merge_SecondFault_IsObservedAndDropped()
        {
            var a = MultiTest.Manual("a", null);
            var b = MultiTest.Manual("b", null);
            a.SettlesOnDispose = false;
            IOnityAsyncEnumerator<int> enumerator = a.Merge(b).GetAsyncEnumerator();
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            b.Fail(new FormatException("first"));
            Assert.That(move.Completed, Is.False, "cleanup waits for the outstanding move of a");
            a.Fail(new InvalidOperationException("second"));
            Assert.That(move.Error, Is.TypeOf<FormatException>());
            MultiTest.Dispose(enumerator);
        }

        [Test]
        public void Merge_DisposeWhilePending_SettlesEveryMoveAndDisposesOnce()
        {
            var a = MultiTest.Manual("a", null);
            var b = MultiTest.Manual("b", null);
            IOnityAsyncEnumerator<int> enumerator = a.Merge(b).GetAsyncEnumerator();
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            MultiTest.Dispose(enumerator);
            Assert.That(move.Value, Is.False);
            Assert.That(a.Disposals, Is.EqualTo(1));
            Assert.That(b.Disposals, Is.EqualTo(1));
            Assert.That(a.HasPendingMove || b.HasPendingMove, Is.False);
            MultiTest.Dispose(enumerator);
            Assert.That(a.Disposals + b.Disposals, Is.EqualTo(2));
        }

        [Test]
        public void Merge_Cancellation_BeforeAndDuringMoves()
        {
            var token = new CancellationToken(true);
            var idle = MultiTest.Ints("i", null, 1);
            IOnityAsyncEnumerator<int> precanceled = idle.Merge(MultiTest.Ints("j", null, 2)).GetAsyncEnumerator(token);
            Assert.That(MultiTest.Move(precanceled).Canceled, Is.True);
            Assert.That(idle.Acquires, Is.Zero);
            MultiTest.Dispose(precanceled);

            var a = MultiTest.Manual("a", null);
            var b = MultiTest.Manual("b", null);
            using var cts = new CancellationTokenSource();
            IOnityAsyncEnumerator<int> enumerator = a.Merge(b).GetAsyncEnumerator(cts.Token);
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            cts.Cancel();
            Assert.That(move.Canceled, Is.True);
            Assert.That(move.CanceledToken, Is.EqualTo(cts.Token));
            Assert.That(a.Disposals + b.Disposals, Is.EqualTo(2));
            MultiTest.Dispose(enumerator);
        }

        [Test]
        public void Merge_DisposalFailure_TakesPrecedenceInIndexOrder()
        {
            var a = MultiTest.Ints("a", null);
            var b = MultiTest.Ints("b", null);
            a.DisposeFailure = new InvalidOperationException("a");
            b.DisposeFailure = new InvalidOperationException("b");
            OnityTask<int[]> task = a.Merge(b).ToArrayAsync();
            Assert.That(task.IsCompleted, Is.True);
            Exception error = MultiTest.Catch(() => task.GetAwaiter().GetResult());
            Assert.That(error, Is.TypeOf<InvalidOperationException>().With.Message.EqualTo("a"));
            Assert.That(a.Disposals + b.Disposals, Is.EqualTo(2));
        }

        // ---- Zip ----------------------------------------------------------------------------------

        [Test]
        public void Zip_PairsByPosition_AndStopsAtTheShorterSource()
        {
            var first = MultiTest.Ints("first", null, 1, 2, 3);
            var second = new MultiTestStream<string>("second", null, "a", "b");
            (int First, string Second)[] pairs = MultiTest.Drain(first.Zip(second));
            Assert.That(pairs.Length, Is.EqualTo(2));
            Assert.That(pairs[0].First, Is.EqualTo(1));
            Assert.That(pairs[1].Second, Is.EqualTo("b"));
            Assert.That(first.Moves, Is.EqualTo(3));
            Assert.That(second.Moves, Is.EqualTo(3));

            var shortFirst = MultiTest.Ints("short", null, 1);
            var longSecond = MultiTest.Ints("long", null, 5, 6);
            Assert.That(MultiTest.Drain(shortFirst.Zip(longSecond, (x, y) => x + y)), Is.EqualTo(new[] { 6 }));
            Assert.That(longSecond.Moves, Is.EqualTo(1), "the second source is not moved after the first ended");
        }

        [Test]
        public void Zip_DisposesInArgumentOrder_AndSelectorFaultsFault()
        {
            var log = new List<string>();
            var first = MultiTest.Ints("first", log, 1, 2);
            var second = MultiTest.Ints("second", log, 3, 4);
            IOnityAsyncEnumerator<int> enumerator = first.Zip(second, (x, y) =>
                y == 4 ? throw new FormatException() : x * y).GetAsyncEnumerator();
            MultiTest.ExpectItem(enumerator, 3);
            MultiTestResult<bool> fault = MultiTest.Move(enumerator, log, "fault");
            Assert.That(fault.Error, Is.TypeOf<FormatException>());
            Assert.That(log, Is.EqualTo(new[]
            {
                "first.acquire", "second.acquire", "first.dispose", "second.dispose", "fault"
            }));
            MultiTest.Dispose(enumerator);
        }

        [Test]
        public void Zip_SecondFault_FaultsAfterDisposingBoth()
        {
            var first = MultiTest.Ints("first", null, 1, 2);
            var second = MultiTest.Manual("second", null);
            IOnityAsyncEnumerator<int> enumerator = first.Zip(second, (x, y) => x).GetAsyncEnumerator();
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            second.Fail(new FormatException());
            Assert.That(move.Error, Is.TypeOf<FormatException>());
            Assert.That(first.Disposals + second.Disposals, Is.EqualTo(2));
            MultiTest.Dispose(enumerator);
        }

        [Test]
        public void ZipAwait_AwaitsTheSelector()
        {
            var selector = new OnityTaskCompletionSource<int>();
            IOnityAsyncEnumerator<int> enumerator = MultiTest.Ints("a", null, 1)
                .ZipAwait(MultiTest.Ints("b", null, 2), (x, y) => selector.Task).GetAsyncEnumerator();
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            Assert.That(move.Completed, Is.False);
            selector.TrySetResult(42);
            Assert.That(move.Value, Is.True);
            Assert.That(enumerator.Current, Is.EqualTo(42));
            MultiTest.ExpectEnd(enumerator);
            MultiTest.Dispose(enumerator);

            Assert.That(MultiTest.Drain(MultiTest.Ints("a", null, 1, 2).ZipAwait(MultiTest.Ints("b", null, 10, 20),
                (x, y) => OnityTask.FromResult(x + y))), Is.EqualTo(new[] { 11, 22 }));
        }

        [Test]
        public void ZipAwaitWithCancellation_DisposalCancelsTheSelectorToken_AndWaitsForIt()
        {
            var selector = new OnityTaskCompletionSource<int>();
            CancellationToken received = default;
            var a = MultiTest.Ints("a", null, 1);
            var b = MultiTest.Ints("b", null, 2);
            IOnityAsyncEnumerator<int> enumerator = a.ZipAwaitWithCancellation(b, (x, y, token) =>
            {
                received = token;
                return selector.Task;
            }).GetAsyncEnumerator();
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            Assert.That(received.CanBeCanceled, Is.True);
            MultiTestResult disposal = MultiTestResult.Watch(enumerator.DisposeAsync());
            Assert.That(move.Value, Is.False);
            Assert.That(received.IsCancellationRequested, Is.True);
            Assert.That(disposal.Completed, Is.False, "cleanup waits for the pending selector");
            selector.TrySetCanceled(received);
            Assert.That(disposal.Completed, Is.True);
            Assert.That(disposal.Error, Is.Null);
            Assert.That(a.Disposals + b.Disposals, Is.EqualTo(2));
        }
    }
}
