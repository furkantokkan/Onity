using System;
using System.Threading;
#if ONITY_MESSAGING_BENCHMARKS_MESSAGEPIPE_UNITASK
using Cysharp.Threading.Tasks;
#else
using System.Threading.Tasks;
#endif
using MessagePipe;

namespace Onity.Benchmarks
{
    /// <summary>
    /// MessagePipe implementations of the comparison scenarios. Only the <c>MessagePipe</c> namespace is
    /// imported here, because Onity also defines <c>IPublisher&lt;T&gt;</c> and <c>ISubscriber&lt;T&gt;</c>.
    /// Each workload drives the library only through the interfaces a user receives:
    /// <see cref="IPublisher{T}"/>, <see cref="ISubscriber{T}"/>, <see cref="IPublisher{TKey,T}"/>,
    /// <see cref="ISubscriber{TKey,T}"/>, <see cref="IAsyncPublisher{T}"/> and <see cref="IAsyncSubscriber{T}"/>.
    /// Handlers go through MessagePipe's documented <c>Subscribe(Action&lt;T&gt;)</c> extension and its async
    /// <c>Subscribe(Func&lt;T, CancellationToken, ...&gt;)</c> extension with cached delegates.
    /// </summary>
    /// <remarks>
    /// Two MessagePipe flavors compile from this file; the host-only define
    /// <c>ONITY_MESSAGING_BENCHMARKS_MESSAGEPIPE_UNITASK</c> selects the Unity package (UniTask build), whose
    /// async API returns <c>UniTask</c>; without it the NuGet build's <c>ValueTask</c> API is used. The
    /// synchronous workloads are the same source for both. No DI container is used: the public broker types
    /// are constructed directly, see <see cref="OnityMessagingBenchmarkPlayerRunner.MessagePipeSetup"/>.
    /// </remarks>
    internal static class OnityMessagingBenchmarkMessagePipeWorkloads
    {
        internal const string LibraryName = "MessagePipe";

        /// <summary>Name and version of the assembly that implements the API under test.</summary>
        internal static string RuntimeAssembly => typeof(MessageBrokerCore<int>).Assembly.GetName().Name + " "
            + typeof(MessageBrokerCore<int>).Assembly.GetName().Version;

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

        /// <summary>
        /// What MessagePipe's <c>AddMessagePipe</c> registers as container singletons, created once per
        /// workload with default <see cref="MessagePipeOptions"/>, plus the workload's two handler delegates
        /// (all untimed). Each sample creates fresh broker cores on top of it.
        /// </summary>
        private abstract class MessagePipeWorkload : OnityMessagingBenchmarkWorkload
        {
            protected MessagePipeWorkload()
            {
                Options = new MessagePipeOptions();
                Diagnostics = new MessagePipeDiagnosticsInfo(Options);
                IServiceProvider provider = new FilterlessServiceProvider();
                HandlerFactory = new FilterAttachedMessageHandlerFactory(
                    Options, new AttributeFilterProvider<MessageHandlerFilterAttribute>(), provider);
                AsyncHandlerFactory = new FilterAttachedAsyncMessageHandlerFactory(
                    Options, new AttributeFilterProvider<AsyncMessageHandlerFilterAttribute>(), provider);
                Handler = Sink.Receive;
#if ONITY_MESSAGING_BENCHMARKS_MESSAGEPIPE_UNITASK
                AsyncHandler = Sink.ReceiveUniTaskAsync;
#else
                AsyncHandler = Sink.ReceiveAsync;
#endif
            }

            protected MessagePipeOptions Options { get; }

            protected MessagePipeDiagnosticsInfo Diagnostics { get; }

            protected FilterAttachedMessageHandlerFactory HandlerFactory { get; }

            protected FilterAttachedAsyncMessageHandlerFactory AsyncHandlerFactory { get; }

            protected Action<OnityMessagingBenchmarkMessage> Handler { get; }

#if ONITY_MESSAGING_BENCHMARKS_MESSAGEPIPE_UNITASK
            protected Func<OnityMessagingBenchmarkMessage, CancellationToken, UniTask> AsyncHandler { get; }
#else
            protected Func<OnityMessagingBenchmarkMessage, CancellationToken, ValueTask> AsyncHandler { get; }
#endif
        }

