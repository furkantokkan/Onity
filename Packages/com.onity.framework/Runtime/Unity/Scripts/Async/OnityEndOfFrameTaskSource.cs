using System;
using System.Collections.Generic;
using System.Threading;

namespace Onity.Unity.Async
{
    internal sealed class OnityEndOfFrameTaskSource : OnityTaskSourceBase
    {
        private const int k_maxPoolSize = 128;
        private static readonly Stack<OnityEndOfFrameTaskSource> s_pool =
            new Stack<OnityEndOfFrameTaskSource>(32);
        private static readonly Action<object> s_cancel = state =>
            Volatile.Write(ref ((OnityEndOfFrameTaskSource)state).m_canceled, 1);

        private CancellationTokenRegistration m_registration;
        private CancellationToken m_token;
        private int m_canceled;

        internal ulong RegisteredPass { get; private set; }
        internal bool IsCancellationFlagged => Volatile.Read(ref m_canceled) != 0;

        internal static OnityEndOfFrameTaskSource Rent(ulong pass, CancellationToken token)
        {
            OnityEndOfFrameTaskSource source;
            lock (s_pool)
            {
                source = s_pool.Count == 0 ? new OnityEndOfFrameTaskSource() : s_pool.Pop();
            }

            source.Reset(default);
            source.RegisteredPass = pass;
            source.m_token = token;
            source.m_canceled = 0;
            try
            {
                // Only the main-thread owner can publish, after assignment and enqueue.
                if (token.CanBeCanceled)
                {
                    if (ExecutionContext.IsFlowSuppressed())
                    {
                        source.m_registration = token.Register(s_cancel, source, false);
                    }
                    else
                    {
                        AsyncFlowControl flow = ExecutionContext.SuppressFlow();
                        try
                        {
                            source.m_registration = token.Register(s_cancel, source, false);
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

        internal void Publish(bool canceled, Exception failure)
        {
            CancellationToken token = m_token;
            CancellationTokenRegistration registration = m_registration;
            m_registration = default;
            m_token = default;
            RegisteredPass = 0;
            registration.Dispose();

            if (failure != null)
            {
                TrySetException(failure);
            }
            else if (canceled)
            {
                TrySetCanceled(new OperationCanceledException(token));
            }
            else
            {
                TrySetResult();
            }
            // Publication can consume and re-rent this source; touch nothing afterward.
        }

        protected override void ReleaseSource()
        {
            InvalidateVersion();
            lock (s_pool)
            {
                if (s_pool.Count < k_maxPoolSize)
                {
                    s_pool.Push(this);
                }
            }
        }
    }
}
