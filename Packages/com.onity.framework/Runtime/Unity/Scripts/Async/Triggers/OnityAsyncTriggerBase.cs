using System;
using System.Threading;
using Onity.Reactive;
using Onity.Unity.Reactive;
using UnityEngine;

namespace Onity.Unity.Async.Triggers
{
    /// <summary>
    /// Base component for awaitable MonoBehaviour messages (UniTask <c>AsyncTriggerBase</c> parity).
    /// A derived trigger raises each message with <see cref="RaiseEvent"/>; every registered waiter
    /// (an <see cref="OnityAsyncTriggerHandler{T}"/> or an enumerator from
    /// <see cref="GetAsyncEnumerator"/>) that is waiting at that moment is resumed once with the value.
    /// When the component is destroyed every waiter is completed: handlers as canceled, enumerators
    /// with a final <c>false</c> move.
    /// </summary>
    /// <typeparam name="T">Message payload type.</typeparam>
    /// <remarks>
    /// Thread affinity: main thread only. Waiting, raising, disposing and canceling the tokens passed
    /// to waiters must happen on Unity's main thread; continuations run inline inside the Unity
    /// message that raised the event. The one exception is the trigger for a message Unity raises
    /// off the main thread (<c>OnAudioFilterRead</c>), whose waiter list is synchronized. Raising the
    /// same trigger again from such a continuation throws
    /// <see cref="InvalidOperationException"/>, as in UniTask. A component that is never awakened
    /// (inactive object destroyed before activation) gets no <c>OnDestroy</c> from Unity, so in Play
    /// mode an Update monitor completes its waiters once it detects the destroyed component. A derived
    /// trigger that declares its own <c>Awake</c> or <c>OnDestroy</c> hides the private base methods
    /// (Unity calls only the most derived one), which silently stops the base's awake and destroy
    /// bookkeeping.
    /// </remarks>
    public abstract class OnityAsyncTriggerBase<T> : MonoBehaviour, IOnityAsyncEnumerable<T>
    {
        private OnityTriggerEvent<T> m_triggerEvent;
        private IDisposable m_awakeMonitor;
        private bool m_calledAwake;
        private bool m_calledDestroy;

        // Non-null only for a trigger that is raised off the main thread: it guards every access to
        // m_triggerEvent. The lock is reentrant, so a continuation that runs inline under it may wait again.
        private object m_gate;

        /// <summary>Gets whether this component's <c>Awake</c> has run.</summary>
        private protected bool CalledAwake => m_calledAwake;

        /// <summary>
        /// Synchronizes the waiter list so waiters may be added, removed, raised and completed from any
        /// thread. A derived trigger whose Unity message arrives off the main thread calls it from its
        /// constructor. Continuations still run inline on the raising thread, under the list's lock.
        /// </summary>
        private protected void UseThreadSafeWaiters()
        {
            m_gate = new object();
        }

        /// <summary>
        /// Creates an enumerator that yields every value raised after its first move. Values raised
        /// while no move is pending are skipped. Destroying the component ends the enumeration.
        /// Main thread only.
        /// </summary>
        /// <param name="cancellationToken">Token that cancels the pending move and ends the enumeration.</param>
        /// <returns>A caller-owned enumerator; dispose it to stop listening.</returns>
        public IOnityAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new Enumerator(this, cancellationToken);
        }

        /// <summary>
        /// Resumes every waiter that is waiting now with <paramref name="value"/>. Main thread only,
        /// except for a trigger that synchronizes its waiter list.
        /// </summary>
        /// <param name="value">Message payload.</param>
        /// <exception cref="InvalidOperationException">Raised again while the same trigger is resuming waiters.</exception>
        protected void RaiseEvent(T value)
        {
            object gate = m_gate;
            if (gate == null)
            {
                m_triggerEvent.SetResult(value);
                return;
            }

            lock (gate)
            {
                m_triggerEvent.SetResult(value);
            }
        }

        /// <summary>Hook called at the end of <c>Awake</c>.</summary>
        private protected virtual void OnAwakeCalled()
        {
        }

        internal void AddHandler(IOnityTriggerHandler<T> handler)
        {
            object gate = m_gate;
            if (gate == null)
            {
                AddHandlerCore(handler);
                return;
            }

            lock (gate)
            {
                AddHandlerCore(handler);
            }
        }

        internal void RemoveHandler(IOnityTriggerHandler<T> handler)
        {
            object gate = m_gate;
            if (gate == null)
            {
                m_triggerEvent.Remove(handler);
                return;
            }

            lock (gate)
            {
                m_triggerEvent.Remove(handler);
            }
        }

