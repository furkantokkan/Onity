using System;
using System.Threading;

namespace Onity.Unity.Async
{
    internal sealed class OnityPlayerLoopTaskSource :
        OnityTaskSourceBase, IOnityPooledRunner<OnityPlayerLoopTaskSource>
    {
        private static OnityRunnerPool<OnityPlayerLoopTaskSource> s_pool;
        private static readonly Action<object> s_cancel = CancelFromToken;
        private static readonly Action<object> s_cancelImmediately = CancelImmediatelyFromToken;

        private OnityPlayerLoopTaskSource m_nextPooled;
        private CancellationTokenRegistration m_registration;
        private CancellationToken m_token;
        private int m_canceled;

        ref OnityPlayerLoopTaskSource IOnityPooledRunner<OnityPlayerLoopTaskSource>.NextPooled => ref m_nextPooled;

        internal ulong RegisteredPass { get; private set; }
        internal uint RegisteredFrame { get; private set; }
        internal uint MinimumFrames { get; private set; }
        internal bool IsCancellationFlagged => Volatile.Read(ref m_canceled) != 0;

        /// <summary>True while the cycle that issued <paramref name="version"/> has not published.</summary>
        internal bool IsAwaitingPublication(int version)
        {
            return Version == version && IsPending;
        }

        /// <summary>
        /// Rents a cancelable frame wait. A flag-only cancellation is published by the next Update drain;
        /// with <paramref name="cancelImmediately"/> the canceling thread publishes it and runs the
        /// continuation, as UniTask's <c>cancelImmediately</c> does.
        /// </summary>
        internal static OnityPlayerLoopTaskSource Rent(
            ulong pass, uint frame, int minimumFrames, CancellationToken token, bool cancelImmediately)
        {
            // A contended rent allocates instead of waiting.
            if (!s_pool.TryPop(out OnityPlayerLoopTaskSource source))
            {
                source = new OnityPlayerLoopTaskSource();
            }

            source.Reset(default);
            source.RegisteredPass = pass;
            source.RegisteredFrame = frame;
            source.MinimumFrames = (uint)minimumFrames;
            source.m_canceled = 0;
            try
            {
                // Nothing can publish this rental before the main-thread owner has enqueued it, except an
                // immediate cancellation, which publishes through the version-checked claim.
                if (token.CanBeCanceled)
                {
                    source.m_token = token;
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
                registration.Dispose();
                source.m_token = default;
                source.ReleaseSource();
                throw;
            }
        }

        /// <summary>
        /// Publishes the cycle that issued <paramref name="version"/> from the main-thread owner. The
        /// registration is disposed first, which waits for an immediate cancellation that is running, so
        /// the version-checked publication below loses cleanly to it.
        /// </summary>
        internal void Publish(int version, bool canceled, Exception failure)
        {
            CancellationToken token = m_token;
            if (token.CanBeCanceled)
            {
                m_registration.Dispose();
                m_registration = default;
                m_token = default;
            }

            RegisteredPass = 0;
            RegisteredFrame = 0;
            MinimumFrames = 0;

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
            // Do not access fields here: publication may consume and re-rent this object.
        }

        private static void CancelFromToken(object state)
        {
            Volatile.Write(ref ((OnityPlayerLoopTaskSource)state).m_canceled, 1);
            OnityTaskPlayerLoop.NotifyCancellationRequested();
        }

        private static void CancelImmediatelyFromToken(object state)
        {
            // The registration fires only inside its own pending cycle: the owner disposes it before any
            // other publication, and that disposal waits for this callback.
            OnityPlayerLoopTaskSource source = (OnityPlayerLoopTaskSource)state;
            source.TrySetCanceled(new OperationCanceledException(source.m_token), source.Version);
            // Do not access fields here: the continuation may have consumed and released this object.
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
