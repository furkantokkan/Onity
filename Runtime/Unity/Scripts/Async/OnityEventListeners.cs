using System;
using System.Threading;
using Onity.Unity.Async.Triggers;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Engine behind the reusable and one-shot event handlers (UnityEvent and UI Toolkit). A derived class
    /// adds and removes one listener on its event source and forwards each invocation to
    /// <see cref="Deliver"/>; this class owns the pending wait, the token registration and the lifetime.
    /// </summary>
    /// <typeparam name="T">Payload delivered by the event (<c>Unit</c> for an event without one).</typeparam>
    /// <remarks>
    /// Thread affinity: main thread only, including canceling the token. A wait that starts after the
    /// listener was disposed, canceled or consumed completes as canceled. Starting a new wait while one is
    /// pending abandons the older one (UniTask parity). A one-shot listener removes itself before it
    /// completes its wait, so the continuation may start another one-shot wait.
    /// </remarks>
    internal abstract class OnityEventListener<T> : IDisposable
    {
        private static readonly Action<object> s_cancellationCallback = OnCancellationRequested;

        private readonly CancellationToken m_cancellationToken;
        private readonly bool m_callOnce;
        private CancellationTokenRegistration m_registration;

        // The pending wait: an OnityAutoResetTaskCompletionSource (event without payload) or an
        // OnityAutoResetTaskCompletionSource<T> (event with a payload).
        private object m_pending;
        private bool m_isDisposed;

        protected OnityEventListener(CancellationToken cancellationToken, bool callOnce)
        {
            m_cancellationToken = cancellationToken;
            m_callOnce = callOnce;
        }

        /// <summary>Adds the listener to the event source.</summary>
        protected abstract void AddListener();

        /// <summary>Removes the listener from the event source. Safe to call during an invocation.</summary>
        protected abstract void RemoveListener();

        /// <summary>
        /// Starts listening. The derived constructor calls it last, after its own fields are assigned.
        /// A token that is already canceled leaves the listener disposed without touching the source.
        /// </summary>
        protected void Start()
        {
            if (m_cancellationToken.IsCancellationRequested)
            {
                m_isDisposed = true;
                return;
            }

            AddListener();

            if (m_cancellationToken.CanBeCanceled)
            {
                m_registration = OnityTriggerCancellation.Register(m_cancellationToken, s_cancellationCallback, this);
            }
        }

        /// <summary>Resumes the pending wait with the event payload. Call it from the listener callback.</summary>
        protected void Deliver(T value)
        {
            object pending = TakePending();
            if (pending == null)
            {
                return;
            }

            if (m_callOnce)
            {
                DisposeCore();
            }

            if (pending is OnityAutoResetTaskCompletionSource untyped)
            {
                untyped.TrySetResult();
                return;
            }

            ((OnityAutoResetTaskCompletionSource<T>)pending).TrySetResult(value);
        }

        /// <summary>
        /// Stops listening. A wait pending at this moment completes as canceled. Idempotent.
        /// </summary>
        public void Dispose()
        {
            if (!DisposeCore())
            {
                return;
            }

            CompleteCanceled(TakePending());
        }

        /// <summary>Starts a new wait, without the payload, for the next invocation.</summary>
        internal OnityTask WaitAsync()
        {
            // A previous wait that is still pending is abandoned (UniTask core.Reset parity).
            TakePending();

            OnityAutoResetTaskCompletionSource source = OnityAutoResetTaskCompletionSource.Create();
            OnityTask task = source.Task;

            if (m_isDisposed)
            {
                source.TrySetCanceled(m_cancellationToken);
                return task;
            }

            m_pending = source;
            return task;
        }

        /// <summary>Starts a new wait that completes with the payload of the next invocation.</summary>
        internal OnityTask<T> WaitValueAsync()
        {
            // A previous wait that is still pending is abandoned (UniTask core.Reset parity).
            TakePending();

            OnityAutoResetTaskCompletionSource<T> source = OnityAutoResetTaskCompletionSource<T>.Create();
            OnityTask<T> task = source.Task;

            if (m_isDisposed)
            {
                source.TrySetCanceled(m_cancellationToken);
                return task;
            }

            m_pending = source;
            return task;
        }

        private static void OnCancellationRequested(object state)
        {
            OnityEventListener<T> self = (OnityEventListener<T>)state;
            self.DisposeCore();
            self.CompleteCanceled(self.TakePending());
        }

        private bool DisposeCore()
        {
            if (m_isDisposed)
            {
                return false;
            }

            m_isDisposed = true;
            m_registration.Dispose();
            RemoveListener();
            return true;
        }

        private void CompleteCanceled(object pending)
        {
            if (pending == null)
            {
                return;
            }

            if (pending is OnityAutoResetTaskCompletionSource untyped)
            {
                untyped.TrySetCanceled(m_cancellationToken);
                return;
            }

            ((OnityAutoResetTaskCompletionSource<T>)pending).TrySetCanceled(m_cancellationToken);
        }

        private object TakePending()
        {
            return Interlocked.Exchange(ref m_pending, null);
        }
    }

    /// <summary>
    /// Engine behind the event async enumerators (UnityEvent and UI Toolkit). Listens from the first move
    /// on; an event that arrives while no move is pending is skipped. Two tokens cancel it: the source's
    /// own and the consumer's.
    /// </summary>
    /// <typeparam name="T">Payload yielded for each event.</typeparam>
    /// <remarks>
    /// Thread affinity: main thread only. Cancellation by either token completes the pending move as
    /// canceled and ends the enumeration; a later move returns a canceled task. <c>DisposeAsync</c>
    /// completes an uncommitted move with <c>false</c> (Onity enumerator contract).
    /// </remarks>
    internal abstract class OnityEventListenerEnumerator<T> : IOnityAsyncEnumerator<T>
    {
        private static readonly Action<object> s_sourceCancellationCallback = OnSourceCancellationRequested;
        private static readonly Action<object> s_consumerCancellationCallback = OnConsumerCancellationRequested;

        private readonly CancellationToken m_sourceToken;
        private readonly CancellationToken m_consumerToken;
        private CancellationTokenRegistration m_sourceRegistration;
        private CancellationTokenRegistration m_consumerRegistration;
        private OnityAutoResetTaskCompletionSource<bool> m_pending;
        private T m_current;
        private bool m_hasCurrent;
        private bool m_started;
        private bool m_isDisposed;

        protected OnityEventListenerEnumerator(CancellationToken sourceToken, CancellationToken consumerToken)
        {
            m_sourceToken = sourceToken;
            m_consumerToken = consumerToken;
        }

        /// <inheritdoc />
        public T Current
        {
            get
            {
                if (!m_hasCurrent)
                {
                    throw new InvalidOperationException("The event enumerator has no current item.");
                }

                return m_current;
            }
        }

        /// <summary>Adds the listener to the event source.</summary>
        protected abstract void AddListener();

        /// <summary>Removes the listener from the event source. Safe to call during an invocation.</summary>
        protected abstract void RemoveListener();

        /// <summary>Records one event for the pending move. Call it from the listener callback.</summary>
        protected void Deliver(T value)
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

        /// <inheritdoc />
        public OnityTask<bool> MoveNextAsync()
        {
            if (m_pending != null)
            {
                throw new InvalidOperationException("Another MoveNextAsync is outstanding.");
            }

            m_hasCurrent = false;
            m_current = default;

            if (m_sourceToken.IsCancellationRequested)
            {
                return OnityTask<bool>.FromCanceled(m_sourceToken);
            }

            if (m_consumerToken.IsCancellationRequested)
            {
                return OnityTask<bool>.FromCanceled(m_consumerToken);
            }

            if (m_isDisposed)
            {
                return OnityTask<bool>.FromResult(false);
            }

            OnityAutoResetTaskCompletionSource<bool> source = OnityAutoResetTaskCompletionSource<bool>.Create();
            OnityTask<bool> task = source.Task;
            m_pending = source;

            if (!m_started)
            {
                m_started = true;
                AddListener();

                if (m_sourceToken.CanBeCanceled)
                {
                    m_sourceRegistration = OnityTriggerCancellation.Register(
                        m_sourceToken, s_sourceCancellationCallback, this);
                }

                if (m_consumerToken.CanBeCanceled && m_consumerToken != m_sourceToken)
                {
                    m_consumerRegistration = OnityTriggerCancellation.Register(
                        m_consumerToken, s_consumerCancellationCallback, this);
                }
            }

            return task;
        }

        /// <inheritdoc />
        public OnityTask DisposeAsync()
        {
            if (m_isDisposed)
            {
                return OnityTask.CompletedTask;
            }

            m_hasCurrent = false;
            m_current = default;
            DisposeCore();

            // An uncommitted move ends false when disposal wins (Onity enumerator contract).
            TakePending()?.TrySetResult(false);
            return OnityTask.CompletedTask;
        }

        private static void OnSourceCancellationRequested(object state)
        {
            OnityEventListenerEnumerator<T> self = (OnityEventListenerEnumerator<T>)state;
            self.DisposeCore();
            self.TakePending()?.TrySetCanceled(self.m_sourceToken);
        }

        private static void OnConsumerCancellationRequested(object state)
        {
            OnityEventListenerEnumerator<T> self = (OnityEventListenerEnumerator<T>)state;
            self.DisposeCore();
            self.TakePending()?.TrySetCanceled(self.m_consumerToken);
        }

        private void DisposeCore()
        {
            if (m_isDisposed)
            {
                return;
            }

            m_isDisposed = true;
            m_sourceRegistration.Dispose();
            m_consumerRegistration.Dispose();

            if (m_started)
            {
                RemoveListener();
            }
        }

        private OnityAutoResetTaskCompletionSource<bool> TakePending()
        {
            return Interlocked.Exchange(ref m_pending, null);
        }
    }
}
