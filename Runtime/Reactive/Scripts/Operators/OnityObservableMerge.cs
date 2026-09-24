using System;
using Onity.Core;

namespace Onity.Reactive
{
    /// <summary>
    /// Stream merging operator for Onity observables.
    /// </summary>
    public static partial class OnityObservableExtensions
    {
        /// <summary>
        /// Merges this stream with one or more additional streams, forwarding every value from every source.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">First source stream.</param>
        /// <param name="others">Additional source streams to merge with <paramref name="source" />.</param>
        /// <returns>Observable that forwards values from all sources.</returns>
        public static IOnityObservable<T> Merge<T>(
            this IOnityObservable<T> source,
            params IOnityObservable<T>[] others)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (others == null)
            {
                throw new ArgumentNullException(nameof(others));
            }

            for (int i = 0; i < others.Length; i++)
            {
                if (others[i] == null)
                {
                    throw new ArgumentNullException(nameof(others));
                }
            }

            if (others.Length == 0)
            {
                return source;
            }

            return new OnityObservable<T>(
                observer =>
                {
                    IDisposable[] subscriptions = new IDisposable[others.Length + 1];
                    subscriptions[0] = source.Subscribe(value => observer(value));

                    for (int i = 0; i < others.Length; i++)
                    {
                        subscriptions[i + 1] = others[i].Subscribe(value => observer(value));
                    }

                    return new DisposableAction(
                        () =>
                        {
                            for (int i = 0; i < subscriptions.Length; i++)
                            {
                                subscriptions[i]?.Dispose();
                            }
                        });
                });
        }
    }
}
