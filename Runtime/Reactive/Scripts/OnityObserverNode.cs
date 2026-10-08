using System;
using System.Runtime.CompilerServices;
using Onity.Core;
using Unity.IL2CPP.CompilerServices;

namespace Onity.Reactive
{
    /// <summary>
    /// One subscription inside the reactive core. A node is both the entry a source keeps in its node list
    /// and the <see cref="IDisposable" /> handed back to the subscriber, so a plain subscription allocates
    /// one object. Callback nodes carry an <see cref="Action{T}" /> or <see cref="Observer{T}" /> that sources
    /// invoke directly; operator sinks leave both callbacks empty and receive values through
    /// <see cref="OnNext" />. A node is subscribed to at most one source.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    internal abstract class OnityObserverNode<T> : IDisposable
    {
        /// <summary>Action callback that sources invoke directly, or null.</summary>
        internal Action<T> ActionCallback;

        /// <summary>Observer callback that sources invoke directly, or null.</summary>
        internal Observer<T> ObserverCallback;

        /// <summary>Source whose node list holds this node, or null when the node is not registered.</summary>
        internal IOnityNodeOwner<T> Owner;

        /// <summary>Position of this node in its owner's node list while <see cref="Owner" /> is set.</summary>
        internal int Index;

        /// <summary>
        /// Removes the node from the source that holds it. Idempotent, and a no-op after the source was
        /// disposed.
        /// </summary>
        public void Dispose()
        {
            IOnityNodeOwner<T> owner = Owner;

            if (owner == null)
            {
                return;
            }

            owner.RemoveNode(this);
        }

        /// <summary>
        /// Delivers a value to this node: its callback when it has one, otherwise <see cref="OnNext" />.
        /// </summary>
        /// <param name="value">Value payload.</param>
        /// <remarks>
        /// An instance method on purpose: under IL2CPP, generic calls made from static or struct methods
        /// re-check class initialization on every call, and this runs once per delivered value.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Deliver(T value)
        {
            Action<T> action = ActionCallback;

            if (action != null)
            {
                action(value);
                return;
            }

            Observer<T> observer = ObserverCallback;

            if (observer != null)
            {
                observer(value);
                return;
            }

            OnNext(value);
        }

        /// <summary>
        /// Receives a value. Sources call it only for nodes without a callback; operator sinks override it.
        /// </summary>
        /// <param name="value">Value payload.</param>
        internal abstract void OnNext(T value);
    }

    /// <summary>
    /// Subscription node for a plain callback: <see cref="Observer{T}" /> subscriptions and the
    /// <see cref="OnityObservableExtensions.Subscribe{T}(IOnityObservable{T}, Action{T})" /> fast path.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    internal sealed class OnityCallbackNode<T> : OnityObserverNode<T>
    {
        /// <summary>Creates a node that invokes <paramref name="observer" />.</summary>
        /// <param name="observer">Observer callback; never null.</param>
        internal OnityCallbackNode(Observer<T> observer)
        {
            ObserverCallback = observer;
        }

        /// <summary>Creates a node that invokes <paramref name="action" />.</summary>
        /// <param name="action">Action callback; never null.</param>
        internal OnityCallbackNode(Action<T> action)
        {
            ActionCallback = action;
        }

        /// <inheritdoc />
        internal override void OnNext(T value)
        {
            Action<T> action = ActionCallback;

            if (action != null)
            {
                action(value);
                return;
            }

            ObserverCallback(value);
        }
    }

    /// <summary>
    /// Internal fast-path subscribe contract. A source that implements it accepts a node directly, so
    /// callback nodes and operator sinks subscribe without allocating a delegate.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    internal interface IOnityNodeSource<T>
    {
        /// <summary>
        /// Subscribes <paramref name="node" /> with the same semantics as the public
        /// <see cref="IOnityObservable{T}.Subscribe(Observer{T})" /> overload.
        /// </summary>
        /// <param name="node">New node that is not subscribed anywhere yet.</param>
        /// <returns>Disposable that ends the subscription.</returns>
        IDisposable SubscribeNode(OnityObserverNode<T> node);
    }

    /// <summary>
    /// A source that keeps registered nodes in an <see cref="OnityNodeList{T}" />.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    internal interface IOnityNodeOwner<T>
    {
        /// <summary>Removes a node that this owner holds (its <see cref="OnityObserverNode{T}.Owner" />).</summary>
        /// <param name="node">Registered node.</param>
        void RemoveNode(OnityObserverNode<T> node);
    }

    /// <summary>
    /// Subscribe helpers that use the node fast path when a source supports it and the public delegate
    /// path otherwise. Callers pass a validated, non-null source and node.
    /// </summary>
    [Il2CppSetOption(Option.NullChecks, false)]
    internal static class OnityNodeSources
    {
        /// <summary>
        /// Subscribes <paramref name="node" /> to <paramref name="source" />.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">Upstream source; never null.</param>
        /// <param name="node">New node that is not subscribed anywhere yet.</param>
        /// <returns>Disposable that ends the subscription; never null.</returns>
        internal static IDisposable Subscribe<T>(IOnityObservable<T> source, OnityObserverNode<T> node)
        {
            // Exact and base-class type checks are cheaper than an interface type check under IL2CPP.
            if (source is Subject<T> subject)
            {
                return subject.SubscribeNode(node);
            }

            if (source is OnityOperatorObservable<T> operatorObservable)
            {
                return operatorObservable.SubscribeNode(node);
            }

            if (source is ReactiveProperty<T> property)
            {
                return property.SubscribeNode(node);
            }

            if (source is IOnityNodeSource<T> nodeSource)
            {
                return nodeSource.SubscribeNode(node);
            }

            Observer<T> observer = node.ObserverCallback ?? new Observer<T>(node.OnNext);
            return source.Subscribe(observer) ?? DisposableAction.Empty;
        }

        /// <summary>
        /// Subscribes <paramref name="onNext" /> to <paramref name="source" /> through a callback node when
        /// the source supports nodes, otherwise through an <see cref="Observer{T}" /> wrapper.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">Source stream; never null.</param>
        /// <param name="onNext">Value callback; never null.</param>
        /// <returns>Disposable subscription token.</returns>
        internal static IDisposable SubscribeAction<T>(IOnityObservable<T> source, Action<T> onNext)
        {
            if (source is Subject<T> subject)
            {
                return subject.SubscribeNode(new OnityCallbackNode<T>(onNext));
            }

            if (source is OnityOperatorObservable<T> operatorObservable)
            {
                return operatorObservable.SubscribeNode(new OnityCallbackNode<T>(onNext));
            }

            if (source is ReactiveProperty<T> property)
            {
                return property.SubscribeNode(new OnityCallbackNode<T>(onNext));
            }

            if (source is IOnityNodeSource<T> nodeSource)
            {
                return nodeSource.SubscribeNode(new OnityCallbackNode<T>(onNext));
            }

            return source.Subscribe(new Observer<T>(onNext));
        }
    }

    /// <summary>
    /// Cold throw helpers, kept out of line so hot methods stay small.
    /// </summary>
    internal static class OnityReactiveThrow
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void SubjectDisposed()
        {
            throw new ObjectDisposedException("Subject");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void ReactivePropertyDisposed()
        {
            throw new ObjectDisposedException("ReactiveProperty");
        }
    }
}
