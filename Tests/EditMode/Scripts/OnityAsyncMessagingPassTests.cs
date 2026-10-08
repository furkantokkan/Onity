using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using NUnit.Framework;
using Onity.Messaging;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Pins the delivery pass of <see cref="AsyncMessageChannel{TMessage}"/>: handlers that complete
    /// synchronously run inline, the first pending one continues the pass in order, failures and
    /// cancellation complete the returned task instead of throwing, every pass keeps the handlers it started
    /// with, and the subscription bookkeeping matches the synchronous channel.
    /// </summary>
    [TestFixture]
    public sealed class OnityAsyncMessagingPassTests
    {
        [Test]
        public void PublishAsync_AllHandlersSynchronous_CompletesSynchronouslyInOrder()
        {
            using AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            List<string> received = new List<string>();
            SubscribeNamed(channel, received, "A");
            SubscribeNamed(channel, received, "B");
            SubscribeNamed(channel, received, "C");

            ValueTask publish = channel.PublishAsync(1, CancellationToken.None);

            Assert.That(publish.IsCompletedSuccessfully, Is.True);
            publish.GetAwaiter().GetResult();
            Assert.That(received, Is.EqualTo(new[] { "A:1", "B:1", "C:1" }));
        }

        [Test]
        public void PublishAsync_WithoutSubscribers_CompletesSynchronously()
        {
            using AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();

            ValueTask publish = channel.PublishAsync(1, CancellationToken.None);

            Assert.That(publish.IsCompletedSuccessfully, Is.True);
        }

        [Test]
        public async Task PublishAsync_FirstPendingHandler_ContinuesThePassInOrder()
        {
            using AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            List<string> received = new List<string>();
            TaskCompletionSource<bool> gate = new TaskCompletionSource<bool>();
            SubscribeNamed(channel, received, "A");
            channel.Subscribe(
                (value, token) =>
                {
                    received.Add("B:" + value);
                    return new ValueTask(gate.Task);
                });
            SubscribeNamed(channel, received, "C");

            ValueTask publish = channel.PublishAsync(1, CancellationToken.None);

            Assert.That(publish.IsCompleted, Is.False);
            Assert.That(received, Is.EqualTo(new[] { "A:1", "B:1" }));

            gate.SetResult(true);
            await publish;

            Assert.That(received, Is.EqualTo(new[] { "A:1", "B:1", "C:1" }));
        }

        [Test]
        public void PublishAsync_HandlerThrowsSynchronously_ReturnsFaultedTask()
        {
            using AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            InvalidOperationException failure = new InvalidOperationException("handler failed");
            int laterCount = 0;
            channel.Subscribe((value, token) => throw failure);
            channel.Subscribe(
                (value, token) =>
                {
                    laterCount++;
                    return default;
                });

            ValueTask publish = default;

            Assert.DoesNotThrow(() => publish = channel.PublishAsync(1, CancellationToken.None));
            Assert.That(publish.IsFaulted, Is.True);

            InvalidOperationException thrown =
                Assert.Throws<InvalidOperationException>(() => publish.GetAwaiter().GetResult());

            Assert.That(thrown, Is.SameAs(failure));
            Assert.That(laterCount, Is.EqualTo(0));
        }

        [Test]
        public void PublishAsync_HandlerThrowsOperationCanceled_ReturnsCanceledTask()
        {
            using AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            channel.Subscribe((value, token) => throw new OperationCanceledException());

            ValueTask publish = default;

            Assert.DoesNotThrow(() => publish = channel.PublishAsync(1, CancellationToken.None));
            Assert.That(publish.IsCanceled, Is.True);
        }

        [Test]
        public void PublishAsync_HandlerReturnsFaultedTask_ReturnsFaultedTask()
        {
            using AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            InvalidOperationException failure = new InvalidOperationException("task failed");
            channel.Subscribe((value, token) => new ValueTask(Task.FromException(failure)));

            ValueTask publish = channel.PublishAsync(1, CancellationToken.None);

            Assert.That(publish.IsFaulted, Is.True);

            InvalidOperationException thrown =
                Assert.Throws<InvalidOperationException>(() => publish.GetAwaiter().GetResult());

            Assert.That(thrown, Is.SameAs(failure));
        }

        [Test]
        public void PublishAsync_CanceledToken_ReturnsCanceledTaskWithoutDelivering()
        {
            using AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            using CancellationTokenSource source = new CancellationTokenSource();
            int delivered = 0;
            channel.Subscribe(
                (value, token) =>
                {
                    delivered++;
                    return default;
                });
            source.Cancel();

            ValueTask publish = default;

            Assert.DoesNotThrow(() => publish = channel.PublishAsync(1, source.Token));
            Assert.That(publish.IsCanceled, Is.True);
            Assert.That(delivered, Is.EqualTo(0));
        }

        [Test]
        public void PublishAsync_CanceledTokenWithoutSubscribers_ReturnsCanceledTask()
        {
            using AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            using CancellationTokenSource source = new CancellationTokenSource();
            source.Cancel();

            ValueTask publish = channel.PublishAsync(1, source.Token);

            Assert.That(publish.IsCanceled, Is.True);
        }

        [Test]
        public void PublishAsync_DisposedChannel_ReturnsFaultedTaskEvenWithCanceledToken()
        {
            AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            using CancellationTokenSource source = new CancellationTokenSource();
            source.Cancel();
            channel.Dispose();

            ValueTask publish = default;

            Assert.DoesNotThrow(() => publish = channel.PublishAsync(1, source.Token));
            Assert.That(publish.IsFaulted, Is.True);
            Assert.Throws<ObjectDisposedException>(() => publish.GetAwaiter().GetResult());
        }

        [Test]
        public async Task PublishAsync_TokenCanceledWhileHandlerPending_StopsBeforeTheNextHandler()
        {
            using AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            using CancellationTokenSource source = new CancellationTokenSource();
            TaskCompletionSource<bool> gate = new TaskCompletionSource<bool>();
            int laterCount = 0;
            channel.Subscribe((value, token) => new ValueTask(gate.Task));
            channel.Subscribe(
                (value, token) =>
                {
                    laterCount++;
                    return default;
                });

            ValueTask publish = channel.PublishAsync(1, source.Token);
            source.Cancel();
            gate.SetResult(true);

            try
            {
                await publish;
                Assert.Fail("Expected the publish to end canceled.");
            }
            catch (OperationCanceledException)
            {
            }

            Assert.That(laterCount, Is.EqualTo(0));
        }

        [Test]
        public async Task PublishAsync_HandlerDisposedDuringPass_StillReceivesThatPass()
        {
            using AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            List<string> received = new List<string>();
            IDisposable second = null;
            channel.Subscribe(
                (value, token) =>
                {
                    received.Add("A:" + value);
                    second.Dispose();
                    return default;
                });
            second = SubscribeNamed(channel, received, "B");

            await channel.PublishAsync(1, CancellationToken.None);
            await channel.PublishAsync(2, CancellationToken.None);

            Assert.That(received, Is.EqualTo(new[] { "A:1", "B:1", "A:2" }));
            Assert.That(channel.SubscriberCount, Is.EqualTo(1));
        }

        [Test]
        public async Task PublishAsync_HandlerAddedDuringPass_FirstReceivesTheNextPass()
        {
            using AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            List<string> received = new List<string>();
            bool isAdded = false;
            channel.Subscribe(
                (value, token) =>
                {
                    received.Add("A:" + value);

                    if (!isAdded)
                    {
                        isAdded = true;
                        SubscribeNamed(channel, received, "C");
                    }

                    return default;
                });
            SubscribeNamed(channel, received, "B");

            await channel.PublishAsync(1, CancellationToken.None);
            await channel.PublishAsync(2, CancellationToken.None);

            Assert.That(received, Is.EqualTo(new[] { "A:1", "B:1", "A:2", "B:2", "C:2" }));
        }

        [Test]
        public async Task PublishAsync_NestedPass_KeepsItsOwnStartSet()
        {
            using AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            List<string> received = new List<string>();
            channel.Subscribe(
                (value, token) =>
                {
                    received.Add("A:" + value);

                    if (value != 1)
                    {
                        return default;
                    }

                    SubscribeNamed(channel, received, "C");
                    return channel.PublishAsync(2, token);
                });
            SubscribeNamed(channel, received, "B");

            await channel.PublishAsync(1, CancellationToken.None);

            Assert.That(received, Is.EqualTo(new[] { "A:1", "A:2", "B:2", "C:2", "B:1" }));
        }

        [Test]
        public async Task PublishAsync_OverlappingPasses_KeepTheirOwnStartSets()
        {
            using AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            List<string> received = new List<string>();
            TaskCompletionSource<bool> gate = new TaskCompletionSource<bool>();
            channel.Subscribe(
                (value, token) =>
                {
                    received.Add("A:" + value);
                    return value == 1 ? new ValueTask(gate.Task) : default;
                });
            IDisposable second = SubscribeNamed(channel, received, "B");

            ValueTask first = channel.PublishAsync(1, CancellationToken.None);
            second.Dispose();

            Assert.That(channel.SubscriberCount, Is.EqualTo(2));

            await channel.PublishAsync(2, CancellationToken.None);

            Assert.That(first.IsCompleted, Is.False);

            gate.SetResult(true);
            await first;

            Assert.That(received, Is.EqualTo(new[] { "A:1", "A:2", "B:1" }));
            Assert.That(channel.SubscriberCount, Is.EqualTo(1));
        }

        [Test]
        public async Task PublishAsync_RepeatedRemovalsDuringPasses_KeepEveryPassConsistent()
        {
            using AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            List<int> transientReceived = new List<int>();
            IDisposable transient = null;
            int residentSum = 0;
            channel.Subscribe(
                (value, token) =>
                {
                    transient?.Dispose();
                    transient = channel.Subscribe(
                        (transientValue, transientToken) =>
                        {
                            transientReceived.Add(transientValue);
                            return default;
                        });
                    return default;
                });
            channel.Subscribe(
                (value, token) =>
                {
                    residentSum += value;
                    return default;
                });

            for (int value = 1; value <= 6; value++)
            {
                await channel.PublishAsync(value, CancellationToken.None);
            }

            // The transient added during pass n is in the start set of pass n + 1, where it is also removed.
            Assert.That(transientReceived, Is.EqualTo(new[] { 2, 3, 4, 5, 6 }));
            Assert.That(residentSum, Is.EqualTo(21));
            Assert.That(channel.SubscriberCount, Is.EqualTo(3));
        }

        [Test]
        public async Task PublishAsync_HandlerThrowsAfterPendingHandler_FaultsAndEndsThePass()
        {
            using AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            TaskCompletionSource<bool> gate = new TaskCompletionSource<bool>();
            InvalidOperationException failure = new InvalidOperationException("handler failed");
            channel.Subscribe((value, token) => new ValueTask(gate.Task));
            channel.Subscribe((value, token) => throw failure);
            IDisposable third = channel.Subscribe((value, token) => default);

            ValueTask publish = channel.PublishAsync(1, CancellationToken.None);
            third.Dispose();

            Assert.That(channel.SubscriberCount, Is.EqualTo(3));

            gate.SetResult(true);

            try
            {
                await publish;
                Assert.Fail("Expected the publish to fault.");
            }
            catch (InvalidOperationException exception)
            {
                Assert.That(exception, Is.SameAs(failure));
            }

            Assert.That(channel.SubscriberCount, Is.EqualTo(2));
        }

        [Test]
        public void PublishAsync_SynchronousFailure_EndsThePassSoRemovalIsImmediateAgain()
        {
            using AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            IDisposable first = channel.Subscribe((value, token) => throw new InvalidOperationException());
            channel.Subscribe((value, token) => default);

            ValueTask publish = channel.PublishAsync(1, CancellationToken.None);

            Assert.That(publish.IsFaulted, Is.True);

            first.Dispose();

            Assert.That(channel.SubscriberCount, Is.EqualTo(1));
        }

        [Test]
        public void PublishAsync_SynchronouslyCompletedSourceTask_ConsumesTheResultOnce()
        {
            using AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            CompletedValueTaskSource source = new CompletedValueTaskSource();
            channel.Subscribe((value, token) => new ValueTask(source, 7));

            ValueTask publish = channel.PublishAsync(1, CancellationToken.None);

            Assert.That(publish.IsCompletedSuccessfully, Is.True);
            Assert.That(source.GetResultCount, Is.EqualTo(1));
            Assert.That(source.LastToken, Is.EqualTo(7));
        }

        [Test]
        public async Task DisposeOutsidePass_MovesLastIntoFreedSlot_AndMovedSubscriptionStillDisposes()
        {
            using AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            List<string> received = new List<string>();
            IDisposable first = SubscribeNamed(channel, received, "A");
            SubscribeNamed(channel, received, "B");
            IDisposable last = SubscribeNamed(channel, received, "C");

            first.Dispose();
            await channel.PublishAsync(1, CancellationToken.None);

            Assert.That(received, Is.EqualTo(new[] { "C:1", "B:1" }));

            received.Clear();
            last.Dispose();
            await channel.PublishAsync(2, CancellationToken.None);

            Assert.That(received, Is.EqualTo(new[] { "B:2" }));
            Assert.That(channel.SubscriberCount, Is.EqualTo(1));
        }

        [Test]
        public void DisposeSubscription_TwiceAndAfterChannelDispose_IsNoOp()
        {
            AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            List<string> received = new List<string>();
            IDisposable first = SubscribeNamed(channel, received, "A");
            IDisposable second = SubscribeNamed(channel, received, "B");

            first.Dispose();
            first.Dispose();

            Assert.That(channel.SubscriberCount, Is.EqualTo(1));

            channel.Dispose();

            Assert.DoesNotThrow(() => second.Dispose());
            Assert.DoesNotThrow(() => second.Dispose());
            Assert.That(channel.SubscriberCount, Is.EqualTo(0));
        }

        private static IDisposable SubscribeNamed(AsyncMessageChannel<int> channel, List<string> received, string name)
        {
            return channel.Subscribe(
                (value, token) =>
                {
                    received.Add(name + ":" + value);
                    return default;
                });
        }

        private sealed class CompletedValueTaskSource : IValueTaskSource
        {
            public int GetResultCount { get; private set; }

            public short LastToken { get; private set; }

            public ValueTaskSourceStatus GetStatus(short token)
            {
                return ValueTaskSourceStatus.Succeeded;
            }

            public void GetResult(short token)
            {
                GetResultCount++;
                LastToken = token;
            }

            public void OnCompleted(
                Action<object> continuation,
                object state,
                short token,
                ValueTaskSourceOnCompletedFlags flags)
            {
                throw new InvalidOperationException("The source is already completed.");
            }
        }
    }
}
