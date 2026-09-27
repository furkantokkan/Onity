using System;
using System.Threading;

namespace Onity.Unity.Async
{
    /// <summary>Single-consumer handle for a channel shared by multiple producers.</summary>
    /// <typeparam name="T">Item type.</typeparam>
    public sealed class OnityChannelReader<T>
    {
        private readonly OnityChannel<T> m_channel;

        internal OnityChannelReader(OnityChannel<T> channel)
        {
            m_channel = channel;
        }

        /// <summary>Reads a buffered item immediately when no consumer lease is held.</summary>
        /// <param name="item">Consumed item, or default when no read is accepted.</param>
        /// <returns>True when an item was consumed, otherwise false.</returns>
        /// <remarks>A bounded read promotes the oldest queued writer before publication.
        /// Warm buffered reads without queued writer publication allocate no channel storage;
        /// pending publication, bridges and user continuations may allocate.</remarks>
        public bool TryRead(out T item) => m_channel.TryRead(out item);

        /// <summary>Reads an item or holds the consumer lease until a pending outcome commits.</summary>
        /// <param name="cancellationToken">Cancels only an uncommitted read.</param>
        /// <returns>A native task containing the next item.</returns>
        /// <remarks>Pre-cancellation wins before a lease conflict. Normal closed/empty channels
        /// fault with OnityChannelClosedException; error closure uses the original error, including
        /// faulted OCE. Cancellation never closes the channel. Pending waiters allocate.</remarks>
        /// <exception cref="InvalidOperationException">Another consumer holds the lease.</exception>
        public OnityTask<T> ReadAsync(CancellationToken cancellationToken = default) =>
            m_channel.ReadAsync(cancellationToken);

        /// <summary>Creates an inert stream that drains items before terminal end/error.</summary>
        /// <param name="cancellationToken">Captured cooperative token.</param>
        /// <returns>An independent enumerator description with no initial consumer lease.</returns>
        /// <remarks>The first move acquires the lease until terminal cleanup or disposal.
        /// Captured and enumeration tokens are registered directly: equal tokens once, distinct
        /// tokens twice. Captured pre-cancellation wins ties. Dispose abandoned enumerators;
        /// disposal releases the lease without closing the channel or discarding buffered items.
        /// Delivery already committed stays true even if later disposal invalidates Current.</remarks>
        public IOnityAsyncEnumerable<T> ReadAllAsync(CancellationToken cancellationToken = default)
        {
            return new Description(m_channel, cancellationToken);
        }

        private sealed class Description : IOnityAsyncEnumerable<T>
        {
            private readonly OnityChannel<T> m_channel;
            private readonly CancellationToken m_token;

            internal Description(OnityChannel<T> channel, CancellationToken token)
            {
                m_channel = channel;
                m_token = token;
            }

            public IOnityAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                return new Enumerator(m_channel, m_token, cancellationToken);
            }
        }

        // Dedicated channel transaction state: a generic stream base cannot
        // atomically commit dequeue and Current with channel cancellation/disposal.
        internal sealed class Enumerator : IOnityAsyncEnumerator<T>
        {
            private readonly OnityChannel<T> m_channel;
            internal readonly CancellationToken CapturedToken;
            internal readonly CancellationToken EnumerationToken;
            internal bool Started;
            internal bool Disposed;
            internal bool Ended;
            internal bool Outstanding;
            internal OnityTaskCompletionSource<bool> OutstandingSource;
            internal bool CurrentValid;
            internal bool DisposalPublished;
            internal T Item;
            internal OnityTask<bool> TerminalTask;
            internal OnityTaskCompletionSource DisposalSource;
            internal OnityTaskCompletionSource<bool> DisposalWaitSource;
            internal OnityChannelReadWaiter<T> Pending;

            internal Enumerator(OnityChannel<T> channel, CancellationToken capturedToken,
                CancellationToken enumerationToken)
            {
                m_channel = channel;
                CapturedToken = capturedToken;
                EnumerationToken = enumerationToken;
            }

            public T Current
            {
                get
                {
                    lock (m_channel.Gate)
                    {
                        if (!CurrentValid)
                        {
                            throw new InvalidOperationException("Current is unavailable outside a successful move.");
                        }
                        return Item;
                    }
                }
            }

            public OnityTask<bool> MoveNextAsync() => m_channel.Move(this);
            public OnityTask DisposeAsync() => m_channel.Dispose(this);
        }
    }
}
