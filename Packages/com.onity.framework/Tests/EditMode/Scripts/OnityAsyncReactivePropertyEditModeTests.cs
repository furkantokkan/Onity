using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.TestTools;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class OnityAsyncReactivePropertyEditModeTests
    {
        private const int k_waitMilliseconds = 5000;

        private sealed class Probe<T>
        {
            internal bool Completed;
            internal int Calls;
            internal T Result;
            internal Exception Error;

            internal bool IsCanceled => Error is OperationCanceledException;

            internal static Probe<T> Observe(OnityTask<T> task)
            {
                Probe<T> probe = new Probe<T>();
                OnityTaskAwaiter<T> awaiter = task.GetAwaiter();
                if (awaiter.IsCompleted)
                {
                    probe.Capture(awaiter);
                }
                else
                {
                    awaiter.OnCompleted(() => probe.Capture(awaiter));
                }

                return probe;
            }

            private void Capture(OnityTaskAwaiter<T> awaiter)
            {
                Calls++;
                try
                {
                    Result = awaiter.GetResult();
                }
                catch (Exception exception)
                {
                    Error = exception;
                }

                Completed = true;
            }
        }

        private static Probe<bool> Move<T>(IOnityAsyncEnumerator<T> enumerator)
        {
            return Probe<bool>.Observe(enumerator.MoveNextAsync());
        }

        private static void Dispose<T>(IOnityAsyncEnumerator<T> enumerator)
        {
            enumerator.DisposeAsync().GetAwaiter().GetResult();
        }

        private static void WaitUntil(Func<bool> condition)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(k_waitMilliseconds);
            while (!condition() && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(1);
            }

            Assert.That(condition(), Is.True, "Condition was not met in time.");
        }

        // Awaits the next value, then sets the property from the continuation. The publication that
        // resumed it is still running, so the set is rejected; the rejection is returned (null if none).
        private static async OnityTask<Exception> SetFromWaiterContinuationAsync(
            OnityAsyncReactiveProperty<int> property)
        {
            int value = await property.WaitAsync();
            try
            {
                property.Value = value + 1;
                return null;
            }
            catch (InvalidOperationException exception)
            {
                return exception;
            }
        }

        // Same as above, resumed by an enumerator move instead of a WaitAsync waiter.
        private static async OnityTask<Exception> SetFromEnumeratorContinuationAsync(
            OnityAsyncReactiveProperty<int> property)
        {
            IOnityAsyncEnumerator<int> enumerator = property.WithoutCurrent().GetAsyncEnumerator();
            try
            {
                if (await enumerator.MoveNextAsync())
                {
                    property.Value = enumerator.Current + 1;
                }

                return null;
            }
            catch (InvalidOperationException exception)
            {
                return exception;
            }
            finally
            {
                await enumerator.DisposeAsync();
            }
        }

        // WaitAsync

        [Test]
        public void SetValue_WakesEveryCurrentWaiter_ExactlyOnce()
        {
            OnityAsyncReactiveProperty<int> property = new OnityAsyncReactiveProperty<int>(0);
            Probe<int> first = Probe<int>.Observe(property.WaitAsync());
            Probe<int> second = Probe<int>.Observe(property.WaitAsync());
            Probe<int> third = Probe<int>.Observe(property.WaitAsync());
            Assert.That(first.Completed || second.Completed || third.Completed, Is.False);

            property.Value = 5;

            Assert.That(first.Result, Is.EqualTo(5));
            Assert.That(second.Result, Is.EqualTo(5));
            Assert.That(third.Result, Is.EqualTo(5));

            property.Value = 6;

            Assert.That(first.Calls, Is.EqualTo(1));
            Assert.That(second.Calls, Is.EqualTo(1));
            Assert.That(third.Calls, Is.EqualTo(1));
            Assert.That(first.Result, Is.EqualTo(5));
        }

        [Test]
        public void SetValue_DoesNotCompleteWaitersCreatedAfterIt()
        {
            OnityAsyncReactiveProperty<int> property = new OnityAsyncReactiveProperty<int>(0);
            property.Value = 1;

            Probe<int> waiter = Probe<int>.Observe(property.WaitAsync());

            Assert.That(waiter.Completed, Is.False);
            property.Value = 2;
            Assert.That(waiter.Result, Is.EqualTo(2));
        }

        [Test]
        public void SetValue_PublishesEqualValuesToo_NoEqualitySkip()
        {
            OnityAsyncReactiveProperty<int> property = new OnityAsyncReactiveProperty<int>(7);
            Probe<int> waiter = Probe<int>.Observe(property.WaitAsync());

            property.Value = 7;

            Assert.That(waiter.Completed, Is.True);
            Assert.That(waiter.Result, Is.EqualTo(7));

            IOnityAsyncEnumerator<int> enumerator = property.WithoutCurrent().GetAsyncEnumerator();
            Probe<bool> move = Move(enumerator);
            property.Value = 7;
            Assert.That(move.Result, Is.True);
            Assert.That(enumerator.Current, Is.EqualTo(7));
            Dispose(enumerator);
        }

        [Test]
        public void SetValue_WithNoSubscribers_StoresValue()
        {
            OnityAsyncReactiveProperty<string> property = new OnityAsyncReactiveProperty<string>("a");

            property.Value = "b";

            Assert.That(property.Value, Is.EqualTo("b"));
            string implicitValue = property;
            Assert.That(implicitValue, Is.EqualTo("b"));
        }

        [Test]
        public void SetValue_WithNothingObserving_CreatesNoPublicationState()
        {
            OnityAsyncReactiveProperty<int> property = new OnityAsyncReactiveProperty<int>(0);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 1; i <= 100; i++)
            {
                property.Value = i;
            }

            property.Dispose();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.EqualTo(0));
            Assert.That(property.Value, Is.EqualTo(100));

            // The first subscriber creates the publication state and later sets reach it.
            Probe<int> waiter = Probe<int>.Observe(property.WaitAsync());
            property.Value = 101;
            Assert.That(waiter.Result, Is.EqualTo(101));
        }

        [Test]
        public void WaitAsync_PreCanceledToken_IsCanceledWithThatToken()
        {
            OnityAsyncReactiveProperty<int> property = new OnityAsyncReactiveProperty<int>(0);
            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                source.Cancel();

                OperationCanceledException exception = Assert.Throws<OperationCanceledException>(
                    () => property.WaitAsync(source.Token).GetAwaiter().GetResult());

                Assert.That(exception.CancellationToken, Is.EqualTo(source.Token));
            }

            property.Value = 1;
        }

        [Test]
        public void WaitAsync_CancelDuringWait_CancelsOnlyThatWaiter()
        {
            OnityAsyncReactiveProperty<int> property = new OnityAsyncReactiveProperty<int>(0);
            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                Probe<int> canceled = Probe<int>.Observe(property.WaitAsync(source.Token));
                Probe<int> survivor = Probe<int>.Observe(property.WaitAsync());

                source.Cancel();

                Assert.That(canceled.IsCanceled, Is.True);
                Assert.That(((OperationCanceledException)canceled.Error).CancellationToken, Is.EqualTo(source.Token));
                Assert.That(survivor.Completed, Is.False);

                property.Value = 3;

                Assert.That(survivor.Result, Is.EqualTo(3));
                Assert.That(canceled.Calls, Is.EqualTo(1));
            }
        }

        [Test]
        public void WaitAsync_CancelableToken_CompletesWithValueAndIgnoresLaterCancel()
        {
            OnityAsyncReactiveProperty<int> property = new OnityAsyncReactiveProperty<int>(0);
            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                Probe<int> waiter = Probe<int>.Observe(property.WaitAsync(source.Token));

                property.Value = 4;
                source.Cancel();

                Assert.That(waiter.Result, Is.EqualTo(4));
                Assert.That(waiter.Error, Is.Null);
                Assert.That(waiter.Calls, Is.EqualTo(1));
            }
        }

        [Test]
        public void WaitAsync_CancelFromAnotherThread_CancelsWaiter()
        {
            OnityAsyncReactiveProperty<int> property = new OnityAsyncReactiveProperty<int>(0);
            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                Probe<int> waiter = Probe<int>.Observe(property.WaitAsync(source.Token));

                Task.Run(() => source.Cancel()).Wait(k_waitMilliseconds);
                WaitUntil(() => waiter.Completed);

                Assert.That(waiter.IsCanceled, Is.True);
            }
        }

        [Test]
        public void WaitAsync_PooledWaitersAreReusedAcrossManyCycles()
        {
            OnityAsyncReactiveProperty<int> property = new OnityAsyncReactiveProperty<int>(0);
            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                for (int cycle = 1; cycle <= 1000; cycle++)
                {
                    Probe<int> plain = Probe<int>.Observe(property.WaitAsync());
                    Probe<int> cancelable = Probe<int>.Observe(property.WaitAsync(source.Token));
                    property.Value = cycle;
                    Assert.That(plain.Result, Is.EqualTo(cycle));
                    Assert.That(cancelable.Result, Is.EqualTo(cycle));
                }

                source.Cancel();
            }
        }

        // Re-entrant set. A publication delivers to its subscribers one after the other, and their
        // continuations run inline. A set from such a continuation would reach the entries that come
        // later in the outer pass after the newer value (out of order), so it is rejected (UniTask
        // parity) before it changes the value or the running pass.

        [Test]
        public void SetValue_FromWaiterCallback_ThrowsAndKeepsTheRunningPublicationInOrder()
        {
            OnityAsyncReactiveProperty<int> property = new OnityAsyncReactiveProperty<int>(0);
            Exception nested = null;
            OnityTaskAwaiter<int> awaiter = property.WaitAsync().GetAwaiter();
            awaiter.OnCompleted(() =>
            {
                try
                {
                    property.Value = awaiter.GetResult() + 1;
                }
                catch (Exception exception)
                {
                    nested = exception;
                }
            });
            Probe<int> later = Probe<int>.Observe(property.WaitAsync());
            IOnityAsyncEnumerator<int> enumerator = property.WithoutCurrent().GetAsyncEnumerator();
            Probe<bool> move = Move(enumerator);

            property.Value = 10;

            Assert.That(nested, Is.InstanceOf<InvalidOperationException>());
            Assert.That(property.Value, Is.EqualTo(10));
            Assert.That(later.Result, Is.EqualTo(10));
            Assert.That(later.Calls, Is.EqualTo(1));
            Assert.That(move.Result, Is.True);
            Assert.That(enumerator.Current, Is.EqualTo(10));

            // The rejected set left the property usable: the next publication runs normally.
            Probe<bool> next = Move(enumerator);
            property.Value = 12;
            Assert.That(next.Result, Is.True);
            Assert.That(enumerator.Current, Is.EqualTo(12));
            Dispose(enumerator);
        }

        [Test]
        public void SetValue_FromAwaitedWaiterContinuation_ThrowsInvalidOperation()
        {
            OnityAsyncReactiveProperty<int> property = new OnityAsyncReactiveProperty<int>(0);
            Probe<Exception> consumer = Probe<Exception>.Observe(SetFromWaiterContinuationAsync(property));
            Assert.That(consumer.Completed, Is.False);

            property.Value = 10;

            Assert.That(consumer.Completed, Is.True);
            Assert.That(consumer.Error, Is.Null);
            Assert.That(consumer.Result, Is.InstanceOf<InvalidOperationException>());
            Assert.That(property.Value, Is.EqualTo(10));
        }

        [Test]
        public void SetValue_FromAwaitedEnumeratorContinuation_ThrowsInvalidOperation()
        {
            OnityAsyncReactiveProperty<int> property = new OnityAsyncReactiveProperty<int>(0);
            Probe<Exception> consumer = Probe<Exception>.Observe(SetFromEnumeratorContinuationAsync(property));
            Assert.That(consumer.Completed, Is.False);

            property.Value = 10;

            Assert.That(consumer.Completed, Is.True);
            Assert.That(consumer.Error, Is.Null);
            Assert.That(consumer.Result, Is.InstanceOf<InvalidOperationException>());
            Assert.That(property.Value, Is.EqualTo(10));
        }

        // Enumeration

        [Test]
        public void Enumeration_YieldsCurrentThenSubsequentValues()
        {
            OnityAsyncReactiveProperty<int> property = new OnityAsyncReactiveProperty<int>(1);
            IOnityAsyncEnumerator<int> enumerator = property.GetAsyncEnumerator();

            Probe<bool> first = Move(enumerator);
            Assert.That(first.Result, Is.True);
            Assert.That(enumerator.Current, Is.EqualTo(1));

            Probe<bool> second = Move(enumerator);
            Assert.That(second.Completed, Is.False);
            property.Value = 2;
            Assert.That(second.Result, Is.True);
            Assert.That(enumerator.Current, Is.EqualTo(2));

            Probe<bool> third = Move(enumerator);
            property.Value = 3;
            Assert.That(third.Result, Is.True);
            Assert.That(enumerator.Current, Is.EqualTo(3));

            Dispose(enumerator);
        }

        [Test]
        public void Enumeration_CurrentValueIsReadAtTheFirstMove()
        {
            OnityAsyncReactiveProperty<int> property = new OnityAsyncReactiveProperty<int>(1);
            IOnityAsyncEnumerator<int> enumerator = property.GetAsyncEnumerator();

            property.Value = 9;
            Probe<bool> first = Move(enumerator);

            Assert.That(first.Result, Is.True);
            Assert.That(enumerator.Current, Is.EqualTo(9));
            Dispose(enumerator);
        }

        [Test]
        public void WithoutCurrent_SkipsCurrentAndYieldsOnlyLaterValues()
        {
            OnityAsyncReactiveProperty<int> property = new OnityAsyncReactiveProperty<int>(1);
            IOnityAsyncEnumerator<int> enumerator = property.WithoutCurrent().GetAsyncEnumerator();

            Probe<bool> first = Move(enumerator);
            Assert.That(first.Completed, Is.False);

            property.Value = 2;

            Assert.That(first.Result, Is.True);
            Assert.That(enumerator.Current, Is.EqualTo(2));
            Dispose(enumerator);
        }

        [Test]
        public void Enumeration_ValueSetWhileNoMoveIsPending_IsNotQueued()
        {
            OnityAsyncReactiveProperty<int> property = new OnityAsyncReactiveProperty<int>(0);
            IOnityAsyncEnumerator<int> enumerator = property.WithoutCurrent().GetAsyncEnumerator();

            property.Value = 1;
            Probe<bool> move = Move(enumerator);

            Assert.That(move.Completed, Is.False);
            property.Value = 2;
            Assert.That(move.Result, Is.True);
            Assert.That(enumerator.Current, Is.EqualTo(2));
            Dispose(enumerator);
        }

        [Test]
        public void Enumeration_EveryEnumeratorReceivesEachValue()
        {
            OnityAsyncReactiveProperty<int> property = new OnityAsyncReactiveProperty<int>(0);
            IOnityAsyncEnumerator<int> a = property.WithoutCurrent().GetAsyncEnumerator();
            IOnityAsyncEnumerator<int> b = property.WithoutCurrent().GetAsyncEnumerator();
            Probe<bool> moveA = Move(a);
            Probe<bool> moveB = Move(b);

            property.Value = 8;

            Assert.That(moveA.Result, Is.True);
            Assert.That(moveB.Result, Is.True);
            Assert.That(a.Current, Is.EqualTo(8));
            Assert.That(b.Current, Is.EqualTo(8));

            Dispose(a);
            Probe<bool> moveB2 = Move(b);
            property.Value = 9;
            Assert.That(moveB2.Result, Is.True);
            Assert.That(b.Current, Is.EqualTo(9));
            Dispose(b);
        }

        [Test]
        public void Enumeration_CurrentWithoutAMoveThrows_AndSecondOutstandingMoveThrows()
        {
            OnityAsyncReactiveProperty<int> property = new OnityAsyncReactiveProperty<int>(0);
            IOnityAsyncEnumerator<int> enumerator = property.WithoutCurrent().GetAsyncEnumerator();

            Assert.Throws<InvalidOperationException>(() => { int unused = enumerator.Current; });
            Probe<bool> move = Move(enumerator);
            Assert.Throws<InvalidOperationException>(() => enumerator.MoveNextAsync());
            Assert.Throws<InvalidOperationException>(() => { int unused = enumerator.Current; });

            property.Value = 1;
            Assert.That(move.Result, Is.True);
            Assert.That(enumerator.Current, Is.EqualTo(1));
            Dispose(enumerator);
        }

        [Test]
        public void Enumeration_PreCanceledToken_CancelsFirstMoveAndStaysTerminal()
        {
            OnityAsyncReactiveProperty<int> property = new OnityAsyncReactiveProperty<int>(0);
            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                source.Cancel();
                IOnityAsyncEnumerator<int> enumerator = property.WithoutCurrent().GetAsyncEnumerator(source.Token);

                Assert.Catch<OperationCanceledException>(() => enumerator.MoveNextAsync().GetAwaiter().GetResult());
                property.Value = 1;
                Assert.Catch<OperationCanceledException>(() => enumerator.MoveNextAsync().GetAwaiter().GetResult());
                Dispose(enumerator);
            }
        }

        [Test]
        public void Enumeration_TokenCanceledWhilePending_CancelsMoveAndUnsubscribes()
        {
            OnityAsyncReactiveProperty<int> property = new OnityAsyncReactiveProperty<int>(0);
            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                IOnityAsyncEnumerator<int> enumerator = property.WithoutCurrent().GetAsyncEnumerator(source.Token);
                Probe<bool> move = Move(enumerator);

                source.Cancel();

                Assert.That(move.IsCanceled, Is.True);
                property.Value = 1;
                Assert.That(move.Calls, Is.EqualTo(1));
                Assert.Catch<OperationCanceledException>(() => enumerator.MoveNextAsync().GetAwaiter().GetResult());
                Dispose(enumerator);
            }
        }

        // Disposal

        [Test]
        public void Dispose_CompletesPendingEnumerators_AndCancelsPendingWaiters()
        {
            OnityAsyncReactiveProperty<int> property = new OnityAsyncReactiveProperty<int>(0);
            IOnityAsyncEnumerator<int> current = property.GetAsyncEnumerator();
            Assert.That(Move(current).Result, Is.True);
            Probe<bool> pending = Move(current);
            IOnityAsyncEnumerator<int> later = property.WithoutCurrent().GetAsyncEnumerator();
            Probe<bool> laterPending = Move(later);
            Probe<int> waiter = Probe<int>.Observe(property.WaitAsync());

            property.Dispose();

            Assert.That(pending.Result, Is.False);
            Assert.That(laterPending.Result, Is.False);
            Assert.That(waiter.IsCanceled, Is.True);
            Assert.That(Move(current).Result, Is.False);
            Assert.That(Move(later).Result, Is.False);
            Dispose(current);
            Dispose(later);
        }

        [Test]
        public void Dispose_IsIdempotent_AndSetValueAfterwardsStillStores()
        {
            OnityAsyncReactiveProperty<int> property = new OnityAsyncReactiveProperty<int>(0);
            Probe<int> waiter = Probe<int>.Observe(property.WaitAsync());

            property.Dispose();
            property.Dispose();
            property.Value = 4;

            Assert.That(waiter.Calls, Is.EqualTo(1));
            Assert.That(property.Value, Is.EqualTo(4));
        }

        [Test]
        public void EnumeratorDispose_EndsPendingMoveFalse_IsIdempotent_AndUnsubscribes()
        {
            OnityAsyncReactiveProperty<int> property = new OnityAsyncReactiveProperty<int>(0);
            IOnityAsyncEnumerator<int> enumerator = property.WithoutCurrent().GetAsyncEnumerator();
            Probe<bool> pending = Move(enumerator);

            Dispose(enumerator);
            Dispose(enumerator);

            Assert.That(pending.Result, Is.False);
            Assert.Throws<InvalidOperationException>(() => { int unused = enumerator.Current; });
            property.Value = 1;
            Assert.That(pending.Calls, Is.EqualTo(1));
            Assert.That(Move(enumerator).Result, Is.False);
        }

        // Text and conversion

        [Test]
        public void ToString_FormatsLatestValue()
        {
            Assert.That(new OnityAsyncReactiveProperty<int>(42).ToString(), Is.EqualTo("42"));
            Assert.That(new OnityAsyncReactiveProperty<string>("x").ToString(), Is.EqualTo("x"));
            Assert.That(new OnityAsyncReactiveProperty<string>(null).ToString(), Is.Null);
        }

        [Test]
        public void InterfaceView_ExposesWritableAndReadOnlyContracts()
        {
            OnityAsyncReactiveProperty<int> property = new OnityAsyncReactiveProperty<int>(1);
            IOnityAsyncReactiveProperty<int> writable = property;
            IOnityReadOnlyAsyncReactiveProperty<int> readOnly = property;
            IOnityAsyncEnumerable<int> sequence = property;

            writable.Value = 5;

            Assert.That(readOnly.Value, Is.EqualTo(5));
            Assert.That(sequence.GetAsyncEnumerator(), Is.Not.Null);
        }

        // Read-only property

        [Test]
        public void ReadOnly_FromSource_ReflectsSourceItemsAndPublishes()
        {
            OnityChannel<int> channel = OnityChannel.CreateUnbounded<int>();
            using (CancellationTokenSource source = new CancellationTokenSource())
            using (OnityReadOnlyAsyncReactiveProperty<int> property =
                channel.Reader.ReadAllAsync().ToReadOnlyAsyncReactiveProperty(-1, source.Token))
            {
                Assert.That(property.Value, Is.EqualTo(-1));
                Probe<int> waiter = Probe<int>.Observe(property.WaitAsync());
                IOnityAsyncEnumerator<int> enumerator = property.GetAsyncEnumerator();
                Assert.That(Move(enumerator).Result, Is.True);
                Assert.That(enumerator.Current, Is.EqualTo(-1));
                Probe<bool> pending = Move(enumerator);

                channel.Writer.TryWrite(7);
                WaitUntil(() => property.Value == 7);

                Assert.That(waiter.Result, Is.EqualTo(7));
                Assert.That(pending.Result, Is.True);
                Assert.That(enumerator.Current, Is.EqualTo(7));
                int implicitValue = property;
                Assert.That(implicitValue, Is.EqualTo(7));
                Assert.That(property.ToString(), Is.EqualTo("7"));
                Dispose(enumerator);
                source.Cancel();
            }
        }

        [Test]
        public void ReadOnly_WithoutInitialValue_StartsAtDefault_AndWithoutCurrentSkipsIt()
        {
            OnityChannel<int> channel = OnityChannel.CreateUnbounded<int>();
            using (CancellationTokenSource source = new CancellationTokenSource())
            using (OnityReadOnlyAsyncReactiveProperty<int> property =
                channel.Reader.ReadAllAsync().ToReadOnlyAsyncReactiveProperty(source.Token))
            {
                Assert.That(property.Value, Is.EqualTo(0));
                IOnityAsyncEnumerator<int> enumerator = property.WithoutCurrent().GetAsyncEnumerator();
                Probe<bool> pending = Move(enumerator);
                Assert.That(pending.Completed, Is.False);

                channel.Writer.TryWrite(3);
                WaitUntil(() => property.Value == 3);

                Assert.That(pending.Result, Is.True);
                Assert.That(enumerator.Current, Is.EqualTo(3));
                Dispose(enumerator);
                source.Cancel();
            }
        }

        [Test]
        public void ReadOnly_SourceCancellation_EndsPumpWithoutFault_AndKeepsLastValue()
        {
            OnityChannel<int> channel = OnityChannel.CreateUnbounded<int>();
            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                OnityReadOnlyAsyncReactiveProperty<int> property =
                    channel.Reader.ReadAllAsync().ToReadOnlyAsyncReactiveProperty(0, source.Token);
                channel.Writer.TryWrite(5);
                WaitUntil(() => property.Value == 5);

                source.Cancel();
                channel.Writer.TryWrite(6);

                Thread.Sleep(20);
                Assert.That(property.Value, Is.EqualTo(5));
                property.Dispose();
            }
        }

        [Test]
        public void ReadOnly_FaultingSource_LogsFault_EndsPump_KeepsLastValue_AndLeavesSubscribersPending()
        {
            // The channel hands out its buffered item first, then faults. Everything runs synchronously
            // inside the constructor, so the log is expected before the property is created.
            OnityChannel<int> channel = OnityChannel.CreateUnbounded<int>();
            channel.Writer.TryWrite(5);
            channel.Writer.TryComplete(new InvalidOperationException("source failure"));
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: source failure"));

            OnityReadOnlyAsyncReactiveProperty<int> property =
                channel.Reader.ReadAllAsync().ToReadOnlyAsyncReactiveProperty(0, CancellationToken.None);
            WaitUntil(() => property.Value == 5);

            // The source has ended, so the pump is gone: subscribers added now are not completed
            // (UniTask parity) until the property is disposed.
            Probe<int> waiter = Probe<int>.Observe(property.WaitAsync());
            IOnityAsyncEnumerator<int> enumerator = property.WithoutCurrent().GetAsyncEnumerator();
            Probe<bool> pending = Move(enumerator);
            Thread.Sleep(20);

            Assert.That(property.Value, Is.EqualTo(5));
            Assert.That(waiter.Completed, Is.False);
            Assert.That(pending.Completed, Is.False);

            property.Dispose();

            Assert.That(waiter.IsCanceled, Is.True);
            Assert.That(pending.Result, Is.False);
            Assert.That(property.Value, Is.EqualTo(5));
            Dispose(enumerator);
        }

        [Test]
        public void ReadOnly_Dispose_StopsPump_CompletesSubscribers()
        {
            OnityChannel<int> channel = OnityChannel.CreateUnbounded<int>();
            OnityReadOnlyAsyncReactiveProperty<int> property =
                channel.Reader.ReadAllAsync().ToReadOnlyAsyncReactiveProperty(0, CancellationToken.None);
            IOnityAsyncEnumerator<int> enumerator = property.WithoutCurrent().GetAsyncEnumerator();
            Probe<bool> pending = Move(enumerator);
            Probe<int> waiter = Probe<int>.Observe(property.WaitAsync());

            property.Dispose();

            Assert.That(pending.Result, Is.False);
            Assert.That(waiter.IsCanceled, Is.True);
            channel.Writer.TryWrite(9);
            Thread.Sleep(20);
            Assert.That(property.Value, Is.EqualTo(0));
            Dispose(enumerator);
        }

        [Test]
        public void ReadOnly_NullSource_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => new OnityReadOnlyAsyncReactiveProperty<int>(null, CancellationToken.None));
            Assert.Throws<ArgumentNullException>(
                () => new OnityReadOnlyAsyncReactiveProperty<int>(1, null, CancellationToken.None));
        }
    }
}
