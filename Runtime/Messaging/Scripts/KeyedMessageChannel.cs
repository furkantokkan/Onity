using System;
using System.Collections.Generic;

namespace Onity.Messaging
{
    /// <summary>
    /// Keyed message channel that routes a published message to only the
    /// subscribers registered for its key. Each key owns an inner
    /// <see cref="MessageChannel{TMessage}"/>, reusing its swap-back removal and
    /// allocation-free steady-state publish.
    /// </summary>
    /// <typeparam name="TKey">Routing key type.</typeparam>
    /// <typeparam name="TMessage">Message type.</typeparam>
    public sealed class KeyedMessageChannel<TKey, TMessage> :
        IKeyedPublisher<TKey, TMessage>,
        IKeyedSubscriber<TKey, TMessage>,
        IDisposable
    {
        private const int k_defaultCapacity = 8;

        private readonly Dictionary<TKey, MessageChannel<TMessage>> m_channels;
        private bool m_isDisposed;

        /// <summary>
        /// Initializes a new keyed channel instance.
        /// </summary>
        public KeyedMessageChannel()
        {
            m_channels = new Dictionary<TKey, MessageChannel<TMessage>>(k_defaultCapacity);
            m_isDisposed = false;
        }

        /// <summary>
        /// Number of keys that currently own a subscriber channel.
        /// </summary>
        public int KeyCount => m_channels.Count;

        /// <summary>
        /// Active subscriber count for a single key.
        /// </summary>
        /// <param name="key">Routing key.</param>
        /// <returns>Subscriber count for the key, or zero when unknown.</returns>
        public int GetSubscriberCount(TKey key)
        {
            ThrowIfDisposed();

            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (m_channels.TryGetValue(key, out MessageChannel<TMessage> channel))
            {
                return channel.SubscriberCount;
            }

            return 0;
        }

        /// <inheritdoc />
        public IDisposable Subscribe(TKey key, MessageHandler<TMessage> handler)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            ThrowIfDisposed();

            if (!m_channels.TryGetValue(key, out MessageChannel<TMessage> channel))
            {
                channel = new MessageChannel<TMessage>();
                m_channels.Add(key, channel);
            }

            return channel.Subscribe(handler);
        }

        /// <inheritdoc />
        public void Publish(TKey key, TMessage message)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            ThrowIfDisposed();

            if (m_channels.TryGetValue(key, out MessageChannel<TMessage> channel))
            {
                channel.Publish(message);
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

            foreach (KeyValuePair<TKey, MessageChannel<TMessage>> pair in m_channels)
            {
                pair.Value.Dispose();
            }

            m_channels.Clear();
        }

        private void ThrowIfDisposed()
        {
            if (m_isDisposed)
            {
                throw new ObjectDisposedException(nameof(KeyedMessageChannel<TKey, TMessage>));
            }
        }
    }
}