        private void AddHandlerCore(IOnityTriggerHandler<T> handler)
        {
            if (!m_calledAwake && !m_calledDestroy && this == null)
            {
                // Destroyed before Awake: Unity will never call OnDestroy for this component.
                RaiseDestroyed();
            }

            if (m_calledDestroy)
            {
                handler.OnCompleted();
                return;
            }

            if (!m_calledAwake)
            {
                StartAwakeMonitor();
            }

            m_triggerEvent.Add(handler);
        }

        private void Awake()
        {
            m_calledAwake = true;
            StopAwakeMonitor();
            OnAwakeCalled();
        }

        private void OnDestroy()
        {
            RaiseDestroyed();
        }

        private void RaiseDestroyed()
        {
            object gate = m_gate;
            if (gate == null)
            {
                RaiseDestroyedCore();
                return;
            }

            lock (gate)
            {
                RaiseDestroyedCore();
            }
        }

        private void RaiseDestroyedCore()
        {
            if (m_calledDestroy)
            {
                return;
            }

            m_calledDestroy = true;
            StopAwakeMonitor();
            m_triggerEvent.SetCompleted();
        }

        private void StartAwakeMonitor()
        {
            if (m_awakeMonitor != null || !Application.isPlaying)
            {
                return;
            }

            m_awakeMonitor = OnityUnityObservable.EveryUpdate().Subscribe(_ => MonitorAwake());
        }

        private void MonitorAwake()
        {
            if (m_calledAwake || m_calledDestroy)
            {
                StopAwakeMonitor();
                return;
            }

            if (this == null)
            {
                RaiseDestroyed();
            }
        }

        private void StopAwakeMonitor()
        {
            IDisposable monitor = m_awakeMonitor;
            m_awakeMonitor = null;
            monitor?.Dispose();
        }

        private sealed class Enumerator : IOnityAsyncEnumerator<T>, IOnityTriggerHandler<T>
        {
            private static readonly Action<object> s_cancellationCallback = OnCancellationRequested;

            private readonly OnityAsyncTriggerBase<T> m_parent;
            private readonly CancellationToken m_cancellationToken;
            private CancellationTokenRegistration m_registration;
            private OnityAutoResetTaskCompletionSource<bool> m_pending;
            private T m_current;
            private bool m_hasCurrent;
            private bool m_called;
            private bool m_isCompleted;
            private bool m_isDisposed;

            internal Enumerator(OnityAsyncTriggerBase<T> parent, CancellationToken cancellationToken)
            {
                m_parent = parent;
                m_cancellationToken = cancellationToken;
            }

            IOnityTriggerHandler<T> IOnityTriggerHandler<T>.Prev { get; set; }

            IOnityTriggerHandler<T> IOnityTriggerHandler<T>.Next { get; set; }

            public T Current
            {
                get
                {
                    if (!m_hasCurrent)
                    {
                        throw new InvalidOperationException("The trigger enumerator has no current item.");
                    }

                    return m_current;
                }
            }

            public OnityTask<bool> MoveNextAsync()
            {
                if (m_pending != null)
                {
                    throw new InvalidOperationException("Another MoveNextAsync is outstanding.");
                }

                m_hasCurrent = false;
                m_current = default;

                if (m_cancellationToken.IsCancellationRequested)
                {
                    return OnityTask<bool>.FromCanceled(m_cancellationToken);
                }

                if (m_isCompleted || m_isDisposed)
                {
                    return OnityTask<bool>.FromResult(false);
                }

                OnityAutoResetTaskCompletionSource<bool> source = OnityAutoResetTaskCompletionSource<bool>.Create();
                OnityTask<bool> task = source.Task;
                m_pending = source;

                if (!m_called)
                {
                    m_called = true;
                    m_parent.AddHandler(this);

                    if (!m_isCompleted && m_cancellationToken.CanBeCanceled)
                    {
                        m_registration = OnityTriggerCancellation.Register(
                            m_cancellationToken, s_cancellationCallback, this);
                    }
                }

                return task;
            }

            public OnityTask DisposeAsync()
            {
                if (m_isDisposed)
                {
                    return OnityTask.CompletedTask;
                }

                m_isDisposed = true;
                m_hasCurrent = false;
                m_current = default;
                m_registration.Dispose();
                m_parent.RemoveHandler(this);

                // An uncommitted move ends false when disposal wins (Onity enumerator contract).
                TakePending()?.TrySetResult(false);
                return OnityTask.CompletedTask;
            }

            void IOnityTriggerHandler<T>.OnNext(T value)
            {
                OnityAutoResetTaskCompletionSource<bool> pending = TakePending();
                if (pending == null)
                {
                    return;
                }

                m_current = value;
                m_hasCurrent = true;
                pending.TrySetResult(true);
            }

            void IOnityTriggerHandler<T>.OnCompleted()
            {
                m_isCompleted = true;
                m_registration.Dispose();
                TakePending()?.TrySetResult(false);
            }

