using System;
using System.Threading;
using Onity.Core;

namespace Onity.Unity.Async
{
    internal sealed class OnityEveryUpdateAsyncEnumerable : IOnityAsyncEnumerable<Unit>
    {
        internal static readonly OnityEveryUpdateAsyncEnumerable Instance = new OnityEveryUpdateAsyncEnumerable();

        public IOnityAsyncEnumerator<Unit> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new Enumerator(cancellationToken);
        }

        private sealed class Enumerator : OnityAsyncEnumeratorBase<Unit>
        {
            private readonly object m_gate = new object();
            private readonly CancellationToken m_callerToken;
            private readonly Action<OnityAsyncStreamOutcome> m_onQuiescence;
            private OnityAsyncStreamTokenLifetime m_lifetime;
            private OnityTaskCompletionSource m_cleanup;
            private OnityAsyncStreamOutcome m_cleanupOutcome;
            private bool m_waitPending;
            private bool m_quiescent;
            private bool m_finalizing;

            internal Enumerator(CancellationToken callerToken)
            {
                m_callerToken = callerToken;
                m_onQuiescence = OnQuiescence;
            }

            protected override OnityTask<bool> MoveCore()
            {
                if (IsClosing)
                {
                    return OnityTask<bool>.FromResult(false);
                }
                if (m_lifetime == null)
                {
                    m_lifetime = new OnityAsyncStreamTokenLifetime();
                    m_lifetime.Initialize(m_callerToken);
                }
                if (IsClosing)
                {
                    return OnityTask<bool>.FromResult(false);
                }

                // Yield performs thread/session validation on every active move,
                // including a canceled owned token. No timer/subscription is retained.
                OnityTask wait = OnityTask.Yield(OnityPlayerLoopTiming.Update, m_lifetime.Token);
                lock (m_gate)
                {
                    m_waitPending = true;
                }
                if (wait.IsCompleted)
                {
                    OnityAsyncStreamOutcome outcome;
                    try
                    {
                        outcome = OnityAsyncStreamOutcome.Read(wait);
                    }
                    catch (Exception exception)
                    {
                        outcome = OnityAsyncStreamOutcome.Failure(exception);
                    }
                    OnWaitObserved();
                    if (outcome.Status == OnityTaskSourceStatus.Succeeded)
                    {
                        return OnityTask<bool>.FromResult(true);
                    }
                    var failed = new OnityTaskCompletionSource<bool>();
                    outcome.Publish(failed, true);
                    return failed.Task;
                }

                var output = new OnityTaskCompletionSource<bool>();
                new OnityAsyncStreamCleanupObserver(wait, outcome =>
                {
                    OnWaitObserved();
                    outcome.Publish(output, true);
                }).Register();
                return output.Task;
            }

            protected override bool ReadCurrentCore(out Unit current)
            {
                current = Unit.Default;
                return true;
            }

            protected override OnityTask CleanupCore()
            {
                if (m_lifetime == null)
                {
                    return OnityTask.Completed;
                }
                OnityTask task;
                lock (m_gate)
                {
                    if (m_cleanup != null)
                    {
                        return m_cleanup.Task;
                    }
                    m_cleanup = new OnityTaskCompletionSource();
                    task = m_cleanup.Task;
                }
                try
                {
                    OnityTask quiescence = m_lifetime.Close();
                    if (quiescence.IsCompleted)
                    {
                        OnQuiescence(OnityAsyncStreamOutcome.Read(quiescence));
                    }
                    else
                    {
                        new OnityAsyncStreamCleanupObserver(quiescence, m_onQuiescence).Register();
                    }
                }
                catch (Exception exception)
                {
                    OnQuiescence(OnityAsyncStreamOutcome.Failure(exception));
                }
                return task;
            }

            private void OnWaitObserved()
            {
                lock (m_gate)
                {
                    m_waitPending = false;
                }
                FinishCleanup();
            }

            private void OnQuiescence(OnityAsyncStreamOutcome outcome)
            {
                lock (m_gate)
                {
                    m_quiescent = true;
                    m_cleanupOutcome = outcome;
                }
                FinishCleanup();
            }

            private void FinishCleanup()
            {
                OnityTaskCompletionSource output;
                OnityAsyncStreamOutcome outcome;
                lock (m_gate)
                {
                    if (m_cleanup == null || !m_quiescent || m_waitPending || m_finalizing)
                    {
                        return;
                    }
                    m_finalizing = true;
                    output = m_cleanup;
                    outcome = m_cleanupOutcome;
                }
                try
                {
                    m_lifetime.Dispose();
                }
                catch (Exception exception)
                {
                    outcome = OnityAsyncStreamOutcome.Failure(exception);
                }
                outcome.Publish<bool>(output, true);
            }
        }
    }
}
