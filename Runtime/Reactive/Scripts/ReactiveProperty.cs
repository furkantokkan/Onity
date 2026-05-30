using System;
using System.Collections.Generic;
using Onity.Core;

namespace Onity.Reactive
{
    /// <summary>
    /// Mutable reactive property.
    /// </summary>
    /// <typeparam name="T">Property value type.</typeparam>
    public sealed class ReactiveProperty<T> : IReadOnlyReactiveProperty<T>, IOnityObservable<T>, IDisposable
    {
        private readonly Subject<T> m_subject;
        private readonly IEqualityComparer<T> m_comparer;
        private T m_value;
        private bool m_isDisposed;

        /// <summary>
        /// Initializes a new property instance.
        /// </summary>
        /// <param name="initialValue">Initial value.</param>
        /// <param name="comparer">Optional custom comparer.</param>
        public ReactiveProperty(T initialValue = default, IEqualityComparer<T> comparer = null)
        {
            m_value = initialValue;
            m_comparer = comparer ?? EqualityComparer<T>.Default;
            m_subject = new Subject<T>();
            m_isDisposed = false;
        }

        /// <inheritdoc />
        public T Value
        {
            get => m_value;
            set => SetValue(value);
        }

        /// <inheritdoc />
        public IDisposable Subscribe(Observer<T> observer)
        {
            return Subscribe(observer, true);
        }

        /// <inheritdoc />
        public IDisposable Subscribe(OnityObserver<T> observer)
        {
            return Subscribe(observer, true);
        }

        /// <inheritdoc />
        public IDisposable Subscribe(Observer<T> observer, bool emitCurrentValue = true)
        {
            ThrowIfDisposed();

            if (observer == null)
            {
                throw new ArgumentNullException(nameof(observer));
            }

            if (emitCurrentValue)
            {
                observer(m_value);
            }

            return m_subject.Subscribe(observer);
        }

        /// <summary>
        /// Subscribes with lifecycle callbacks.
        /// </summary>
        /// <param name="observer">Lifecycle observer.</param>
        /// <param name="emitCurrentValue">Emit current value before future updates.</param>
        /// <returns>Disposable subscription token.</returns>
        public IDisposable Subscribe(OnityObserver<T> observer, bool emitCurrentValue = true)
        {
            ThrowIfDisposed();

            if (observer == null)
            {
                throw new ArgumentNullException(nameof(observer));
            }

            if (emitCurrentValue)
            {
                observer.OnNext(m_value);
            }

            int trackingId = OnityObservableTracker.RegisterSubscription(this, observer);
            IDisposable subscription = m_subject.Subscribe(observer.OnNext);

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
        /// Updates the value and notifies observers when changed.
        /// </summary>
        /// <param name="value">New value.</param>
        /// <returns>True if value changed; otherwise false.</returns>
        public bool SetValue(T value)
        {
            ThrowIfDisposed();

            if (m_comparer.Equals(m_value, value))
            {
                return false;
            }

            m_value = value;
            m_subject.OnNext(m_value);
            return true;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (m_isDisposed)
            {
                return;
            }

            m_isDisposed = true;
            m_subject.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (m_isDisposed)
            {
                throw new ObjectDisposedException(nameof(ReactiveProperty<T>));
            }
        }
    }
}
