using System;
using System.Buffers;
using System.Threading;
using UnityEngine;

namespace Onity.Unity.Async
{
    // Shares one subscription to a push source (a reactive property or a message subscriber) between
    // one-shot waiters. Onity's Subject<T> and MessageChannel<T> also deliver a publication to
    // subscriptions added while that publication is being delivered, so a waiter that resumed inline
    // and awaited again with its own subscription would receive the same value again, and a wait loop
    // would never yield. Here each delivery takes only the waiters registered before it, so a waiter
    // registered during a delivery waits for the next one.
    // Threading: Wait, deliveries and subscription changes run on the source's thread (Unity's main
    // thread for Onity's sources). A waiter's cancellation may run on any thread; it only touches the
    // waiter list under the gate, and releases the source subscription only on the source's thread.
    // Waiters and their completion sources are pooled; a generation tag stops a stale delivery or
    // cancellation from completing a recycled waiter.
    internal abstract class OnityWaitHub<T>
    {
        private readonly object m_gate = new object();
        private Entry[] m_entries = Array.Empty<Entry>();
        private int m_count;
        private int m_deliveryDepth;
        private IDisposable m_subscription;
        private int m_subscriptionThreadId;
        private bool m_isSubscribing;
        private bool m_wasIdleAfterDelivery;

        internal OnityTask<T> Wait(Func<T, bool> predicate, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return OnityAutoResetTaskCompletionSource<T>.CreateFromCanceled(cancellationToken, out _).Task;
            }

            Waiter waiter = Waiter.Rent(this, predicate, cancellationToken, out int generation, out OnityTask<T> task);
            bool isRegistered;
            bool subscribe;

            lock (m_gate)
            {
                isRegistered = waiter.IsPending(generation);

                if (isRegistered)
                {
                    Add(new Entry(waiter, generation));
                    m_wasIdleAfterDelivery = false;
                }

                // A source that publishes from inside Subscribe re-enters Wait; subscribe only once.
                subscribe = isRegistered && m_subscription == null && m_isSubscribing == false;
                m_isSubscribing |= subscribe;
            }

            // The waiter may be recycled after this; it is not touched again here.
            waiter.Release();

            if (subscribe)
            {
                Subscribe();
            }

            return task;
        }

        protected abstract IDisposable SubscribeSource();

