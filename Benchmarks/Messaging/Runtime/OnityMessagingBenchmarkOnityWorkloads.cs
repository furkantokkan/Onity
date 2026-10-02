using System;
using System.Threading;
using System.Threading.Tasks;
using Onity.Messaging;

namespace Onity.Benchmarks
{
    /// <summary>
    /// Onity.Messaging implementations of the comparison scenarios. Only <c>Onity.Messaging</c> is imported
    /// here, because MessagePipe also defines <c>IPublisher&lt;T&gt;</c> and <c>ISubscriber&lt;T&gt;</c>. Each
    /// workload drives the library only through the interfaces a user receives: <see cref="IPublisher{T}"/>
    /// and <see cref="ISubscriber{T}"/> from a <see cref="MessageBroker"/>, <see cref="IKeyedPublisher{TKey,T}"/>
    /// and <see cref="IKeyedSubscriber{TKey,T}"/> over a <see cref="KeyedMessageChannel{TKey,T}"/>, and
    /// <see cref="IAsyncPublisher{T}"/> and <see cref="IAsyncSubscriber{T}"/> over an
    /// <see cref="AsyncMessageChannel{T}"/>. Handlers are a cached <see cref="MessageHandler{T}"/> and a
    /// cached <c>Func&lt;T, CancellationToken, ValueTask&gt;</c>.
    /// </summary>
    internal static class OnityMessagingBenchmarkOnityWorkloads
    {
        internal const string LibraryName = "Onity";

        /// <summary>Name and version of the assembly that implements the API under test.</summary>
        internal static string RuntimeAssembly => typeof(MessageBroker).Assembly.GetName().Name + " "
            + typeof(MessageBroker).Assembly.GetName().Version;

        internal static OnityMessagingBenchmarkWorkload Create(string scenarioId)
        {
            switch (scenarioId)
            {
                case OnityMessagingBenchmarkScenarios.PublishNoSubscribers:
                    return new PublishWorkload(0);
                case OnityMessagingBenchmarkScenarios.Publish1:
                    return new PublishWorkload(1);
                case OnityMessagingBenchmarkScenarios.Publish8:
                    return new PublishWorkload(OnityMessagingBenchmarkGolden.ManySubscribers);
                case OnityMessagingBenchmarkScenarios.SubscribeDispose:
                    return new SubscribeDisposeWorkload(1);
                case OnityMessagingBenchmarkScenarios.SubscribeDispose64:
                    return new SubscribeDisposeWorkload(OnityMessagingBenchmarkGolden.ManyResidents);
                case OnityMessagingBenchmarkScenarios.KeyedPublish:
                    return new KeyedPublishWorkload();
                case OnityMessagingBenchmarkScenarios.KeyedSubscribeDispose:
                    return new KeyedSubscribeDisposeWorkload();
                case OnityMessagingBenchmarkScenarios.AsyncPublish1:
                    return new AsyncPublishWorkload(1);
                case OnityMessagingBenchmarkScenarios.AsyncPublish8:
                    return new AsyncPublishWorkload(OnityMessagingBenchmarkGolden.ManySubscribers);
                default:
                    throw new ArgumentException("Unknown messaging benchmark scenario: " + scenarioId, nameof(scenarioId));
            }
        }

        private static void DisposeAll(IDisposable[] subscriptions)
        {
            for (int i = 0; i < subscriptions.Length; i++)
            {
                subscriptions[i].Dispose();
                subscriptions[i] = null;
            }
        }

        /// <summary>Holds the two handler delegates of one workload, created once from its sink (untimed).</summary>
        private abstract class OnityWorkload : OnityMessagingBenchmarkWorkload
        {
            protected OnityWorkload()
            {
                Handler = Sink.Receive;
                AsyncHandler = Sink.ReceiveAsync;
            }

            protected MessageHandler<OnityMessagingBenchmarkMessage> Handler { get; }

            protected Func<OnityMessagingBenchmarkMessage, CancellationToken, ValueTask> AsyncHandler { get; }
        }

