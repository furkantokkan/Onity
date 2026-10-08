using System;
using Onity.Core;

namespace Onity.Reactive
{
    /// <summary>
    /// Delegate-backed observable implementation for reactive v2 streams.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    internal sealed class OnityObservableV2<T> : IOnityObservableV2<T>
    {
        private readonly Func<OnityObserver<T>, IDisposable> m_subscribe;

        /// <summary>
        /// Initializes a new v2 observable wrapper.
        /// </summary>
        /// <param name="subscribe">Subscribe callback.</param>
        public OnityObservableV2(Func<OnityObserver<T>, IDisposable> subscribe)
        {
            m_subscribe = subscribe ?? throw new ArgumentNullException(nameof(subscribe));
        }

        /// <inheritdoc />
        public IDisposable Subscribe(OnityObserver<T> observer)
        {
            if (observer == null)
            {
                throw new ArgumentNullException(nameof(observer));
            }

            int trackingId = OnityObservableTracker.RegisterSubscription(this, observer);
            IDisposable innerSubscription = m_subscribe(observer) ?? DisposableAction.Empty;

            return new DisposableAction(
                () =>
                {
                    innerSubscription.Dispose();
                    observer.Dispose();
                    OnityObservableTracker.CompleteSubscription(trackingId);
                });
        }
    }

    /// <summary>
    /// Factory methods for v2 observable streams.
    /// </summary>
    internal static class OnityObservableV2
    {
        /// <summary>
        /// Creates an observable from event add/remove callbacks.
        /// </summary>
        /// <typeparam name="T">Event payload type.</typeparam>
        /// <param name="addHandler">Event add callback.</param>
        /// <param name="removeHandler">Event remove callback.</param>
        /// <returns>Event-backed observable.</returns>
        public static IOnityObservableV2<T> FromEvent<T>(
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

            return new OnityObservableV2<T>(
                observer =>
                {
                    Action<T> handler = observer.OnNext;
                    addHandler(handler);

                    return new DisposableAction(
                        () => removeHandler(handler));
                });
        }

        /// <summary>
        /// Creates an observable that emits one value and completes.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="value">Single value.</param>
        /// <returns>Single-value observable.</returns>
        public static IOnityObservableV2<T> Return<T>(T value)
        {
            return new OnityObservableV2<T>(
                observer =>
                {
                    observer.OnNext(value);
                    observer.OnCompleted();
                    return DisposableAction.Empty;
                });
        }

        /// <summary>
        /// Returns an observable that never emits.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <returns>Shared empty observable instance.</returns>
        public static IOnityObservableV2<T> Empty<T>()
        {
            return EmptyCache<T>.Instance;
        }

        private static class EmptyCache<T>
        {
            public static readonly IOnityObservableV2<T> Instance =
                new OnityObservableV2<T>(_ => DisposableAction.Empty);
        }
    }
}
