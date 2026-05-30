using System;
using System.Collections.Generic;
using NUnit.Framework;
using Onity.Messaging;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class OnityKeyedMessagingTests
    {
        [Test]
        public void Publish_ReachesOnlySubscribersForKey()
        {
            using KeyedMessageChannel<string, KeyedMessage> channel =
                new KeyedMessageChannel<string, KeyedMessage>();

            List<int> aReceived = new List<int>();
            List<int> bReceived = new List<int>();

            channel.Subscribe("A", message => aReceived.Add(message.Value));
            channel.Subscribe("B", message => bReceived.Add(message.Value));

            channel.Publish("A", new KeyedMessage(10));

            Assert.That(aReceived, Is.EqualTo(new[] { 10 }));
            Assert.That(bReceived, Is.Empty);
        }

        [Test]
        public void Publish_DoesNotCrossDeliverBetweenKeys()
        {
            using KeyedMessageChannel<string, KeyedMessage> channel =
                new KeyedMessageChannel<string, KeyedMessage>();

            List<int> aReceived = new List<int>();
            List<int> bReceived = new List<int>();

            channel.Subscribe("A", message => aReceived.Add(message.Value));
            channel.Subscribe("B", message => bReceived.Add(message.Value));

            channel.Publish("A", new KeyedMessage(1));
            channel.Publish("B", new KeyedMessage(2));
            channel.Publish("A", new KeyedMessage(3));

            Assert.That(aReceived, Is.EqualTo(new[] { 1, 3 }));
            Assert.That(bReceived, Is.EqualTo(new[] { 2 }));
        }

        [Test]
        public void Publish_DeliversToAllSubscribersOfSameKey()
        {
            using KeyedMessageChannel<string, KeyedMessage> channel =
                new KeyedMessageChannel<string, KeyedMessage>();

            int firstCount = 0;
            int secondCount = 0;

            channel.Subscribe("A", _ => firstCount++);
            channel.Subscribe("A", _ => secondCount++);

            channel.Publish("A", new KeyedMessage(0));

            Assert.That(firstCount, Is.EqualTo(1));
            Assert.That(secondCount, Is.EqualTo(1));
            Assert.That(channel.GetSubscriberCount("A"), Is.EqualTo(2));
        }

        [Test]
        public void Unsubscribe_StopsDeliveryForThatHandlerOnly()
        {
            using KeyedMessageChannel<string, KeyedMessage> channel =
                new KeyedMessageChannel<string, KeyedMessage>();

            List<int> kept = new List<int>();
            int removedCount = 0;

            channel.Subscribe("A", message => kept.Add(message.Value));
            IDisposable removable = channel.Subscribe("A", _ => removedCount++);

            channel.Publish("A", new KeyedMessage(1));
            removable.Dispose();
            channel.Publish("A", new KeyedMessage(2));

            Assert.That(kept, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(removedCount, Is.EqualTo(1));
            Assert.That(channel.GetSubscriberCount("A"), Is.EqualTo(1));
        }

        [Test]
        public void Unsubscribe_LeavesOtherKeysUntouched()
        {
            using KeyedMessageChannel<string, KeyedMessage> channel =
                new KeyedMessageChannel<string, KeyedMessage>();

            int aCount = 0;
            int bCount = 0;

            IDisposable aToken = channel.Subscribe("A", _ => aCount++);
            channel.Subscribe("B", _ => bCount++);

            aToken.Dispose();

            channel.Publish("A", new KeyedMessage(0));
            channel.Publish("B", new KeyedMessage(0));

            Assert.That(aCount, Is.EqualTo(0));
            Assert.That(bCount, Is.EqualTo(1));
        }

        [Test]
        public void Publish_ToUnknownKeyDoesNothing()
        {
            using KeyedMessageChannel<string, KeyedMessage> channel =
                new KeyedMessageChannel<string, KeyedMessage>();

            int received = 0;
            channel.Subscribe("A", _ => received++);

            Assert.DoesNotThrow(() => channel.Publish("Z", new KeyedMessage(99)));
            Assert.That(received, Is.EqualTo(0));
            Assert.That(channel.GetSubscriberCount("Z"), Is.EqualTo(0));
        }

        [Test]
        public void Unsubscribe_DuringPublishDoesNotSkipRemainingSubscribers()
        {
            using KeyedMessageChannel<int, KeyedMessage> channel =
                new KeyedMessageChannel<int, KeyedMessage>();

            int firstCount = 0;
            int secondCount = 0;
            IDisposable firstToken = null;

            firstToken = channel.Subscribe(1, _ =>
            {
                firstCount++;
                firstToken.Dispose();
            });
            channel.Subscribe(1, _ => secondCount++);

            channel.Publish(1, new KeyedMessage(0));
            channel.Publish(1, new KeyedMessage(0));

            Assert.That(firstCount, Is.EqualTo(1));
            Assert.That(secondCount, Is.EqualTo(2));
            Assert.That(channel.GetSubscriberCount(1), Is.EqualTo(1));
        }

        [Test]
        public void Subscribe_NullKeyThrows()
        {
            using KeyedMessageChannel<string, KeyedMessage> channel =
                new KeyedMessageChannel<string, KeyedMessage>();

            Assert.Throws<ArgumentNullException>(() => channel.Subscribe(null, _ => { }));
        }

        [Test]
        public void Subscribe_NullHandlerThrows()
        {
            using KeyedMessageChannel<string, KeyedMessage> channel =
                new KeyedMessageChannel<string, KeyedMessage>();

            Assert.Throws<ArgumentNullException>(() => channel.Subscribe("A", null));
        }

        [Test]
        public void Operations_AfterDisposeThrow()
        {
            KeyedMessageChannel<string, KeyedMessage> channel =
                new KeyedMessageChannel<string, KeyedMessage>();

            channel.Dispose();

            Assert.Throws<ObjectDisposedException>(() => channel.Subscribe("A", _ => { }));
            Assert.Throws<ObjectDisposedException>(() => channel.Publish("A", new KeyedMessage(0)));
        }

        [Test]
        public void KeyCount_TracksDistinctSubscribedKeys()
        {
            using KeyedMessageChannel<string, KeyedMessage> channel =
                new KeyedMessageChannel<string, KeyedMessage>();

            Assert.That(channel.KeyCount, Is.EqualTo(0));

            channel.Subscribe("A", _ => { });
            channel.Subscribe("A", _ => { });
            channel.Subscribe("B", _ => { });

            Assert.That(channel.KeyCount, Is.EqualTo(2));
        }

        private readonly struct KeyedMessage
        {
            public KeyedMessage(int value)
            {
                Value = value;
            }

            public int Value { get; }
        }
    }
}
