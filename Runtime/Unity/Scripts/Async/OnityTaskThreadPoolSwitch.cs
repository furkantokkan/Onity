using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Awaitable returned by <see cref="OnityTask.SwitchToThreadPool(CancellationToken)"/>.
    /// Every await queues to the thread pool, including awaits started on a worker thread.
    /// </summary>
    /// <remarks>
    /// Cancellation is observed in GetResult on the destination worker. Continuation registration
    /// does not capture execution context; async builders retain responsibility for context flow.
    /// WebGL players are unsupported; the Editor is supported with WebGL selected.
    /// </remarks>
    public readonly struct OnityTaskThreadPoolSwitch
    {
        private readonly CancellationToken m_cancellationToken;

        internal OnityTaskThreadPoolSwitch(CancellationToken cancellationToken)
        {
            m_cancellationToken = cancellationToken;
        }

        /// <summary>
        /// Returns the awaiter for this switch.
        /// </summary>
        /// <returns>Thread-pool switch awaiter.</returns>
        public OnityTaskThreadPoolSwitchAwaiter GetAwaiter()
        {
            return new OnityTaskThreadPoolSwitchAwaiter(m_cancellationToken);
        }
    }

    /// <summary>
    /// Awaiter that always queues to the thread pool without capturing execution context.
    /// </summary>
    public readonly struct OnityTaskThreadPoolSwitchAwaiter : ICriticalNotifyCompletion
    {
        private readonly CancellationToken m_cancellationToken;

        internal OnityTaskThreadPoolSwitchAwaiter(CancellationToken cancellationToken)
        {
            m_cancellationToken = cancellationToken;
        }

        /// <summary>
        /// Always false: a worker continuation is queued for every await.
        /// </summary>
        public bool IsCompleted => false;

        /// <summary>
        /// Completes the switch on the worker, observing the original cancellation token.
        /// </summary>
        /// <exception cref="OperationCanceledException">The token is canceled.</exception>
        /// <exception cref="PlatformNotSupportedException">Called in a WebGL player.</exception>
        public void GetResult()
        {
            OnityTaskThreadPoolDispatcher.ThrowIfUnsupported();
            m_cancellationToken.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// Queues the continuation without capturing execution context.
        /// </summary>
        /// <param name="continuation">Continuation to run on a worker.</param>
        /// <exception cref="ArgumentNullException">The continuation is null.</exception>
        /// <exception cref="PlatformNotSupportedException">Called in a WebGL player.</exception>
        public void OnCompleted(Action continuation)
        {
            OnityTaskThreadPoolDispatcher.Queue(continuation);
        }

        /// <summary>
        /// Queues the continuation without capturing execution context.
        /// </summary>
        /// <param name="continuation">Continuation to run on a worker.</param>
        /// <exception cref="ArgumentNullException">The continuation is null.</exception>
        /// <exception cref="PlatformNotSupportedException">Called in a WebGL player.</exception>
        public void UnsafeOnCompleted(Action continuation)
        {
            OnityTaskThreadPoolDispatcher.Queue(continuation);
        }
    }

    internal static class OnityTaskThreadPoolDispatcher
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        private static readonly WaitCallback s_runContinuation = RunContinuation;
#endif

        internal static void ThrowIfUnsupported()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            throw new PlatformNotSupportedException("Onity thread-pool operations are unavailable in WebGL players.");
#endif
        }

        internal static void Queue(Action continuation)
        {
            ThrowIfUnsupported();
            if (continuation == null)
            {
                throw new ArgumentNullException(nameof(continuation));
            }

#if !UNITY_WEBGL || UNITY_EDITOR
            if (ThreadPool.UnsafeQueueUserWorkItem(s_runContinuation, continuation) == false)
            {
                throw new InvalidOperationException("The thread pool rejected the Onity continuation.");
            }
#endif
        }

        internal static async OnityTask Run(
            Action action, bool returnToMainThread, CancellationToken cancellationToken, int session)
        {
            try
            {
                await new OnityTaskThreadPoolSwitch(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                action();
            }
            finally
            {
                if (returnToMainThread)
                {
                    // Cleanup must return even when the work token was canceled. Keep the session
                    // captured before dispatch so old work cannot resume in a later Play session.
                    await new OnityTaskThreadSwitch(default, session);
                }
            }

            // Observe cancellation up to publication, including time spent in the return queue.
            // A delegate fault exits through finally and never reaches this successful-path check.
            cancellationToken.ThrowIfCancellationRequested();
        }

        internal static async OnityTask<T> Run<T>(
            Func<T> function, bool returnToMainThread, CancellationToken cancellationToken, int session)
        {
            T result;
            try
            {
                await new OnityTaskThreadPoolSwitch(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                result = function();
            }
            finally
            {
                if (returnToMainThread)
                {
                    await new OnityTaskThreadSwitch(default, session);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        private static void RunContinuation(object state)
        {
            ((Action)state)();
        }
#endif
    }
}
