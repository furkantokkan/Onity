using System;
using Onity.Core;

namespace Onity.Reactive
{
    /// <summary>
    /// Delegate-backed observable implementation.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    public sealed class OnityObservable<T> : IOnityObservable<T>
    {
        private readonly Func<Observer<T>, IDisposable> m_subscribe;

        /// <summary>
        /// Initializes a new observable wrapper.
        /// </summary>
        /// <param name="subscribe">Subscribe callback.</param>
        public OnityObservable(Func<Observer<T>, IDisposable> subscribe)
        {
            m_subscribe = subscribe ?? throw new ArgumentNullException(nameof(subscribe));
        }

        /// <inheritdoc />
        public IDisposable Subscribe(Observer<T> observer)
        {
            if (observer == null)
            {
                throw new ArgumentNullException(nameof(observer));
            }

            return m_subscribe(observer) ?? DisposableAction.Empty;
        }

        /// <inheritdoc />
        public IDisposable Subscribe(OnityObserver<T> observer)
        {
            if (observer == null)
            {
                throw new ArgumentNullException(nameof(observer));
            }

            int trackingId = OnityObservableTracker.RegisterSubscription(this, observer);
            IDisposable subscription = m_subscribe(observer.OnNext) ?? DisposableAction.Empty;

            return new DisposableAction(
                () =>
                {
                    subscription.Dispose();
                    observer.OnCompleted();
                    observer.Dispose();
                    OnityObservableTracker.CompleteSubscription(trackingId);
                });
        }
    }

    /// <summary>
    /// Observable factories for event and empty stream creation.
    /// </summary>
    public static class OnityObservable
    {
        /// <summary>
        /// Creates an observable from a callback-style event pair.
        /// </summary>
        /// <typeparam name="T">Event value type.</typeparam>
        /// <param name="addHandler">Event subscribe callback.</param>
        /// <param name="removeHandler">Event unsubscribe callback.</param>
        /// <returns>Observable wrapper for the event stream.</returns>
        public static IOnityObservable<T> FromEvent<T>(
            Action<Action<T>> addHandler,
            Action<Action<T>> removeHandler)
        {
            if (addHandler == null)
            {
                throw new ArgumentNullException(nameof(addHandler));
            }

            if (removeHandler == null)
            {
                throw new ArgumentNullException(nameof(removeHandler));
            }

            return new OnityObservable<T>(
                observer =>
                {
                    Action<T> handler = value => observer(value);
                    addHandler(handler);
                    return new DisposableAction(() => removeHandler(handler));
                });
        }

        /// <summary>
        /// Creates an observable that emits one value and completes by disposal.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="value">Single emitted value.</param>
        /// <returns>Observable emitting one value per subscription.</returns>
        public static IOnityObservable<T> Return<T>(T value)
        {
            return new OnityObservable<T>(
                observer =>
                {
                    observer(value);
                    return DisposableAction.Empty;
                });
        }

        /// <summary>
        /// Returns an observable that never emits.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <returns>Shared empty observable instance.</returns>
        public static IOnityObservable<T> Empty<T>()
        {
            return EmptyCache<T>.Instance;
        }

        private static class EmptyCache<T>
        {
            public static readonly IOnityObservable<T> Instance =
                new OnityObservable<T>(_ => DisposableAction.Empty);
        }
    }
}
