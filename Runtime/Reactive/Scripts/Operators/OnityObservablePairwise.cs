using System;
using Unity.IL2CPP.CompilerServices;

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

            return new OnityPairwiseObservable<T>(source);
        }
    }

    /// <summary>
    /// Observable returned by <see cref="OnityObservableExtensions.Pairwise{T}" />. Each subscription keeps
    /// its own previous value.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    internal sealed class OnityPairwiseObservable<T> : OnityOperatorObservable<OnityPair<T>>
    {
        private readonly IOnityObservable<T> m_source;

        /// <summary>Creates the operator; the argument is validated by the caller.</summary>
        /// <param name="source">Upstream source.</param>
        internal OnityPairwiseObservable(IOnityObservable<T> source)
        {
            m_source = source;
        }

        /// <inheritdoc />
        public override IDisposable SubscribeNode(OnityObserverNode<OnityPair<T>> downstream)
        {
            return OnityNodeSources.Subscribe(m_source, new Sink(downstream));
        }

        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        private sealed class Sink : OnityOperatorSink<T, OnityPair<T>>
        {
            private bool m_hasPrevious;
            private T m_previousValue;

            internal Sink(OnityObserverNode<OnityPair<T>> downstream)
                : base(downstream)
            {
            }

            internal override void OnNext(T value)
            {
                if (m_hasPrevious == false)
                {
                    m_hasPrevious = true;
                    m_previousValue = value;
                    return;
                }

                T previousValue = m_previousValue;
                m_previousValue = value;
                m_downstream.Deliver(new OnityPair<T>(previousValue, value));
            }
        }
    }
}
