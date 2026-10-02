using System;
using Unity.IL2CPP.CompilerServices;

namespace Onity.Reactive
{
    /// <summary>
    /// Observable returned by <see cref="OnityObservableExtensions.Skip{T}" /> for a positive count.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    internal sealed class OnitySkipObservable<T> : OnityOperatorObservable<T>
    {
        private readonly IOnityObservable<T> m_source;
        private readonly int m_count;

        /// <summary>Creates the operator; the arguments are validated by the caller.</summary>
        /// <param name="source">Upstream source.</param>
        /// <param name="count">Number of values to skip; positive.</param>
        internal OnitySkipObservable(IOnityObservable<T> source, int count)
        {
            m_source = source;
            m_count = count;
        }

        /// <inheritdoc />
        public override IDisposable SubscribeNode(OnityObserverNode<T> downstream)
        {
            return OnityNodeSources.Subscribe(m_source, new Sink(downstream, m_count));
        }

        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        private sealed class Sink : OnityOperatorSink<T, T>
        {
            private int m_remaining;

            internal Sink(OnityObserverNode<T> downstream, int count)
                : base(downstream)
            {
                m_remaining = count;
            }

            internal override void OnNext(T value)
            {
                if (m_remaining > 0)
                {
                    m_remaining--;
                    return;
                }

                m_downstream.Deliver(value);
            }
        }
    }

    /// <summary>
    /// Observable returned by <see cref="OnityObservableExtensions.SkipWhile{T}" />.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    internal sealed class OnitySkipWhileObservable<T> : OnityOperatorObservable<T>
    {
        private readonly IOnityObservable<T> m_source;
        private readonly Predicate<T> m_predicate;

        /// <summary>Creates the operator; the arguments are validated by the caller.</summary>
        /// <param name="source">Upstream source.</param>
        /// <param name="predicate">Skip condition.</param>
        internal OnitySkipWhileObservable(IOnityObservable<T> source, Predicate<T> predicate)
        {
            m_source = source;
            m_predicate = predicate;
        }

        /// <inheritdoc />
        public override IDisposable SubscribeNode(OnityObserverNode<T> downstream)
        {
            return OnityNodeSources.Subscribe(m_source, new Sink(downstream, m_predicate));
        }

        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        private sealed class Sink : OnityOperatorSink<T, T>
        {
            private readonly Predicate<T> m_predicate;
            private bool m_isSkipping;

            internal Sink(OnityObserverNode<T> downstream, Predicate<T> predicate)
                : base(downstream)
            {
                m_predicate = predicate;
                m_isSkipping = true;
            }

            internal override void OnNext(T value)
            {
                if (m_isSkipping)
                {
                    if (m_predicate(value))
                    {
                        return;
                    }

                    m_isSkipping = false;
                }

                m_downstream.Deliver(value);
            }
        }
    }

    /// <summary>
    /// Observable returned by <see cref="OnityObservableExtensions.Take{T}" /> for a positive count. The
    /// sink disposes its upstream subscription after the last value, also when that value arrives while
    /// the subscription is still being created.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    internal sealed class OnityTakeObservable<T> : OnityOperatorObservable<T>
    {
        private readonly IOnityObservable<T> m_source;
        private readonly int m_count;

        /// <summary>Creates the operator; the arguments are validated by the caller.</summary>
        /// <param name="source">Upstream source.</param>
        /// <param name="count">Maximum number of values to forward; positive.</param>
        internal OnityTakeObservable(IOnityObservable<T> source, int count)
        {
            m_source = source;
            m_count = count;
        }

        /// <inheritdoc />
        public override IDisposable SubscribeNode(OnityObserverNode<T> downstream)
        {
            Sink sink = new Sink(downstream, m_count);
            return sink.Subscribe(m_source);
        }

        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        private sealed class Sink : OnityOperatorSink<T, T>
        {
            private int m_remaining;
            private IDisposable m_upstream;
            private bool m_shouldDisposeAfterSubscribe;

            internal Sink(OnityObserverNode<T> downstream, int count)
                : base(downstream)
            {
                m_remaining = count;
            }

            internal IDisposable Subscribe(IOnityObservable<T> source)
            {
                IDisposable upstream = OnityNodeSources.Subscribe(source, this);
                m_upstream = upstream;

                if (m_shouldDisposeAfterSubscribe)
                {
                    upstream.Dispose();
                }

                return upstream;
            }

            internal override void OnNext(T value)
            {
                if (m_remaining <= 0)
                {
                    return;
                }

                m_remaining--;
                m_downstream.Deliver(value);

                if (m_remaining > 0)
                {
                    return;
                }

                IDisposable upstream = m_upstream;

                if (upstream == null)
                {
                    m_shouldDisposeAfterSubscribe = true;
                    return;
                }

                upstream.Dispose();
            }
        }
    }

