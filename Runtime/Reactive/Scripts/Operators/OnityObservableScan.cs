using System;

namespace Onity.Reactive
{
    /// <summary>
    /// Stateful accumulation operator for Onity observables.
    /// </summary>
    public static partial class OnityObservableExtensions
    {
        /// <summary>
        /// Accumulates source values into a running state and emits the state after each value.
        /// </summary>
        /// <typeparam name="TSource">Source value type.</typeparam>
        /// <typeparam name="TState">Accumulated state type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="seed">Initial accumulator state.</param>
        /// <param name="accumulator">Folds the current state and the next value into the new state.</param>
        /// <returns>Observable that emits the accumulated state for every source value.</returns>
        public static IOnityObservable<TState> Scan<TSource, TState>(
            this IOnityObservable<TSource> source,
            TState seed,
            Func<TState, TSource, TState> accumulator)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (accumulator == null)
            {
                throw new ArgumentNullException(nameof(accumulator));
            }

            return new OnityObservable<TState>(
                observer =>
                {
                    TState state = seed;

                    return source.Subscribe(
                        value =>
                        {
                            state = accumulator(state, value);
                            observer(state);
                        });
                });
        }
    }
}
