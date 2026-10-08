using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Messaging;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.TestTools;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Covers the message-bus async streams: ReceiveAsync, the buffered ReceiveAllAsync over a
    /// synchronous subscriber (with each overflow policy), and SubscribeQueued backpressure.
    /// </summary>
    [TestFixture]
    public sealed class OnityMessagingAsyncStreamEditModeTests
    {
        [Test]
        public void ReceiveAsync_CompletesWithNextMessage()
        {
            using MessageChannel<int> channel = new MessageChannel<int>();
            OnityTask<int> receive = channel.ReceiveAsync();

            Assert.That(receive.IsCompleted, Is.False);

            channel.Publish(7);

            Assert.That(receive.GetAwaiter().GetResult(), Is.EqualTo(7));
        }

        [Test]
        public void ReceiveAsync_Predicate_SkipsNonMatchingMessages()
        {
            using MessageChannel<int> channel = new MessageChannel<int>();
            OnityTask<int> receive = channel.ReceiveAsync(message => message > 5);

            channel.Publish(1);

            Assert.That(receive.IsCompleted, Is.False);

            channel.Publish(6);

            Assert.That(receive.GetAwaiter().GetResult(), Is.EqualTo(6));
        }

        [Test]
        public void ReceiveAsync_Canceled_UnsubscribesAndGetsNoLateMessage()
        {
            using MessageChannel<int> channel = new MessageChannel<int>();
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            OnityTask<int> receive = channel.ReceiveAsync(cancellation.Token);

            Assert.That(channel.SubscriberCount, Is.EqualTo(1));

            cancellation.Cancel();

            Assert.That(receive.IsCanceled, Is.True);
            Assert.That(channel.SubscriberCount, Is.EqualTo(0));

            channel.Publish(3);

            Assert.That(() => receive.GetAwaiter().GetResult(), Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public void ReceiveAsync_LoopThatReceivesAgain_SeesEachMessageOnce()
        {
            using MessageChannel<int> channel = new MessageChannel<int>();
            List<int> received = new List<int>();

            ReceiveLoopAsync(channel, received, 3).Forget();

            channel.Publish(1);
            channel.Publish(2);
            channel.Publish(3);
            channel.Publish(4);

            Assert.That(received, Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void ReceiveAsync_DisposedChannel_IsCanceled()
        {
            MessageChannel<int> channel = new MessageChannel<int>();
            channel.Dispose();

            OnityTask<int> receive = channel.ReceiveAsync();

            Assert.That(receive.IsCanceled, Is.True);
            Assert.That(() => receive.GetAwaiter().GetResult(), Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public async Task ReceiveAllAsync_YieldsMessagesInPublishOrder()
        {
            using MessageChannel<int> channel = new MessageChannel<int>();
            IOnityAsyncEnumerator<int> enumerator = channel.ReceiveAllAsync(8).GetAsyncEnumerator();

            try
            {
                OnityTask<bool> first = enumerator.MoveNextAsync();
                channel.Publish(1);
                channel.Publish(2);
                channel.Publish(3);

                Assert.That(await first, Is.True);
                Assert.That(enumerator.Current, Is.EqualTo(1));
                Assert.That(await enumerator.MoveNextAsync(), Is.True);
                Assert.That(enumerator.Current, Is.EqualTo(2));
                Assert.That(await enumerator.MoveNextAsync(), Is.True);
                Assert.That(enumerator.Current, Is.EqualTo(3));
            }
            finally
            {
                await enumerator.DisposeAsync();
            }
        }

        [Test]
        public async Task ReceiveAllAsync_FaultOverflow_DrainsAcceptedMessagesThenFaults()
        {
            using MessageChannel<int> channel = new MessageChannel<int>();
            IOnityAsyncEnumerator<int> enumerator = channel.ReceiveAllAsync(2).GetAsyncEnumerator();
            OnityTask<bool> first = enumerator.MoveNextAsync();

            channel.Publish(1);
            channel.Publish(2);
            channel.Publish(3);
            channel.Publish(4);

            Assert.That(channel.SubscriberCount, Is.EqualTo(0));
            Assert.That(await first, Is.True);
            Assert.That(enumerator.Current, Is.EqualTo(1));
            Assert.That(await enumerator.MoveNextAsync(), Is.True);
            Assert.That(enumerator.Current, Is.EqualTo(2));
            Assert.That(await enumerator.MoveNextAsync(), Is.True);
            Assert.That(enumerator.Current, Is.EqualTo(3));

            OnityTask<bool> faulted = enumerator.MoveNextAsync();

            Assert.That(faulted.IsFaulted, Is.True);
            Assert.That(() => faulted.GetAwaiter().GetResult(), Throws.TypeOf<InvalidOperationException>());
            await enumerator.DisposeAsync();
        }

        [Test]
        public async Task ReceiveAllAsync_DropOldest_KeepsNewestMessages()
        {
            using MessageChannel<int> channel = new MessageChannel<int>();
            IOnityAsyncEnumerator<int> enumerator =
                channel.ReceiveAllAsync(2, OnityBufferOverflow.DropOldest).GetAsyncEnumerator();

            try
            {
                OnityTask<bool> first = enumerator.MoveNextAsync();
                channel.Publish(1);
                channel.Publish(2);
                channel.Publish(3);
                channel.Publish(4);

                Assert.That(await first, Is.True);
                Assert.That(enumerator.Current, Is.EqualTo(1));
                Assert.That(await enumerator.MoveNextAsync(), Is.True);
                Assert.That(enumerator.Current, Is.EqualTo(3));
                Assert.That(await enumerator.MoveNextAsync(), Is.True);
                Assert.That(enumerator.Current, Is.EqualTo(4));
                Assert.That(channel.SubscriberCount, Is.EqualTo(1));
            }
            finally
            {
                await enumerator.DisposeAsync();
            }
        }

        [Test]
        public async Task ReceiveAllAsync_DropNewest_KeepsOldestMessages()
        {
            using MessageChannel<int> channel = new MessageChannel<int>();
            IOnityAsyncEnumerator<int> enumerator =
                channel.ReceiveAllAsync(2, OnityBufferOverflow.DropNewest).GetAsyncEnumerator();

            try
            {
                OnityTask<bool> first = enumerator.MoveNextAsync();
                channel.Publish(1);
                channel.Publish(2);
                channel.Publish(3);
                channel.Publish(4);

                Assert.That(await first, Is.True);
                Assert.That(enumerator.Current, Is.EqualTo(1));
                Assert.That(await enumerator.MoveNextAsync(), Is.True);
                Assert.That(enumerator.Current, Is.EqualTo(2));
                Assert.That(await enumerator.MoveNextAsync(), Is.True);
                Assert.That(enumerator.Current, Is.EqualTo(3));

                OnityTask<bool> pending = enumerator.MoveNextAsync();

                Assert.That(pending.IsCompleted, Is.False);

                channel.Publish(5);

                Assert.That(await pending, Is.True);
                Assert.That(enumerator.Current, Is.EqualTo(5));
            }
            finally
            {
                await enumerator.DisposeAsync();
            }
        }

        [Test]
        public void ReceiveAllAsync_Dispose_EndsPendingMoveAndUnsubscribes()
        {
            using MessageChannel<int> channel = new MessageChannel<int>();
            IOnityAsyncEnumerator<int> enumerator = channel.ReceiveAllAsync(4).GetAsyncEnumerator();
            OnityTask<bool> pending = enumerator.MoveNextAsync();

            Assert.That(channel.SubscriberCount, Is.EqualTo(1));

            enumerator.DisposeAsync().GetAwaiter().GetResult();

            Assert.That(pending.GetAwaiter().GetResult(), Is.False);
            Assert.That(channel.SubscriberCount, Is.EqualTo(0));
        }

        [Test]
        public void ReceiveAllAsync_Cancel_CancelsPendingMoveAndUnsubscribes()
        {
            using MessageChannel<int> channel = new MessageChannel<int>();
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            IOnityAsyncEnumerator<int> enumerator =
                channel.ReceiveAllAsync(4).GetAsyncEnumerator(cancellation.Token);
            OnityTask<bool> pending = enumerator.MoveNextAsync();

            cancellation.Cancel();

            Assert.That(pending.IsCanceled, Is.True);
            Assert.That(channel.SubscriberCount, Is.EqualTo(0));
            Assert.That(() => pending.GetAwaiter().GetResult(), Throws.InstanceOf<OperationCanceledException>());

            OnityTask<bool> next = enumerator.MoveNextAsync();

            Assert.That(next.IsCanceled, Is.True);
            Assert.That(() => next.GetAwaiter().GetResult(), Throws.InstanceOf<OperationCanceledException>());
            enumerator.DisposeAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void ReceiveAllAsync_InvalidArguments_Throw()
        {
            using MessageChannel<int> channel = new MessageChannel<int>();

            Assert.That(
                () => ((ISubscriber<int>)null).ReceiveAllAsync(1),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => channel.ReceiveAllAsync(0), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => channel.ReceiveAllAsync(1, (OnityBufferOverflow)42),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public async Task SubscribeQueued_RunsHandlersOneAtATimeInPublishOrder()
        {
            using AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            List<string> log = new List<string>();
            OnityTaskCompletionSource gate = new OnityTaskCompletionSource();
            using IDisposable subscription = channel.SubscribeQueued(
                async (message, _) =>
                {
                    log.Add("start " + message);

                    if (message == 1)
                    {
                        await gate.Task;
                    }

                    log.Add("end " + message);
                },
                4,
                CancellationToken.None);

            await channel.PublishAsync(1, CancellationToken.None);
            await channel.PublishAsync(2, CancellationToken.None);
            await channel.PublishAsync(3, CancellationToken.None);

            Assert.That(log, Is.EqualTo(new[] { "start 1" }));

            gate.TrySetResult();

            Assert.That(
                log,
                Is.EqualTo(new[] { "start 1", "end 1", "start 2", "end 2", "start 3", "end 3" }));
        }

        [Test]
        public async Task SubscribeQueued_FullQueue_HoldsPublisherUntilSpaceFrees()
        {
            using AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            OnityTaskCompletionSource gate = new OnityTaskCompletionSource();
            List<int> handled = new List<int>();
            using IDisposable subscription = channel.SubscribeQueued(
                async (message, _) =>
                {
                    if (message == 1)
                    {
                        await gate.Task;
                    }

                    handled.Add(message);
                },
                1,
                CancellationToken.None);

            await channel.PublishAsync(1, CancellationToken.None);
            await channel.PublishAsync(2, CancellationToken.None);
            Task third = channel.PublishAsync(3, CancellationToken.None).AsTask();

            Assert.That(third.IsCompleted, Is.False);

            gate.TrySetResult();
            await third;

            Assert.That(handled, Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public async Task SubscribeQueued_PublisherCanceledWhileWaiting_ThrowsAndSkipsMessage()
        {
            using AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            OnityTaskCompletionSource gate = new OnityTaskCompletionSource();
            List<int> handled = new List<int>();
            using IDisposable subscription = channel.SubscribeQueued(
                async (message, _) =>
                {
                    if (message == 1)
                    {
                        await gate.Task;
                    }

                    handled.Add(message);
                },
                1,
                CancellationToken.None);

            await channel.PublishAsync(1, CancellationToken.None);
            await channel.PublishAsync(2, CancellationToken.None);
            using CancellationTokenSource publisher = new CancellationTokenSource();
            Task third = channel.PublishAsync(3, publisher.Token).AsTask();

            publisher.Cancel();

            try
            {
                await third;
                Assert.Fail("Expected the waiting publish to be canceled.");
            }
            catch (OperationCanceledException)
            {
            }

            gate.TrySetResult();

            Assert.That(handled, Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public async Task SubscribeQueued_LifetimeEnd_UnsubscribesAndReleasesWaitingPublishers()
        {
            using AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            using CancellationTokenSource lifetime = new CancellationTokenSource();
            OnityTaskCompletionSource gate = new OnityTaskCompletionSource();
            List<int> handled = new List<int>();
            CancellationToken handlerToken = default;
            IDisposable subscription = channel.SubscribeQueued(
                async (message, cancellationToken) =>
                {
                    handlerToken = cancellationToken;

                    if (message == 1)
                    {
                        await gate.Task.AttachExternalCancellation(cancellationToken);
                    }

                    handled.Add(message);
                },
                1,
                lifetime.Token);

            await channel.PublishAsync(1, CancellationToken.None);
            await channel.PublishAsync(2, CancellationToken.None);
            Task third = channel.PublishAsync(3, CancellationToken.None).AsTask();

            Assert.That(third.IsCompleted, Is.False);

            lifetime.Cancel();
            await third;

            Assert.That(handlerToken.IsCancellationRequested, Is.True);
            Assert.That(channel.SubscriberCount, Is.EqualTo(0));
            Assert.That(handled, Is.Empty);
            subscription.Dispose();
        }

        [Test]
        public async Task SubscribeQueued_HandlerFault_IsLoggedAndNextMessageRuns()
        {
            using AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            List<int> handled = new List<int>();
            using IDisposable subscription = channel.SubscribeQueued(
                (message, _) =>
                {
                    if (message == 1)
                    {
                        throw new InvalidOperationException("queued handler failure");
                    }

                    handled.Add(message);
                    return OnityTask.CompletedTask;
                },
                4,
                CancellationToken.None);

            LogAssert.Expect(LogType.Exception, new Regex("queued handler failure"));

            await channel.PublishAsync(1, CancellationToken.None);
            await channel.PublishAsync(2, CancellationToken.None);

            Assert.That(handled, Is.EqualTo(new[] { 2 }));
        }

        [Test]
        public async Task SubscribeQueued_Dispose_StopsHandlingNewMessages()
        {
            using AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            List<int> handled = new List<int>();
            IDisposable subscription = channel.SubscribeQueued(
                (message, _) =>
                {
                    handled.Add(message);
                    return OnityTask.CompletedTask;
                },
                4,
                CancellationToken.None);

            await channel.PublishAsync(1, CancellationToken.None);
            subscription.Dispose();

            Assert.That(channel.SubscriberCount, Is.EqualTo(0));

            await channel.PublishAsync(2, CancellationToken.None);

            Assert.That(handled, Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void SubscribeQueued_InvalidArguments_Throw()
        {
            using AsyncMessageChannel<int> channel = new AsyncMessageChannel<int>();
            Func<int, CancellationToken, OnityTask> handler = (_, _) => OnityTask.CompletedTask;

            Assert.That(
                () => ((IAsyncSubscriber<int>)null).SubscribeQueued(handler, 1, CancellationToken.None),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => channel.SubscribeQueued(null, 1, CancellationToken.None),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => channel.SubscribeQueued(handler, 0, CancellationToken.None),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        private static async OnityTaskVoid ReceiveLoopAsync(ISubscriber<int> subscriber, List<int> received, int count)
        {
            while (received.Count < count)
            {
                received.Add(await subscriber.ReceiveAsync());
            }
        }
    }
}
