using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Onity.Core;
using Unity.IL2CPP.CompilerServices;

namespace Onity.Reactive
{
    /// <summary>
    /// Mutable reactive property.
    /// </summary>
    /// <typeparam name="T">Property value type.</typeparam>
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    public sealed class ReactiveProperty<T> :
        IReadOnlyReactiveProperty<T>,
        IOnityObservable<T>,
        IDisposable,
        IOnityNodeSource<T>,
        IOnityNodeOwner<T>
    {
        private readonly IEqualityComparer<T> m_comparer;

        // The default comparer as its class type, so the common case calls it without interface dispatch.
        private readonly EqualityComparer<T> m_defaultComparer;

        // Mutable struct: always used through this field, never copied, so it must not be readonly.
        private OnityNodeList<T> m_nodes;
        private T m_value;
        private bool m_isDisposed;

        /// <summary>
        /// Initializes a new property instance.
        /// </summary>
        /// <param name="initialValue">Initial value.</param>
        /// <param name="comparer">Optional custom comparer.</param>
        public ReactiveProperty(T initialValue = default, IEqualityComparer<T> comparer = null)
        {
            m_value = initialValue;
            m_defaultComparer = comparer == null ? EqualityComparer<T>.Default : null;
            m_comparer = comparer ?? m_defaultComparer;
            m_nodes = OnityNodeList<T>.Create();
            m_isDisposed = false;
        }

        /// <inheritdoc />
        public T Value
        {
            get => m_value;
            set => SetValue(value);
        }

        /// <inheritdoc />
        public IDisposable Subscribe(Observer<T> observer)
        {
            return Subscribe(observer, true);
        }

        /// <inheritdoc />
        public IDisposable Subscribe(OnityObserver<T> observer)
        {
            return Subscribe(observer, true);
        }

        /// <inheritdoc />
        public IDisposable Subscribe(Observer<T> observer, bool emitCurrentValue = true)
        {
            ThrowIfDisposed();

            if (observer == null)
            {
                throw new ArgumentNullException(nameof(observer));
            }

            if (emitCurrentValue)
            {
                observer(m_value);
            }

            return AddNode(new OnityCallbackNode<T>(observer));
        }

        /// <summary>
        /// Subscribes with lifecycle callbacks.
        /// </summary>
        /// <param name="observer">Lifecycle observer.</param>
        /// <param name="emitCurrentValue">Emit current value before future updates.</param>
        /// <returns>Disposable subscription token.</returns>
        public IDisposable Subscribe(OnityObserver<T> observer, bool emitCurrentValue = true)
        {
            ThrowIfDisposed();

            if (observer == null)
            {
                throw new ArgumentNullException(nameof(observer));
            }

            if (emitCurrentValue)
            {
                observer.OnNext(m_value);
            }

            int trackingId = OnityObservableTracker.RegisterSubscription(this, observer);
            IDisposable subscription = AddNode(new OnityCallbackNode<T>(new Observer<T>(observer.OnNext)));

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
        /// Updates the value and notifies observers when changed.
        /// </summary>
        /// <param name="value">New value.</param>
        /// <returns>True if value changed; otherwise false.</returns>
        public bool SetValue(T value)
        {
            if (m_isDisposed)
            {
                OnityReactiveThrow.ReactivePropertyDisposed();
            }

            EqualityComparer<T> defaultComparer = m_defaultComparer;
            bool isEqual = defaultComparer != null
                ? defaultComparer.Equals(m_value, value)
                : m_comparer.Equals(m_value, value);

            if (isEqual)
            {
                return false;
            }

            m_value = value;

            // Same single-subscriber path as Subject<T>.OnNext; see there for why a non-null Single after
            // the callback means nothing changed.
            OnityObserverNode<T> single = m_nodes.Single;
            int depth = m_nodes.BeginNotification();
            int next;

            if (single != null)
            {
                try
                {
                    Action<T> action = single.ActionCallback;

                    if (action != null)
                    {
                        action(value);
                    }
                    else
                    {
                        Observer<T> observer = single.ObserverCallback;

                        if (observer != null)
                        {
                            observer(value);
                        }
                        else
                        {
                            single.OnNext(value);
                        }
                    }
                }
                catch (Exception exception)
                {
                    OnityObservableExceptionHandler.Publish(exception);
                }

                if (m_nodes.Single != null)
                {
                    m_nodes.EndUnchangedNotification(depth);
                    return true;
                }

                next = 1;
            }
            else
            {
                next = 0;
            }

            // One exception region for the whole pass: a throwing observer is reported, then delivery
            // resumes with the next observer. Publish never throws, so the depth needs no finally.
            while (true)
            {
                try
                {
                    DeliverFrom(ref next, value);
                    break;
                }
                catch (Exception exception)
                {
                    OnityObservableExceptionHandler.Publish(exception);
                }
            }

            if (m_nodes.EndNotification(depth))
            {
                m_nodes.Compact();
            }

            return true;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (m_isDisposed)
            {
                return;
            }

            m_isDisposed = true;
            m_nodes.Clear();
        }

        /// <summary>
        /// Emits the current value to <paramref name="node" />, then registers it; the same order as
        /// <see cref="Subscribe(Observer{T}, bool)" /> with <c>emitCurrentValue</c> set.
        /// </summary>
        /// <param name="node">New node that is not subscribed anywhere yet.</param>
        /// <returns>The registered node.</returns>
        internal IDisposable SubscribeNode(OnityObserverNode<T> node)
        {
            ThrowIfDisposed();
            node.Deliver(m_value);
            return AddNode(node);
        }

        /// <inheritdoc />
        IDisposable IOnityNodeSource<T>.SubscribeNode(OnityObserverNode<T> node)
        {
            return SubscribeNode(node);
        }

        /// <inheritdoc />
        void IOnityNodeOwner<T>.RemoveNode(OnityObserverNode<T> node)
        {
            m_nodes.Remove(node);
        }

        private IDisposable AddNode(OnityObserverNode<T> node)
        {
            // A subscriber may dispose the property while it receives the current value. Registration then
            // throws ObjectDisposedException with the object name "Subject", unchanged from when the
            // property kept its subscribers in an inner Subject.
            if (m_isDisposed)
            {
                OnityReactiveThrow.SubjectDisposed();
            }

            m_nodes.Add(node, this);
            return node;
        }

        private void ThrowIfDisposed()
        {
            if (m_isDisposed)
            {
                OnityReactiveThrow.ReactivePropertyDisposed();
            }
        }

        // Same pass as Subject<T>.DeliverFrom, including the written-out callback dispatch: no exception
        // handling here, next already points past the receiving node. Inlined into SetValue on IL2CPP; a
        // separate call without exception clauses on Mono.
#if ENABLE_IL2CPP
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        private void DeliverFrom(ref int next, T value)
        {
            int index = next;

            while (index < m_nodes.Count)
            {
                OnityObserverNode<T> node = m_nodes.NodeAt(index);
                index++;

                if (node == null)
                {
                    continue;
                }

                next = index;
                Action<T> action = node.ActionCallback;

                if (action != null)
                {
                    action(value);
                    continue;
                }

                Observer<T> observer = node.ObserverCallback;

                if (observer != null)
                {
                    observer(value);
                    continue;
                }

                node.OnNext(value);
            }
        }
    }
}
