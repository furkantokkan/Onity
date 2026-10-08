using System;
using System.Collections.Generic;
using NUnit.Framework;
using Onity.Messaging;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Pins the subscription bookkeeping of <see cref="MessageChannel{TMessage}"/> and the per-key channels of
    /// <see cref="KeyedMessageChannel{TKey,TMessage}"/>: swap-back removal outside a pass, in-order compaction
    /// after a pass, idempotent disposal, and the pass rules for subscribers added or removed by a handler.
    /// </summary>
    [TestFixture]
    public sealed class MessageChannelSubscriptionTests
    {
        [Test]
        public void DisposeFirst_MovesLastIntoFreedSlot_AndMovedSubscriptionStillDisposes()
        {
            using MessageChannel<int> channel = new MessageChannel<int>();
            List<string> received = new List<string>();
            IDisposable first = SubscribeNamed(channel, received, "A");
            SubscribeNamed(channel, received, "B");
            IDisposable last = SubscribeNamed(channel, received, "C");

            first.Dispose();
            channel.Publish(1);

            Assert.That(received, Is.EqualTo(new[] { "C", "B" }));

            received.Clear();
            last.Dispose();
            channel.Publish(2);

            Assert.That(received, Is.EqualTo(new[] { "B" }));
            Assert.That(channel.SubscriberCount, Is.EqualTo(1));
        }

        [Test]
        public void DisposeMiddle_MovesLastIntoFreedSlot_AndMovedSubscriptionStillDisposes()
        {
            using MessageChannel<int> channel = new MessageChannel<int>();
            List<string> received = new List<string>();
            SubscribeNamed(channel, received, "A");
            IDisposable middle = SubscribeNamed(channel, received, "B");
            SubscribeNamed(channel, received, "C");
            IDisposable last = SubscribeNamed(channel, received, "D");

            middle.Dispose();
            channel.Publish(1);

            Assert.That(received, Is.EqualTo(new[] { "A", "D", "C" }));

            received.Clear();
            last.Dispose();
            channel.Publish(2);

            Assert.That(received, Is.EqualTo(new[] { "A", "C" }));
            Assert.That(channel.SubscriberCount, Is.EqualTo(2));
        }

        [Test]
        public void DisposeLast_KeepsTheOrderOfTheOthers()
        {
            using MessageChannel<int> channel = new MessageChannel<int>();
            List<string> received = new List<string>();
            IDisposable first = SubscribeNamed(channel, received, "A");
            SubscribeNamed(channel, received, "B");
            IDisposable last = SubscribeNamed(channel, received, "C");

            last.Dispose();
            channel.Publish(1);

            Assert.That(received, Is.EqualTo(new[] { "A", "B" }));

            received.Clear();
            first.Dispose();
            channel.Publish(2);

            Assert.That(received, Is.EqualTo(new[] { "B" }));
            Assert.That(channel.SubscriberCount, Is.EqualTo(1));
        }

        [Test]
        public void DisposeTwice_IsNoOp_AndKeepsTheSubscriberMovedIntoItsSlot()
        {
            using MessageChannel<int> channel = new MessageChannel<int>();
            List<string> received = new List<string>();
            IDisposable first = SubscribeNamed(channel, received, "A");
            SubscribeNamed(channel, received, "B");

            first.Dispose();
            first.Dispose();
            channel.Publish(1);

            Assert.That(received, Is.EqualTo(new[] { "B" }));
            Assert.That(channel.SubscriberCount, Is.EqualTo(1));
        }

        [Test]
        public void DisposeSubscription_AfterChannelDispose_IsNoOp()
        {
            MessageChannel<int> channel = new MessageChannel<int>();
            IDisposable subscription = channel.Subscribe(_ => { });

            channel.Dispose();

            Assert.DoesNotThrow(() => subscription.Dispose());
            Assert.DoesNotThrow(() => subscription.Dispose());
            Assert.That(channel.SubscriberCount, Is.EqualTo(0));
        }

        [Test]
        public void Subscribe_AfterDispose_Throws()
        {
            MessageChannel<int> channel = new MessageChannel<int>();
            channel.Dispose();

            Assert.That(() => channel.Subscribe(_ => { }), Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public void Subscribe_NullHandlerAfterDispose_ThrowsArgumentNullFirst()
        {
            MessageChannel<int> channel = new MessageChannel<int>();
            channel.Dispose();

            Assert.That(() => channel.Subscribe(null), Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void SubscribeDuringPublish_IsReachedLaterInTheSamePass()
        {
            using MessageChannel<int> channel = new MessageChannel<int>();
            List<string> received = new List<string>();
            bool isAdded = false;

            channel.Subscribe(
                value =>
                {
                    received.Add("A:" + value);

                    if (!isAdded)
                    {
                        isAdded = true;
                        channel.Subscribe(added => received.Add("C:" + added));
                    }
                });
            channel.Subscribe(value => received.Add("B:" + value));

            channel.Publish(1);
            channel.Publish(2);

            Assert.That(received, Is.EqualTo(new[] { "A:1", "B:1", "C:1", "A:2", "B:2", "C:2" }));
        }

        [Test]
        public void DisposeLaterSubscriberDuringPublish_SkipsIt_AndCompactsInOrderAfterThePass()
        {
            using MessageChannel<int> channel = new MessageChannel<int>();
            List<string> received = new List<string>();
            IDisposable second = null;
            int countSeenByLast = -1;

            channel.Subscribe(
                value =>
                {
                    received.Add("A:" + value);
                    second?.Dispose();
                });
            second = SubscribeNamed(channel, received, "B");
            SubscribeNamed(channel, received, "C");
            channel.Subscribe(
                value =>
                {
                    received.Add("D:" + value);
                    countSeenByLast = channel.SubscriberCount;
                });

            channel.Publish(1);

            // The cleared slot still counts until the pass ends.
            Assert.That(received, Is.EqualTo(new[] { "A:1", "C", "D:1" }));
            Assert.That(countSeenByLast, Is.EqualTo(4));
            Assert.That(channel.SubscriberCount, Is.EqualTo(3));

            received.Clear();
            channel.Publish(2);

            // Compaction after a pass keeps the order; it does not move the last subscriber forward.
            Assert.That(received, Is.EqualTo(new[] { "A:2", "C", "D:2" }));
        }

        [Test]
        public void NestedPublish_RemovalIsCompactedOnlyAfterTheOutermostPass()
        {
            using MessageChannel<int> channel = new MessageChannel<int>();
            List<string> received = new List<string>();
            IDisposable removable = null;
            int countAfterNestedPass = -1;

            channel.Subscribe(
                value =>
                {
                    received.Add("A:" + value);

                    if (value == 1)
                    {
                        channel.Publish(2);
                        countAfterNestedPass = channel.SubscriberCount;
                    }
                    else
                    {
                        removable.Dispose();
                    }
                });
            removable = SubscribeNamed(channel, received, "B");
            SubscribeNamed(channel, received, "C");

            channel.Publish(1);

            Assert.That(received, Is.EqualTo(new[] { "A:1", "A:2", "C", "C" }));
            Assert.That(countAfterNestedPass, Is.EqualTo(3));
            Assert.That(channel.SubscriberCount, Is.EqualTo(2));
        }

        [Test]
        public void ThrowingHandler_PropagatesSkipsTheRest_AndLeavesTheChannelConsistent()
        {
            using MessageChannel<int> channel = new MessageChannel<int>();
            List<string> received = new List<string>();
            InvalidOperationException failure = new InvalidOperationException("handler failed");
            IDisposable third = null;
            bool shouldThrow = true;

            IDisposable first = channel.Subscribe(
                value =>
                {
                    received.Add("A");

                    if (shouldThrow)
                    {
                        third.Dispose();
                        throw failure;
                    }
                });
            SubscribeNamed(channel, received, "B");
            third = SubscribeNamed(channel, received, "C");

            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => channel.Publish(1));

            Assert.That(thrown, Is.SameAs(failure));
            Assert.That(received, Is.EqualTo(new[] { "A" }));
            Assert.That(channel.SubscriberCount, Is.EqualTo(2));

            shouldThrow = false;
            received.Clear();
            channel.Publish(2);

            Assert.That(received, Is.EqualTo(new[] { "A", "B" }));

            // Outside a pass, removal is immediate again.
            first.Dispose();

            Assert.That(channel.SubscriberCount, Is.EqualTo(1));
        }

        [Test]
        public void DisposeChannelDuringPublish_EndsThePassQuietly()
        {
            MessageChannel<int> channel = new MessageChannel<int>();
            List<string> received = new List<string>();

            channel.Subscribe(
                _ =>
                {
                    received.Add("A");
                    channel.Dispose();
                });
            SubscribeNamed(channel, received, "B");

            Assert.DoesNotThrow(() => channel.Publish(1));
            Assert.That(received, Is.EqualTo(new[] { "A" }));
            Assert.That(channel.SubscriberCount, Is.EqualTo(0));
            Assert.That(() => channel.Publish(2), Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public void SameHandlerSubscribedTwice_IsDeliveredTwice_AndDisposedIndependently()
        {
            using MessageChannel<int> channel = new MessageChannel<int>();
            int callCount = 0;
            MessageHandler<int> handler = _ => callCount++;

            IDisposable first = channel.Subscribe(handler);
            channel.Subscribe(handler);
            channel.Publish(1);

            Assert.That(callCount, Is.EqualTo(2));

            first.Dispose();
            channel.Publish(2);

            Assert.That(callCount, Is.EqualTo(3));
            Assert.That(channel.SubscriberCount, Is.EqualTo(1));
        }

        [Test]
        public void ManySubscribers_DisposedInMixedOrder_LeaveExactlyTheOthers()
        {
            using MessageChannel<int> channel = new MessageChannel<int>();
            List<int> received = new List<int>();
            IDisposable[] subscriptions = new IDisposable[20];

            for (int i = 0; i < subscriptions.Length; i++)
            {
                int id = i;
                subscriptions[i] = channel.Subscribe(_ => received.Add(id));
            }

            for (int i = subscriptions.Length - 2; i >= 0; i -= 2)
            {
                subscriptions[i].Dispose();
            }

            channel.Publish(1);
            received.Sort();

            Assert.That(received, Is.EqualTo(new[] { 1, 3, 5, 7, 9, 11, 13, 15, 17, 19 }));
            Assert.That(channel.SubscriberCount, Is.EqualTo(10));

            for (int i = 1; i < subscriptions.Length; i += 2)
            {
                subscriptions[i].Dispose();
            }

            received.Clear();
            channel.Publish(2);

            Assert.That(received, Is.Empty);
            Assert.That(channel.SubscriberCount, Is.EqualTo(0));
        }

        [Test]
        public void Keyed_PublishNullKey_Throws()
        {
            using KeyedMessageChannel<string, int> channel = new KeyedMessageChannel<string, int>();

            Assert.That(() => channel.Publish(null, 1), Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void Keyed_DisposeFirstOfAKey_MovesLastIntoFreedSlot_AndKeepsTheKey()
        {
            using KeyedMessageChannel<string, int> channel = new KeyedMessageChannel<string, int>();
            List<string> received = new List<string>();
            IDisposable first = channel.Subscribe("A", _ => received.Add("X"));
            channel.Subscribe("A", _ => received.Add("Y"));
            IDisposable last = channel.Subscribe("A", _ => received.Add("Z"));

            first.Dispose();
            channel.Publish("A", 1);

            Assert.That(received, Is.EqualTo(new[] { "Z", "Y" }));

            received.Clear();
            last.Dispose();
            channel.Publish("A", 2);

            Assert.That(received, Is.EqualTo(new[] { "Y" }));
            Assert.That(channel.GetSubscriberCount("A"), Is.EqualTo(1));
            Assert.That(channel.KeyCount, Is.EqualTo(1));
        }

        [Test]
        public void Keyed_DisposeSubscription_AfterChannelDispose_IsNoOp()
        {
            KeyedMessageChannel<int, int> channel = new KeyedMessageChannel<int, int>();
            IDisposable subscription = channel.Subscribe(3, _ => { });

            channel.Dispose();

            Assert.DoesNotThrow(() => subscription.Dispose());
            Assert.DoesNotThrow(() => subscription.Dispose());
        }

        private static IDisposable SubscribeNamed(MessageChannel<int> channel, List<string> received, string name)
        {
            return channel.Subscribe(_ => received.Add(name));
        }
    }
}
