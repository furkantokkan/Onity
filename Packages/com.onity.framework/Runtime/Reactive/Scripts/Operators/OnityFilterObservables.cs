using System;
using System.Collections.Generic;
using Unity.IL2CPP.CompilerServices;

namespace Onity.Reactive
{
    /// <summary>
    /// Observable returned by <see cref="OnityObservableExtensions.Where{T}" />. Forwards values that satisfy
    /// the predicate. A following <c>Select</c> fuses with it into one sink.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    internal sealed class OnityWhereObservable<T> : OnityOperatorObservable<T>
    {
        /// <summary>Upstream source; never null.</summary>
        internal readonly IOnityObservable<T> Source;

        /// <summary>Filter condition; never null.</summary>
        internal readonly Predicate<T> Predicate;

        /// <summary>Creates the operator; the arguments are validated by the caller.</summary>
        /// <param name="source">Upstream source.</param>
        /// <param name="predicate">Filter condition.</param>
        internal OnityWhereObservable(IOnityObservable<T> source, Predicate<T> predicate)
        {
            Source = source;
            Predicate = predicate;
        }

        /// <inheritdoc />
        public override IDisposable SubscribeNode(OnityObserverNode<T> downstream)
        {
            return OnityNodeSources.Subscribe(Source, new Sink(downstream, Predicate));
        }

        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        private sealed class Sink : OnityOperatorSink<T, T>
        {
            private readonly Predicate<T> m_predicate;

            internal Sink(OnityObserverNode<T> downstream, Predicate<T> predicate)
                : base(downstream)
            {
                m_predicate = predicate;
            }

            internal override void OnNext(T value)
            {
                if (m_predicate(value))
                {
                    m_downstream.Deliver(value);
                }
            }
        }
    }

    /// <summary>
    /// Observable returned by <see cref="OnityObservableExtensions.Select{TSource, TResult}" />.
    /// </summary>
    /// <typeparam name="TSource">Source value type.</typeparam>
    /// <typeparam name="TResult">Projected value type.</typeparam>
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    internal sealed class OnitySelectObservable<TSource, TResult> : OnityOperatorObservable<TResult>
    {
        private readonly IOnityObservable<TSource> m_source;
        private readonly Func<TSource, TResult> m_selector;

        /// <summary>Creates the operator; the arguments are validated by the caller.</summary>
        /// <param name="source">Upstream source.</param>
        /// <param name="selector">Projection function.</param>
        internal OnitySelectObservable(IOnityObservable<TSource> source, Func<TSource, TResult> selector)
        {
            m_source = source;
            m_selector = selector;
        }

        /// <inheritdoc />
        public override IDisposable SubscribeNode(OnityObserverNode<TResult> downstream)
        {
            return OnityNodeSources.Subscribe(m_source, new Sink(downstream, m_selector));
        }

        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        private sealed class Sink : OnityOperatorSink<TSource, TResult>
        {
            private readonly Func<TSource, TResult> m_selector;

            internal Sink(OnityObserverNode<TResult> downstream, Func<TSource, TResult> selector)
                : base(downstream)
            {
                m_selector = selector;
            }

            internal override void OnNext(TSource value)
            {
                m_downstream.Deliver(m_selector(value));
            }
        }
    }

    /// <summary>
    /// <c>Where</c> followed by <c>Select</c> in one operator: one sink per subscription that calls the
    /// predicate, then the selector for values that pass, in the same order as the two operators.
    /// </summary>
    /// <typeparam name="TSource">Source value type.</typeparam>
    /// <typeparam name="TResult">Projected value type.</typeparam>
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    internal sealed class OnityWhereSelectObservable<TSource, TResult> : OnityOperatorObservable<TResult>
    {
        private readonly IOnityObservable<TSource> m_source;
        private readonly Predicate<TSource> m_predicate;
        private readonly Func<TSource, TResult> m_selector;

        /// <summary>Creates the fused operator; the arguments are validated by the callers.</summary>
        /// <param name="source">Source of the <c>Where</c> operator.</param>
        /// <param name="predicate">Filter condition.</param>
        /// <param name="selector">Projection function.</param>
        internal OnityWhereSelectObservable(
            IOnityObservable<TSource> source,
            Predicate<TSource> predicate,
            Func<TSource, TResult> selector)
        {
            m_source = source;
            m_predicate = predicate;
            m_selector = selector;
        }

        /// <inheritdoc />
        public override IDisposable SubscribeNode(OnityObserverNode<TResult> downstream)
        {
            return OnityNodeSources.Subscribe(m_source, new Sink(downstream, m_predicate, m_selector));
        }

        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        private sealed class Sink : OnityOperatorSink<TSource, TResult>
        {
            private readonly Predicate<TSource> m_predicate;
            private readonly Func<TSource, TResult> m_selector;

            internal Sink(
                OnityObserverNode<TResult> downstream,
                Predicate<TSource> predicate,
                Func<TSource, TResult> selector)
                : base(downstream)
            {
                m_predicate = predicate;
                m_selector = selector;
            }

            internal override void OnNext(TSource value)
            {
                if (m_predicate(value))
                {
                    m_downstream.Deliver(m_selector(value));
                }
            }
        }
    }

    /// <summary>
    /// Observable returned by <see cref="OnityObservableExtensions.DistinctUntilChanged{T}" />. Each
    /// subscription tracks its own last value.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    internal sealed class OnityDistinctUntilChangedObservable<T> : OnityOperatorObservable<T>
    {
        private readonly IOnityObservable<T> m_source;
        private readonly IEqualityComparer<T> m_comparer;

        /// <summary>Creates the operator; the arguments are validated by the caller.</summary>
        /// <param name="source">Upstream source.</param>
        /// <param name="comparer">Equality comparer; never null.</param>
        internal OnityDistinctUntilChangedObservable(IOnityObservable<T> source, IEqualityComparer<T> comparer)
        {
            m_source = source;
            m_comparer = comparer;
        }

        /// <inheritdoc />
        public override IDisposable SubscribeNode(OnityObserverNode<T> downstream)
        {
            return OnityNodeSources.Subscribe(m_source, new Sink(downstream, m_comparer));
        }

        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        private sealed class Sink : OnityOperatorSink<T, T>
        {
            private readonly IEqualityComparer<T> m_comparer;
            private bool m_hasValue;
            private T m_lastValue;

            internal Sink(OnityObserverNode<T> downstream, IEqualityComparer<T> comparer)
                : base(downstream)
            {
                m_comparer = comparer;
            }

            internal override void OnNext(T value)
            {
                if (m_hasValue && m_comparer.Equals(m_lastValue, value))
                {
                    return;
                }

                m_hasValue = true;
                m_lastValue = value;
                m_downstream.Deliver(value);
            }
        }
    }
}
