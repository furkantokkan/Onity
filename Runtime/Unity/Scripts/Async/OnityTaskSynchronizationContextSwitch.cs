using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Awaitable returned by <see cref="OnityTask.SwitchToSynchronizationContext"/>. Every await
    /// posts its continuation to the given <see cref="SynchronizationContext"/>, even when the caller
    /// already runs on it.
    /// </summary>
    /// <remarks>
    /// Cancellation is observed when the await completes, on the destination context:
    /// <see cref="OnitySynchronizationContextSwitchAwaiter.GetResult"/> throws
    /// <see cref="OperationCanceledException"/> when the token is canceled, including a token that was
    /// already canceled when the switch was requested. The value can be awaited any number of times.
    /// </remarks>
    public readonly struct OnitySynchronizationContextSwitch
    {
        private readonly SynchronizationContext m_context;
        private readonly CancellationToken m_cancellationToken;

        internal OnitySynchronizationContextSwitch(
            SynchronizationContext context,
            CancellationToken cancellationToken)
        {
            m_context = context;
            m_cancellationToken = cancellationToken;
        }

        /// <summary>
        /// Returns the awaiter for this switch.
        /// </summary>
        /// <returns>Switch awaiter.</returns>
        public OnitySynchronizationContextSwitchAwaiter GetAwaiter()
        {
            return new OnitySynchronizationContextSwitchAwaiter(m_context, m_cancellationToken);
        }
    }

    /// <summary>
    /// Awaiter for <see cref="OnitySynchronizationContextSwitch"/>.
    /// </summary>
    public readonly struct OnitySynchronizationContextSwitchAwaiter : ICriticalNotifyCompletion
    {
        private static readonly SendOrPostCallback s_runContinuation = RunContinuation;

        private readonly SynchronizationContext m_context;
        private readonly CancellationToken m_cancellationToken;

        internal OnitySynchronizationContextSwitchAwaiter(
            SynchronizationContext context,
            CancellationToken cancellationToken)
        {
            m_context = context;
            m_cancellationToken = cancellationToken;
        }

        /// <summary>
        /// Always false: the continuation is posted to the context for every await.
        /// </summary>
        public bool IsCompleted => false;

        /// <summary>
        /// Completes the switch on the destination context, observing the token.
        /// </summary>
        /// <exception cref="OperationCanceledException">The token is canceled.</exception>
        public void GetResult()
        {
            m_cancellationToken.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// Posts the continuation to the context.
        /// </summary>
        /// <param name="continuation">Continuation to run on the context.</param>
        /// <exception cref="ArgumentNullException">The continuation is null.</exception>
        public void OnCompleted(Action continuation)
        {
            Post(continuation);
        }

        /// <summary>
        /// Posts the continuation to the context.
        /// </summary>
        /// <param name="continuation">Continuation to run on the context.</param>
        /// <exception cref="ArgumentNullException">The continuation is null.</exception>
        public void UnsafeOnCompleted(Action continuation)
        {
            Post(continuation);
        }

        private void Post(Action continuation)
        {
            if (continuation == null)
            {
                throw new ArgumentNullException(nameof(continuation));
            }

            m_context.Post(s_runContinuation, continuation);
        }

        private static void RunContinuation(object state)
        {
            ((Action)state)();
        }
    }

    /// <summary>
    /// Scope returned by <see cref="OnityTask.ReturnToSynchronizationContext"/> and
    /// <see cref="OnityTask.ReturnToCurrentSynchronizationContext"/>. Used with
    /// <c>await using</c>, it resumes on the chosen <see cref="SynchronizationContext"/> when the scope
    /// closes, so code that switched to another thread inside the scope returns to the original
    /// context.
    /// </summary>
    public readonly struct OnityReturnToSynchronizationContext
    {
        private readonly SynchronizationContext m_context;
        private readonly bool m_dontPostWhenSameContext;
        private readonly CancellationToken m_cancellationToken;

        internal OnityReturnToSynchronizationContext(
            SynchronizationContext context,
            bool dontPostWhenSameContext,
            CancellationToken cancellationToken)
        {
            m_context = context;
            m_dontPostWhenSameContext = dontPostWhenSameContext;
            m_cancellationToken = cancellationToken;
        }

        /// <summary>
        /// Closes the scope. Awaiting the result resumes on the context, or continues inline when
        /// the scope was created not to post while already on it, or when it has no context.
        /// </summary>
        /// <returns>Awaitable that resumes on the context.</returns>
        public OnityReturnToSynchronizationContextAwaiter DisposeAsync()
        {
            return new OnityReturnToSynchronizationContextAwaiter(
                m_context, m_dontPostWhenSameContext, m_cancellationToken);
        }
    }

    /// <summary>
    /// Awaiter that resumes on a <see cref="SynchronizationContext"/> when a return scope closes.
    /// </summary>
    public readonly struct OnityReturnToSynchronizationContextAwaiter : ICriticalNotifyCompletion
    {
        private static readonly SendOrPostCallback s_runContinuation = RunContinuation;

        private readonly SynchronizationContext m_context;
        private readonly bool m_dontPostWhenSameContext;
        private readonly CancellationToken m_cancellationToken;

        internal OnityReturnToSynchronizationContextAwaiter(
            SynchronizationContext context,
            bool dontPostWhenSameContext,
            CancellationToken cancellationToken)
        {
            m_context = context;
            m_dontPostWhenSameContext = dontPostWhenSameContext;
            m_cancellationToken = cancellationToken;
        }

        /// <summary>
        /// True when there is no context to return to, or when the scope does not post while the
        /// current context already is the target; false otherwise.
        /// </summary>
        public bool IsCompleted
        {
            get
            {
                if (m_context == null)
                {
                    return true;
                }

                return m_dontPostWhenSameContext && ReferenceEquals(SynchronizationContext.Current, m_context);
            }
        }

        /// <summary>
        /// Returns this awaiter, so the scope's <c>DisposeAsync()</c> result can be awaited.
        /// </summary>
        /// <returns>This awaiter.</returns>
        public OnityReturnToSynchronizationContextAwaiter GetAwaiter()
        {
            return this;
        }

        /// <summary>
        /// Completes the return, observing the token on the destination context.
        /// </summary>
        /// <exception cref="OperationCanceledException">The token is canceled.</exception>
        public void GetResult()
        {
            m_cancellationToken.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// Posts the continuation to the context.
        /// </summary>
        /// <param name="continuation">Continuation to run on the context.</param>
        /// <exception cref="ArgumentNullException">The continuation is null.</exception>
        public void OnCompleted(Action continuation)
        {
            Post(continuation);
        }

        /// <summary>
        /// Posts the continuation to the context.
        /// </summary>
        /// <param name="continuation">Continuation to run on the context.</param>
        /// <exception cref="ArgumentNullException">The continuation is null.</exception>
        public void UnsafeOnCompleted(Action continuation)
        {
            Post(continuation);
        }

        private void Post(Action continuation)
        {
            if (continuation == null)
            {
                throw new ArgumentNullException(nameof(continuation));
            }

            if (m_context == null)
            {
                continuation();
                return;
            }

            m_context.Post(s_runContinuation, continuation);
        }

        private static void RunContinuation(object state)
        {
            ((Action)state)();
        }
    }

    /// <summary>
    /// Awaitable returned by <see cref="OnityTask.SwitchToTaskPool"/>. Every await queues its
    /// continuation as a work item of the default .NET task scheduler, including awaits started on
    /// a worker thread.
    /// </summary>
    public readonly struct OnityTaskPoolSwitch
    {
        /// <summary>
        /// Returns the awaiter for this switch.
        /// </summary>
        /// <returns>Task-pool switch awaiter.</returns>
        public OnityTaskPoolSwitchAwaiter GetAwaiter()
        {
            return default;
        }
    }

    /// <summary>
    /// Awaiter that always queues to the default .NET task scheduler.
    /// </summary>
    public readonly struct OnityTaskPoolSwitchAwaiter : ICriticalNotifyCompletion
    {
        private static readonly Action<object> s_runContinuation = RunContinuation;

        /// <summary>
        /// Always false: a work item is queued for every await.
        /// </summary>
        public bool IsCompleted => false;

        /// <summary>
        /// Completes the switch. It cannot be canceled.
        /// </summary>
        public void GetResult()
        {
        }

        /// <summary>
        /// Queues the continuation without capturing execution context.
        /// </summary>
        /// <param name="continuation">Continuation to run on the task scheduler.</param>
        /// <exception cref="ArgumentNullException">The continuation is null.</exception>
        /// <exception cref="PlatformNotSupportedException">Called in a WebGL player.</exception>
        public void OnCompleted(Action continuation)
        {
            Queue(continuation);
        }

        /// <summary>
        /// Queues the continuation without capturing execution context.
        /// </summary>
        /// <param name="continuation">Continuation to run on the task scheduler.</param>
        /// <exception cref="ArgumentNullException">The continuation is null.</exception>
        /// <exception cref="PlatformNotSupportedException">Called in a WebGL player.</exception>
        public void UnsafeOnCompleted(Action continuation)
        {
            Queue(continuation);
        }

        private static void Queue(Action continuation)
        {
            OnityTaskThreadPoolDispatcher.ThrowIfUnsupported();
            if (continuation == null)
            {
                throw new ArgumentNullException(nameof(continuation));
            }

            Task.Factory.StartNew(
                s_runContinuation,
                continuation,
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach,
                TaskScheduler.Default);
        }

        private static void RunContinuation(object state)
        {
            ((Action)state)();
        }
    }
}