        /// <summary>
        /// The service provider the handler factories hold. No global or attribute filter is registered,
        /// so MessagePipe never asks it for a service; a request would mean filters are active, so it throws.
        /// </summary>
        private sealed class FilterlessServiceProvider : IServiceProvider
        {
            /// <summary>Always throws: a request means MessagePipe found a filter to resolve.</summary>
            public object GetService(Type serviceType)
            {
                throw new InvalidOperationException("The MessagePipe benchmark registers no filters, so MessagePipe must not resolve "
                    + (serviceType != null ? serviceType.FullName : "null") + ".");
            }
        }

        private sealed class PublishWorkload : MessagePipeWorkload
        {
            private readonly IDisposable[] m_subscriptions;
            private MessageBrokerCore<OnityMessagingBenchmarkMessage> m_core;
            private IPublisher<OnityMessagingBenchmarkMessage> m_publisher;

            internal PublishWorkload(int subscriberCount)
            {
                m_subscriptions = new IDisposable[subscriberCount];
            }

            internal override void Setup()
            {
                m_core = new MessageBrokerCore<OnityMessagingBenchmarkMessage>(Diagnostics, Options);
                m_publisher = new MessageBroker<OnityMessagingBenchmarkMessage>(m_core, HandlerFactory);
                ISubscriber<OnityMessagingBenchmarkMessage> subscriber = new MessageBroker<OnityMessagingBenchmarkMessage>(m_core, HandlerFactory);
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
                m_core.Dispose();
                m_core = null;
                m_publisher = null;
            }
        }

        private sealed class SubscribeDisposeWorkload : MessagePipeWorkload
        {
            private readonly IDisposable[] m_residents;
            private MessageBrokerCore<OnityMessagingBenchmarkMessage> m_core;
            private IPublisher<OnityMessagingBenchmarkMessage> m_publisher;
            private ISubscriber<OnityMessagingBenchmarkMessage> m_subscriber;

            internal SubscribeDisposeWorkload(int residentCount)
            {
                m_residents = new IDisposable[residentCount];
            }

            internal override void Setup()
            {
                m_core = new MessageBrokerCore<OnityMessagingBenchmarkMessage>(Diagnostics, Options);
                m_publisher = new MessageBroker<OnityMessagingBenchmarkMessage>(m_core, HandlerFactory);
                m_subscriber = new MessageBroker<OnityMessagingBenchmarkMessage>(m_core, HandlerFactory);
                for (int i = 0; i < m_residents.Length; i++)
                {
                    m_residents[i] = m_subscriber.Subscribe(Handler);
                }
            }

            internal override void Run(int operations)
            {
                ISubscriber<OnityMessagingBenchmarkMessage> subscriber = m_subscriber;
                Action<OnityMessagingBenchmarkMessage> handler = Handler;
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
                m_core.Dispose();
                m_core = null;
                m_publisher = null;
                m_subscriber = null;
            }
        }

        private sealed class KeyedPublishWorkload : MessagePipeWorkload
        {
            private readonly IDisposable[] m_subscriptions = new IDisposable[OnityMessagingBenchmarkGolden.KeyCount];
            private MessageBrokerCore<int, OnityMessagingBenchmarkMessage> m_core;
            private IPublisher<int, OnityMessagingBenchmarkMessage> m_publisher;

            internal override void Setup()
            {
                m_core = new MessageBrokerCore<int, OnityMessagingBenchmarkMessage>(Diagnostics, Options);
                m_publisher = new MessageBroker<int, OnityMessagingBenchmarkMessage>(m_core, HandlerFactory);
                ISubscriber<int, OnityMessagingBenchmarkMessage> subscriber = new MessageBroker<int, OnityMessagingBenchmarkMessage>(m_core, HandlerFactory);
                for (int key = 0; key < m_subscriptions.Length; key++)
                {
                    m_subscriptions[key] = subscriber.Subscribe(key, Handler);
                }
            }

            internal override void Run(int operations)
            {
                IPublisher<int, OnityMessagingBenchmarkMessage> publisher = m_publisher;
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
                m_core.Dispose();
                m_core = null;
                m_publisher = null;
            }
        }

        private sealed class KeyedSubscribeDisposeWorkload : MessagePipeWorkload
        {
            private readonly IDisposable[] m_residents = new IDisposable[OnityMessagingBenchmarkGolden.KeyCount];
            private MessageBrokerCore<int, OnityMessagingBenchmarkMessage> m_core;
            private IPublisher<int, OnityMessagingBenchmarkMessage> m_publisher;
            private ISubscriber<int, OnityMessagingBenchmarkMessage> m_subscriber;

