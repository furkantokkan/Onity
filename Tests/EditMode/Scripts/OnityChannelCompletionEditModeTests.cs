using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    public sealed class OnityChannelCompletionEditModeTests
    {
        // ---- Completion -------------------------------------------------------------------------

        [Test]
        public void Completion_SucceedsOnlyAfterCloseAndDrain()
        {
            OnityChannel<int> channel = OnityChannel.CreateUnbounded<int>();
            MultiTestResult completion = MultiTestResult.Watch(channel.Reader.Completion);
            channel.Writer.TryWrite(1);
            channel.Writer.TryWrite(2);
            Assert.That(channel.Writer.TryComplete(), Is.True);
            Assert.That(completion.Completed, Is.False, "buffered items are still unread");
            Assert.That(channel.Reader.TryRead(out int first) && first == 1, Is.True);
            Assert.That(completion.Completed, Is.False);
            Assert.That(channel.Reader.TryRead(out int second) && second == 2, Is.True);
            Assert.That(completion.Completed, Is.True);
            Assert.That(completion.Error, Is.Null);
            Assert.That(completion.Canceled, Is.False);
            Assert.That(channel.Reader.Completion.IsCompletedSuccessfully, Is.True);
        }

        [Test]
        public void Completion_EmptyChannel_CompletesInsideTryComplete_ForEveryAwaiter()
        {
            OnityChannel<int> channel = OnityChannel.CreateBounded<int>(1);
            OnityTask shared = channel.Reader.Completion;
            MultiTestResult first = MultiTestResult.Watch(shared);
            MultiTestResult second = MultiTestResult.Watch(channel.Reader.Completion);
            channel.Writer.TryComplete();
            Assert.That(first.Completed && second.Completed, Is.True);
            Assert.That(first.Error ?? second.Error, Is.Null);
        }

        [Test]
        public void Completion_ErrorClosure_FaultsWithTheOriginalError_AfterDrain([Values] bool requestedEarly)
        {
            OnityChannel<int> channel = OnityChannel.CreateUnbounded<int>();
            var error = new OperationCanceledException("closure");
            MultiTestResult early = requestedEarly ? MultiTestResult.Watch(channel.Reader.Completion) : null;
            channel.Writer.TryWrite(5);
            channel.Writer.TryComplete(error);
            Assert.That(early?.Completed ?? false, Is.False);
            Assert.That(channel.Reader.TryRead(out int item) && item == 5, Is.True);
            MultiTestResult completion = early ?? MultiTestResult.Watch(channel.Reader.Completion);
            Assert.That(completion.Completed, Is.True);
            Assert.That(completion.Canceled, Is.False, "an OperationCanceledException closure stays a fault");
            Assert.That(completion.Error, Is.SameAs(error));
        }

        [Test]
        public void Completion_ReadAllAndReadAsync_DrainTheChannel()
        {
            OnityChannel<int> channel = OnityChannel.CreateUnbounded<int>();
            MultiTestResult completion = MultiTestResult.Watch(channel.Reader.Completion);
            channel.Writer.TryWrite(1);
            channel.Writer.TryWrite(2);
            channel.Writer.TryComplete();
            Assert.That(MultiTestResult<int>.Watch(channel.Reader.ReadAsync()).Value, Is.EqualTo(1));
            IOnityAsyncEnumerator<int> enumerator = channel.Reader.ReadAllAsync().GetAsyncEnumerator();
            MultiTest.ExpectItem(enumerator, 2);
            Assert.That(completion.Completed, Is.True);
            MultiTest.ExpectEnd(enumerator);
            MultiTest.Dispose(enumerator);
        }

        // ---- WaitToReadAsync ----------------------------------------------------------------------

        [Test]
        public void WaitToReadAsync_ReportsBufferedItems_WithoutConsumingThem()
        {
            OnityChannel<int> channel = OnityChannel.CreateUnbounded<int>();
            channel.Writer.TryWrite(4);
            MultiTestResult<bool> wait = MultiTestResult<bool>.Watch(channel.Reader.WaitToReadAsync());
            Assert.That(wait.Value, Is.True);
            Assert.That(channel.Reader.TryRead(out int item) && item == 4, Is.True);
        }

        [Test]
        public void WaitToReadAsync_PendingWait_HoldsTheLease_UntilAnItemIsBuffered()
        {
            OnityChannel<int> channel = OnityChannel.CreateUnbounded<int>();
            MultiTestResult<bool> wait = MultiTestResult<bool>.Watch(channel.Reader.WaitToReadAsync());
            Assert.That(wait.Completed, Is.False);
            Assert.That(channel.Reader.TryRead(out _), Is.False);
            Assert.Throws<InvalidOperationException>(() => channel.Reader.ReadAsync());
            Assert.Throws<InvalidOperationException>(() => channel.Reader.WaitToReadAsync());

            Assert.That(channel.Writer.TryWrite(7), Is.True);
            Assert.That(wait.Value, Is.True);
            Assert.That(channel.Reader.TryRead(out int item) && item == 7, Is.True, "the item stays buffered");
        }

        [Test]
        public void WaitToReadAsync_Completion_ReturnsFalseOrTheOriginalError([Values] bool errorClosure)
        {
            OnityChannel<int> channel = OnityChannel.CreateUnbounded<int>();
            MultiTestResult<bool> pending = MultiTestResult<bool>.Watch(channel.Reader.WaitToReadAsync());
            var error = new FormatException("closure");
            channel.Writer.TryComplete(errorClosure ? error : null);
            MultiTestResult<bool> later = MultiTestResult<bool>.Watch(channel.Reader.WaitToReadAsync());
            foreach (MultiTestResult<bool> result in new[] { pending, later })
            {
                Assert.That(result.Completed, Is.True);
                if (errorClosure)
                {
                    Assert.That(result.Error, Is.SameAs(error));
                }
                else
                {
                    Assert.That(result.Error, Is.Null);
                    Assert.That(result.Value, Is.False);
                }
            }
        }

        [Test]
        public void WaitToReadAsync_Cancellation_ReleasesTheLease_AndPrecancellationWinsTheLeaseConflict()
        {
            OnityChannel<int> channel = OnityChannel.CreateUnbounded<int>();
            using var cts = new CancellationTokenSource();
            MultiTestResult<bool> wait = MultiTestResult<bool>.Watch(channel.Reader.WaitToReadAsync(cts.Token));
            MultiTestResult<bool> precanceled = MultiTestResult<bool>.Watch(
                channel.Reader.WaitToReadAsync(new CancellationToken(true)));
            Assert.That(precanceled.Canceled, Is.True, "pre-cancellation wins before the lease conflict");
            cts.Cancel();
            Assert.That(wait.Canceled, Is.True);
            Assert.That(wait.CanceledToken, Is.EqualTo(cts.Token));
            channel.Writer.TryWrite(1);
            Assert.That(channel.Reader.TryRead(out int item) && item == 1, Is.True, "the lease was released");
        }

        [Test]
        public void WaitToReadAsync_ConflictsWithAnActiveReadAllLease()
        {
            OnityChannel<int> channel = OnityChannel.CreateUnbounded<int>();
            IOnityAsyncEnumerator<int> enumerator = channel.Reader.ReadAllAsync().GetAsyncEnumerator();
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            Assert.Throws<InvalidOperationException>(() => channel.Reader.WaitToReadAsync());
            channel.Writer.TryWrite(3);
            Assert.That(move.Value, Is.True, "the pending ReadAll move still receives the item");
            MultiTest.Dispose(enumerator);
        }

        [Test]
        public void WaitToReadAsync_BoundedWriteAsync_SignalsTheWait()
        {
            OnityChannel<int> channel = OnityChannel.CreateBounded<int>(1);
            MultiTestResult<bool> wait = MultiTestResult<bool>.Watch(channel.Reader.WaitToReadAsync());
            MultiTestResult write = MultiTestResult.Watch(channel.Writer.WriteAsync(9));
            Assert.That(write.Completed, Is.True);
            Assert.That(wait.Value, Is.True);
            Assert.That(channel.Reader.TryRead(out int item) && item == 9, Is.True);
        }

        [Test]
        public void WaitAndTryRead_ConsumerLoop_ReadsEveryItemThenEnds()
        {
            OnityChannel<int> channel = OnityChannel.CreateUnbounded<int>();
            var items = new List<int>();
            MultiTestResult consumer = MultiTestResult.Watch(Consume(channel.Reader, items));
            channel.Writer.TryWrite(1);
            channel.Writer.TryWrite(2);
            Assert.That(items, Is.EqualTo(new[] { 1, 2 }));
            channel.Writer.TryWrite(3);
            channel.Writer.Complete();
            Assert.That(consumer.Completed, Is.True);
            Assert.That(consumer.Error, Is.Null);
            Assert.That(items, Is.EqualTo(new[] { 1, 2, 3 }));
        }

        private static async OnityTask Consume(OnityChannelReader<int> reader, List<int> items)
        {
            while (await reader.WaitToReadAsync())
            {
                while (reader.TryRead(out int item))
                {
                    items.Add(item);
                }
            }
        }

        // ---- Complete, conversions, exception -----------------------------------------------------------

        [Test]
        public void Complete_ThrowsWhenTheChannelIsAlreadyCompleted()
        {
            OnityChannel<int> channel = OnityChannel.CreateUnbounded<int>();
            channel.Writer.Complete();
            Assert.Throws<OnityChannelClosedException>(() => channel.Writer.Complete());
            Assert.Throws<OnityChannelClosedException>(() => channel.Writer.Complete(new FormatException()));
            Assert.That(channel.Writer.TryComplete(), Is.False);
        }

        [Test]
        public void ImplicitConversions_ReturnTheChannelHandles()
        {
            OnityChannel<int> channel = OnityChannel.CreateUnbounded<int>();
            OnityChannelReader<int> reader = channel;
            OnityChannelWriter<int> writer = channel;
            Assert.That(reader, Is.SameAs(channel.Reader));
            Assert.That(writer, Is.SameAs(channel.Writer));
            OnityChannel<int> missing = null;
            OnityChannelReader<int> missingReader = missing;
            OnityChannelWriter<int> missingWriter = missing;
            Assert.That(missingReader, Is.Null);
            Assert.That(missingWriter, Is.Null);
        }

        [Test]
        public void ClosedException_MessageConstructors()
        {
            var inner = new FormatException();
            Assert.That(new OnityChannelClosedException("custom").Message, Is.EqualTo("custom"));
            var withInner = new OnityChannelClosedException("custom", inner);
            Assert.That(withInner.Message, Is.EqualTo("custom"));
            Assert.That(withInner.InnerException, Is.SameAs(inner));
            Assert.That(new OnityChannelClosedException(inner).InnerException, Is.SameAs(inner));
            Assert.That(new OnityChannelClosedException(), Is.InstanceOf<InvalidOperationException>());
        }
    }
}
