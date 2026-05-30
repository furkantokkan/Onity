using System;
using System.Collections.Generic;

namespace Onity.Messaging
{
    /// <summary>
    /// Default message broker that owns typed message channels.
    /// </summary>
    public sealed class MessageBroker : IMessageBroker, IDisposable
    {
        private readonly Dictionary<Type, object> m_channels;
        private readonly object m_gate;
        private bool m_isDisposed;

        /// <summary>
        /// Initializes a new broker.
        /// </summary>
        public MessageBroker()
        {
            m_channels = new Dictionary<Type, object>(64);
            m_gate = new object();
            m_isDisposed = false;
        }

        /// <inheritdoc />
        public IPublisher<TMessage> GetPublisher<TMessage>()
        {
            return GetOrCreateChannel<TMessage>();
        }

        /// <inheritdoc />
        public ISubscriber<TMessage> GetSubscriber<TMessage>()
        {
            return GetOrCreateChannel<TMessage>();
        }

        /// <summary>
        /// Current number of created message channels.
        /// </summary>
        public int ChannelCount
        {
            get
            {
                ThrowIfDisposed();

                lock (m_gate)
                {
                    return m_channels.Count;
                }
            }
        }

        /// <summary>
        /// Writes message channel diagnostics into the provided buffer.
        /// </summary>
        /// <param name="results">Destination list.</param>
        public void GetDiagnostics(List<MessageChannelDiagnostics> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            ThrowIfDisposed();
            results.Clear();

            lock (m_gate)
            {
                foreach (KeyValuePair<Type, object> pair in m_channels)
                {
                    int subscriberCount = 0;

                    if (pair.Value is IMessageChannelDiagnostics channelDiagnostics)
                    {
                        subscriberCount = channelDiagnostics.SubscriberCount;
                    }

                    results.Add(new MessageChannelDiagnostics(pair.Key, subscriberCount));
                }
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (m_isDisposed)
            {
                return;
            }

            m_isDisposed = true;

            lock (m_gate)
            {
                foreach (KeyValuePair<Type, object> pair in m_channels)
                {
                    if (pair.Value is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }

                m_channels.Clear();
            }
        }

        private MessageChannel<TMessage> GetOrCreateChannel<TMessage>()
        {
            ThrowIfDisposed();

            Type key = typeof(TMessage);

            lock (m_gate)
            {
                if (m_channels.TryGetValue(key, out object existing))
                {
                    return (MessageChannel<TMessage>)existing;
                }

                MessageChannel<TMessage> channel = new MessageChannel<TMessage>();
                m_channels.Add(key, channel);
                return channel;
            }
        }

        private void ThrowIfDisposed()
        {
            if (m_isDisposed)
            {
                throw new ObjectDisposedException(nameof(MessageBroker));
            }
        }
    }

    /// <summary>
    /// Snapshot information for one message channel.
    /// </summary>
    public readonly struct MessageChannelDiagnostics
    {
        /// <summary>
        /// Message payload type used as channel key.
        /// </summary>
        public Type MessageType { get; }

        /// <summary>
        /// Active subscriber count for the channel.
        /// </summary>
        public int SubscriberCount { get; }

        /// <summary>
        /// Initializes a diagnostics entry.
        /// </summary>
        /// <param name="messageType">Message type key.</param>
        /// <param name="subscriberCount">Subscriber count.</param>
        public MessageChannelDiagnostics(Type messageType, int subscriberCount)
        {
            MessageType = messageType;
            SubscriberCount = subscriberCount;
        }
    }
}