    /// <summary>
    /// Observable returned by <see cref="OnityObservableExtensions.TakeWhile{T}" />. The sink disposes its
    /// upstream subscription at the first value that fails the predicate, also when that value arrives
    /// while the subscription is still being created.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    internal sealed class OnityTakeWhileObservable<T> : OnityOperatorObservable<T>
    {
        private readonly IOnityObservable<T> m_source;
        private readonly Predicate<T> m_predicate;

        /// <summary>Creates the operator; the arguments are validated by the caller.</summary>
        /// <param name="source">Upstream source.</param>
        /// <param name="predicate">Continuation condition.</param>
        internal OnityTakeWhileObservable(IOnityObservable<T> source, Predicate<T> predicate)
        {
            m_source = source;
            m_predicate = predicate;
        }

        /// <inheritdoc />
        public override IDisposable SubscribeNode(OnityObserverNode<T> downstream)
        {
            Sink sink = new Sink(downstream, m_predicate);
            return sink.Subscribe(m_source);
        }

        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        private sealed class Sink : OnityOperatorSink<T, T>
        {
            private readonly Predicate<T> m_predicate;
            private IDisposable m_upstream;
            private bool m_isComplete;
            private bool m_shouldDisposeAfterSubscribe;

            internal Sink(OnityObserverNode<T> downstream, Predicate<T> predicate)
                : base(downstream)
            {
                m_predicate = predicate;
            }

            internal IDisposable Subscribe(IOnityObservable<T> source)
            {
                IDisposable upstream = OnityNodeSources.Subscribe(source, this);
                m_upstream = upstream;

                if (m_shouldDisposeAfterSubscribe)
                {
                    upstream.Dispose();
                }

                return upstream;
            }

            internal override void OnNext(T value)
            {
                if (m_isComplete)
                {
                    return;
                }

                if (m_predicate(value) == false)
                {
                    m_isComplete = true;
                    IDisposable upstream = m_upstream;

                    if (upstream == null)
                    {
                        m_shouldDisposeAfterSubscribe = true;
                        return;
                    }

                    upstream.Dispose();
                    return;
                }

                m_downstream.Deliver(value);
            }
        }
    }

    /// <summary>
    /// Observable returned by <see cref="OnityObservableExtensions.StartWith{T}" />. It needs no sink: it
    /// delivers the initial value to the downstream node, then subscribes that node to the source.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    internal sealed class OnityStartWithObservable<T> : OnityOperatorObservable<T>
    {
        private readonly IOnityObservable<T> m_source;
        private readonly T m_initialValue;

        /// <summary>Creates the operator; the arguments are validated by the caller.</summary>
        /// <param name="source">Upstream source.</param>
        /// <param name="initialValue">Prefix value.</param>
        internal OnityStartWithObservable(IOnityObservable<T> source, T initialValue)
        {
            m_source = source;
            m_initialValue = initialValue;
        }

        /// <inheritdoc />
        public override IDisposable SubscribeNode(OnityObserverNode<T> downstream)
        {
            downstream.Deliver(m_initialValue);
            return OnityNodeSources.Subscribe(m_source, downstream);
        }
    }
}