        private sealed class PublishWorkload : OnityWorkload
        {
            private readonly IDisposable[] m_subscriptions;
            private MessageBroker m_broker;
            private IPublisher<OnityMessagingBenchmarkMessage> m_publisher;

            internal PublishWorkload(int subscriberCount)
            {
                m_subscriptions = new IDisposable[subscriberCount];
            }

            internal override void Setup()
            {
                m_broker = new MessageBroker();
                m_publisher = m_broker.GetPublisher<OnityMessagingBenchmarkMessage>();
                ISubscriber<OnityMessagingBenchmarkMessage> subscriber = m_broker.GetSubscriber<OnityMessagingBenchmarkMessage>();
                for (int i = 0; i < m_subscriptions.Length; i++)
                {
                    m_subscriptions[i] = subscriber.Subscribe(Handler);
                }
            }

            internal override void Run(int operations)
            {
                IPublisher<OnityMessagingBenchmarkMessage> publisher = m_publisher;
                for (int i = 0; i < operations; i++)
                {
                    publisher.Publish(new OnityMessagingBenchmarkMessage(i));
                }
            }

            internal override void Probe(int value)
            {
                m_publisher.Publish(new OnityMessagingBenchmarkMessage(value));
            }

            internal override void Teardown()
            {
                DisposeAll(m_subscriptions);
                m_broker.Dispose();
                m_broker = null;
                m_publisher = null;
            }
        }

        private sealed class SubscribeDisposeWorkload : OnityWorkload
        {
            private readonly IDisposable[] m_residents;
            private MessageBroker m_broker;
            private IPublisher<OnityMessagingBenchmarkMessage> m_publisher;
            private ISubscriber<OnityMessagingBenchmarkMessage> m_subscriber;

            internal SubscribeDisposeWorkload(int residentCount)
            {
                m_residents = new IDisposable[residentCount];
            }

            internal override void Setup()
            {
                m_broker = new MessageBroker();
                m_publisher = m_broker.GetPublisher<OnityMessagingBenchmarkMessage>();
                m_subscriber = m_broker.GetSubscriber<OnityMessagingBenchmarkMessage>();
                for (int i = 0; i < m_residents.Length; i++)
                {
                    m_residents[i] = m_subscriber.Subscribe(Handler);
                }
            }

            internal override void Run(int operations)
            {
                ISubscriber<OnityMessagingBenchmarkMessage> subscriber = m_subscriber;
                MessageHandler<OnityMessagingBenchmarkMessage> handler = Handler;
                for (int i = 0; i < operations; i++)
                {
                    subscriber.Subscribe(handler).Dispose();
                }
            }

            internal override void Probe(int value)
            {
                m_publisher.Publish(new OnityMessagingBenchmarkMessage(value));
            }

            internal override void Teardown()
            {
                DisposeAll(m_residents);
                m_broker.Dispose();
                m_broker = null;
                m_publisher = null;
                m_subscriber = null;
            }
        }

        private sealed class KeyedPublishWorkload : OnityWorkload
        {
            private readonly IDisposable[] m_subscriptions = new IDisposable[OnityMessagingBenchmarkGolden.KeyCount];
            private KeyedMessageChannel<int, OnityMessagingBenchmarkMessage> m_channel;
            private IKeyedPublisher<int, OnityMessagingBenchmarkMessage> m_publisher;

            internal override void Setup()
            {
                m_channel = new KeyedMessageChannel<int, OnityMessagingBenchmarkMessage>();
                m_publisher = m_channel;
                IKeyedSubscriber<int, OnityMessagingBenchmarkMessage> subscriber = m_channel;
                for (int key = 0; key < m_subscriptions.Length; key++)
                {
                    m_subscriptions[key] = subscriber.Subscribe(key, Handler);
                }
            }

            internal override void Run(int operations)
            {
                IKeyedPublisher<int, OnityMessagingBenchmarkMessage> publisher = m_publisher;
                for (int i = 0; i < operations; i++)
                {
                    publisher.Publish(i & OnityMessagingBenchmarkGolden.KeyMask, new OnityMessagingBenchmarkMessage(i));
                }
            }

