using System;

namespace Onity.Reactive
{
    /// <summary>
    /// Holds a consecutive pair of values emitted by <see cref="OnityObservableExtensions.Pairwise{T}" />.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    public readonly struct OnityPair<T>
    {
        /// <summary>
        /// Previous source value.
        /// </summary>
        public readonly T Previous;

        /// <summary>
        /// Current source value.
        /// </summary>
        public readonly T Current;

        /// <summary>
        /// Initializes a new consecutive value pair.
        /// </summary>
        /// <param name="previous">Previous source value.</param>
        /// <param name="current">Current source value.</param>
        public OnityPair(T previous, T current)
        {
            Previous = previous;
            Current = current;
        }
    }

    /// <summary>
    /// Consecutive value pairing operator for Onity observables.
    /// </summary>
    public static partial class OnityObservableExtensions
    {
        /// <summary>
        /// Pairs each source value with the previous source value.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <returns>Observable that emits a pair once a second value is available, skipping the first value.</returns>
        public static IOnityObservable<OnityPair<T>> Pairwise<T>(this IOnityObservable<T> source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            return new OnityObservable<OnityPair<T>>(
                observer =>
                {
                    bool hasPrevious = false;
                    T previousValue = default;

                    return source.Subscribe(
                        value =>
                        {
                            if (hasPrevious == false)
                            {
                                hasPrevious = true;
                                previousValue = value;
                                return;
                            }

                            T capturedPrevious = previousValue;
                            previousValue = value;
                            observer(new OnityPair<T>(capturedPrevious, value));
                        });
                });
        }
    }
}
