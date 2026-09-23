using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Onity.Unity.Async
{
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
    public class OnityTaskCompletionSource<T> : IOnityTaskSource<T>, IOnityTaskSource
    {
        private const int k_version = 1;

        private readonly object m_gate = new object();

        private Action m_firstContinuation;
        private Action m_secondContinuation;
        private List<Action> m_otherContinuations;
        private TaskCompletionSource<T> m_taskBridge;
        private ExceptionDispatchInfo m_exception;
        private CancellationToken m_cancellationToken;
        private T m_result;
        private int m_status;

        /// <summary>
        /// Initializes an incomplete source.
        /// </summary>
        public OnityTaskCompletionSource()
        {
        }

        /// <summary>
        /// Gets the task completed by this source.
        /// </summary>
        public OnityTask<T> Task => new OnityTask<T>((IOnityTaskSource<T>)this);

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
            return (OnityTaskSourceStatus)Volatile.Read(ref m_status);
        }

        private T GetResult(int token)
        {
            OnityTaskSourceStatus status;
            T result;
            ExceptionDispatchInfo exception;
            CancellationToken cancellationToken;

            lock (m_gate)
            {
                ValidateToken(token);
                status = (OnityTaskSourceStatus)m_status;
                result = m_result;
                exception = m_exception;
                cancellationToken = m_cancellationToken;
            }

            if (status == OnityTaskSourceStatus.Pending)
            {
                throw new InvalidOperationException("OnityTask is not completed.");
            }

            if (status == OnityTaskSourceStatus.Canceled)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (status == OnityTaskSourceStatus.Faulted)
            {
                exception.Throw();
            }

            return result;
        }

        private Task<T> GetTaskBridge(int token)
        {
            lock (m_gate)
            {
                ValidateToken(token);
                if (m_taskBridge == null)
                {
                    m_taskBridge = new TaskCompletionSource<T>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    OnityTaskSourceStatus status = (OnityTaskSourceStatus)m_status;
                    if (status != OnityTaskSourceStatus.Pending)
                    {
                        CompleteTaskBridge(
                            m_taskBridge, status, m_result, m_exception, m_cancellationToken);
                    }
                }

                return m_taskBridge.Task;
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
                m_cancellationToken = cancellationToken;
                firstContinuation = m_firstContinuation;
                secondContinuation = m_secondContinuation;
                otherContinuations = m_otherContinuations;
                m_firstContinuation = null;
                m_secondContinuation = null;
                m_otherContinuations = null;
                if (m_taskBridge != null)
                {
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
}