            void IOnityTriggerHandler<T>.OnError(Exception exception)
            {
                m_isCompleted = true;
                m_registration.Dispose();
                TakePending()?.TrySetException(exception);
            }

            void IOnityTriggerHandler<T>.OnCanceled(CancellationToken cancellationToken)
            {
                m_isCompleted = true;
                m_registration.Dispose();
                TakePending()?.TrySetCanceled(cancellationToken);
            }

            private OnityAutoResetTaskCompletionSource<bool> TakePending()
            {
                return Interlocked.Exchange(ref m_pending, null);
            }

            private static void OnCancellationRequested(object state)
            {
                Enumerator self = (Enumerator)state;
                self.DisposeCore();
                self.TakePending()?.TrySetCanceled(self.m_cancellationToken);
            }

            private void DisposeCore()
            {
                if (m_isDisposed)
                {
                    return;
                }

                m_isDisposed = true;
                m_registration.Dispose();
                m_parent.RemoveHandler(this);
            }
        }
    }

    /// <summary>
    /// A waiter node stored in an <see cref="OnityTriggerEvent{T}"/> list (UniTask
    /// <c>ITriggerHandler</c> parity). Implement it to build a custom multicast source; the list
    /// owns <see cref="Prev"/> and <see cref="Next"/>.
    /// </summary>
    /// <typeparam name="T">Value type delivered to the waiter.</typeparam>
    /// <remarks>
    /// Thread affinity: main thread only. A terminal callback (<see cref="OnCompleted"/>,
    /// <see cref="OnError"/> or <see cref="OnCanceled"/>) is the last call the waiter receives: the list
    /// removes the waiter before invoking it.
    /// </remarks>
    public interface IOnityTriggerHandler<T>
    {
        /// <summary>Gets or sets the previous waiter. Owned by <see cref="OnityTriggerEvent{T}"/>; do not touch.</summary>
        IOnityTriggerHandler<T> Prev { get; set; }

        /// <summary>Gets or sets the next waiter. Owned by <see cref="OnityTriggerEvent{T}"/>; do not touch.</summary>
        IOnityTriggerHandler<T> Next { get; set; }

        /// <summary>Receives a value raised by <see cref="OnityTriggerEvent{T}.SetResult"/>.</summary>
        /// <param name="value">Raised value.</param>
        void OnNext(T value);

        /// <summary>Receives the failure raised by <see cref="OnityTriggerEvent{T}.SetError"/>.</summary>
        /// <param name="exception">Failure to publish to the waiter.</param>
        void OnError(Exception exception);

        /// <summary>Receives the completion raised by <see cref="OnityTriggerEvent{T}.SetCompleted"/>.</summary>
        void OnCompleted();

        /// <summary>Receives the cancellation raised by <see cref="OnityTriggerEvent{T}.SetCanceled"/>.</summary>
        /// <param name="cancellationToken">Token reported by the canceling call.</param>
        void OnCanceled(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Allocation-free doubly linked waiter list (UniTask <c>TriggerEvent</c> parity) that
    /// <see cref="OnityAsyncTriggerBase{T}"/> is built on. Mutable struct: keep it in a non-readonly
    /// field and never copy it.
    /// </summary>
    /// <typeparam name="T">Value type delivered to the waiters.</typeparam>
    /// <remarks>
    /// Thread affinity: main thread only. A waiter added while values are being delivered does not
    /// receive the current value; a waiter removed during delivery is skipped. A terminal call
    /// (<see cref="SetCompleted"/>, <see cref="SetCanceled"/> or <see cref="SetError"/>) made while a
    /// delivery is running stops that delivery and runs once it unwinds; the first such call wins
    /// (UniTask throws here). Raising a value from inside a delivery throws
    /// <see cref="InvalidOperationException"/>. A waiter that throws is logged and, for a value, removed.
    /// </remarks>
    public struct OnityTriggerEvent<T>
    {
        private enum TerminalKind : byte
        {
            None,
            Completed,
            Canceled,
            Error
        }

        private IOnityTriggerHandler<T> m_head;
        private IOnityTriggerHandler<T> m_tail;
        private IOnityTriggerHandler<T> m_iteratingNext;
        private IOnityTriggerHandler<T> m_iteratingLast;
        private bool m_isIterating;
        private TerminalKind m_pendingTerminal;
        private Exception m_pendingError;
        private CancellationToken m_pendingToken;

        /// <summary>Delivers <paramref name="value"/> to every waiter registered before the call.</summary>
        /// <param name="value">Value to deliver.</param>
        /// <exception cref="InvalidOperationException">Called while a delivery is running.</exception>
        public void SetResult(T value)
        {
            if (m_isIterating)
            {
                throw new InvalidOperationException("Can not trigger itself in iterating.");
            }

            if (m_head == null)
            {
                return;
            }

            m_isIterating = true;
            m_iteratingNext = m_head;
            m_iteratingLast = m_tail;

            try
            {
                while (m_iteratingNext != null)
                {
                    IOnityTriggerHandler<T> handler = m_iteratingNext;
                    m_iteratingNext = handler == m_iteratingLast ? null : handler.Next;

                    try
                    {
                        handler.OnNext(value);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                        Remove(handler);
                    }
                }
            }
            finally
            {
                m_isIterating = false;
                m_iteratingNext = null;
                m_iteratingLast = null;
            }

            if (m_pendingTerminal != TerminalKind.None)
            {
                // A terminal call arrived from a continuation: run it now that the delivery unwound.
                TerminalKind kind = m_pendingTerminal;
                Exception error = m_pendingError;
                CancellationToken token = m_pendingToken;
                m_pendingTerminal = TerminalKind.None;
                m_pendingError = null;
                m_pendingToken = default;
                SetTerminal(kind, error, token);
            }
        }

        /// <summary>
        /// Completes and removes every waiter. A call made from a continuation of a running delivery
        /// takes effect once that delivery unwinds.
        /// </summary>
        public void SetCompleted()
        {
            SetTerminal(TerminalKind.Completed, null, default);
        }

        /// <summary>
        /// Cancels and removes every waiter. A call made from a continuation of a running delivery
        /// takes effect once that delivery unwinds.
        /// </summary>
        /// <param name="cancellationToken">Token reported to each waiter.</param>
        public void SetCanceled(CancellationToken cancellationToken)
        {
            SetTerminal(TerminalKind.Canceled, null, cancellationToken);
        }

        /// <summary>
        /// Faults and removes every waiter. A call made from a continuation of a running delivery
        /// takes effect once that delivery unwinds.
        /// </summary>
        /// <param name="exception">Failure reported to each waiter.</param>
        /// <exception cref="ArgumentNullException"><paramref name="exception"/> is null.</exception>
        public void SetError(Exception exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            SetTerminal(TerminalKind.Error, exception, default);
        }

        /// <summary>
        /// Appends a waiter. A waiter added during a delivery does not receive that delivery's value.
        /// </summary>
        /// <param name="handler">Waiter to register; it must not be registered in another list.</param>
        /// <exception cref="ArgumentNullException"><paramref name="handler"/> is null.</exception>
        public void Add(IOnityTriggerHandler<T> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            handler.Prev = m_tail;
            handler.Next = null;

            if (m_tail == null)
            {
                m_head = handler;
            }
            else
            {
                m_tail.Next = handler;
            }

            m_tail = handler;
        }

        /// <summary>
        /// Unregisters a waiter without notifying it. A waiter that is not in the list is ignored.
        /// Removing a waiter during a delivery skips it for the rest of that delivery.
        /// </summary>
        /// <param name="handler">Waiter to unregister.</param>
        /// <exception cref="ArgumentNullException"><paramref name="handler"/> is null.</exception>
        public void Remove(IOnityTriggerHandler<T> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            IOnityTriggerHandler<T> prev = handler.Prev;
            IOnityTriggerHandler<T> next = handler.Next;

            if (prev == null && m_head != handler)
            {
                return;
            }

            if (handler == m_iteratingNext)
            {
                m_iteratingNext = handler == m_iteratingLast ? null : next;
            }

            if (handler == m_iteratingLast)
            {
                m_iteratingLast = prev;
            }

            if (prev == null)
            {
                m_head = next;
            }
            else
            {
                prev.Next = next;
            }

            if (next == null)
            {
                m_tail = prev;
            }
            else
            {
                next.Prev = prev;
            }

            handler.Prev = null;
            handler.Next = null;
        }

        private void SetTerminal(TerminalKind kind, Exception error, CancellationToken token)
        {
            if (m_isIterating)
            {
                // Called from a continuation: stop delivering and run after unwinding; first call wins.
                if (m_pendingTerminal == TerminalKind.None)
                {
                    m_pendingTerminal = kind;
                    m_pendingError = error;
                    m_pendingToken = token;
                }

                m_iteratingNext = null;
                return;
            }

            m_isIterating = true;

            try
            {
                while (m_head != null)
                {
                    IOnityTriggerHandler<T> handler = m_head;
                    Remove(handler);

                    try
                    {
                        switch (kind)
                        {
                            case TerminalKind.Completed:
                                handler.OnCompleted();
                                break;
                            case TerminalKind.Canceled:
                                handler.OnCanceled(token);
                                break;
                            default:
                                handler.OnError(error);
                                break;
                        }
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                    }
                }
            }
            finally
            {
                m_isIterating = false;
                m_pendingTerminal = TerminalKind.None;
                m_pendingError = null;
                m_pendingToken = default;
            }
        }
    }
}
