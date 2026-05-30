using System;
using System.Threading;
using System.Threading.Tasks;
using Onity.Core;

namespace Onity.Messaging
{
    /// <summary>
    /// Typed awaitable message channel. <see cref="PublishAsync"/> delivers a message
    /// to every subscriber sequentially, awaiting each handler before invoking the next.
    /// Delivery iterates over a pooled snapshot of the handlers so a subscribe or
    /// unsubscribe issued from inside a handler cannot corrupt the in-flight pass,
    /// mirroring the swap-back removal of <see cref="MessageChannel{TMessage}"/>.
    /// </summary>
    /// <typeparam name="TMessage">Message type.</typeparam>
    public sealed class AsyncMessageChannel<TMessage> : IAsyncPublisher<TMessage>, IAsyncSubscriber<TMessage>, IDisposable
    {
        private const int k_defaultCapacity = 8;

        private SubscriptionEntry[] m_entries;
        private Func<TMessage, CancellationToken, ValueTask>[] m_snapshot;
        private int m_count;
        private int m_nextId;
        private bool m_isPublishing;
        private bool m_hasPendingRemovals;
        private bool m_isDisposed;

        /// <summary>
        /// Initializes a new asynchronous channel instance.
        /// </summary>
        public AsyncMessageChannel()
        {
            m_entries = new SubscriptionEntry[k_defaultCapacity];
            m_snapshot = new Func<TMessage, CancellationToken, ValueTask>[k_defaultCapacity];
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
        public IDisposable Subscribe(Func<TMessage, CancellationToken, ValueTask> handler)
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
        public async ValueTask PublishAsync(TMessage message, CancellationToken ct)
        {
            ThrowIfDisposed();
            ct.ThrowIfCancellationRequested();

            Func<TMessage, CancellationToken, ValueTask>[] snapshot = RentSnapshot(out int snapshotCount);
            m_isPublishing = true;

            try
            {
                for (int i = 0; i < snapshotCount; i++)
                {
                    Func<TMessage, CancellationToken, ValueTask> handler = snapshot[i];

                    if (handler == null)
                    {
                        continue;
                    }

                    ct.ThrowIfCancellationRequested();
                    await handler(message, ct);
                }
            }
            finally
            {
                ReturnSnapshot(snapshot, snapshotCount);
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
            m_snapshot = Array.Empty<Func<TMessage, CancellationToken, ValueTask>>();
            m_count = 0;
            m_hasPendingRemovals = false;
        }

        private Func<TMessage, CancellationToken, ValueTask>[] RentSnapshot(out int snapshotCount)
        {
            Func<TMessage, CancellationToken, ValueTask>[] buffer = m_snapshot;

            if (buffer.Length < m_count)
            {
                buffer = new Func<TMessage, CancellationToken, ValueTask>[m_count];
            }

            m_snapshot = Array.Empty<Func<TMessage, CancellationToken, ValueTask>>();

            for (int i = 0; i < m_count; i++)
            {
                buffer[i] = m_entries[i].Handler;
            }

            snapshotCount = m_count;
            return buffer;
        }

        private void ReturnSnapshot(Func<TMessage, CancellationToken, ValueTask>[] buffer, int snapshotCount)
        {
            for (int i = 0; i < snapshotCount; i++)
            {
                buffer[i] = null;
            }

            if (!m_isDisposed && buffer.Length >= k_defaultCapacity && m_snapshot.Length < buffer.Length)
            {
                m_snapshot = buffer;
            }
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
                throw new ObjectDisposedException(nameof(AsyncMessageChannel<TMessage>));
            }
        }

        private struct SubscriptionEntry
        {
            public int Id;
            public Func<TMessage, CancellationToken, ValueTask> Handler;

            public SubscriptionEntry(int id, Func<TMessage, CancellationToken, ValueTask> handler)
            {
                Id = id;
                Handler = handler;
            }
        }
    }
}
