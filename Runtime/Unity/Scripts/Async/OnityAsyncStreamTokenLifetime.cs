using System;
using System.Threading;

namespace Onity.Unity.Async
{
    // Owns only the child CTS. A caller's CTS is never canceled or disposed.
    internal sealed class OnityAsyncStreamTokenLifetime
    {
        private static readonly Action<object> s_forward = state =>
            ((OnityAsyncStreamTokenLifetime)state).Cancel(true);

        private readonly object m_gate = new object();
        private readonly CancellationToken m_token;
        private CancellationTokenSource m_source;
        private CancellationTokenRegistration m_registration;
        private OnityTaskCompletionSource m_quiescence;
        private Exception m_failure;
        private bool m_initializing = true;
        private bool m_closed;
        private bool m_closeCancelPending;
        private bool m_detaching;
        private bool m_detached;
        private bool m_published;
        private int m_activeCancels;

        internal OnityAsyncStreamTokenLifetime()
        {
            m_source = new CancellationTokenSource();
            m_token = m_source.Token;
        }

        internal CancellationToken Token => m_token;

        internal void Initialize(CancellationToken callerToken)
        {
            CancellationTokenRegistration registration = default;
            try
            {
                if (callerToken.CanBeCanceled)
                {
                    if (ExecutionContext.IsFlowSuppressed())
                    {
                        registration = callerToken.Register(s_forward, this, false);
                    }
                    else
                    {
                        AsyncFlowControl flow = ExecutionContext.SuppressFlow();
                        try
                        {
                            registration = callerToken.Register(s_forward, this, false);
                        }
                        finally
                        {
                            flow.Undo();
                        }
                    }
                }
            }
            finally
            {
                lock (m_gate)
                {
                    m_registration = registration;
                    m_initializing = false;
                }
                CompleteQuiescence();
            }
        }

        internal OnityTask Close()
        {
            OnityTask task;
            bool cancel;
            lock (m_gate)
            {
                m_quiescence ??= new OnityTaskCompletionSource();
                task = m_quiescence.Task;
                cancel = !m_closed;
                m_closed = true;
                m_closeCancelPending |= cancel;
            }
            if (cancel)
            {
                Cancel(false);
            }
            CompleteQuiescence();
            return task;
        }

        private void Cancel(bool forwarding)
        {
            CancellationTokenSource source;
            lock (m_gate)
            {
                if (m_source == null || (forwarding && m_closed))
                {
                    return;
                }
                m_activeCancels++;
                if (!forwarding)
                {
                    m_closeCancelPending = false;
                }
                source = m_source;
            }
            try
            {
                source.Cancel();
            }
            catch (Exception exception)
            {
                lock (m_gate)
                {
                    m_failure ??= exception;
                }
            }
            finally
            {
                lock (m_gate)
                {
                    m_activeCancels--;
                }
                CompleteQuiescence();
            }
        }

        private void CompleteQuiescence()
        {
            CancellationTokenRegistration registration;
            lock (m_gate)
            {
                if (!m_closed || m_initializing || m_closeCancelPending || m_activeCancels != 0 || m_detaching)
                {
                    return;
                }
                m_detaching = true;
                registration = m_registration;
                m_registration = default;
            }
            try
            {
                registration.Dispose();
            }
            catch (Exception exception)
            {
                lock (m_gate)
                {
                    m_failure ??= exception;
                }
            }

            OnityTaskCompletionSource output;
            Exception failure;
            lock (m_gate)
            {
                m_detached = true;
                if (m_published)
                {
                    return;
                }
                m_published = true;
                output = m_quiescence;
                failure = m_failure;
            }
            if (failure != null)
            {
                output.TrySetFault(failure);
            }
            else
            {
                output.TrySetResult();
            }
        }

        internal void Dispose()
        {
            CancellationTokenSource source;
            lock (m_gate)
            {
                if (!m_detached || m_activeCancels != 0 || m_initializing)
                {
                    throw new InvalidOperationException("The stream token lifetime is not quiescent.");
                }
                source = m_source;
                m_source = null;
            }
            source?.Dispose();
        }
    }
}