        // Called by the source for every publication.
        protected void OnNext(T value)
        {
            Entry[] snapshot = TakeEntries(out int count, true);

            try
            {
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        snapshot[i].Waiter.Deliver(this, snapshot[i].Generation, value);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                    }
                }
            }
            finally
            {
                ReturnEntries(snapshot, count);
                IDisposable idleSubscription = null;

                lock (m_gate)
                {
                    m_deliveryDepth--;

                    if (m_deliveryDepth == 0 && m_count == 0 && m_subscription != null)
                    {
                        // Stay subscribed through one idle delivery, so a waiter that registers again
                        // shortly after (for example from a posted continuation) reuses the subscription.
                        if (m_wasIdleAfterDelivery)
                        {
                            idleSubscription = m_subscription;
                            m_subscription = null;
                            m_wasIdleAfterDelivery = false;
                        }
                        else
                        {
                            m_wasIdleAfterDelivery = true;
                        }
                    }
                }

                idleSubscription?.Dispose();
            }
        }

        private void Subscribe()
        {
            IDisposable subscription;

            try
            {
                subscription = SubscribeSource();
            }
            catch (Exception exception)
            {
                // A disposed source cancels the waiters; any other subscribe failure faults them.
                Entry[] snapshot;
                int count;

                lock (m_gate)
                {
                    m_isSubscribing = false;
                    snapshot = TakeEntries(out count, false);
                }

                for (int i = 0; i < count; i++)
                {
                    snapshot[i].Waiter.Fail(snapshot[i].Generation, exception);
                }

                ReturnEntries(snapshot, count);
                return;
            }

            lock (m_gate)
            {
                m_isSubscribing = false;
                m_subscription = subscription;
                m_subscriptionThreadId = Environment.CurrentManagedThreadId;
                m_wasIdleAfterDelivery = false;
            }
        }

        private void Requeue(Waiter waiter, int generation)
        {
            lock (m_gate)
            {
                if (waiter.IsPending(generation))
                {
                    Add(new Entry(waiter, generation));
                }
            }
        }

        private void Remove(Waiter waiter, int generation)
        {
            IDisposable idleSubscription = null;

            lock (m_gate)
            {
                for (int i = 0; i < m_count; i++)
                {
                    if (ReferenceEquals(m_entries[i].Waiter, waiter) && m_entries[i].Generation == generation)
                    {
                        m_count--;
                        Array.Copy(m_entries, i + 1, m_entries, i, m_count - i);
                        m_entries[m_count] = default;
                        break;
                    }
                }

                // A cancellation on the source's thread releases an unused subscription now; from
                // another thread, the next delivery releases it (the sources are not thread-safe).
                if (m_count == 0
                    && m_deliveryDepth == 0
                    && m_subscription != null
                    && Environment.CurrentManagedThreadId == m_subscriptionThreadId)
                {
                    idleSubscription = m_subscription;
                    m_subscription = null;
                    m_wasIdleAfterDelivery = false;
                }
            }

            idleSubscription?.Dispose();
        }

        private void Add(Entry entry)
        {
            if (m_count == m_entries.Length)
            {
                Array.Resize(ref m_entries, m_count == 0 ? 4 : m_count * 2);
            }

            m_entries[m_count++] = entry;
        }

        private Entry[] TakeEntries(out int count, bool isDelivery)
        {
            Entry[] snapshot = null;

            lock (m_gate)
            {
                count = m_count;

                if (count != 0)
                {
                    snapshot = ArrayPool<Entry>.Shared.Rent(count);
                    Array.Copy(m_entries, snapshot, count);
                    Array.Clear(m_entries, 0, count);
                    m_count = 0;
                }

                if (isDelivery)
                {
                    m_deliveryDepth++;
                }
            }

            return snapshot;
        }

        private static void ReturnEntries(Entry[] snapshot, int count)
        {
            if (snapshot == null)
            {
                return;
            }

            Array.Clear(snapshot, 0, count);
            ArrayPool<Entry>.Shared.Return(snapshot);
        }

        private static CancellationTokenRegistration RegisterWithoutContext(
            CancellationToken token,
            Action<object> callback,
            object state)
        {
            if (ExecutionContext.IsFlowSuppressed())
            {
                return token.Register(callback, state, false);
            }

            AsyncFlowControl flow = ExecutionContext.SuppressFlow();

            try
            {
                return token.Register(callback, state, false);
            }
            finally
            {
                flow.Undo();
            }
        }

        private readonly struct Entry
        {
            public readonly Waiter Waiter;
            public readonly int Generation;

            public Entry(Waiter waiter, int generation)
            {
                Waiter = waiter;
                Generation = generation;
            }
        }

        // One pooled wait. m_state holds the current generation (even) or, once claimed by a
        // completion, generation | 1. The waiter returns to the pool after both the renter has finished
        // registering it and its completion has run (m_releases reaches two).
        private sealed class Waiter
        {
            private const int k_maxPoolSize = 64;

            private static readonly Action<object> s_onCanceled = state => ((Waiter)state).OnCanceled();
            private static readonly object s_poolGate = new object();
            private static Waiter s_poolHead;
            private static int s_poolCount;

            private OnityWaitHub<T> m_hub;
            private Func<T, bool> m_predicate;
            private OnityAutoResetTaskCompletionSource<T> m_source;
            private CancellationToken m_token;
            private CancellationTokenRegistration m_registration;
            private int m_state;
            private int m_releases;
            private Waiter m_nextFree;

            internal static Waiter Rent(
                OnityWaitHub<T> hub,
                Func<T, bool> predicate,
                CancellationToken cancellationToken,
                out int generation,
                out OnityTask<T> task)
            {
                Waiter waiter = Pop() ?? new Waiter();
                waiter.m_hub = hub;
                waiter.m_predicate = predicate;
                waiter.m_source = OnityAutoResetTaskCompletionSource<T>.Create();
                waiter.m_token = cancellationToken;
                waiter.m_releases = 0;
                generation = (waiter.m_state | 1) + 1;
                Volatile.Write(ref waiter.m_state, generation);
                task = waiter.m_source.Task;

                if (cancellationToken.CanBeCanceled)
                {
                    // Runs OnCanceled inline when the token was canceled after the caller's check.
                    waiter.m_registration = RegisterWithoutContext(cancellationToken, s_onCanceled, waiter);
                }

                return waiter;
            }

            internal bool IsPending(int generation)
            {
                return Volatile.Read(ref m_state) == generation;
            }

            internal void Deliver(OnityWaitHub<T> hub, int generation, T value)
            {
                if (IsPending(generation) == false)
                {
                    return;
                }

                // A concurrent cancellation may already have cleared the predicate; the claim below then fails.
                Func<T, bool> predicate = m_predicate;
                Exception error = null;
                bool matches = true;

                if (predicate != null)
                {
                    try
                    {
                        matches = predicate(value);
                    }
                    catch (Exception exception)
                    {
                        error = exception;
                    }
                }

                if (matches == false)
                {
                    hub.Requeue(this, generation);
                    return;
                }

                if (TryClaim(generation) == false)
                {
                    return;
                }

                // Waits for a cancellation callback that lost the claim on another thread.
                m_registration.Dispose();
                OnityAutoResetTaskCompletionSource<T> source = m_source;
                Clear();
                Release();

                if (error != null)
                {
                    source.TrySetException(error);
                }
                else
                {
                    source.TrySetResult(value);
                }
            }

            internal void Fail(int generation, Exception exception)
            {
                if (TryClaim(generation) == false)
                {
                    return;
                }

                m_registration.Dispose();
                OnityAutoResetTaskCompletionSource<T> source = m_source;
                Clear();
                Release();

                if (exception is ObjectDisposedException)
                {
                    source.TrySetCanceled();
                }
                else
                {
                    source.TrySetException(exception);
                }
            }

            internal void Release()
            {
                if (Interlocked.Increment(ref m_releases) != 2)
                {
                    return;
                }

                m_registration = default;

                lock (s_poolGate)
                {
                    if (s_poolCount < k_maxPoolSize)
                    {
                        m_nextFree = s_poolHead;
                        s_poolHead = this;
                        s_poolCount++;
                    }
                }
            }

            private static Waiter Pop()
            {
                lock (s_poolGate)
                {
                    Waiter waiter = s_poolHead;

                    if (waiter != null)
                    {
                        s_poolHead = waiter.m_nextFree;
                        waiter.m_nextFree = null;
                        s_poolCount--;
                    }

                    return waiter;
                }
            }

            private bool TryClaim(int generation)
            {
                return Interlocked.CompareExchange(ref m_state, generation | 1, generation) == generation;
            }

            private void OnCanceled()
            {
                int generation = Volatile.Read(ref m_state);

                if ((generation & 1) != 0 || TryClaim(generation) == false)
                {
                    return;
                }

                OnityWaitHub<T> hub = m_hub;
                OnityAutoResetTaskCompletionSource<T> source = m_source;
                CancellationToken token = m_token;
                hub.Remove(this, generation);
                Clear();
                Release();
                source.TrySetCanceled(token);
            }

            private void Clear()
            {
                m_hub = null;
                m_predicate = null;
                m_source = null;
                m_token = default;
            }
        }
    }
}
