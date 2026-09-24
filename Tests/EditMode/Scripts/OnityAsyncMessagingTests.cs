using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Messaging;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class OnityAsyncMessagingTests
    {
        [Test]
        public async Task PublishAsync_DeliversToSubscriber()
        {
            using AsyncMessageChannel<AsyncMessage> channel = new AsyncMessageChannel<AsyncMessage>();

            List<int> received = new List<int>();
            channel.Subscribe((message, _) =>
            {
                received.Add(message.Value);
                return default;
            });

            await channel.PublishAsync(new AsyncMessage(7), CancellationToken.None);

            Assert.That(received, Is.EqualTo(new[] { 7 }));
        }

        [Test]
        public async Task PublishAsync_DeliversToHandlersInSubscriptionOrder()
        {
            using AsyncMessageChannel<AsyncMessage> channel = new AsyncMessageChannel<AsyncMessage>();

            List<int> order = new List<int>();

            channel.Subscribe(async (_, ct) =>
            {
                await Task.Yield();
                order.Add(1);
            });
            channel.Subscribe(async (_, ct) =>
            {
                await Task.Yield();
                order.Add(2);
            });
            channel.Subscribe(async (_, ct) =>
            {
                await Task.Yield();
                order.Add(3);
            });

            await channel.PublishAsync(new AsyncMessage(0), CancellationToken.None);

            Assert.That(order, Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public async Task PublishAsync_AwaitsEachHandlerBeforeStartingNext()
        {
            using AsyncMessageChannel<AsyncMessage> channel = new AsyncMessageChannel<AsyncMessage>();

            int activeHandlers = 0;
            int maxConcurrent = 0;
            List<int> completionOrder = new List<int>();

            channel.Subscribe(async (_, ct) =>
            {
                int active = Interlocked.Increment(ref activeHandlers);
                maxConcurrent = Math.Max(maxConcurrent, active);
                await Task.Yield();
                completionOrder.Add(1);
                Interlocked.Decrement(ref activeHandlers);
            });
            channel.Subscribe(async (_, ct) =>
            {
                int active = Interlocked.Increment(ref activeHandlers);
                maxConcurrent = Math.Max(maxConcurrent, active);
                await Task.Yield();
                completionOrder.Add(2);
                Interlocked.Decrement(ref activeHandlers);
            });

            await channel.PublishAsync(new AsyncMessage(0), CancellationToken.None);

            Assert.That(maxConcurrent, Is.EqualTo(1));
            Assert.That(completionOrder, Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public async Task PublishAsync_AwaitsAllSubscribers()
        {
            using AsyncMessageChannel<AsyncMessage> channel = new AsyncMessageChannel<AsyncMessage>();

            int firstCount = 0;
            int secondCount = 0;

            channel.Subscribe(async (_, ct) =>
            {
                await Task.Yield();
                firstCount++;
            });
            channel.Subscribe(async (_, ct) =>
            {
                await Task.Yield();
                secondCount++;
            });

            await channel.PublishAsync(new AsyncMessage(0), CancellationToken.None);

            Assert.That(firstCount, Is.EqualTo(1));
            Assert.That(secondCount, Is.EqualTo(1));
        }

        [Test]
        public void PublishAsync_HonorsAlreadyCancelledToken()
        {
            using AsyncMessageChannel<AsyncMessage> channel = new AsyncMessageChannel<AsyncMessage>();

            int delivered = 0;
            channel.Subscribe((_, ct) =>
            {
                delivered++;
                return default;
            });

            using CancellationTokenSource cts = new CancellationTokenSource();
            cts.Cancel();

            // Awaiting a CT-canceled async ValueTask surfaces TaskCanceledException (a subclass of
            // OperationCanceledException), so assert the cancellation family with CatchAsync, not the exact type.
            Assert.CatchAsync<OperationCanceledException>(async () =>
                await channel.PublishAsync(new AsyncMessage(0), cts.Token));
            Assert.That(delivered, Is.EqualTo(0));
        }

        [Test]
        public void PublishAsync_StopsDeliveryWhenCancelledMidPass()
        {
            using AsyncMessageChannel<AsyncMessage> channel = new AsyncMessageChannel<AsyncMessage>();

            using CancellationTokenSource cts = new CancellationTokenSource();
            int firstCount = 0;
            int secondCount = 0;

            channel.Subscribe((_, ct) =>
            {
                firstCount++;
                cts.Cancel();
                return default;
            });
            channel.Subscribe((_, ct) =>
            {
                secondCount++;
                return default;
            });

            // Awaiting a CT-canceled async ValueTask surfaces TaskCanceledException (a subclass of
            // OperationCanceledException), so assert the cancellation family with CatchAsync, not the exact type.
            Assert.CatchAsync<OperationCanceledException>(async () =>
                await channel.PublishAsync(new AsyncMessage(0), cts.Token));
            Assert.That(firstCount, Is.EqualTo(1));
            Assert.That(secondCount, Is.EqualTo(0));
        }

        [Test]
        public async Task PublishAsync_PassesTokenToHandler()
        {
            using AsyncMessageChannel<AsyncMessage> channel = new AsyncMessageChannel<AsyncMessage>();

            using CancellationTokenSource cts = new CancellationTokenSource();
            CancellationToken observed = default;

            channel.Subscribe((_, ct) =>
            {
                observed = ct;
                return default;
            });

            await channel.PublishAsync(new AsyncMessage(0), cts.Token);

            Assert.That(observed, Is.EqualTo(cts.Token));
        }

        [Test]
        public async Task Unsubscribe_StopsFurtherDelivery()
        {
            using AsyncMessageChannel<AsyncMessage> channel = new AsyncMessageChannel<AsyncMessage>();

            List<int> kept = new List<int>();
            int removedCount = 0;

            channel.Subscribe((message, _) =>
            {
                kept.Add(message.Value);
                return default;
            });
            IDisposable removable = channel.Subscribe((_, ct) =>
            {
                removedCount++;
                return default;
            });

            await channel.PublishAsync(new AsyncMessage(1), CancellationToken.None);
            removable.Dispose();
            await channel.PublishAsync(new AsyncMessage(2), CancellationToken.None);

            Assert.That(kept, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(removedCount, Is.EqualTo(1));
            Assert.That(channel.SubscriberCount, Is.EqualTo(1));
        }

        [Test]
        public async Task Unsubscribe_DuringPublishDoesNotSkipRemainingSubscribers()
        {
            using AsyncMessageChannel<AsyncMessage> channel = new AsyncMessageChannel<AsyncMessage>();

            int firstCount = 0;
            int secondCount = 0;
            IDisposable firstToken = null;

            firstToken = channel.Subscribe((_, ct) =>
            {
                firstCount++;
                firstToken.Dispose();
                return default;
            });
            channel.Subscribe((_, ct) =>
            {
                secondCount++;
                return default;
            });

            await channel.PublishAsync(new AsyncMessage(0), CancellationToken.None);
            await channel.PublishAsync(new AsyncMessage(0), CancellationToken.None);

            Assert.That(firstCount, Is.EqualTo(1));
            Assert.That(secondCount, Is.EqualTo(2));
            Assert.That(channel.SubscriberCount, Is.EqualTo(1));
        }

        [Test]
        public void Subscribe_NullHandlerThrows()
        {
            using AsyncMessageChannel<AsyncMessage> channel = new AsyncMessageChannel<AsyncMessage>();

            Assert.Throws<ArgumentNullException>(() => channel.Subscribe(null));
        }

        [Test]
        public void Operations_AfterDisposeThrow()
        {
            AsyncMessageChannel<AsyncMessage> channel = new AsyncMessageChannel<AsyncMessage>();

            channel.Dispose();

            Assert.Throws<ObjectDisposedException>(() => channel.Subscribe((_, ct) => default));
            Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await channel.PublishAsync(new AsyncMessage(0), CancellationToken.None));
        }

        private readonly struct AsyncMessage
        {
            public AsyncMessage(int value)
            {
                Value = value;
            }

            public int Value { get; }
        }
    }
}
