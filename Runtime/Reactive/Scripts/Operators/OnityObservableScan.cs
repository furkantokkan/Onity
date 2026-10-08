using System;
using Unity.IL2CPP.CompilerServices;

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

            return new OnityScanObservable<TSource, TState>(source, seed, accumulator);
        }
    }

    /// <summary>
    /// Observable returned by <see cref="OnityObservableExtensions.Scan{TSource, TState}" />. Each subscription
    /// starts from the seed and keeps its own state.
    /// </summary>
    /// <typeparam name="TSource">Source value type.</typeparam>
    /// <typeparam name="TState">Accumulated state type.</typeparam>
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    internal sealed class OnityScanObservable<TSource, TState> : OnityOperatorObservable<TState>
    {
        private readonly IOnityObservable<TSource> m_source;
        private readonly TState m_seed;
        private readonly Func<TState, TSource, TState> m_accumulator;

        /// <summary>Creates the operator; the arguments are validated by the caller.</summary>
        /// <param name="source">Upstream source.</param>
        /// <param name="seed">Initial accumulator state.</param>
        /// <param name="accumulator">Folds the current state and the next value into the new state.</param>
        internal OnityScanObservable(
            IOnityObservable<TSource> source,
            TState seed,
            Func<TState, TSource, TState> accumulator)
        {
            m_source = source;
            m_seed = seed;
            m_accumulator = accumulator;
        }

        /// <inheritdoc />
        public override IDisposable SubscribeNode(OnityObserverNode<TState> downstream)
        {
            return OnityNodeSources.Subscribe(m_source, new Sink(downstream, m_seed, m_accumulator));
        }

        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        private sealed class Sink : OnityOperatorSink<TSource, TState>
        {
            private readonly Func<TState, TSource, TState> m_accumulator;
            private TState m_state;

            internal Sink(
                OnityObserverNode<TState> downstream,
                TState seed,
                Func<TState, TSource, TState> accumulator)
                : base(downstream)
            {
                m_state = seed;
                m_accumulator = accumulator;
            }

            internal override void OnNext(TSource value)
            {
                TState state = m_accumulator(m_state, value);
                m_state = state;
                m_downstream.Deliver(state);
            }
        }
    }
}
