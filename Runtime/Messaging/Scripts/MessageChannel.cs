using System;
using Onity.Core;

namespace Onity.Messaging
{
    /// <summary>
    /// Typed message channel with allocation-free publish on steady state.
    /// </summary>
    /// <typeparam name="TMessage">Message type.</typeparam>
    public sealed class MessageChannel<TMessage> : IPublisher<TMessage>, ISubscriber<TMessage>, IMessageChannelDiagnostics, IDisposable
    {
        private const int k_defaultCapacity = 8;

        private SubscriptionEntry[] m_entries;
        private int m_count;
        private int m_nextId;
        private bool m_isPublishing;
        private bool m_hasPendingRemovals;
        private bool m_isDisposed;

        /// <summary>
        /// Initializes a new channel instance.
        /// </summary>
        public MessageChannel()
        {
            m_entries = new SubscriptionEntry[k_defaultCapacity];
            m_count = 0;
            m_nextId = 1;
            m_isPublishing = false;
            m_hasPendingRemovals = false;
            m_isDisposed = false;
        }

        /// <summary>
        /// Active subscriber count for diagnostics.
        /// </summary>
        public int SubscriberCount => m_count;

        /// <inheritdoc />
        public IDisposable Subscribe(MessageHandler<TMessage> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            ThrowIfDisposed();
            EnsureCapacity(m_count + 1);

            int id = m_nextId++;
            m_entries[m_count] = new SubscriptionEntry(id, handler);
            m_count++;

            return new DisposableAction(() => Unsubscribe(id));
        }

        /// <inheritdoc />
        public void Publish(TMessage message)
        {
            ThrowIfDisposed();

            m_isPublishing = true;

            try
            {
                for (int i = 0; i < m_count; i++)
                {
                    MessageHandler<TMessage> handler = m_entries[i].Handler;

                    if (handler == null)
                    {
                        continue;
                    }

                    handler(message);
                }
            }
            finally
            {
                m_isPublishing = false;

                if (m_hasPendingRemovals)
                {
                    Compact();
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
            m_entries = Array.Empty<SubscriptionEntry>();
            m_count = 0;
            m_hasPendingRemovals = false;
        }

        private void EnsureCapacity(int requiredCapacity)
        {
            if (m_entries.Length >= requiredCapacity)
            {
                return;
            }

            int newCapacity = m_entries.Length * 2;

            if (newCapacity < requiredCapacity)
            {
                newCapacity = requiredCapacity;
            }

            Array.Resize(ref m_entries, newCapacity);
        }

        private void Unsubscribe(int id)
        {
            if (m_isDisposed)
            {
                return;
            }

            int index = FindIndexById(id);

            if (index < 0)
            {
                return;
            }

            if (m_isPublishing)
            {
                m_entries[index].Handler = null;
                m_hasPendingRemovals = true;
                return;
            }

            RemoveAtSwapBack(index);
        }

        private int FindIndexById(int id)
        {
            for (int i = 0; i < m_count; i++)
            {
                if (m_entries[i].Id == id)
                {
                    return i;
                }
            }

            return -1;
        }

        private void RemoveAtSwapBack(int index)
        {
            int lastIndex = m_count - 1;
            m_entries[index] = m_entries[lastIndex];
            m_entries[lastIndex] = default;
            m_count--;
        }

        private void Compact()
        {
            int writeIndex = 0;

            for (int readIndex = 0; readIndex < m_count; readIndex++)
            {
                SubscriptionEntry entry = m_entries[readIndex];

                if (entry.Handler == null)
                {
                    continue;
                }

                if (writeIndex != readIndex)
                {
                    m_entries[writeIndex] = entry;
                }

                writeIndex++;
            }

            for (int clearIndex = writeIndex; clearIndex < m_count; clearIndex++)
            {
                m_entries[clearIndex] = default;
            }

            m_count = writeIndex;
            m_hasPendingRemovals = false;
        }

        private void ThrowIfDisposed()
        {
            if (m_isDisposed)
            {
                throw new ObjectDisposedException(nameof(MessageChannel<TMessage>));
            }
        }

        private struct SubscriptionEntry
        {
            public int Id;
            public MessageHandler<TMessage> Handler;

            public SubscriptionEntry(int id, MessageHandler<TMessage> handler)
            {
                Id = id;
                Handler = handler;
            }
        }
    }

    internal interface IMessageChannelDiagnostics
    {
        int SubscriberCount { get; }
    }
}
