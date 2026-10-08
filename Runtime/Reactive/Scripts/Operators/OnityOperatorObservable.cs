using System;
using Onity.Core;
using Unity.IL2CPP.CompilerServices;

namespace Onity.Reactive
{
    /// <summary>
    /// Base class of the synchronous operator observables. Each subscription creates one sink node that
    /// subscribes to the upstream source through the node fast path and forwards to the downstream node.
    /// </summary>
    /// <typeparam name="T">Value type of the operator's output.</typeparam>
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    internal abstract class OnityOperatorObservable<T> : IOnityObservable<T>, IOnityNodeSource<T>
    {
        /// <inheritdoc />
        public IDisposable Subscribe(Observer<T> observer)
        {
            if (observer == null)
            {
                throw new ArgumentNullException(nameof(observer));
            }

            return SubscribeNode(new OnityCallbackNode<T>(observer));
        }

        /// <inheritdoc />
        public IDisposable Subscribe(OnityObserver<T> observer)
        {
            if (observer == null)
            {
                throw new ArgumentNullException(nameof(observer));
            }

            int trackingId = OnityObservableTracker.RegisterSubscription(this, observer);
            IDisposable subscription = SubscribeNode(new OnityCallbackNode<T>(new Observer<T>(observer.OnNext)));

            return new DisposableAction(
                () =>
                {
                    subscription.Dispose();
                    observer.OnCompleted();
                    observer.Dispose();
                    OnityObservableTracker.CompleteSubscription(trackingId);
                });
        }

        /// <summary>
        /// Subscribes <paramref name="downstream" /> to this operator.
        /// </summary>
        /// <param name="downstream">New node that is not subscribed anywhere yet.</param>
        /// <returns>Disposable that ends the subscription; never null.</returns>
        public abstract IDisposable SubscribeNode(OnityObserverNode<T> downstream);
    }

    /// <summary>
    /// Base class of operator sinks: a node subscribed upstream that forwards to one downstream node.
    /// </summary>
    /// <typeparam name="TSource">Upstream value type.</typeparam>
    /// <typeparam name="TResult">Downstream value type.</typeparam>
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    internal abstract class OnityOperatorSink<TSource, TResult> : OnityObserverNode<TSource>
    {
        /// <summary>Node that receives this sink's output; never null.</summary>
        protected readonly OnityObserverNode<TResult> m_downstream;

        /// <summary>Creates a sink that forwards to <paramref name="downstream" />.</summary>
        /// <param name="downstream">Receiving node; never null.</param>
        protected OnityOperatorSink(OnityObserverNode<TResult> downstream)
        {
            m_downstream = downstream;
        }
    }
}
