using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Onity.Unity.Async
{
    internal interface IOnityMultiConsumerTaskSource
    {
    }

    /// <summary>
    /// Completes an Onity task from a callback or another external operation.
    /// The source is not pooled, so its task can be awaited by multiple consumers.
    /// </summary>
    public sealed class OnityTaskCompletionSource : OnityTaskCompletionSource<bool>
    {
        /// <summary>
        /// Initializes an incomplete source.
        /// </summary>
        public OnityTaskCompletionSource()
        {
        }

        /// <summary>
        /// Gets the task completed by this source.
        /// </summary>
        public new OnityTask Task => new OnityTask((IOnityTaskSource)this);

        /// <summary>
        /// Completes the task successfully if it is still pending.
        /// </summary>
        /// <returns>True if this call completed the task.</returns>
        public bool TrySetResult()
        {
            return base.TrySetResult(true);
        }
    }

    /// <summary>
    /// Completes a typed Onity task from a callback or another external operation.
    /// The source is not pooled, so its task can be awaited by multiple consumers.
    /// </summary>
    /// <typeparam name="T">Result type.</typeparam>
    public class OnityTaskCompletionSource<T> :
        IOnityTaskSource<T>, IOnityTaskSource, IOnityMultiConsumerTaskSource
    {
        private const int k_version = 1;

        private sealed class UnobservedFault
        {
            private static readonly SendOrPostCallback s_reportCallback = ReportPosted;

            private readonly ExceptionDispatchInfo m_exception;
            private readonly SynchronizationContext m_context;
            private readonly int m_contextThreadId;
            private int m_observed;

            public UnobservedFault(
                ExceptionDispatchInfo exception,
                SynchronizationContext context,
                int contextThreadId)
            {
                m_exception = exception;
                m_context = context;
                m_contextThreadId = contextThreadId;
            }

            ~UnobservedFault()
            {
                if (Volatile.Read(ref m_observed) != 0)
                {
                    return;
                }

                if (m_context != null)
                {
                    try
                    {
                        m_context.Post(s_reportCallback, this);
                        return;
                    }
                    catch (Exception)
                    {
                        // A context may be unavailable during domain shutdown.
                    }
                }

                ReportToConsole();
            }

            public void Observe()
            {
                if (Interlocked.Exchange(ref m_observed, 1) == 0)
                {
                    GC.SuppressFinalize(this);
                }
            }

            private static void ReportPosted(object state)
            {
                UnobservedFault fault = (UnobservedFault)state;
                if (Volatile.Read(ref fault.m_observed) != 0)
                {
                    return;
                }

                if (Thread.CurrentThread.ManagedThreadId == fault.m_contextThreadId)
                {
                    try
                    {
                        Debug.LogException(fault.m_exception.SourceException);
                        return;
                    }
                    catch (Exception)
                    {
                        // A custom context may dispatch after Unity shuts down.
                    }
                }

                fault.ReportToConsole();
            }

            private void ReportToConsole()
            {
                try
                {
                    Console.Error.WriteLine("Unobserved OnityTask exception: "
                        + m_exception.SourceException);
                }
                catch (Exception)
                {
                    // Finalizers must never throw during process shutdown.
                }
            }
        }

        private readonly SynchronizationContext m_reportingContext = SynchronizationContext.Current;
        private readonly int m_reportingThreadId = Thread.CurrentThread.ManagedThreadId;
        private readonly object m_gate;

        private Action m_firstContinuation;
        private Action m_secondContinuation;
        private List<Action> m_otherContinuations;
        private TaskCompletionSource<T> m_taskBridge;
        private ExceptionDispatchInfo m_exception;
        private UnobservedFault m_unobservedFault;
        private CancellationToken m_cancellationToken;
        private T m_result;
        private int m_status;
        private bool m_coordinatorObservedFault;

        /// <summary>
        /// Initializes an incomplete source.
        /// </summary>
        public OnityTaskCompletionSource()
        {
            m_gate = new object();
        }

        internal OnityTaskCompletionSource(bool lockOnSelf)
        {
            m_gate = lockOnSelf ? this : new object();
        }

        /// <summary>
        /// Gets the task completed by this source.
        /// </summary>
        public OnityTask<T> Task => new OnityTask<T>((IOnityTaskSource<T>)this);

        internal bool HasTaskBridge => Volatile.Read(ref m_taskBridge) != null;

        /// <summary>
        /// Completes the task with a result if it is still pending.
        /// </summary>
        /// <param name="result">Result to publish.</param>
        /// <returns>True if this call completed the task.</returns>
        public bool TrySetResult(T result)
        {
            return TryComplete(OnityTaskSourceStatus.Succeeded, result, null, default);
        }

        /// <summary>
        /// Completes the task with an exception if it is still pending.
        /// An <see cref="OperationCanceledException"/> completes it as canceled.
        /// </summary>
        /// <param name="exception">Failure to publish.</param>
        /// <returns>True if this call completed the task.</returns>
        public bool TrySetException(Exception exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            if (exception is OperationCanceledException canceledException)
            {
                return TrySetCanceled(canceledException.CancellationToken);
            }

            return TryComplete(OnityTaskSourceStatus.Faulted, default, exception, default);
        }

        internal bool TrySetFault(Exception exception)
        {
            // A native source can fault with OperationCanceledException.
            // Keep its fault status instead of applying the public cancellation rule.
            return TryComplete(OnityTaskSourceStatus.Faulted, default, exception, default);
        }

        /// <summary>
        /// Cancels the task if it is still pending.
        /// </summary>
        /// <param name="cancellationToken">Token reported to awaiting consumers.</param>
        /// <returns>True if this call completed the task.</returns>
        public bool TrySetCanceled(CancellationToken cancellationToken = default)
        {
            return TryComplete(OnityTaskSourceStatus.Canceled, default, null, cancellationToken);
        }

        int IOnityTaskSource<T>.Version => k_version;

        int IOnityTaskSource.Version => k_version;

        OnityTaskSourceStatus IOnityTaskSource<T>.GetStatus(int token)
        {
            return GetStatus(token);
        }

        OnityTaskSourceStatus IOnityTaskSource.GetStatus(int token)
        {
            return GetStatus(token);
        }

        Task<T> IOnityTaskSource<T>.AsTask(int token)
        {
            return GetTaskBridge(token);
        }

        Task IOnityTaskSource.AsTask(int token)
        {
            return GetTaskBridge(token);
        }

        void IOnityTaskSource<T>.OnCompleted(Action continuation, int token)
        {
            AddContinuation(continuation, token);
        }

        void IOnityTaskSource.OnCompleted(Action continuation, int token)
        {
            AddContinuation(continuation, token);
        }

        T IOnityTaskSource<T>.GetResult(int token)
        {
            return GetResult(token);
        }

        void IOnityTaskSource.GetResult(int token)
        {
            GetResult(token);
        }

        private OnityTaskSourceStatus GetStatus(int token)
        {
            ValidateToken(token);
            OnityTaskSourceStatus status =
                (OnityTaskSourceStatus)Volatile.Read(ref m_status);
            if (status != OnityTaskSourceStatus.Pending)
            {
                return status;
            }

            if (Volatile.Read(ref m_taskBridge) == null)
            {
                return OnityTaskSourceStatus.Pending;
            }

            // The task bridge completes under this gate before status is
            // published. A pending read with a bridge waits for that publication.
            lock (m_gate)
            {
                return (OnityTaskSourceStatus)m_status;
            }
        }

        private T GetResult(int token)
        {
            // Completion publishes every outcome field before the terminal status.
            // GetStatus acquires that publication and waits for an in-flight bridge.
            OnityTaskSourceStatus status = GetStatus(token);

            if (status == OnityTaskSourceStatus.Pending)
            {
                throw new InvalidOperationException("OnityTask is not completed.");
            }

            if (status == OnityTaskSourceStatus.Canceled)
            {
                throw new OperationCanceledException(m_cancellationToken);
            }

            if (status == OnityTaskSourceStatus.Faulted)
            {
                m_unobservedFault?.Observe();
                m_exception.Throw();
            }

            return m_result;
        }

        internal OnityTaskSourceStatus ReadCompletedOutcome(
            out Exception fault,
            out CancellationToken cancellationToken)
        {
            OnityTaskSourceStatus status = GetStatus(k_version);
            fault = null;
            cancellationToken = status == OnityTaskSourceStatus.Canceled
                ? m_cancellationToken
                : default;
            if (status == OnityTaskSourceStatus.Faulted)
            {
                // The gate serializes fault observation with bridge creation.
                // A later AsTask bridge inherits this observation.
                lock (m_gate)
                {
                    fault = m_exception.SourceException;
                    m_coordinatorObservedFault = true;
                    m_unobservedFault?.Observe();
                    if (m_taskBridge != null)
                    {
                        _ = m_taskBridge.Task.Exception;
                    }
                }
            }

            return status;
        }

        private Task<T> GetTaskBridge(int token)
        {
            lock (m_gate)
            {
                ValidateToken(token);
                if (m_taskBridge == null)
                {
                    Volatile.Write(ref m_taskBridge, CreateTaskBridge());
                    OnityTaskSourceStatus status = (OnityTaskSourceStatus)m_status;
                    if (status != OnityTaskSourceStatus.Pending)
                    {
                        m_unobservedFault?.Observe();
                        CompleteTaskBridge(
                            m_taskBridge, status, m_result, m_exception, m_cancellationToken);
                        if (status == OnityTaskSourceStatus.Faulted
                            && m_coordinatorObservedFault)
                        {
                            _ = m_taskBridge.Task.Exception;
                        }
                    }
                }

                return m_taskBridge.Task;
            }
        }

        internal static TaskCompletionSource<T> CreateTaskBridge()
        {
            if (ExecutionContext.IsFlowSuppressed())
            {
                return new TaskCompletionSource<T>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            // A promise has no delegate to run in its creation context. Mono's
            // options constructor still captures it; consumers capture their own
            // context when they register continuations after this scope ends.
            AsyncFlowControl flowControl = ExecutionContext.SuppressFlow();
            try
            {
                return new TaskCompletionSource<T>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }
            finally
            {
                flowControl.Undo();
            }
        }

        private void AddContinuation(Action continuation, int token)
        {
            if (continuation == null)
            {
                throw new ArgumentNullException(nameof(continuation));
            }

            bool invokeNow;
            lock (m_gate)
            {
                ValidateToken(token);
                invokeNow = m_status != (int)OnityTaskSourceStatus.Pending;
                if (!invokeNow)
                {
                    if (m_firstContinuation == null)
                    {
                        m_firstContinuation = continuation;
                    }
                    else if (m_secondContinuation == null)
                    {
                        m_secondContinuation = continuation;
                    }
                    else
                    {
                        if (m_otherContinuations == null)
                        {
                            m_otherContinuations = new List<Action>();
                        }

                        m_otherContinuations.Add(continuation);
                    }
                }
            }

            if (invokeNow)
            {
                OnityTaskContinuation.Invoke(continuation);
            }
        }

        private bool TryComplete(
            OnityTaskSourceStatus status,
            T result,
            Exception exception,
            CancellationToken cancellationToken)
        {
            Action firstContinuation;
            Action secondContinuation;
            List<Action> otherContinuations;
            ExceptionDispatchInfo capturedException;

            lock (m_gate)
            {
                if (m_status != (int)OnityTaskSourceStatus.Pending)
                {
                    return false;
                }

                capturedException = exception == null
                    ? null
                    : ExceptionDispatchInfo.Capture(exception);
                m_result = result;
                m_exception = capturedException;
                if (capturedException != null && m_taskBridge == null)
                {
                    SynchronizationContext context = m_reportingContext;
                    int contextThreadId = m_reportingThreadId;
                    if (context == null)
                    {
                        context = SynchronizationContext.Current;
                        contextThreadId = Thread.CurrentThread.ManagedThreadId;
                    }

                    m_unobservedFault = new UnobservedFault(
                        capturedException, context, contextThreadId);
                }

                m_cancellationToken = cancellationToken;
                firstContinuation = m_firstContinuation;
                secondContinuation = m_secondContinuation;
                otherContinuations = m_otherContinuations;
                m_firstContinuation = null;
                m_secondContinuation = null;
                m_otherContinuations = null;
                if (m_taskBridge != null)
                {
                    m_unobservedFault?.Observe();
                    CompleteTaskBridge(
                        m_taskBridge, status, result, capturedException, cancellationToken);
                }

                Volatile.Write(ref m_status, (int)status);
            }

            OnityTaskContinuation.Invoke(firstContinuation);
            OnityTaskContinuation.Invoke(secondContinuation);
            if (otherContinuations != null)
            {
                for (int i = 0; i < otherContinuations.Count; i++)
                {
                    OnityTaskContinuation.Invoke(otherContinuations[i]);
                }
            }

            return true;
        }

        private static void CompleteTaskBridge(
            TaskCompletionSource<T> bridge,
            OnityTaskSourceStatus status,
            T result,
            ExceptionDispatchInfo exception,
            CancellationToken cancellationToken)
        {
            if (status == OnityTaskSourceStatus.Succeeded)
            {
                bridge.TrySetResult(result);
            }
            else if (status == OnityTaskSourceStatus.Canceled)
            {
                bridge.TrySetCanceled(cancellationToken);
            }
            else
            {
                bridge.TrySetException(exception.SourceException);
            }
        }

        private static void ValidateToken(int token)
        {
            if (token != k_version)
            {
                throw new InvalidOperationException("The OnityTask source token is invalid.");
            }
        }
    }

    internal sealed class OnityPreservedTaskSource : OnityTaskCompletionSource<bool>,
        IOnityPreservedTaskContinuation
    {
        private IOnityTaskSource m_source;
        private readonly int m_token;

        public OnityPreservedTaskSource(IOnityTaskSource source, int token)
            : base(true)
        {
            m_source = source;
            m_token = token;
            ((IOnityPreservedTaskSource)source).OnCompleted(this, token);
        }

        public new OnityTask Task => new OnityTask((IOnityTaskSource)this);

        void IOnityPreservedTaskContinuation.Complete()
        {
            Complete();
        }

        private void Complete()
        {
            IOnityTaskSource source = m_source;
            m_source = null;
            int token = m_token;
            OnityTaskSourceStatus status = OnityTaskSourceStatus.Pending;

            try
            {
                status = source.GetStatus(token);
                ((OnityTaskSourceBase)source).GetPreservedResult(token, this);
                TrySetResult(true);
            }
            catch (OperationCanceledException exception)
            {
                if (status != OnityTaskSourceStatus.Canceled)
                {
                    TrySetFault(exception);
                }
                else
                {
                    TrySetCanceled(exception.CancellationToken);
                }
            }
            catch (Exception exception)
            {
                TrySetFault(exception);
            }
        }
    }

    internal sealed class OnityPreservedTaskSource<T> : OnityTaskCompletionSource<T>,
        IOnityPreservedTaskContinuation
    {
        private IOnityTaskSource<T> m_source;
        private readonly int m_token;

        public OnityPreservedTaskSource(IOnityTaskSource<T> source, int token)
            : base(true)
        {
            m_source = source;
            m_token = token;
            ((IOnityPreservedTaskSource)source).OnCompleted(this, token);
        }

        void IOnityPreservedTaskContinuation.Complete()
        {
            Complete();
        }

        private void Complete()
        {
            IOnityTaskSource<T> source = m_source;
            m_source = null;
            int token = m_token;
            OnityTaskSourceStatus status = OnityTaskSourceStatus.Pending;

            try
            {
                status = source.GetStatus(token);
                T result = ((OnityTaskSourceBase<T>)source).GetPreservedResult(token, this);
                TrySetResult(result);
            }
            catch (OperationCanceledException exception)
            {
                if (status != OnityTaskSourceStatus.Canceled)
                {
                    TrySetFault(exception);
                }
                else
                {
                    TrySetCanceled(exception.CancellationToken);
                }
            }
            catch (Exception exception)
            {
                TrySetFault(exception);
            }
        }
    }
}
