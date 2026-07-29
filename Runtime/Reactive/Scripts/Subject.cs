using System;
using Onity.Core;

namespace Onity.Reactive
{
    /// <summary>
    /// Lightweight subject primitive.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    public sealed class Subject<T> : IOnityObservable<T>, IDisposable
    {
        private const int k_defaultCapacity = 8;

        private SubscriptionEntry[] m_entries;
        private int m_count;
        private int m_nextId;
        private bool m_isNotifying;
        private bool m_hasPendingRemovals;
        private bool m_isDisposed;

        /// <summary>
        /// Initializes a new subject.
        /// </summary>
        public Subject()
        {
            m_entries = new SubscriptionEntry[k_defaultCapacity];
            m_count = 0;
            m_nextId = 1;
            m_isNotifying = false;
            m_hasPendingRemovals = false;
            m_isDisposed = false;
        }

        /// <summary>
        /// Subscribes to notifications.
        /// </summary>
        /// <param name="observer">Observer callback.</param>
        /// <returns>Disposable subscription token.</returns>
        public IDisposable Subscribe(Observer<T> observer)
        {
            if (observer == null)
            {
                throw new ArgumentNullException(nameof(observer));
            }

            ThrowIfDisposed();
            EnsureCapacity(m_count + 1);

            int id = m_nextId++;
            m_entries[m_count] = new SubscriptionEntry(id, observer);
            m_count++;

            return new DisposableAction(() => Unsubscribe(id));
        }

        /// <inheritdoc />
        public IDisposable Subscribe(OnityObserver<T> observer)
        {
            if (observer == null)
            {
                throw new ArgumentNullException(nameof(observer));
            }

            int trackingId = OnityObservableTracker.RegisterSubscription(this, observer);
            IDisposable subscription = Subscribe(observer.OnNext);

            return new DisposableAction(
                () =>
                {
                    subscription.Dispose();
                    observer.OnCompleted();
                    observer.Dispose();
                    OnityObservableTracker.CompleteSubscription(trackingId);
                });
        }

        /// <summary>
        /// Pushes a value to all observers.
        /// </summary>
        /// <param name="value">Value payload.</param>
        public void OnNext(T value)
        {
            ThrowIfDisposed();

            m_isNotifying = true;

            try
            {
                for (int i = 0; i < m_count; i++)
                {
                    Observer<T> observer = m_entries[i].Observer;

                    if (observer == null)
                    {
                        continue;
                    }

                    try
                    {
                        observer(value);
                    }
                    catch (Exception exception)
                    {
                        OnityObservableExceptionHandler.Publish(exception);
                    }
                }
            }
            finally
            {
                m_isNotifying = false;

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

            int index = FindIndex(id);

            if (index < 0)
            {
                return;
            }

            if (m_isNotifying)
            {
                m_entries[index].Observer = null;
                m_hasPendingRemovals = true;
                return;
            }

            RemoveAtSwapBack(index);
        }

        private int FindIndex(int id)
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

                if (entry.Observer == null)
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
                throw new ObjectDisposedException(nameof(Subject<T>));
            }
        }

        private struct SubscriptionEntry
        {
            public int Id;
            public Observer<T> Observer;

            public SubscriptionEntry(int id, Observer<T> observer)
            {
                Id = id;
                Observer = observer;
            }
        }
    }
}
