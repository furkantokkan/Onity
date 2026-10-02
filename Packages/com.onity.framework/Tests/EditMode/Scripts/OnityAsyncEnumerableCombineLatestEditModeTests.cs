using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    public sealed class OnityAsyncEnumerableCombineLatestEditModeTests
    {
        [Test]
        public void Arguments_AreValidatedSynchronously()
        {
            IOnityAsyncEnumerable<int> missing = null;
            var source = MultiTest.Ints("s", null, 1);
            Func<int, int, int> selector = null;
            Assert.Throws<ArgumentNullException>(() => missing.CombineLatest(source, (x, y) => x));
            Assert.Throws<ArgumentNullException>(() => source.CombineLatest(missing, (x, y) => x));
            Assert.Throws<ArgumentNullException>(() => source.CombineLatest(source, selector));
            Assert.Throws<ArgumentNullException>(() => source.CombineLatest(source, missing, (x, y, z) => x));
            Assert.That(source.Acquires, Is.Zero);
        }

        [Test]
        public void SynchronousSources_UseTheLatestItemUntilAllProduced_ThenOneResultPerItem()
        {
            var first = MultiTest.Ints("first", null, 1, 2, 3);
            var second = MultiTest.Ints("second", null, 10, 11, 12);
            Assert.That(MultiTest.Drain(first.CombineLatest(second, (x, y) => x * 100 + y)),
                Is.EqualTo(new[] { 310, 311, 312 }));
            Assert.That(first.Disposals + second.Disposals, Is.EqualTo(2));
        }

        [Test]
        public void PendingSources_RelayEveryArrival_AndMoveAgainOnlyOnLaterMoves()
        {
            var first = MultiTest.Manual("first", null);
            var second = MultiTest.Manual("second", null);
            IOnityAsyncEnumerator<int> enumerator = first.CombineLatest(second, (x, y) => x * 100 + y)
                .GetAsyncEnumerator();
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            first.Push(1);
            Assert.That(move.Completed, Is.False);
            Assert.That(first.Moves, Is.EqualTo(2), "a source without a partner is moved again at once");
            second.Push(10);
            Assert.That(move.Value, Is.True);
            Assert.That(enumerator.Current, Is.EqualTo(110));

            first.Push(2);
            MultiTest.ExpectItem(enumerator, 210);
            Assert.That(first.Moves + second.Moves, Is.EqualTo(3), "a queued result needs no new move");

            MultiTestResult<bool> next = MultiTest.Move(enumerator);
            Assert.That(first.Moves, Is.EqualTo(3));
            Assert.That(second.Moves, Is.EqualTo(2));
            second.Push(11);
            Assert.That(next.Value, Is.True);
            Assert.That(enumerator.Current, Is.EqualTo(211));
            MultiTest.Dispose(enumerator);
            Assert.That(first.Disposals + second.Disposals, Is.EqualTo(2));
            Assert.That(first.HasPendingMove || second.HasPendingMove, Is.False);
        }

        [Test]
        public void ResultsArrivingBetweenMoves_AreQueuedInArrivalOrder_NotConflated()
        {
            var first = MultiTest.Manual("first", null, 1);
            var second = MultiTest.Manual("second", null, 10);
            IOnityAsyncEnumerator<int> enumerator = first.CombineLatest(second, (x, y) => x * 100 + y)
                .GetAsyncEnumerator();
            MultiTest.ExpectItem(enumerator, 110);
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            first.Push(2);
            Assert.That(move.Value, Is.True);
            Assert.That(enumerator.Current, Is.EqualTo(210));
            second.Push(20);
            MultiTest.ExpectItem(enumerator, 220);
            MultiTest.Dispose(enumerator);
        }

        [Test]
        public void ASourceEndingEmpty_EndsTheStreamAtOnce()
        {
            var log = new List<string>();
            var first = MultiTest.Manual("first", log);
            var second = MultiTest.Manual("second", log);
            IOnityAsyncEnumerator<int> enumerator = first.CombineLatest(second, (x, y) => x + y).GetAsyncEnumerator();
            MultiTestResult<bool> move = MultiTest.Move(enumerator, log, "end");
            first.End();
            Assert.That(move.Completed, Is.True);
            Assert.That(move.Value, Is.False);
            Assert.That(log, Is.EqualTo(new[]
            {
                "first.acquire", "second.acquire", "first.dispose", "second.dispose", "end"
            }));
            MultiTest.Dispose(enumerator);
        }

        [Test]
        public void EndsAfterEverySourceEnded_WhenEachProducedAnItem()
        {
            var first = MultiTest.Manual("first", null, 1);
            var second = MultiTest.Manual("second", null, 2);
            IOnityAsyncEnumerator<int> enumerator = first.CombineLatest(second, (x, y) => x + y).GetAsyncEnumerator();
            MultiTest.ExpectItem(enumerator, 3);
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            first.End();
            Assert.That(move.Completed, Is.False);
            second.Push(5);
            Assert.That(move.Value, Is.True);
            Assert.That(enumerator.Current, Is.EqualTo(6));
            MultiTestResult<bool> end = MultiTest.Move(enumerator);
            second.End();
            Assert.That(end.Value, Is.False);
            Assert.That(end.Completed, Is.True);
            MultiTest.Dispose(enumerator);
        }

        [Test]
        public void Faults_FromEitherSourceOrTheSelector_FaultAfterDisposingEverySource([Values(0, 1, 2)] int origin)
        {
            var log = new List<string>();
            var first = MultiTest.Manual("first", log, 1);
            var second = MultiTest.Manual("second", log, 2);
            IOnityAsyncEnumerator<int> enumerator = first.CombineLatest(second, (x, y) =>
                origin == 2 && x == 3 ? throw new OperationCanceledException() : x + y).GetAsyncEnumerator();
            MultiTest.ExpectItem(enumerator, 3);
            MultiTestResult<bool> move = MultiTest.Move(enumerator, log, "fault");
            if (origin == 0)
            {
                first.Fail(new FormatException());
            }
            else if (origin == 1)
            {
                second.Fail(new FormatException());
            }
            else
            {
                first.Push(3);
            }
            Assert.That(move.Completed, Is.True);
            Assert.That(move.Canceled, Is.False, "a selector exception is a fault, even an OCE");
            Assert.That(move.Error, origin == 2 ? Is.TypeOf<OperationCanceledException>() : Is.TypeOf<FormatException>());
            Assert.That(log, Is.EqualTo(new[]
            {
                "first.acquire", "second.acquire", "first.dispose", "second.dispose", "fault"
            }));
            MultiTest.Dispose(enumerator);
        }

        [Test]
        public void Cancellation_BeforeAndDuringMoves()
        {
            var idle = MultiTest.Ints("idle", null, 1);
            var token = new CancellationToken(true);
            IOnityAsyncEnumerator<int> precanceled = idle.CombineLatest(MultiTest.Ints("other", null, 2), (x, y) => x)
                .GetAsyncEnumerator(token);
            MultiTestResult<bool> canceled = MultiTest.Move(precanceled);
            Assert.That(canceled.Canceled, Is.True);
            Assert.That(idle.Acquires, Is.Zero);
            MultiTest.Dispose(precanceled);

            var first = MultiTest.Manual("first", null);
            var second = MultiTest.Manual("second", null);
            using var cts = new CancellationTokenSource();
            IOnityAsyncEnumerator<int> enumerator = first.CombineLatest(second, (x, y) => x)
                .GetAsyncEnumerator(cts.Token);
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            cts.Cancel();
            Assert.That(move.Canceled, Is.True);
            Assert.That(move.CanceledToken, Is.EqualTo(cts.Token));
            Assert.That(first.Disposals + second.Disposals, Is.EqualTo(2));
            MultiTest.Dispose(enumerator);
        }

        [Test]
        public void HigherArities_CombineEverySource()
        {
            IOnityAsyncEnumerable<int> one = OnityAsyncEnumerable.Return(1);
            Assert.That(MultiTest.Drain(one.CombineLatest(OnityAsyncEnumerable.Return("x"), OnityAsyncEnumerable.Return('y'),
                (a, b, c) => a + b + c)), Is.EqualTo(new[] { "1xy" }));
            Assert.That(MultiTest.Drain(one.CombineLatest(one, one, one, one, one, one, one, one, one, one, one, one, one,
                OnityAsyncEnumerable.Return(100),
                (a, b, c, d, e, f, g, h, i, j, k, l, m, n, o) => a + b + c + d + e + f + g + h + i + j + k + l + m + n + o)),
                Is.EqualTo(new[] { 114 }));
        }
    }
}
