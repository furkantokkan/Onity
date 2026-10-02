using System;
using System.Threading;

namespace Onity.Unity.Async
{
    internal sealed class OnityEndOfFrameTaskSource :
        OnityTaskSourceBase, IOnityPooledRunner<OnityEndOfFrameTaskSource>
    {
        private static OnityRunnerPool<OnityEndOfFrameTaskSource> s_pool;
        private static readonly Action<object> s_cancel = state =>
            Volatile.Write(ref ((OnityEndOfFrameTaskSource)state).m_canceled, 1);
        private static readonly Action<object> s_cancelImmediately = CancelImmediatelyFromToken;

        private OnityEndOfFrameTaskSource m_nextPooled;
        private CancellationTokenRegistration m_registration;
        private CancellationToken m_token;
        private int m_canceled;

        ref OnityEndOfFrameTaskSource IOnityPooledRunner<OnityEndOfFrameTaskSource>.NextPooled => ref m_nextPooled;

        internal ulong RegisteredPass { get; private set; }
        internal bool IsCancellationFlagged => Volatile.Read(ref m_canceled) != 0;

        /// <summary>True while the cycle that issued <paramref name="version"/> has not published.</summary>
        internal bool IsAwaitingPublication(int version)
        {
            return Version == version && IsPending;
        }

        internal static OnityEndOfFrameTaskSource Rent(ulong pass, CancellationToken token, bool cancelImmediately)
        {
            // A contended rent allocates instead of waiting.
            if (!s_pool.TryPop(out OnityEndOfFrameTaskSource source))
            {
                source = new OnityEndOfFrameTaskSource();
            }

            source.Reset(default);
            source.RegisteredPass = pass;
            source.m_token = token;
            source.m_canceled = 0;
            try
            {
                // Only the main-thread owner can publish, after assignment and enqueue, except an immediate
                // cancellation, which publishes through the version-checked claim.
                if (token.CanBeCanceled)
                {
                    Action<object> callback = cancelImmediately ? s_cancelImmediately : s_cancel;
                    if (ExecutionContext.IsFlowSuppressed())
                    {
                        source.m_registration = token.Register(callback, source, false);
                    }
                    else
                    {
                        AsyncFlowControl flow = ExecutionContext.SuppressFlow();
                        try
                        {
                            source.m_registration = token.Register(callback, source, false);
                        }
                        finally
                        {
                            flow.Undo();
                        }
                    }
                }
                return source;
            }
            catch
            {
                CancellationTokenRegistration registration = source.m_registration;
                source.m_registration = default;
                source.m_token = default;
                registration.Dispose();
                source.ReleaseSource();
                throw;
            }
        }

        /// <summary>
        /// Publishes the cycle that issued <paramref name="version"/> from the main-thread owner. Disposing
        /// the registration first waits for a running immediate cancellation, which then wins.
        /// </summary>
        internal void Publish(int version, bool canceled, Exception failure)
        {
            CancellationToken token = m_token;
            m_registration.Dispose();
            m_registration = default;
            m_token = default;
            RegisteredPass = 0;

            if (failure != null)
            {
                TrySetException(failure, version);
            }
            else if (canceled)
            {
                TrySetCanceled(new OperationCanceledException(token), version);
            }
            else
            {
                TrySetOwnedResult(version);
            }
            // Publication can consume and re-rent this source; touch nothing afterward.
        }

        private static void CancelImmediatelyFromToken(object state)
        {
            // The registration fires only inside its own pending cycle: the owner disposes it before any
            // other publication, and that disposal waits for this callback.
            OnityEndOfFrameTaskSource source = (OnityEndOfFrameTaskSource)state;
            source.TrySetCanceled(new OperationCanceledException(source.m_token), source.Version);
        }

        protected override void ReleaseSource()
        {
            // The compare-and-swap that claimed the release already retired the token version, and the
            // only other caller is Rent before any task value exists. A contended or full return lets
            // the source be collected.
            s_pool.TryPush(this, OnityTaskSettings.s_sourcePoolCapacity);
        }
    }
}
