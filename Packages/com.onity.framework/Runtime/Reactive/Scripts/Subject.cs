using System;
using System.Runtime.CompilerServices;
using Onity.Core;
using Unity.IL2CPP.CompilerServices;

namespace Onity.Reactive
{
    /// <summary>
    /// Lightweight subject primitive.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    public sealed class Subject<T> : IOnityObservable<T>, IDisposable, IOnityNodeSource<T>, IOnityNodeOwner<T>
    {
        // Mutable struct: always used through this field, never copied, so it must not be readonly.
        private OnityNodeList<T> m_nodes;
        private bool m_isDisposed;

        /// <summary>
        /// Initializes a new subject.
        /// </summary>
        public Subject()
        {
            m_nodes = OnityNodeList<T>.Create();
            m_isDisposed = false;
        }

        /// <summary>
        /// Subscribes to notifications.
        /// </summary>
        /// <param name="observer">Observer callback.</param>
        /// <returns>Disposable subscription token.</returns>
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
            IDisposable subscription = Subscribe(observer.OnNext);

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
        /// Pushes a value to all observers.
        /// </summary>
        /// <param name="value">Value payload.</param>
        public void OnNext(T value)
        {
            // A non-null single node implies the subject is not disposed (Clear resets it).
            OnityObserverNode<T> single = m_nodes.Single;
            int depth;
            int next;

            if (single != null)
            {
                // One live subscriber and nothing pending: deliver without the loop, in its own exception
                // region and inside the same depth save/restore as the general pass.
                depth = m_nodes.BeginNotification();

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

                // During the callback Single can only become null (a subscribe, an unsubscribe or Dispose),
                // never another node, because swap-back removal and compaction need depth 0. A non-null
                // Single therefore means nothing changed and nothing is pending.
                if (m_nodes.Single != null)
                {
                    m_nodes.EndUnchangedNotification(depth);
                    return;
                }

                // The callback changed the list: the general pass continues after the single slot, where
                // nodes added during the callback sit.
                next = 1;
            }
            else
            {
                if (m_isDisposed)
                {
                    OnityReactiveThrow.SubjectDisposed();
                }

                depth = m_nodes.BeginNotification();
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
        /// Registers <paramref name="node" /> and returns it as the subscription.
        /// </summary>
        /// <param name="node">New node that is not subscribed anywhere yet.</param>
        /// <returns>The registered node.</returns>
        internal IDisposable SubscribeNode(OnityObserverNode<T> node)
        {
            if (m_isDisposed)
            {
                OnityReactiveThrow.SubjectDisposed();
            }

            m_nodes.Add(node, this);
            return node;
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

        // Delivers to every node from index next on, re-reading the list at each step. No exception
        // handling here: next already points past the receiving node, so OnNext resumes after a throwing
        // observer. IL2CPP inlines this loop into OnNext's exception region; Mono keeps the call, so the
        // loop runs in a method without exception clauses and its locals stay in registers.
        // The callback dispatch of OnityObserverNode<T>.Deliver is written out here: MSVC does not inline
        // that call into this loop, and a call per node costs more than the dispatch itself.
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