            internal override void Probe(int value)
            {
                m_publisher.Publish(OnityMessagingBenchmarkGolden.ProbeKey, new OnityMessagingBenchmarkMessage(value));
            }

            internal override void Teardown()
            {
                DisposeAll(m_subscriptions);
                m_channel.Dispose();
                m_channel = null;
                m_publisher = null;
            }
        }

        private sealed class KeyedSubscribeDisposeWorkload : OnityWorkload
        {
            private readonly IDisposable[] m_residents = new IDisposable[OnityMessagingBenchmarkGolden.KeyCount];
            private KeyedMessageChannel<int, OnityMessagingBenchmarkMessage> m_channel;
            private IKeyedPublisher<int, OnityMessagingBenchmarkMessage> m_publisher;
            private IKeyedSubscriber<int, OnityMessagingBenchmarkMessage> m_subscriber;

            internal override void Setup()
            {
                m_channel = new KeyedMessageChannel<int, OnityMessagingBenchmarkMessage>();
                m_publisher = m_channel;
                m_subscriber = m_channel;
                for (int key = 0; key < m_residents.Length; key++)
                {
                    m_residents[key] = m_subscriber.Subscribe(key, Handler);
                }
            }

            internal override void Run(int operations)
            {
                IKeyedSubscriber<int, OnityMessagingBenchmarkMessage> subscriber = m_subscriber;
                MessageHandler<OnityMessagingBenchmarkMessage> handler = Handler;
                for (int i = 0; i < operations; i++)
                {
                    subscriber.Subscribe(i & OnityMessagingBenchmarkGolden.KeyMask, handler).Dispose();
                }
            }

            internal override void Probe(int value)
            {
                m_publisher.Publish(OnityMessagingBenchmarkGolden.ProbeKey, new OnityMessagingBenchmarkMessage(value));
            }

            internal override void Teardown()
            {
                DisposeAll(m_residents);
                m_channel.Dispose();
                m_channel = null;
                m_publisher = null;
                m_subscriber = null;
            }
        }

        private sealed class AsyncPublishWorkload : OnityWorkload
        {
            private readonly IDisposable[] m_subscriptions;
            private AsyncMessageChannel<OnityMessagingBenchmarkMessage> m_channel;
            private IAsyncPublisher<OnityMessagingBenchmarkMessage> m_publisher;

            internal AsyncPublishWorkload(int handlerCount)
            {
                m_subscriptions = new IDisposable[handlerCount];
            }

            internal override void Setup()
            {
                m_channel = new AsyncMessageChannel<OnityMessagingBenchmarkMessage>();
                m_publisher = m_channel;
                IAsyncSubscriber<OnityMessagingBenchmarkMessage> subscriber = m_channel;
                for (int i = 0; i < m_subscriptions.Length; i++)
                {
                    m_subscriptions[i] = subscriber.Subscribe(AsyncHandler);
                }
            }

            internal override void Run(int operations)
            {
                IAsyncPublisher<OnityMessagingBenchmarkMessage> publisher = m_publisher;
                for (int i = 0; i < operations; i++)
                {
                    ValueTask publish = publisher.PublishAsync(new OnityMessagingBenchmarkMessage(i), CancellationToken.None);
                    if (!publish.IsCompletedSuccessfully)
                    {
                        throw CreateIncompletePublishException(i);
                    }

                    publish.GetAwaiter().GetResult();
                }
            }

            internal override void Probe(int value)
            {
                ValueTask publish = m_publisher.PublishAsync(new OnityMessagingBenchmarkMessage(value), CancellationToken.None);
                if (!publish.IsCompletedSuccessfully)
                {
                    throw CreateIncompletePublishException(-1);
                }

                publish.GetAwaiter().GetResult();
            }

            internal override void Teardown()
            {
                DisposeAll(m_subscriptions);
                m_channel.Dispose();
                m_channel = null;
                m_publisher = null;
            }
        }
    }
}
