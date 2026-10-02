using System;
using System.Threading;

namespace Onity.Unity.Async
{
    // Shared plumbing of the stream sources (Never, task conversions, Create).
    internal static class OnityStreamSourceTokens
    {
        // Registers a cancellation callback without capturing the caller's ExecutionContext; the callback
        // runs on whichever thread cancels the token.
        internal static CancellationTokenRegistration Register(
            CancellationToken token, Action<object> callback, object state)
        {
            if (ExecutionContext.IsFlowSuppressed())
            {
                return token.Register(callback, state, false);
            }
            AsyncFlowControl flow = ExecutionContext.SuppressFlow();
            try
            {
                return token.Register(callback, state, false);
            }
            finally
            {
                flow.Undo();
            }
        }
    }

    internal static class OnityStreamSourceOutcomes
    {
        internal static OnityAsyncStreamOutcome Canceled(CancellationToken token)
        {
            return new OnityAsyncStreamOutcome { Status = OnityTaskSourceStatus.Canceled, Token = token };
        }

        // A move task that already carries the outcome; a failed or canceled outcome keeps its status.
        internal static OnityTask<bool> MoveTask(OnityAsyncStreamOutcome outcome)
        {
            if (outcome.Status == OnityTaskSourceStatus.Succeeded)
            {
                return OnityTask<bool>.FromResult(outcome.Value);
            }
            var source = new OnityTaskCompletionSource<bool>();
            outcome.Publish(source, false);
            return source.Task;
        }
    }

    // One pending move that is completed by a signal, by cancellation of the enumeration token, or with
    // false on cleanup. Completion is idempotent, so the first of the three wins and the others are ignored.
    internal sealed class OnityStreamPendingMove
    {
        private static readonly Action<object> s_cancel = state => ((OnityStreamPendingMove)state).OnCanceled();

        private readonly object m_gate = new object();
        private readonly OnityTaskCompletionSource<bool> m_source = new OnityTaskCompletionSource<bool>();
        private readonly CancellationToken m_token;
        private CancellationTokenRegistration m_registration;
        private bool m_closed;

        internal OnityStreamPendingMove(CancellationToken token)
        {
            m_token = token;
        }

        internal OnityTask<bool> Task => m_source.Task;

        // Starts observing the token. An already canceled token completes the move as canceled inline.
        internal void Start()
        {
            if (!m_token.CanBeCanceled)
            {
                return;
            }
            CancellationTokenRegistration registration = OnityStreamSourceTokens.Register(m_token, s_cancel, this);
            bool dispose;
            lock (m_gate)
            {
                dispose = m_closed;
                if (!dispose)
                {
                    m_registration = registration;
                }
            }
            if (dispose)
            {
                registration.Dispose();
            }
        }

        internal void Complete(OnityAsyncStreamOutcome outcome)
        {
            outcome.Publish(m_source, outcome.Value);
        }

        // Releases the token registration and settles a still pending move as the end of the stream.
        internal void Close()
        {
            CancellationTokenRegistration registration;
            lock (m_gate)
            {
                m_closed = true;
                registration = m_registration;
                m_registration = default;
            }
            registration.Dispose();
            m_source.TrySetResult(false);
        }

        private void OnCanceled()
        {
            m_source.TrySetCanceled(m_token);
        }
    }
}
