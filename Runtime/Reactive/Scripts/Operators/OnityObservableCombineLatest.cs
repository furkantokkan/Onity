using System;
using Onity.Core;

namespace Onity.Reactive
{
    /// <summary>
    /// Latest-value combination operator for Onity observables.
    /// </summary>
    public static partial class OnityObservableExtensions
    {
        /// <summary>
        /// Combines the latest values of two streams whenever either stream emits.
        /// </summary>
        /// <typeparam name="TFirst">First source value type.</typeparam>
        /// <typeparam name="TSecond">Second source value type.</typeparam>
        /// <typeparam name="TResult">Combined result type.</typeparam>
        /// <param name="first">First source stream.</param>
        /// <param name="second">Second source stream.</param>
        /// <param name="resultSelector">Combines the latest value of each source into a result.</param>
        /// <returns>Observable that emits a combined result once both sources have produced a value.</returns>
        public static IOnityObservable<TResult> CombineLatest<TFirst, TSecond, TResult>(
            this IOnityObservable<TFirst> first,
            IOnityObservable<TSecond> second,
            Func<TFirst, TSecond, TResult> resultSelector)
        {
            if (first == null)
            {
                throw new ArgumentNullException(nameof(first));
            }

            if (second == null)
            {
                throw new ArgumentNullException(nameof(second));
            }

            if (resultSelector == null)
            {
                throw new ArgumentNullException(nameof(resultSelector));
            }

            return new OnityObservable<TResult>(
                observer =>
                {
                    bool hasFirst = false;
                    bool hasSecond = false;
                    TFirst latestFirst = default;
                    TSecond latestSecond = default;

                    IDisposable firstSubscription = first.Subscribe(
                        value =>
                        {
                            hasFirst = true;
                            latestFirst = value;

                            if (hasSecond)
                            {
                                observer(resultSelector(latestFirst, latestSecond));
                            }
                        });

                    IDisposable secondSubscription = second.Subscribe(
                        value =>
                        {
                            hasSecond = true;
                            latestSecond = value;

                            if (hasFirst)
                            {
                                observer(resultSelector(latestFirst, latestSecond));
                            }
                        });

                    return new DisposableAction(
                        () =>
                        {
                            firstSubscription.Dispose();
                            secondSubscription.Dispose();
                        });
                });
        }
    }
}