            internal override void Setup()
            {
                m_core = new MessageBrokerCore<int, OnityMessagingBenchmarkMessage>(Diagnostics, Options);
                m_publisher = new MessageBroker<int, OnityMessagingBenchmarkMessage>(m_core, HandlerFactory);
                m_subscriber = new MessageBroker<int, OnityMessagingBenchmarkMessage>(m_core, HandlerFactory);
                for (int key = 0; key < m_residents.Length; key++)
                {
                    m_residents[key] = m_subscriber.Subscribe(key, Handler);
                }
            }

            internal override void Run(int operations)
            {
                ISubscriber<int, OnityMessagingBenchmarkMessage> subscriber = m_subscriber;
                Action<OnityMessagingBenchmarkMessage> handler = Handler;
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
                m_core.Dispose();
                m_core = null;
                m_publisher = null;
                m_subscriber = null;
            }
        }

        private sealed class AsyncPublishWorkload : MessagePipeWorkload
        {
            private readonly IDisposable[] m_subscriptions;
            private AsyncMessageBrokerCore<OnityMessagingBenchmarkMessage> m_core;
            private IAsyncPublisher<OnityMessagingBenchmarkMessage> m_publisher;

            internal AsyncPublishWorkload(int handlerCount)
            {
                m_subscriptions = new IDisposable[handlerCount];
            }

            internal override void Setup()
            {
                m_core = new AsyncMessageBrokerCore<OnityMessagingBenchmarkMessage>(Diagnostics, Options);
                m_publisher = new AsyncMessageBroker<OnityMessagingBenchmarkMessage>(m_core, AsyncHandlerFactory);
                IAsyncSubscriber<OnityMessagingBenchmarkMessage> subscriber = new AsyncMessageBroker<OnityMessagingBenchmarkMessage>(m_core, AsyncHandlerFactory);
                for (int i = 0; i < m_subscriptions.Length; i++)
                {
                    m_subscriptions[i] = subscriber.Subscribe(AsyncHandler);
                }
            }

#if ONITY_MESSAGING_BENCHMARKS_MESSAGEPIPE_UNITASK
            internal override void Run(int operations)
            {
                IAsyncPublisher<OnityMessagingBenchmarkMessage> publisher = m_publisher;
                for (int i = 0; i < operations; i++)
                {
                    UniTask publish = publisher.PublishAsync(new OnityMessagingBenchmarkMessage(i), AsyncPublishStrategy.Sequential, CancellationToken.None);
                    if (publish.Status != UniTaskStatus.Succeeded)
                    {
                        throw CreateIncompletePublishException(i);
                    }

                    publish.GetAwaiter().GetResult();
                }
            }

            internal override void Probe(int value)
            {
                UniTask publish = m_publisher.PublishAsync(new OnityMessagingBenchmarkMessage(value), AsyncPublishStrategy.Sequential, CancellationToken.None);
                if (publish.Status != UniTaskStatus.Succeeded)
                {
                    throw CreateIncompletePublishException(-1);
                }

                publish.GetAwaiter().GetResult();
            }
#else
            internal override void Run(int operations)
            {
                IAsyncPublisher<OnityMessagingBenchmarkMessage> publisher = m_publisher;
                for (int i = 0; i < operations; i++)
                {
                    ValueTask publish = publisher.PublishAsync(new OnityMessagingBenchmarkMessage(i), AsyncPublishStrategy.Sequential, CancellationToken.None);
                    if (!publish.IsCompletedSuccessfully)
                    {
                        throw CreateIncompletePublishException(i);
                    }

                    publish.GetAwaiter().GetResult();
                }
            }

            internal override void Probe(int value)
            {
                ValueTask publish = m_publisher.PublishAsync(new OnityMessagingBenchmarkMessage(value), AsyncPublishStrategy.Sequential, CancellationToken.None);
                if (!publish.IsCompletedSuccessfully)
                {
                    throw CreateIncompletePublishException(-1);
                }

                publish.GetAwaiter().GetResult();
            }
#endif

            internal override void Teardown()
            {
                DisposeAll(m_subscriptions);
                m_core.Dispose();
                m_core = null;
                m_publisher = null;
            }
        }
    }
}
