using System;
using System.Collections.Generic;
using NUnit.Framework;
using Onity.DI;
using Onity.Messaging;
using Onity.Reactive;
using Onity.Unity.Messaging;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class MessageChannelTests
    {
        [Test]
        public void Subscribe_NullHandler_Throws()
        {
            using MessageChannel<int> channel = new MessageChannel<int>();

            Assert.That(
                () => channel.Subscribe(null),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void Publish_InvokesAllActiveSubscribers()
        {
            using MessageChannel<int> channel = new MessageChannel<int>();

            int firstCallCount = 0;
            int secondCallCount = 0;

            IDisposable firstSubscription = channel.Subscribe(_ => firstCallCount++);
            IDisposable secondSubscription = channel.Subscribe(_ => secondCallCount++);

            channel.Publish(1);
            channel.Publish(2);

            Assert.That(firstCallCount, Is.EqualTo(2));
            Assert.That(secondCallCount, Is.EqualTo(2));

            firstSubscription.Dispose();
            secondSubscription.Dispose();
        }

        [Test]
        public void DisposeSubscription_StopsFutureMessages()
        {
            using MessageChannel<int> channel = new MessageChannel<int>();

            int received = 0;
            IDisposable subscription = channel.Subscribe(_ => received++);

            channel.Publish(10);
            subscription.Dispose();
            channel.Publish(20);

            Assert.That(received, Is.EqualTo(1));
        }

        [Test]
        public void DisposeDuringPublish_RemovesSubscriberWithoutBreakingIteration()
        {
            using MessageChannel<int> channel = new MessageChannel<int>();

            int selfDisposingCallCount = 0;
            int persistentCallCount = 0;
            IDisposable selfDisposing = null;

            selfDisposing = channel.Subscribe(
                _ =>
                {
                    selfDisposingCallCount++;
                    selfDisposing.Dispose();
                });

            IDisposable persistent = channel.Subscribe(_ => persistentCallCount++);

            channel.Publish(1);
            channel.Publish(2);

            Assert.That(selfDisposingCallCount, Is.EqualTo(1));
            Assert.That(persistentCallCount, Is.EqualTo(2));

            persistent.Dispose();
        }

        [Test]
        public void MessageBroker_ReturnsSharedChannelPerMessageType()
        {
            using MessageBroker broker = new MessageBroker();

            IPublisher<int> publisher = broker.GetPublisher<int>();
            ISubscriber<int> subscriber = broker.GetSubscriber<int>();

            int received = 0;
            IDisposable subscription = subscriber.Subscribe(_ => received++);
            publisher.Publish(7);

            Assert.That(received, Is.EqualTo(1));
            subscription.Dispose();
        }

        [Test]
        public void Channel_Dispose_Publish_Throws()
        {
            MessageChannel<int> channel = new MessageChannel<int>();
            channel.Dispose();

            Assert.That(
                () => channel.Publish(1),
                Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public void MessageBroker_Diagnostics_ReturnsChannelAndSubscriberCounts()
        {
            using MessageBroker broker = new MessageBroker();
            IPublisher<int> publisher = broker.GetPublisher<int>();
            ISubscriber<int> subscriber = broker.GetSubscriber<int>();

            IDisposable subscription = subscriber.Subscribe(_ => { });

            try
            {
                List<MessageChannelDiagnostics> diagnostics = new List<MessageChannelDiagnostics>(4);
                broker.GetDiagnostics(diagnostics);

                Assert.That(broker.ChannelCount, Is.EqualTo(1));
                Assert.That(diagnostics.Count, Is.EqualTo(1));
                Assert.That(diagnostics[0].MessageType, Is.EqualTo(typeof(int)));
                Assert.That(diagnostics[0].SubscriberCount, Is.EqualTo(1));

                publisher.Publish(7);
            }
            finally
            {
                subscription.Dispose();
            }
        }

        [Test]
        public void MessageBrokerExtensions_PublishAndSubscribe_ProvideManagerStyleAccess()
        {
            using MessageBroker broker = new MessageBroker();
            int received = 0;
            IDisposable subscription = broker.Subscribe<int>(_ => received++);

            broker.Publish(42);

            Assert.That(received, Is.EqualTo(1));
            subscription.Dispose();
        }

        [Test]
        public void MessageBroker_Observe_BridgesMessagesIntoObservableStream()
        {
            using MessageBroker broker = new MessageBroker();
            List<int> values = new List<int>(4);
            IDisposable subscription = broker.Observe<int>().Subscribe(values.Add);

            broker.Publish(2);
            broker.Publish(4);

            Assert.That(values.Count, Is.EqualTo(2));
            Assert.That(values[0], Is.EqualTo(2));
            Assert.That(values[1], Is.EqualTo(4));
            subscription.Dispose();
        }

        [Test]
        public void EventHub_PublishSubscribeAndObserve_UseSameBrokerScope()
        {
            using MessageBroker broker = new MessageBroker();
            OnityEventHub eventHub = new OnityEventHub(broker);
            int callbackCount = 0;
            int lastObserved = 0;
            IDisposable callbackSubscription = eventHub.Subscribe<int>(_ => callbackCount++);
            IDisposable streamSubscription = eventHub.Observe<int>().Subscribe(value => lastObserved = value);

            eventHub.Publish(9);

            Assert.That(callbackCount, Is.EqualTo(1));
            Assert.That(lastObserved, Is.EqualTo(9));

            callbackSubscription.Dispose();
            streamSubscription.Dispose();
        }

        [Test]
        public void EventHub_Observe_CachesObservablePerMessageType()
        {
            using MessageBroker broker = new MessageBroker();
            OnityEventHub eventHub = new OnityEventHub(broker);

            IOnityObservable<int> first = eventHub.Observe<int>();
            IOnityObservable<int> second = eventHub.Observe<int>();

            Assert.That(ReferenceEquals(first, second), Is.True);
        }

        [Test]
        public void EventHub_CanBeResolvedThroughContainerBinding()
        {
            using OnityContainer container = new OnityContainer();
            container.BindInterfacesAndSelfTo<MessageBroker>().AsSingle();
            container.BindInterfacesAndSelfTo<OnityEventHub>().AsSingle();

            OnityEventHub eventHub = container.Resolve<OnityEventHub>();

            Assert.That(eventHub, Is.Not.Null);
        }
    }
}
