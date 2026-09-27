using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class OnityChannelEditModeTests
    {
        [Test]
        public void CapacityValidation_AndHandlesAreStable()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => OnityChannel.CreateBounded<int>(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => OnityChannel.CreateBounded<int>(-1));
            var channel = OnityChannel.CreateUnbounded<int>();
            Assert.That(channel.Reader, Is.SameAs(channel.Reader));
            Assert.That(channel.Writer, Is.SameAs(channel.Writer));
            Assert.That(channel.Reader.TryRead(out _), Is.False);
            Assert.That(channel.Writer.TryComplete(), Is.True);
        }

        [TestCase(1)]
        [TestCase(128)]
        public void BoundedRing_WrapsWithoutReordering_AndRejectsOnlyFullWrites(int capacity)
        {
            var channel = OnityChannel.CreateBounded<int>(capacity);
            for (int cycle = 0; cycle < 6; cycle++)
            {
                for (int index = 0; index < capacity; index++)
                {
                    Assert.That(channel.Writer.TryWrite(cycle * capacity + index), Is.True);
                }
                Assert.That(channel.Writer.TryWrite(-1), Is.False);
                for (int index = 0; index < capacity; index++)
                {
                    Assert.That(channel.Reader.TryRead(out int value), Is.True);
                    Assert.That(value, Is.EqualTo(cycle * capacity + index));
                }
                Assert.That(channel.Reader.TryRead(out _), Is.False);
            }
            channel.Writer.TryComplete();
        }

        [Test]
        public void UnboundedGrowth_DrainsAcceptedItemsInOrder()
        {
            var channel = OnityChannel.CreateUnbounded<int>();
            for (int value = 0; value < 1024; value++)
            {
                Assert.That(channel.Writer.TryWrite(value), Is.True);
            }
            Assert.That(channel.Writer.TryComplete(), Is.True);
            int[] values = Read(channel.Reader.ReadAllAsync().ToArrayAsync());
            Assert.That(values.Length, Is.EqualTo(1024));
            for (int index = 0; index < values.Length; index++)
            {
                Assert.That(values[index], Is.EqualTo(index));
            }
        }

        [Test]
        public void QueuedWriters_BackpressureAndPromotion_PreventTryWriteBypass()
        {
            var channel = OnityChannel.CreateBounded<int>(1);
            Assert.That(channel.Writer.TryWrite(10), Is.True);
            var first = channel.Writer.WriteAsync(20);
            var second = channel.Writer.WriteAsync(30);
            try
            {
                Assert.That(first.IsCompleted, Is.False);
                Assert.That(second.IsCompleted, Is.False);
                Assert.That(channel.Writer.TryWrite(99), Is.False);
                Assert.That(Read(channel.Reader.ReadAsync()), Is.EqualTo(10));
                Read(first);
                Assert.That(second.IsCompleted, Is.False);
                Assert.That(channel.Writer.TryWrite(99), Is.False);
                Assert.That(Read(channel.Reader.ReadAsync()), Is.EqualTo(20));
                Read(second);
                Assert.That(channel.Writer.TryWrite(99), Is.False);
                Assert.That(Read(channel.Reader.ReadAsync()), Is.EqualTo(30));
                Assert.That(channel.Writer.TryWrite(40), Is.True);
                Assert.That(Read(channel.Reader.ReadAsync()), Is.EqualTo(40));
            }
            finally
            {
                channel.Writer.TryComplete();
                Observe(first);
                Observe(second);
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void CancelQueuedWriter_HeadMiddleOrTail_DetachesOnlyThatItem(int canceledIndex)
        {
            var channel = OnityChannel.CreateBounded<int>(1);
            var cancellations = new[] { new CancellationTokenSource(), new CancellationTokenSource(), new CancellationTokenSource() };
            var writers = new OnityTask[3];
            channel.Writer.TryWrite(0);
            try
            {
                for (int index = 0; index < writers.Length; index++)
                {
                    writers[index] = channel.Writer.WriteAsync(index + 1, cancellations[index].Token);
                }
                cancellations[canceledIndex].Cancel();
                CheckCanceled(writers[canceledIndex], cancellations[canceledIndex].Token);
                var expected = new List<int> { 0 };
                for (int index = 0; index < writers.Length; index++)
                {
                    if (index != canceledIndex)
                    {
                        expected.Add(index + 1);
                    }
                }
                foreach (int value in expected)
                {
                    Assert.That(Read(channel.Reader.ReadAsync()), Is.EqualTo(value));
                }
                Assert.That(channel.Reader.TryRead(out _), Is.False);
                for (int index = 0; index < writers.Length; index++)
                {
                    if (index != canceledIndex)
                    {
                        Read(writers[index]);
                    }
                }
            }
            finally
            {
                channel.Writer.TryComplete();
                foreach (var writer in writers)
                {
                    Observe(writer);
                }
                foreach (var cancellation in cancellations)
                {
                    cancellation.Dispose();
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Completion_DrainsBuffer_FirstCloseWins_AndPendingWritersRetainInnerError(bool errorClosure)
        {
            var error = new OperationCanceledException("faulted channel closure");
            var channel = OnityChannel.CreateBounded<int>(1);
            channel.Writer.TryWrite(7);
            var rejected = channel.Writer.WriteAsync(8);
            Assert.That(channel.Writer.TryComplete(errorClosure ? error : null), Is.True);
            Assert.That(channel.Writer.TryComplete(new InvalidOperationException("second close")), Is.False);
            Assert.That(rejected.IsFaulted, Is.True);
            var rejection = (OnityChannelClosedException)Catch(() => Read(rejected));
            Assert.That(rejection.InnerException, Is.SameAs(errorClosure ? error : null));
            Assert.That(Read(channel.Reader.ReadAsync()), Is.EqualTo(7));
            var terminal = channel.Reader.ReadAsync();
            Assert.That(terminal.IsFaulted, Is.True);
            Assert.That(terminal.IsCanceled, Is.False);
            Exception actual = Catch(() => Read(terminal));
            if (errorClosure)
            {
                Assert.That(actual, Is.SameAs(error));
            }
            else
            {
                Assert.That(actual, Is.TypeOf<OnityChannelClosedException>());
            }
            Assert.That(channel.Writer.TryWrite(9), Is.False);
            var lateWriter = channel.Writer.WriteAsync(9);
            Assert.That(((OnityChannelClosedException)Catch(() => Read(lateWriter))).InnerException,
                Is.SameAs(errorClosure ? error : null));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReadAll_DrainsBeforeStickyEndOrOriginalFault_AndInvalidatesCurrent(bool errorClosure)
        {
            var error = new OperationCanceledException("ReadAll fault");
            var channel = OnityChannel.CreateUnbounded<int>();
            channel.Writer.TryWrite(1);
            channel.Writer.TryWrite(2);
            channel.Writer.TryComplete(errorClosure ? error : null);
            var iterator = channel.Reader.ReadAllAsync().GetAsyncEnumerator();
            try
            {
                Assert.Throws<InvalidOperationException>(() => { var ignored = iterator.Current; });
                Assert.That(Read(iterator.MoveNextAsync()), Is.True);
                Assert.That(iterator.Current, Is.EqualTo(1));
                Assert.That(Read(iterator.MoveNextAsync()), Is.True);
                Assert.That(iterator.Current, Is.EqualTo(2));
                for (int repeat = 0; repeat < 2; repeat++)
                {
                    var move = iterator.MoveNextAsync();
                    if (errorClosure)
                    {
                        Assert.That(move.IsFaulted, Is.True);
                        Assert.That(move.IsCanceled, Is.False);
                        Assert.That(Catch(() => Read(move)), Is.SameAs(error));
                    }
                    else
                    {
                        Assert.That(Read(move), Is.False);
                    }
                    Assert.Throws<InvalidOperationException>(() => { var ignored = iterator.Current; });
                }
            }
            finally
            {
                Read(iterator.DisposeAsync());
            }
            Assert.That(Read(iterator.MoveNextAsync()), Is.False);
        }

        [Test]
        public void DirectPrecancellation_PrecedesLeaseAndClosure_AndConsumesNothing()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                var channel = OnityChannel.CreateBounded<int>(1);
                var iterator = channel.Reader.ReadAllAsync().GetAsyncEnumerator();
                var move = iterator.MoveNextAsync();
                try
                {
                    CheckCanceled(channel.Reader.ReadAsync(cancellation.Token), cancellation.Token);
                    CheckCanceled(channel.Writer.WriteAsync(9, cancellation.Token), cancellation.Token);
                    Assert.That(move.IsCompleted, Is.False);
                    channel.Writer.TryWrite(10);
                    Assert.That(Read(move), Is.True);
                    Assert.That(iterator.Current, Is.EqualTo(10));
                    channel.Writer.TryComplete();
                    CheckCanceled(channel.Reader.ReadAsync(cancellation.Token), cancellation.Token);
                    CheckCanceled(channel.Writer.WriteAsync(9, cancellation.Token), cancellation.Token);
                }
                finally
                {
                    channel.Writer.TryComplete();
                    Read(iterator.DisposeAsync());
                }
            }
        }

        [Test]
        public void ConsumerConflictsAndOverlap_ThrowSynchronously_AndFailedFirstMoveCanRetry()
        {
            var channel = OnityChannel.CreateUnbounded<int>();
            var direct = channel.Reader.ReadAsync();
            var iterator = channel.Reader.ReadAllAsync().GetAsyncEnumerator();
            try
            {
                Assert.Throws<InvalidOperationException>(() => channel.Reader.ReadAsync());
                Assert.Throws<InvalidOperationException>(() => iterator.MoveNextAsync());
                Assert.That(channel.Reader.TryRead(out _), Is.False);
                channel.Writer.TryWrite(21);
                Assert.That(Read(direct), Is.EqualTo(21));
                var accepted = iterator.MoveNextAsync();
                Assert.Throws<InvalidOperationException>(() => iterator.MoveNextAsync());
                Assert.Throws<InvalidOperationException>(() => channel.Reader.ReadAsync());
                channel.Writer.TryWrite(22);
                Assert.That(Read(accepted), Is.True);
                Assert.That(iterator.Current, Is.EqualTo(22));
                channel.Writer.TryWrite(23);
                Assert.That(channel.Reader.TryRead(out _), Is.False);
                Assert.That(iterator.Current, Is.EqualTo(22));
                Read(iterator.DisposeAsync());
                Assert.That(Read(channel.Reader.ReadAsync()), Is.EqualTo(23));
            }
            finally
            {
                channel.Writer.TryComplete();
                Observe(direct);
                Read(iterator.DisposeAsync());
            }
        }

        [Test]
        public void IdleReadAll_IsInert_AndIdleDisposalDoesNotAcquireOrCloseChannel()
        {
            var channel = OnityChannel.CreateUnbounded<int>();
            var first = channel.Reader.ReadAllAsync().GetAsyncEnumerator();
            var second = channel.Reader.ReadAllAsync().GetAsyncEnumerator();
            channel.Writer.TryWrite(1);
            Assert.That(Read(channel.Reader.ReadAsync()), Is.EqualTo(1));
            Read(first.DisposeAsync());
            Assert.That(Read(first.MoveNextAsync()), Is.False);
            channel.Writer.TryWrite(2);
            Assert.That(Read(second.MoveNextAsync()), Is.True);
            Assert.That(second.Current, Is.EqualTo(2));
            Read(second.DisposeAsync());
            Assert.That(channel.Writer.TryWrite(3), Is.True);
            Assert.That(Read(channel.Reader.ReadAsync()), Is.EqualTo(3));
            channel.Writer.TryComplete();
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void TokenPair_PrecancelCapturedWinsTies_AndReportedTokensAreOriginal(int mode)
        {
            using (var captured = new CancellationTokenSource())
            using (var enumeration = new CancellationTokenSource())
            {
                if (mode != 1)
                {
                    captured.Cancel();
                }
                if (mode != 0)
                {
                    enumeration.Cancel();
                }
                var channel = OnityChannel.CreateUnbounded<int>();
                channel.Writer.TryWrite(11);
                var iterator = channel.Reader.ReadAllAsync(captured.Token).GetAsyncEnumerator(enumeration.Token);
                CheckCanceled(iterator.MoveNextAsync(), mode == 1 ? enumeration.Token : captured.Token);
                Read(iterator.DisposeAsync());
                Assert.That(Read(channel.Reader.ReadAsync()), Is.EqualTo(11));
                channel.Writer.TryComplete();
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TokenPair_PendingFirstCallbackWins_AndOldCallbacksCannotCancelReplacement(bool capturedFirst)
        {
            using (var captured = new CancellationTokenSource())
            using (var enumeration = new CancellationTokenSource())
            {
                var channel = OnityChannel.CreateUnbounded<int>();
                var old = channel.Reader.ReadAllAsync(captured.Token).GetAsyncEnumerator(enumeration.Token);
                var move = old.MoveNextAsync();
                if (capturedFirst)
                {
                    captured.Cancel();
                }
                else
                {
                    enumeration.Cancel();
                }
                CheckCanceled(move, capturedFirst ? captured.Token : enumeration.Token);
                Read(old.DisposeAsync());
                var replacement = channel.Reader.ReadAllAsync().GetAsyncEnumerator();
                var next = replacement.MoveNextAsync();
                try
                {
                    captured.Cancel();
                    enumeration.Cancel();
                    Assert.That(next.IsCompleted, Is.False);
                    Assert.That(channel.Writer.TryWrite(29), Is.True);
                    Assert.That(Read(next), Is.True);
                    Assert.That(replacement.Current, Is.EqualTo(29));
                }
                finally
                {
                    channel.Writer.TryComplete();
                    Read(replacement.DisposeAsync());
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SoleOrEqualTokens_ArePreserved_AndDisposalNeverOwnsCaller(bool equal)
        {
            using (var caller = new CancellationTokenSource())
            {
                var channel = OnityChannel.CreateUnbounded<int>();
                var iterator = channel.Reader.ReadAllAsync(caller.Token).GetAsyncEnumerator(equal ? caller.Token : default);
                var move = iterator.MoveNextAsync();
                caller.Cancel();
                CheckCanceled(move, caller.Token);
                Read(iterator.DisposeAsync());
                Assert.DoesNotThrow(() => caller.Cancel());
                channel.Writer.TryComplete();
            }
            using (var caller = new CancellationTokenSource())
            {
                var channel = OnityChannel.CreateUnbounded<int>();
                var iterator = channel.Reader.ReadAllAsync(caller.Token).GetAsyncEnumerator(equal ? caller.Token : default);
                var move = iterator.MoveNextAsync();
                Read(iterator.DisposeAsync());
                Assert.That(Read(move), Is.False);
                Assert.That(caller.IsCancellationRequested, Is.False);
                Assert.DoesNotThrow(() => caller.Cancel());
                channel.Writer.TryComplete();
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DisposalDeliveryOrder_PreservesFalseWinnerBuffer_OrTrueCommit(bool deliveryFirst)
        {
            var channel = OnityChannel.CreateBounded<int>(1);
            var iterator = channel.Reader.ReadAllAsync().GetAsyncEnumerator();
            var move = iterator.MoveNextAsync();
            if (deliveryFirst)
            {
                Assert.That(channel.Writer.TryWrite(43), Is.True);
                Assert.That(Read(move), Is.True);
                Assert.That(iterator.Current, Is.EqualTo(43));
                Read(iterator.DisposeAsync());
                Assert.That(Read(move), Is.True);
                Assert.That(channel.Reader.TryRead(out _), Is.False);
            }
            else
            {
                Read(iterator.DisposeAsync());
                Assert.That(Read(move), Is.False);
                Assert.That(channel.Writer.TryWrite(43), Is.True);
                Assert.That(Read(channel.Reader.ReadAsync()), Is.EqualTo(43));
            }
            Assert.Throws<InvalidOperationException>(() => { var ignored = iterator.Current; });
            channel.Writer.TryComplete();
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void CompletedDispose_ImpliesPendingMovePublished_AndContinuationCanRequestFalse(int escape)
        {
            var channel = OnityChannel.CreateUnbounded<int>();
            var iterator = channel.Reader.ReadAllAsync().GetAsyncEnumerator();
            OnityTask<bool> move = iterator.MoveNextAsync();
            OnityTask<bool> preserved = escape == 1 ? move.Preserve() : default;
            Task<bool> bridge = escape == 2 ? move.AsTask() : null;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            object waiter = iterator.GetType().GetField("Pending", flags).GetValue(iterator);
            Type waiterType = waiter.GetType().BaseType;
            object gate = waiterType.GetField("m_gate", flags).GetValue(waiter);
            FieldInfo initializing = waiterType.GetField("m_initializing", flags);
            MethodInfo finish = waiterType.GetMethod("Finish", flags);
            // Keep the real pending waiter pinned as during token registration.
            // This makes disposal publication observable before Finish returns.
            lock (gate)
            {
                initializing.SetValue(waiter, true);
            }
            bool callbackRan = false;
            Exception error = null;
            OnityTask dispose = default;
            try
            {
                dispose = iterator.DisposeAsync();
                Assert.That(dispose.IsCompleted, Is.False);
                Assert.That(move.IsCompleted, Is.False);
                dispose.GetAwaiter().UnsafeOnCompleted(() =>
                {
                    try
                    {
                        dispose.GetAwaiter().GetResult();
                        if (!move.IsCompleted)
                        {
                            throw new InvalidOperationException("Dispose published before its owned Move source.");
                        }
                        if (iterator.MoveNextAsync().GetAwaiter().GetResult())
                        {
                            throw new InvalidOperationException("Disposed move accepted an item.");
                        }
                        callbackRan = true;
                    }
                    catch (Exception exception)
                    {
                        error = exception;
                    }
                });
            }
            finally
            {
                lock (gate)
                {
                    initializing.SetValue(waiter, false);
                }
                finish.Invoke(waiter, null);
                channel.Writer.TryComplete();
            }
            Assert.That(error, Is.Null);
            Assert.That(callbackRan, Is.True);
            // Bridge/preservation observers may run after the disposal observer.
            bool result = escape == 2 ? bridge.GetAwaiter().GetResult()
                : (escape == 1 ? preserved : move).GetAwaiter().GetResult();
            Assert.That(result, Is.False);
            Assert.That(iterator.DisposeAsync(), Is.EqualTo(dispose));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ActualAwaitForeach_BreakOrThrow_ReleasesLeaseWithoutClosingSharedChannel(bool throwBody)
        {
            var channel = OnityChannel.CreateUnbounded<int>();
            channel.Writer.TryWrite(51);
            channel.Writer.TryWrite(52);
            var fault = new InvalidOperationException("channel foreach body");
            var result = Consume(channel.Reader.ReadAllAsync(), throwBody, fault);
            if (throwBody)
            {
                Assert.That(Catch(() => Read(result)), Is.SameAs(fault));
            }
            else
            {
                Assert.That(Read(result), Is.EqualTo(51));
            }
            Assert.That(Read(channel.Reader.ReadAsync()), Is.EqualTo(52));
            Assert.That(channel.Writer.TryWrite(53), Is.True);
            Assert.That(Read(channel.Reader.ReadAsync()), Is.EqualTo(53));
            channel.Writer.TryComplete();
        }

        [Test]
        public void WorkerCancelVersusWriterPromotion_ConservesTheAcceptedOrCanceledItem()
        {
            for (int round = 0; round < 32; round++)
            {
                using (var cancellation = new CancellationTokenSource())
                using (var start = new ManualResetEventSlim())
                {
                    var channel = OnityChannel.CreateBounded<int>(1);
                    channel.Writer.TryWrite(0);
                    var write = channel.Writer.WriteAsync(1, cancellation.Token);
                    Task cancel = Task.Run(() => { start.Wait(); cancellation.Cancel(); });
                    Task<int> promote = Task.Run(() => { start.Wait(); return Read(channel.Reader.ReadAsync()); });
                    try
                    {
                        start.Set();
                        Join(cancel);
                        Join(promote);
                        Assert.That(promote.GetAwaiter().GetResult(), Is.Zero);
                        Wait(() => write.IsCompleted);
                        if (write.IsCanceled)
                        {
                            CheckCanceled(write, cancellation.Token);
                            Assert.That(channel.Reader.TryRead(out _), Is.False);
                        }
                        else
                        {
                            Read(write);
                            Assert.That(Read(channel.Reader.ReadAsync()), Is.EqualTo(1));
                        }
                    }
                    finally
                    {
                        start.Set();
                        Join(cancel);
                        Join(promote);
                        channel.Writer.TryComplete();
                        Observe(write);
                    }
                }
            }
        }

        [Test]
        public void WorkerCancelVersusDirectHandoff_ConservesDeliveryOrReplacementBuffer()
        {
            for (int round = 0; round < 32; round++)
            {
                using (var cancellation = new CancellationTokenSource())
                using (var start = new ManualResetEventSlim())
                {
                    var channel = OnityChannel.CreateBounded<int>(1);
                    var read = channel.Reader.ReadAsync(cancellation.Token);
                    Task cancel = Task.Run(() => { start.Wait(); cancellation.Cancel(); });
                    Task<bool> write = Task.Run(() => { start.Wait(); return channel.Writer.TryWrite(61); });
                    try
                    {
                        start.Set();
                        Join(cancel);
                        Join(write);
                        Assert.That(write.GetAwaiter().GetResult(), Is.True);
                        Wait(() => read.IsCompleted);
                        if (read.IsCanceled)
                        {
                            CheckCanceled(read, cancellation.Token);
                            Assert.That(Read(channel.Reader.ReadAsync()), Is.EqualTo(61));
                        }
                        else
                        {
                            Assert.That(Read(read), Is.EqualTo(61));
                            Assert.That(channel.Reader.TryRead(out _), Is.False);
                        }
                    }
                    finally
                    {
                        start.Set();
                        Join(cancel);
                        Join(write);
                        channel.Writer.TryComplete();
                        Observe(read);
                    }
                }
            }
        }

        [Test]
        public void NativeCancelConsumerCallback_CanHoldWhileReplacementReaderMakesProgress()
        {
            using (var cancellation = new CancellationTokenSource())
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                var channel = OnityChannel.CreateUnbounded<int>();
                var old = channel.Reader.ReadAllAsync(cancellation.Token).GetAsyncEnumerator();
                var move = old.MoveNextAsync();
                Exception callbackError = null;
                bool timedOut = false;
                move.GetAwaiter().UnsafeOnCompleted(() =>
                {
                    try
                    {
                        try
                        {
                            move.GetAwaiter().GetResult();
                        }
                        catch (OperationCanceledException)
                        {
                        }
                        entered.Set();
                        timedOut = !release.Wait(TimeSpan.FromSeconds(5));
                    }
                    catch (Exception exception)
                    {
                        callbackError = exception;
                    }
                });
                Task worker = Task.Run(() => cancellation.Cancel());
                try
                {
                    Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
                    var replacement = channel.Reader.ReadAsync();
                    Assert.That(channel.Writer.TryWrite(71), Is.True);
                    Assert.That(Read(replacement), Is.EqualTo(71));
                    release.Set();
                    Join(worker);
                    Assert.That(timedOut, Is.False);
                    Assert.That(callbackError, Is.Null);
                    Read(old.DisposeAsync());
                }
                finally
                {
                    release.Set();
                    Join(worker);
                    channel.Writer.TryComplete();
                    Read(old.DisposeAsync());
                }
            }
        }

        [Test]
        public void IdleEnumerationCancellation_IsObservedAtNextMove_AndLeavesBufferedItemForReplacement()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                var channel = OnityChannel.CreateUnbounded<int>();
                channel.Writer.TryWrite(81);
                channel.Writer.TryWrite(82);
                var iterator = channel.Reader.ReadAllAsync(cancellation.Token).GetAsyncEnumerator();
                Assert.That(Read(iterator.MoveNextAsync()), Is.True);
                cancellation.Cancel();
                Assert.That(iterator.Current, Is.EqualTo(81));
                Assert.That(channel.Reader.TryRead(out _), Is.False);
                CheckCanceled(iterator.MoveNextAsync(), cancellation.Token);
                Read(iterator.DisposeAsync());
                Assert.That(Read(channel.Reader.ReadAsync()), Is.EqualTo(82));
                channel.Writer.TryComplete();
            }
        }

        [Test]
        public void InitialMoveVersusTokenCancellation_SettlesAndReleasesLeaseWithoutClaimingBufferedFutureItem()
        {
            for (int round = 0; round < 32; round++)
            {
                using (var captured = new CancellationTokenSource())
                using (var enumeration = new CancellationTokenSource())
                using (var start = new ManualResetEventSlim())
                {
                    var channel = OnityChannel.CreateUnbounded<int>();
                    var iterator = channel.Reader.ReadAllAsync(captured.Token).GetAsyncEnumerator(enumeration.Token);
                    OnityTask<bool> move = default;
                    Task factory = Task.Run(() => { start.Wait(); move = iterator.MoveNextAsync(); });
                    Task cancel = Task.Run(() => { start.Wait(); captured.Cancel(); });
                    try
                    {
                        start.Set();
                        Join(factory);
                        Join(cancel);
                        Wait(() => move.IsCompleted);
                        CheckCanceled(move, captured.Token);
                        var disposal = iterator.DisposeAsync();
                        Assert.That(disposal.IsCompleted, Is.True);
                        Read(disposal);
                        Assert.That(move.IsCompleted, Is.True);
                        channel.Writer.TryWrite(91);
                        Assert.That(Read(channel.Reader.ReadAsync()), Is.EqualTo(91));
                        Assert.That(enumeration.IsCancellationRequested, Is.False);
                    }
                    finally
                    {
                        start.Set();
                        Join(factory);
                        Join(cancel);
                        channel.Writer.TryComplete();
                        Read(iterator.DisposeAsync());
                    }
                }
            }
        }

        private static async OnityTask<int> Consume(IOnityAsyncEnumerable<int> source, bool throws, Exception error)
        {
            await foreach (int value in source)
            {
                if (throws)
                {
                    throw error;
                }
                return value;
            }
            return -1;
        }

        private static void CheckCanceled<T>(OnityTask<T> task, CancellationToken token)
        {
            Assert.That(task.IsCanceled, Is.True);
            Assert.That(task.IsFaulted, Is.False);
            var error = (OperationCanceledException)Catch(() => Read(task));
            Assert.That(error.CancellationToken, Is.EqualTo(token));
        }
        private static void CheckCanceled(OnityTask task, CancellationToken token)
        {
            Assert.That(task.IsCanceled, Is.True);
            Assert.That(task.IsFaulted, Is.False);
            var error = (OperationCanceledException)Catch(() => Read(task));
            Assert.That(error.CancellationToken, Is.EqualTo(token));
        }
        private static T Read<T>(OnityTask<T> task)
        {
            Assert.That(task.IsCompleted, Is.True);
            return task.GetAwaiter().GetResult();
        }
        private static void Read(OnityTask task)
        {
            Assert.That(task.IsCompleted, Is.True);
            task.GetAwaiter().GetResult();
        }
        private static void Observe<T>(OnityTask<T> task) => Catch(() => task.GetAwaiter().GetResult());
        private static void Observe(OnityTask task) => Catch(() => task.GetAwaiter().GetResult());
        private static void Wait(Func<bool> predicate)
        {
            Assert.That(SpinWait.SpinUntil(predicate, TimeSpan.FromSeconds(5)), Is.True, "Channel outcome timed out.");
        }
        private static void Join(Task task)
        {
            Wait(() => task.IsCompleted);
            task.GetAwaiter().GetResult();
        }
        private static Exception Catch(Action action)
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
    }
}
