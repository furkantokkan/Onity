using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    public sealed class OnityAsyncEnumerableUntilEditModeTests
    {
        private static async OnityTask AwaitGate(OnityTask gate)
        {
            await gate;
        }

        [Test]
        public void Arguments_AreValidatedSynchronously()
        {
            IOnityAsyncEnumerable<int> missing = null;
            var source = MultiTest.Ints("s", null, 1);
            Func<CancellationToken, OnityTask> factory = null;
            Assert.Throws<ArgumentNullException>(() => missing.TakeUntil(OnityTask.Completed));
            Assert.Throws<ArgumentNullException>(() => missing.TakeUntil(token => OnityTask.Completed));
            Assert.Throws<ArgumentNullException>(() => source.TakeUntil(factory));
            Assert.Throws<ArgumentNullException>(() => missing.SkipUntil(OnityTask.Completed));
            Assert.Throws<ArgumentNullException>(() => source.SkipUntil(factory));
            Assert.Throws<ArgumentNullException>(() => missing.TakeUntilCanceled(CancellationToken.None));
            Assert.Throws<ArgumentNullException>(() => missing.SkipUntilCanceled(CancellationToken.None));
            Assert.That(source.Acquires, Is.Zero);
        }

        // ---- TakeUntil ------------------------------------------------------------------------

        [Test]
        public void TakeUntil_RelaysItemsUntilTheSignal_AndDisposesTheSourceBeforeTheEnd()
        {
            var log = new List<string>();
            var source = MultiTest.Manual("source", log);
            var signal = new OnityTaskCompletionSource();
            IOnityAsyncEnumerator<int> enumerator = source.TakeUntil(signal.Task).GetAsyncEnumerator();

            MultiTestResult<bool> first = MultiTest.Move(enumerator, log, "first");
            Assert.That(first.Completed, Is.False);
            Assert.That(source.HasPendingMove, Is.True);
            source.Push(7);
            Assert.That(first.Value, Is.True);
            Assert.That(enumerator.Current, Is.EqualTo(7));

            MultiTestResult<bool> end = MultiTest.Move(enumerator, log, "end");
            Assert.That(end.Completed, Is.False);
            signal.TrySetResult();
            Assert.That(end.Completed, Is.True);
            Assert.That(end.Value, Is.False);
            Assert.That(end.Error, Is.Null);
            Assert.That(log, Is.EqualTo(new[] { "source.acquire", "first", "source.dispose", "end" }));
            MultiTest.Dispose(enumerator);
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void TakeUntil_CompletedSignal_EndsWithoutAcquiringTheSource()
        {
            var source = MultiTest.Ints("s", null, 1, 2);
            Assert.That(MultiTest.Drain(source.TakeUntil(OnityTask.Completed)), Is.Empty);
            Assert.That(source.Acquires, Is.Zero);
        }

        [Test]
        public void TakeUntil_PendingSignal_RelaysAFiniteSourceCompletely()
        {
            var source = MultiTest.Ints("s", null, 1, 2, 3);
            var signal = new OnityTaskCompletionSource();
            Assert.That(MultiTest.Drain(source.TakeUntil(signal.Task)), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(source.Disposals, Is.EqualTo(1));
            signal.TrySetResult();
        }

        [Test]
        public void TakeUntil_SignalFaultOrCancellation_EndsTheStreamTheSameWay([Values] bool fault)
        {
            var source = MultiTest.Manual("s", null);
            var signal = new OnityTaskCompletionSource();
            var token = new CancellationToken(true);
            IOnityAsyncEnumerator<int> enumerator = source.TakeUntil(signal.Task).GetAsyncEnumerator();
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            if (fault)
            {
                signal.TrySetException(new InvalidOperationException("signal"));
            }
            else
            {
                signal.TrySetCanceled(token);
            }
            Assert.That(move.Completed, Is.True);
            if (fault)
            {
                Assert.That(move.Error, Is.TypeOf<InvalidOperationException>().With.Message.EqualTo("signal"));
            }
            else
            {
                Assert.That(move.Canceled, Is.True);
                Assert.That(move.CanceledToken, Is.EqualTo(token));
            }
            Assert.That(source.Disposals, Is.EqualTo(1));
            MultiTest.Dispose(enumerator);
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void TakeUntil_SourceFault_FaultsAfterDisposingTheSource()
        {
            var log = new List<string>();
            var source = MultiTest.Manual("source", log);
            var signal = new OnityTaskCompletionSource();
            IOnityAsyncEnumerator<int> enumerator = source.TakeUntil(signal.Task).GetAsyncEnumerator();
            MultiTestResult<bool> move = MultiTest.Move(enumerator, log, "fault");
            source.Fail(new FormatException("source"));
            Assert.That(move.Error, Is.TypeOf<FormatException>());
            Assert.That(log, Is.EqualTo(new[] { "source.acquire", "source.dispose", "fault" }));
            signal.TrySetResult();
            MultiTest.Dispose(enumerator);
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void TakeUntil_PrecanceledEnumeration_CancelsWithoutAcquiringTheSource()
        {
            var source = MultiTest.Ints("s", null, 1);
            var signal = new OnityTaskCompletionSource();
            var token = new CancellationToken(true);
            IOnityAsyncEnumerator<int> enumerator = source.TakeUntil(signal.Task).GetAsyncEnumerator(token);
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            Assert.That(move.Canceled, Is.True);
            Assert.That(move.CanceledToken, Is.EqualTo(token));
            Assert.That(source.Acquires, Is.Zero);
            MultiTest.Dispose(enumerator);
        }

        [Test]
        public void TakeUntil_EnumerationTokenCancelsAPendingMove_EvenWhenTheSourceIgnoresIt()
        {
            var source = MultiTest.Manual("s", null);
            source.HonorsToken = false;
            var signal = new OnityTaskCompletionSource();
            using var cts = new CancellationTokenSource();
            IOnityAsyncEnumerator<int> enumerator = source.TakeUntil(signal.Task).GetAsyncEnumerator(cts.Token);
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            Assert.That(move.Completed, Is.False);
            cts.Cancel();
            Assert.That(move.Canceled, Is.True);
            Assert.That(move.CanceledToken, Is.EqualTo(cts.Token));
            Assert.That(source.Disposals, Is.EqualTo(1));
            Assert.That(source.HasPendingMove, Is.False);
            MultiTest.Dispose(enumerator);
        }

        [Test]
        public void TakeUntil_FactorySignal_GetsAnOwnedToken_CanceledByDisposal_AndCleanupWaitsForIt()
        {
            var source = MultiTest.Manual("s", null);
            var signal = new OnityTaskCompletionSource();
            CancellationToken received = default;
            int created = 0;
            var description = source.TakeUntil(token =>
            {
                created++;
                received = token;
                return signal.Task;
            });
            IOnityAsyncEnumerator<int> enumerator = description.GetAsyncEnumerator();
            Assert.That(created, Is.Zero);
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            Assert.That(created, Is.EqualTo(1));
            Assert.That(received.CanBeCanceled, Is.True);
            Assert.That(received.IsCancellationRequested, Is.False);

            MultiTestResult disposal = MultiTestResult.Watch(enumerator.DisposeAsync());
            Assert.That(move.Completed, Is.True);
            Assert.That(move.Value, Is.False);
            Assert.That(received.IsCancellationRequested, Is.True);
            Assert.That(source.Disposals, Is.EqualTo(1));
            Assert.That(disposal.Completed, Is.False);

            signal.TrySetCanceled(received);
            Assert.That(disposal.Completed, Is.True);
            Assert.That(disposal.Error, Is.Null);
        }

        [Test]
        public void TakeUntil_ThrowingFactory_FaultsTheStream()
        {
            var source = MultiTest.Manual("s", null);
            IOnityAsyncEnumerator<int> enumerator = source
                .TakeUntil(token => throw new InvalidOperationException("factory")).GetAsyncEnumerator();
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            Assert.That(move.Error, Is.TypeOf<InvalidOperationException>().With.Message.EqualTo("factory"));
            MultiTest.Dispose(enumerator);
        }

        [Test]
        public void TakeUntil_SingleConsumerSignal_IsSharedByEveryEnumeration()
        {
            var source = MultiTest.Manual("s", null);
            var gate = new OnityTaskCompletionSource();
            IOnityAsyncEnumerable<int> description = source.TakeUntil(AwaitGate(gate.Task));
            IOnityAsyncEnumerator<int> first = description.GetAsyncEnumerator();
            MultiTestResult<bool> firstMove = MultiTest.Move(first);
            IOnityAsyncEnumerator<int> second = description.GetAsyncEnumerator();
            MultiTestResult<bool> secondMove = MultiTest.Move(second);
            gate.TrySetResult();
            Assert.That(firstMove.Completed && secondMove.Completed, Is.True);
            Assert.That(firstMove.Value || secondMove.Value, Is.False);
            Assert.That(source.Disposals, Is.EqualTo(2));
            MultiTest.Dispose(first);
            MultiTest.Dispose(second);
        }

        [Test]
        public void TakeUntil_DownstreamEarlyTermination_DisposesTheSourceOnce()
        {
            var source = MultiTest.Ints("s", null, 1, 2, 3);
            var signal = new OnityTaskCompletionSource();
            Assert.That(MultiTest.Drain(source.TakeUntil(signal.Task).Take(1)), Is.EqualTo(new[] { 1 }));
            Assert.That(source.Disposals, Is.EqualTo(1));
            signal.TrySetResult();
        }

        // ---- SkipUntil ------------------------------------------------------------------------

        [Test]
        public void SkipUntil_DoesNotTouchTheSourceBeforeTheSignal_ThenRelaysIt()
        {
            var source = MultiTest.Ints("s", null, 1, 2, 3);
            var signal = new OnityTaskCompletionSource();
            IOnityAsyncEnumerator<int> enumerator = source.SkipUntil(signal.Task).GetAsyncEnumerator();
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            Assert.That(move.Completed, Is.False);
            Assert.That(source.Acquires + source.Moves, Is.Zero);
            signal.TrySetResult();
            Assert.That(move.Value, Is.True);
            Assert.That(enumerator.Current, Is.EqualTo(1));
            MultiTest.ExpectItem(enumerator, 2);
            MultiTest.ExpectItem(enumerator, 3);
            MultiTest.ExpectEnd(enumerator);
            Assert.That(source.Disposals, Is.EqualTo(1));
            MultiTest.Dispose(enumerator);
        }

        [Test]
        public void SkipUntil_CompletedSignal_RelaysEverything()
        {
            var source = MultiTest.Ints("s", null, 4, 5);
            Assert.That(MultiTest.Drain(source.SkipUntil(OnityTask.Completed)), Is.EqualTo(new[] { 4, 5 }));
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void SkipUntil_SignalFault_FaultsWithoutAcquiringTheSource()
        {
            var source = MultiTest.Ints("s", null, 1);
            var signal = new OnityTaskCompletionSource();
            IOnityAsyncEnumerator<int> enumerator = source.SkipUntil(signal.Task).GetAsyncEnumerator();
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            signal.TrySetException(new InvalidOperationException("signal"));
            Assert.That(move.Error, Is.TypeOf<InvalidOperationException>());
            Assert.That(source.Acquires, Is.Zero);
            MultiTest.Dispose(enumerator);
        }

        [Test]
        public void SkipUntil_EnumerationTokenCancelsTheWait()
        {
            var source = MultiTest.Ints("s", null, 1);
            var signal = new OnityTaskCompletionSource();
            using var cts = new CancellationTokenSource();
            IOnityAsyncEnumerator<int> enumerator = source.SkipUntil(signal.Task).GetAsyncEnumerator(cts.Token);
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            cts.Cancel();
            Assert.That(move.Canceled, Is.True);
            Assert.That(move.CanceledToken, Is.EqualTo(cts.Token));
            Assert.That(source.Acquires, Is.Zero);
            MultiTest.Dispose(enumerator);
            signal.TrySetResult();
        }

        [Test]
        public void SkipUntil_DisposeWhileWaiting_EndsTheMove_AndCancelsTheFactoryToken()
        {
            var source = MultiTest.Ints("s", null, 1);
            CancellationToken received = default;
            IOnityAsyncEnumerator<int> enumerator = source.SkipUntil(token =>
            {
                received = token;
                return AwaitGate(OnityTaskCancellation(token));
            }).GetAsyncEnumerator();
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            MultiTestResult disposal = MultiTestResult.Watch(enumerator.DisposeAsync());
            Assert.That(move.Value, Is.False);
            Assert.That(received.IsCancellationRequested, Is.True);
            Assert.That(disposal.Completed, Is.True);
            Assert.That(disposal.Error, Is.Null);
            Assert.That(source.Acquires, Is.Zero);
        }

        // ---- TakeUntilCanceled / SkipUntilCanceled ----------------------------------------------

        [Test]
        public void TakeUntilCanceled_EndsNormallyWhenTheTokenIsCanceled()
        {
            var log = new List<string>();
            var source = MultiTest.Manual("source", log);
            using var cts = new CancellationTokenSource();
            IOnityAsyncEnumerator<int> enumerator = source.TakeUntilCanceled(cts.Token).GetAsyncEnumerator();
            MultiTestResult<bool> first = MultiTest.Move(enumerator);
            source.Push(3);
            Assert.That(first.Value, Is.True);
            MultiTestResult<bool> end = MultiTest.Move(enumerator, log, "end");
            cts.Cancel();
            Assert.That(end.Completed, Is.True);
            Assert.That(end.Canceled, Is.False);
            Assert.That(end.Value, Is.False);
            Assert.That(source.Token.IsCancellationRequested, Is.False);
            Assert.That(log, Is.EqualTo(new[] { "source.acquire", "source.dispose", "end" }));
            MultiTest.Dispose(enumerator);
        }

        [Test]
        public void TakeUntilCanceled_PrecanceledToken_IsEmptyWithoutAcquiringTheSource()
        {
            var source = MultiTest.Ints("s", null, 1);
            Assert.That(MultiTest.Drain(source.TakeUntilCanceled(new CancellationToken(true))), Is.Empty);
            Assert.That(source.Acquires, Is.Zero);
        }

        [Test]
        public void TakeUntilCanceled_EnumerationTokenAlsoEndsNormally_WhenTheSourceCancels()
        {
            var source = MultiTest.Manual("s", null);
            using var cts = new CancellationTokenSource();
            IOnityAsyncEnumerator<int> enumerator = source.TakeUntilCanceled(CancellationToken.None)
                .GetAsyncEnumerator(cts.Token);
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            Assert.That(source.Token, Is.EqualTo(cts.Token));
            cts.Cancel();
            Assert.That(move.Completed, Is.True);
            Assert.That(move.Canceled, Is.False);
            Assert.That(move.Error, Is.Null);
            Assert.That(move.Value, Is.False);
            MultiTest.Dispose(enumerator);
        }

        [Test]
        public void TakeUntilCanceled_SourceFault_StillFaults()
        {
            var source = MultiTest.Manual("s", null);
            using var cts = new CancellationTokenSource();
            IOnityAsyncEnumerator<int> enumerator = source.TakeUntilCanceled(cts.Token).GetAsyncEnumerator();
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            source.Fail(new FormatException());
            Assert.That(move.Error, Is.TypeOf<FormatException>());
            MultiTest.Dispose(enumerator);
        }

        [Test]
        public void SkipUntilCanceled_HoldsBackTheSourceUntilTheTokenIsCanceled()
        {
            var source = MultiTest.Ints("s", null, 1, 2);
            using var cts = new CancellationTokenSource();
            IOnityAsyncEnumerator<int> enumerator = source.SkipUntilCanceled(cts.Token).GetAsyncEnumerator();
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            Assert.That(move.Completed, Is.False);
            Assert.That(source.Acquires, Is.Zero);
            cts.Cancel();
            Assert.That(move.Value, Is.True);
            Assert.That(enumerator.Current, Is.EqualTo(1));
            MultiTest.ExpectItem(enumerator, 2);
            MultiTest.ExpectEnd(enumerator);
            MultiTest.Dispose(enumerator);
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void SkipUntilCanceled_PrecanceledToken_RelaysImmediately_AndEnumerationTokenCancels()
        {
            var source = MultiTest.Ints("s", null, 1, 2);
            Assert.That(MultiTest.Drain(source.SkipUntilCanceled(new CancellationToken(true))),
                Is.EqualTo(new[] { 1, 2 }));

            using var operatorToken = new CancellationTokenSource();
            using var enumerationToken = new CancellationTokenSource();
            IOnityAsyncEnumerator<int> enumerator = source.SkipUntilCanceled(operatorToken.Token)
                .GetAsyncEnumerator(enumerationToken.Token);
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            enumerationToken.Cancel();
            Assert.That(move.Canceled, Is.True);
            Assert.That(move.CanceledToken, Is.EqualTo(enumerationToken.Token));
            MultiTest.Dispose(enumerator);
        }

        private static OnityTask OnityTaskCancellation(CancellationToken token)
        {
            var source = new OnityTaskCompletionSource();
            token.Register(() => source.TrySetCanceled(token));
            return source.Task;
        }
    }
}
