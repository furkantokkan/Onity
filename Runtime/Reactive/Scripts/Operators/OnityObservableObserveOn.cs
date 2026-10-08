using System;
using System.Collections.Generic;
using Onity.Core;

namespace Onity.Reactive
{
    /// <summary>
    /// Frame-provider scheduling operator for Onity observables.
    /// </summary>
    public static partial class OnityObservableExtensions
    {
        /// <summary>
        /// Re-posts each source value onto the provided <see cref="OnityFrameProvider" />.
        /// Values are buffered and replayed on the next provider frame tick rather than
        /// forwarded synchronously, which moves emission onto the provider's loop. This is
        /// the required hop after <c>SelectAwait</c> and <c>WhereAwait</c>, whose results
        /// resume off the originating thread.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="frameProvider">Frame provider used to schedule emission.</param>
        /// <returns>Observable that emits source values on the provider frame loop.</returns>
        public static IOnityObservable<T> ObserveOn<T>(
            this IOnityObservable<T> source,
            OnityFrameProvider frameProvider)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (frameProvider == null)
            {
                throw new ArgumentNullException(nameof(frameProvider));
            }

            return new OnityObservable<T>(
                observer =>
                {
                    object gate = new object();
                    Queue<T> pendingValues = new Queue<T>(8);

                    IDisposable sourceSubscription = source.Subscribe(
                        value =>
                        {
                            lock (gate)
                            {
                                pendingValues.Enqueue(value);
                            }
                        });

                    IDisposable frameSubscription = frameProvider.EveryFrame().Subscribe(
                        _ =>
                        {
                            while (true)
                            {
                                T value;

                                lock (gate)
                                {
                                    if (pendingValues.Count == 0)
                                    {
                                        return;
                                    }

                                    value = pendingValues.Dequeue();
                                }

                                observer(value);
                            }
                        });

                    return new DisposableAction(
                        () =>
                        {
                            frameSubscription.Dispose();
                            sourceSubscription.Dispose();
                        });
                });
        }
    }
}
