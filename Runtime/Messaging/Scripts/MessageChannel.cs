using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Unity.IL2CPP.CompilerServices;

namespace Onity.Messaging
{
    /// <summary>
    /// Typed message channel with allocation-free publish on steady state.
    /// </summary>
    /// <remarks>
    /// A subscription is one object: it is both the entry the channel keeps and the token returned to the
    /// subscriber, and it knows its slot, so disposing it is O(1). Outside a publish pass, removal moves the
    /// last subscriber into the freed slot. During a pass, removal only clears the slot, and the slots are
    /// compacted in order after the outermost pass. Invariant: <c>m_count &lt;= m_slots.Length</c>, every
    /// registered subscription sits at its own index, and a slot's subscription and handler are both set or
    /// both cleared.
    /// </remarks>
    /// <typeparam name="TMessage">Message type.</typeparam>
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    public sealed class MessageChannel<TMessage> : IPublisher<TMessage>, ISubscriber<TMessage>, IMessageChannelDiagnostics, IDisposable
    {
        private const int k_defaultCapacity = 8;
        private const string k_objectName = "MessageChannel";

        private Slot[] m_slots;
        private int m_count;
        private int m_publishDepth;
        private bool m_hasPendingRemovals;
        private bool m_isDisposed;

        /// <summary>
        /// Initializes a new channel instance.
        /// </summary>
        public MessageChannel()
        {
            m_slots = new Slot[k_defaultCapacity];
            m_count = 0;
            m_publishDepth = 0;
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

            if (m_isDisposed)
            {
                ThrowDisposed();
            }

            Subscription subscription = new Subscription(this);
            Slot[] slots = m_slots;
            int count = m_count;

            if (count == slots.Length)
            {
                slots = Grow(count);
            }

            subscription.Index = count;
            slots[count].Subscription = subscription;
            slots[count].Handler = handler;
            m_count = count + 1;
            return subscription;
        }

        /// <inheritdoc />
        public void Publish(TMessage message)
        {
            if (m_isDisposed)
            {
                ThrowDisposed();
            }

            // No subscriber means no pass: nothing can be pending either, because a cleared slot still counts.
            if (m_count == 0)
            {
                return;
            }

            // Save and restore instead of increment and decrement; passes of this channel are strictly nested.
            int depth = m_publishDepth;
            m_publishDepth = depth + 1;

            try
            {
                // The count and the slots are re-read at every step, so a handler that subscribes is reached
                // later in this pass and a handler that unsubscribes is skipped.
                for (int i = 0; i < m_count; i++)
                {
                    MessageHandler<TMessage> handler = m_slots[i].Handler;

                    if (handler != null)
                    {
                        handler(message);
                    }
                }
            }
            finally
            {
                m_publishDepth = depth;

                if (depth == 0 && m_hasPendingRemovals)
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
            m_slots = Array.Empty<Slot>();
            m_count = 0;
            m_hasPendingRemovals = false;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowDisposed()
        {
            throw new ObjectDisposedException(k_objectName);
        }

        private Slot[] Grow(int count)
        {
            Slot[] grown = new Slot[Math.Max(count * 2, k_defaultCapacity)];
            Array.Copy(m_slots, grown, count);
            m_slots = grown;
            return grown;
        }

        private void Remove(Subscription subscription)
        {
            if (m_isDisposed)
            {
                return;
            }

            int index = subscription.Index;
            Slot[] slots = m_slots;

            if (m_publishDepth > 0)
            {
                slots[index] = default;
                m_hasPendingRemovals = true;
                return;
            }

            // Outside a pass every slot below the count is live.
            int lastIndex = m_count - 1;

            if (index != lastIndex)
            {
                Subscription last = slots[lastIndex].Subscription;
                slots[index] = slots[lastIndex];
                last.Index = index;
            }

            slots[lastIndex] = default;
            m_count = lastIndex;
        }

        private void Compact()
        {
            Slot[] slots = m_slots;
            int count = m_count;
            int writeIndex = 0;

            for (int readIndex = 0; readIndex < count; readIndex++)
            {
                Subscription subscription = slots[readIndex].Subscription;

                if (subscription == null)
                {
                    continue;
                }

                if (writeIndex != readIndex)
                {
                    slots[writeIndex] = slots[readIndex];
                    subscription.Index = writeIndex;
                }

                writeIndex++;
            }

            for (int clearIndex = writeIndex; clearIndex < count; clearIndex++)
            {
                slots[clearIndex] = default;
            }

            m_count = writeIndex;
            m_hasPendingRemovals = false;
        }

        /// <summary>One registered handler and the subscription that owns it.</summary>
        private struct Slot
        {
            public Subscription Subscription;
            public MessageHandler<TMessage> Handler;
        }

        /// <summary>
        /// One subscription: the entry the channel keeps and the token returned to the subscriber.
        /// </summary>
        [Il2CppSetOption(Option.NullChecks, false)]
        private sealed class Subscription : IDisposable
        {
            private MessageChannel<TMessage> m_owner;

            /// <summary>Slot of this subscription in its channel while it is registered.</summary>
            internal int Index;

            internal Subscription(MessageChannel<TMessage> owner)
            {
                m_owner = owner;
            }

            /// <summary>
            /// Unsubscribes. Runs at most once, also when called concurrently, and is a no-op after the
            /// channel was disposed.
            /// </summary>
            public void Dispose()
            {
                MessageChannel<TMessage> owner = Interlocked.Exchange(ref m_owner, null);

                if (owner != null)
                {
                    owner.Remove(this);
                }
            }
        }
    }

    internal interface IMessageChannelDiagnostics
    {
        int SubscriberCount { get; }
    }
}
