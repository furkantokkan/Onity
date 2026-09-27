using System;
using System.Collections.Generic;
using System.Threading;

namespace Onity.Unity.Async
{
    internal sealed class OnityPlayerLoopTaskSource : OnityTaskSourceBase
    {
        private const int k_maxPoolSize = 128;
        private static readonly Stack<OnityPlayerLoopTaskSource> s_pool =
            new Stack<OnityPlayerLoopTaskSource>(32);
        private static readonly Action<object> s_cancel = state =>
            Volatile.Write(ref ((OnityPlayerLoopTaskSource)state).m_canceled, 1);

        private CancellationTokenRegistration m_registration;
        private CancellationToken m_token;
        private int m_canceled;

        internal ulong RegisteredPass { get; private set; }
        internal uint RegisteredFrame { get; private set; }
        internal uint MinimumFrames { get; private set; }
        internal bool IsCancellationFlagged => Volatile.Read(ref m_canceled) != 0;

        internal static OnityPlayerLoopTaskSource Rent(
            ulong pass, uint frame, int minimumFrames, CancellationToken token)
        {
            OnityPlayerLoopTaskSource source;
            lock (s_pool)
            {
                source = s_pool.Count == 0 ? new OnityPlayerLoopTaskSource() : s_pool.Pop();
            }

            source.Reset(default);
            source.RegisteredPass = pass;
            source.RegisteredFrame = frame;
            source.MinimumFrames = (uint)minimumFrames;
            source.m_token = token;
            source.m_canceled = 0;
            try
            {
                // Nothing can publish this rental until Register has returned and the
                // main-thread owner has enqueued it. Synchronous cancellation only flags it.
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
                registration.Dispose();
                source.m_token = default;
                source.ReleaseSource();
                throw;
            }
        }

        internal void Publish(bool canceled, Exception failure)
        {
            // The owner has removed this source from its queue. Finish the flag-only
            // callback before publication can release and synchronously re-rent it.
            CancellationToken token = m_token;
            CancellationTokenRegistration registration = m_registration;
            m_registration = default;
            m_token = default;
            RegisteredPass = 0;
            RegisteredFrame = 0;
            MinimumFrames = 0;
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
            // Do not access fields here: publication may consume and re-rent this object.
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
