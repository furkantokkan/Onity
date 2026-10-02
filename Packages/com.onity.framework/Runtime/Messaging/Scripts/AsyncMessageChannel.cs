using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Unity.IL2CPP.CompilerServices;

namespace Onity.Messaging
{
    /// <summary>
    /// Typed awaitable message channel. <see cref="PublishAsync"/> delivers a message
    /// to every subscriber sequentially, awaiting each handler before invoking the next.
    /// Each delivery pass walks a snapshot of the handlers registered when it started, so a
    /// subscribe or unsubscribe issued from inside a handler cannot corrupt the in-flight pass:
    /// a handler removed during a pass still receives that pass's message, and a handler added
    /// during a pass first receives the next one. Removal mirrors the swap-back removal of
    /// <see cref="MessageChannel{TMessage}"/>.
    /// </summary>
    /// <remarks>
    /// A pass reads the live handler array without copying it. The array is copied only when a
    /// subscriber leaves while a pass holds it (copy-on-write, into a recycled spare array), so
    /// the passes in flight keep their own start set. Handlers run inline while their
    /// <see cref="ValueTask"/>s complete synchronously; the first pending one moves the rest of the
    /// pass into an awaiting continuation. Invariant: <c>m_count &lt;= m_slots.Length</c>, every
    /// registered subscription sits at its own index, and while a pass is in flight the slots
    /// below its start count in the array it holds never change.
    /// </remarks>
    /// <typeparam name="TMessage">Message type.</typeparam>
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    public sealed class AsyncMessageChannel<TMessage> : IAsyncPublisher<TMessage>, IAsyncSubscriber<TMessage>, IDisposable
    {
        private const int k_defaultCapacity = 8;
        private const string k_objectName = "AsyncMessageChannel";

        private Slot[] m_slots;
        private Slot[] m_spareSlots;
        private Slot[] m_retiredSlots;
        private int m_count;
        private int m_publishCount;
        private bool m_isSlotsShared;
        private bool m_hasPendingRemovals;
        private bool m_isDisposed;

        /// <summary>
        /// Initializes a new asynchronous channel instance.
        /// </summary>
        public AsyncMessageChannel()
        {
            m_slots = new Slot[k_defaultCapacity];
            m_count = 0;
            m_publishCount = 0;
            m_isSlotsShared = false;
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

            Subscription subscription = new Subscription(this);
            Slot[] slots = m_slots;
            int count = m_count;

            if (count == slots.Length)
            {
                slots = Grow(count);
            }

            // The slot at the count lies above the start count of every pass that holds this array, so
            // appending needs no copy.
            subscription.Index = count;
            slots[count].Subscription = subscription;
            slots[count].Handler = handler;
            m_count = count + 1;
            return subscription;
        }

        /// <inheritdoc />
        /// <remarks>
        /// Never throws synchronously: a disposed channel, a canceled token and a handler's exception
        /// complete the returned task exactly as an <c>async</c> method would (faulted, or canceled for an
        /// <see cref="OperationCanceledException"/>).
        /// </remarks>
        public ValueTask PublishAsync(TMessage message, CancellationToken ct)
        {
            bool canBeCanceled = ct.CanBeCanceled;

            if (m_isDisposed || (canBeCanceled && ct.IsCancellationRequested))
            {
                return PublishRejected(ct);
            }

            int count = m_count;

            if (count == 0)
            {
                return default;
            }

            Slot[] slots = m_slots;
            m_isSlotsShared = true;
            m_publishCount++;
            int index = 0;

            try
            {
                while (index < count)
                {
                    Func<TMessage, CancellationToken, ValueTask> handler = slots[index].Handler;
                    index++;

                    if (handler == null)
                    {
                        continue;
                    }

                    if (canBeCanceled)
                    {
                        ct.ThrowIfCancellationRequested();
                    }

                    ValueTask delivery = handler(message, ct);

                    if (!delivery.IsCompletedSuccessfully)
                    {
                        // The continuation ends the pass when it completes.
                        return ContinueAsync(delivery, slots, index, count, message, ct);
                    }

                    // Consume the result once, as await does; this matters for IValueTaskSource-backed tasks.
                    delivery.GetAwaiter().GetResult();
                }
            }
            catch (Exception exception)
            {
                EndPublish();
                return FromException(exception);
            }

            EndPublish();
            return default;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (m_isDisposed)
            {
                return;
            }

            // A pass in flight keeps the array it holds and finishes its own start set.
            m_isDisposed = true;
            m_slots = Array.Empty<Slot>();
            m_spareSlots = null;
            m_retiredSlots = null;
            m_count = 0;
            m_hasPendingRemovals = false;
        }

        // The task an async method returns when it throws: canceled for an OperationCanceledException,
        // faulted otherwise.
        private static ValueTask FromException(Exception exception)
        {
            AsyncValueTaskMethodBuilder builder = AsyncValueTaskMethodBuilder.Create();
            builder.SetException(exception);
            return builder.Task;
        }

        // A disposed channel or a token canceled before the pass: the same checks, in the same order, as the
        // start of the pass, completed through the returned task.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private ValueTask PublishRejected(CancellationToken ct)
        {
            try
            {
                ThrowIfDisposed();
                ct.ThrowIfCancellationRequested();
            }
            catch (Exception exception)
            {
                return FromException(exception);
            }

            return default;
        }

        // Finishes a pass whose handler at index - 1 returned a pending task: awaits it, then delivers to the
        // rest of the pass's start set, awaiting each handler.
        private async ValueTask ContinueAsync(
            ValueTask delivery,
            Slot[] slots,
            int index,
            int count,
            TMessage message,
            CancellationToken ct)
        {
            try
            {
                await delivery;

                while (index < count)
                {
                    Func<TMessage, CancellationToken, ValueTask> handler = slots[index].Handler;
                    index++;

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
                EndPublish();
            }
        }

        private void EndPublish()
        {
            int publishCount = m_publishCount - 1;
            m_publishCount = publishCount;

            if (publishCount != 0)
            {
                return;
            }

            // No pass holds any array now.
            m_isSlotsShared = false;

            if (m_retiredSlots != null)
            {
                RecycleRetiredSlots();
            }

            if (m_hasPendingRemovals)
            {
                Compact();
            }
        }

        private void ThrowIfDisposed()
        {
            if (m_isDisposed)
            {
                ThrowDisposed();
            }
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

            // No pass holds the new array.
            m_isSlotsShared = false;
            return grown;
        }

        // Copy-on-write: gives the channel its own copy of the array the passes in flight hold.
        private void DetachSlots()
        {
            Slot[] shared = m_slots;
            Slot[] copy = m_spareSlots;

            if (copy == null || copy.Length < shared.Length)
            {
                copy = new Slot[shared.Length];
            }
            else
            {
                m_spareSlots = null;
            }

            Array.Copy(shared, copy, m_count);

            // Passes in flight keep the shared array; it becomes the spare once none is left.
            m_retiredSlots = shared;
            m_slots = copy;
            m_isSlotsShared = false;
        }

        private void RecycleRetiredSlots()
        {
            Slot[] retired = m_retiredSlots;
            m_retiredSlots = null;
            Array.Clear(retired, 0, retired.Length);
            m_spareSlots = retired;
        }

        private void Remove(Subscription subscription)
        {
            if (m_isDisposed)
            {
                return;
            }

            int index = subscription.Index;

            if (m_publishCount > 0)
            {
                if (m_isSlotsShared)
                {
                    DetachSlots();
                }

                m_slots[index] = default;
                m_hasPendingRemovals = true;
                return;
            }

            // Outside a pass every slot below the count is live, and no pass holds the array.
            Slot[] slots = m_slots;
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
            public Func<TMessage, CancellationToken, ValueTask> Handler;
        }

        /// <summary>
        /// One subscription: the entry the channel keeps and the token returned to the subscriber.
        /// </summary>
        [Il2CppSetOption(Option.NullChecks, false)]
        private sealed class Subscription : IDisposable
        {
            private AsyncMessageChannel<TMessage> m_owner;

            /// <summary>Slot of this subscription in its channel while it is registered.</summary>
            internal int Index;

            internal Subscription(AsyncMessageChannel<TMessage> owner)
            {
                m_owner = owner;
            }

            /// <summary>
            /// Unsubscribes. Runs at most once, also when called concurrently, and is a no-op after the
            /// channel was disposed.
            /// </summary>
            public void Dispose()
            {
                AsyncMessageChannel<TMessage> owner = Interlocked.Exchange(ref m_owner, null);

                if (owner != null)
                {
                    owner.Remove(this);
                }
            }
        }
    }
}
