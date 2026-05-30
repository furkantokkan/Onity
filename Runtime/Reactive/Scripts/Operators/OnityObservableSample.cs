using System;
using Onity.Core;

namespace Onity.Reactive
{
    /// <summary>
    /// Signal-driven sampling operator for Onity observables.
    /// </summary>
    public static partial class OnityObservableExtensions
    {
        /// <summary>
        /// Emits the latest source value each time <paramref name="sampler" /> emits.
        /// </summary>
        /// <typeparam name="T">Source value type.</typeparam>
        /// <typeparam name="TSignal">Sampling signal value type.</typeparam>
        /// <param name="source">Source stream sampled for its latest value.</param>
        /// <param name="sampler">Signal stream that triggers emission of the latest source value.</param>
        /// <returns>Observable that emits the latest source value on each sampler signal once a value is available.</returns>
        public static IOnityObservable<T> Sample<T, TSignal>(
            this IOnityObservable<T> source,
            IOnityObservable<TSignal> sampler)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (sampler == null)
            {
                throw new ArgumentNullException(nameof(sampler));
            }

            return new OnityObservable<T>(
                observer =>
                {
                    bool hasValue = false;
                    T latestValue = default;

                    IDisposable sourceSubscription = source.Subscribe(
                        value =>
                        {
                            hasValue = true;
                            latestValue = value;
                        });

                    IDisposable samplerSubscription = sampler.Subscribe(
                        _ =>
                        {
                            if (hasValue == false)
                            {
                                return;
                            }

                            observer(latestValue);
                        });

                    return new DisposableAction(
                        () =>
                        {
                            samplerSubscription.Dispose();
                            sourceSubscription.Dispose();
                        });
                });
        }
    }
}
