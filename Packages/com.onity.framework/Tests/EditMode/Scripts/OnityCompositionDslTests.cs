using System;
using NUnit.Framework;
using Onity.Composition;
using Onity.DI;
using Onity.Messaging;
using Onity.Reactive;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class OnityCompositionDslTests
    {
        [Test]
        public void BindReactiveProperty_ResolvesConcreteAndReadOnlyAsSameInstance()
        {
            using OnityContainer container = new OnityContainer();
            ReactiveProperty<int> returned = container.BindReactiveProperty(100);

            ReactiveProperty<int> concrete = container.Resolve<ReactiveProperty<int>>();
            IReadOnlyReactiveProperty<int> readOnly = container.Resolve<IReadOnlyReactiveProperty<int>>();

            Assert.That(concrete, Is.SameAs(returned));
            Assert.That(readOnly, Is.SameAs(returned));
            Assert.That(concrete, Is.SameAs(readOnly));
            Assert.That(readOnly.Value, Is.EqualTo(100));
        }

        [Test]
        public void BindReactiveProperty_SharedInstanceObservesValueChanges()
        {
            using OnityContainer container = new OnityContainer();
            container.BindReactiveProperty(0);

            ReactiveProperty<int> writer = container.Resolve<ReactiveProperty<int>>();
            IReadOnlyReactiveProperty<int> reader = container.Resolve<IReadOnlyReactiveProperty<int>>();

            int observed = -1;
            using IDisposable subscription = reader.Subscribe(value => observed = value);
            Assert.That(observed, Is.EqualTo(0));

            writer.Value = 42;

            Assert.That(observed, Is.EqualTo(42));
            Assert.That(reader.Value, Is.EqualTo(42));
        }

        [Test]
        public void BindSubject_ResolvesSharedInstance()
        {
            using OnityContainer container = new OnityContainer();
            Subject<string> returned = container.BindSubject<string>();

            Subject<string> first = container.Resolve<Subject<string>>();
            Subject<string> second = container.Resolve<Subject<string>>();

            Assert.That(first, Is.SameAs(returned));
            Assert.That(first, Is.SameAs(second));
        }

        [Test]
        public void BindSubject_PublishReachesSubscriber()
        {
            using OnityContainer container = new OnityContainer();
            container.BindSubject<int>();

            Subject<int> subject = container.Resolve<Subject<int>>();

            int received = 0;
            using IDisposable subscription = subject.Subscribe(value => received = value);
            subject.OnNext(7);

            Assert.That(received, Is.EqualTo(7));
        }

        [Test]
        public void DeclareMessage_ResolvesChannelPublisherAndSubscriberAsSameInstance()
        {
            using OnityContainer container = new OnityContainer();
            MessageChannel<SampleMessage> returned = container.DeclareMessage<SampleMessage>();

            MessageChannel<SampleMessage> channel = container.Resolve<MessageChannel<SampleMessage>>();
            IPublisher<SampleMessage> publisher = container.Resolve<IPublisher<SampleMessage>>();
            ISubscriber<SampleMessage> subscriber = container.Resolve<ISubscriber<SampleMessage>>();

            Assert.That(channel, Is.SameAs(returned));
            Assert.That(publisher, Is.SameAs(returned));
            Assert.That(subscriber, Is.SameAs(returned));
        }

        [Test]
        public void DeclareMessage_PublishThroughPublisherReachesSubscriber()
        {
            using OnityContainer container = new OnityContainer();
            container.DeclareMessage<SampleMessage>();

            IPublisher<SampleMessage> publisher = container.Resolve<IPublisher<SampleMessage>>();
            ISubscriber<SampleMessage> subscriber = container.Resolve<ISubscriber<SampleMessage>>();

            int received = 0;
            using IDisposable subscription = subscriber.Subscribe(message => received = message.Amount);
            publisher.Publish(new SampleMessage(15));

            Assert.That(received, Is.EqualTo(15));
        }

        [Test]
        public void DeclareMessage_PublishThroughConcreteChannelReachesInjectedSubscriber()
        {
            using OnityContainer container = new OnityContainer();
            container.DeclareMessage<SampleMessage>();

            MessageChannel<SampleMessage> channel = container.Resolve<MessageChannel<SampleMessage>>();
            ISubscriber<SampleMessage> subscriber = container.Resolve<ISubscriber<SampleMessage>>();

            int callCount = 0;
            using IDisposable subscription = subscriber.Subscribe(_ => callCount++);
            channel.Publish(new SampleMessage(1));
            channel.Publish(new SampleMessage(2));

            Assert.That(callCount, Is.EqualTo(2));
        }

        private readonly struct SampleMessage
        {
            public readonly int Amount;

            public SampleMessage(int amount)
            {
                Amount = amount;
            }
        }
    }
}
