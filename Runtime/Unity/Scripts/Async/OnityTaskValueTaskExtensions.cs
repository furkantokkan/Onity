using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Conversions between <see cref="OnityTask"/> and <see cref="ValueTask"/>.
    /// </summary>
    /// <remarks>
    /// A conversion consumes its source: a pooled single-consumer <see cref="OnityTask"/> must not
    /// be awaited again afterwards, and the resulting <see cref="ValueTask"/> can be awaited once,
    /// as the value-task contract requires. A value task backed by a pending Onity task ignores the
    /// scheduling-context and execution-context flags of its continuation: the continuation runs on
    /// the thread that completes the Onity task.
    /// </remarks>
    public static class OnityTaskValueTaskExtensions
    {
        /// <summary>
        /// Converts the task to a <see cref="ValueTask"/>.
        /// </summary>
        /// <param name="task">Task to convert; it is consumed.</param>
        /// <returns>
        /// A completed value task when the task already succeeded; otherwise a value task backed by
        /// the task, which completes, faults or cancels as the task does.
        /// </returns>
        public static ValueTask AsValueTask(this OnityTask task)
        {
            if (task.IsCompletedSuccessfully)
            {
                task.GetAwaiter().GetResult();
                return default;
            }

            return new ValueTask(new OnityValueTaskSource(task), 0);
        }

        /// <summary>
        /// Converts the typed task to a <see cref="ValueTask{TResult}"/>.
        /// </summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="task">Task to convert; it is consumed.</param>
        /// <returns>
        /// A completed value task when the task already succeeded; otherwise a value task backed by
        /// the task, which completes, faults or cancels as the task does.
        /// </returns>
        public static ValueTask<T> AsValueTask<T>(this OnityTask<T> task)
        {
            if (task.IsCompletedSuccessfully)
            {
                return new ValueTask<T>(task.GetAwaiter().GetResult());
            }

            return new ValueTask<T>(new OnityValueTaskSource<T>(task), 0);
        }

        /// <summary>
        /// Converts a <see cref="ValueTask"/> to an Onity task by awaiting it.
        /// </summary>
        /// <param name="task">Value task to await; it is consumed.</param>
        /// <returns>A task that completes, faults or cancels as the value task does.</returns>
        public static async OnityTask AsOnityTask(this ValueTask task)
        {
            await task;
        }

        /// <summary>
        /// Converts a <see cref="ValueTask{TResult}"/> to a typed Onity task by awaiting it.
        /// </summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="task">Value task to await; it is consumed.</param>
        /// <returns>A task that completes, faults or cancels as the value task does.</returns>
        public static async OnityTask<T> AsOnityTask<T>(this ValueTask<T> task)
        {
            return await task;
        }
    }

    /// <summary>
    /// Conversions from .NET tasks to Onity tasks.
    /// </summary>
    public static class OnityDotNetTaskExtensions
    {
        private static readonly Action<Task, object> s_completeUntyped = CompleteUntyped;

        /// <summary>
        /// Converts a .NET task to a shareable Onity task that completes when the task does.
        /// </summary>
        /// <remarks>
        /// With <paramref name="useCurrentSynchronizationContext"/> true the returned task completes
        /// on the SynchronizationContext that is current when this method is called, which is Unity's
        /// main thread when called from it, so awaiting code resumes there. When no context is
        /// current, or the flag is false, it completes on <see cref="TaskScheduler.Current"/>, on the
        /// thread that completes the .NET task. A task that already succeeded converts without
        /// scheduling anything. A faulted task reports its first inner exception.
        /// </remarks>
        /// <param name="task">Task to convert.</param>
        /// <param name="useCurrentSynchronizationContext">
        /// True to complete on the current SynchronizationContext.
        /// </param>
        /// <returns>A task that completes, faults or cancels as the .NET task does.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="task"/> is null.</exception>
        public static OnityTask AsOnityTask(this Task task, bool useCurrentSynchronizationContext = true)
        {
            if (task == null)
            {
                throw new ArgumentNullException(nameof(task));
            }

            if (task.Status == TaskStatus.RanToCompletion)
            {
                return OnityTask.CompletedTask;
            }

            OnityTaskCompletionSource source = new OnityTaskCompletionSource();
            task.ContinueWith(
                s_completeUntyped,
                source,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                SelectScheduler(useCurrentSynchronizationContext));
            return source.Task;
        }

        /// <summary>
        /// Converts a typed .NET task to a shareable typed Onity task that completes when the task
        /// does. See <see cref="AsOnityTask(Task, bool)"/> for the threading rules.
        /// </summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="task">Task to convert.</param>
        /// <param name="useCurrentSynchronizationContext">
        /// True to complete on the current SynchronizationContext.
        /// </param>
        /// <returns>A task that completes, faults or cancels as the .NET task does.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="task"/> is null.</exception>
        public static OnityTask<T> AsOnityTask<T>(this Task<T> task, bool useCurrentSynchronizationContext = true)
        {
            if (task == null)
            {
                throw new ArgumentNullException(nameof(task));
            }

            if (task.Status == TaskStatus.RanToCompletion)
            {
                return OnityTask<T>.FromResult(task.Result);
            }

            OnityTaskCompletionSource<T> source = new OnityTaskCompletionSource<T>();
            task.ContinueWith(
                OnityTypedTaskCompletion<T>.Callback,
                source,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                SelectScheduler(useCurrentSynchronizationContext));
            return source.Task;
        }

        private static TaskScheduler SelectScheduler(bool useCurrentSynchronizationContext)
        {
            return useCurrentSynchronizationContext && SynchronizationContext.Current != null
                ? TaskScheduler.FromCurrentSynchronizationContext()
                : TaskScheduler.Current;
        }

        private static void CompleteUntyped(Task task, object state)
        {
            OnityTaskCompletionSource source = (OnityTaskCompletionSource)state;
            switch (task.Status)
            {
                case TaskStatus.RanToCompletion:
                    source.TrySetResult();
                    break;
                case TaskStatus.Canceled:
                    source.TrySetCanceled(OnityDotNetTaskOutcome.GetCancellationToken(task));
                    break;
                default:
                    source.TrySetException(OnityDotNetTaskOutcome.GetFirstException(task));
                    break;
            }
        }

        private static class OnityTypedTaskCompletion<T>
        {
            public static readonly Action<Task<T>, object> Callback = Complete;

            private static void Complete(Task<T> task, object state)
            {
                OnityTaskCompletionSource<T> source = (OnityTaskCompletionSource<T>)state;
                switch (task.Status)
                {
                    case TaskStatus.RanToCompletion:
                        source.TrySetResult(task.Result);
                        break;
                    case TaskStatus.Canceled:
                        source.TrySetCanceled(OnityDotNetTaskOutcome.GetCancellationToken(task));
                        break;
                    default:
                        source.TrySetException(OnityDotNetTaskOutcome.GetFirstException(task));
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Reads the outcome details of a completed .NET task.
    /// </summary>
    internal static class OnityDotNetTaskOutcome
    {
        /// <summary>Returns the token a canceled task was canceled with.</summary>
        /// <param name="task">Canceled task.</param>
        /// <returns>The token, or the default token when it cannot be determined.</returns>
        public static CancellationToken GetCancellationToken(Task task)
        {
            try
            {
                task.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException exception)
            {
                return exception.CancellationToken;
            }
            catch (Exception)
            {
                // A task that is not canceled has no token to report.
            }

            return default;
        }

        /// <summary>Returns the first inner exception of a faulted task.</summary>
        /// <param name="task">Faulted task.</param>
        /// <returns>The first inner exception, or the aggregate when it has none.</returns>
        public static Exception GetFirstException(Task task)
        {
            AggregateException aggregate = task.Exception;
            return aggregate == null
                ? new InvalidOperationException("The task did not fault.")
                : aggregate.InnerException ?? aggregate;
        }
    }

    /// <summary>
    /// Value-task source over an untyped Onity task.
    /// </summary>
    internal sealed class OnityValueTaskSource : IValueTaskSource
    {
        private readonly OnityTask m_task;
        private readonly Action m_invokeContinuation;

        private Action<object> m_continuation;
        private object m_continuationState;

        public OnityValueTaskSource(OnityTask task)
        {
            m_task = task;
            m_invokeContinuation = InvokeContinuation;
        }

        public ValueTaskSourceStatus GetStatus(short token)
        {
            switch (m_task.Status)
            {
                case OnityTaskStatus.Succeeded:
                    return ValueTaskSourceStatus.Succeeded;
                case OnityTaskStatus.Faulted:
                    return ValueTaskSourceStatus.Faulted;
                case OnityTaskStatus.Canceled:
                    return ValueTaskSourceStatus.Canceled;
                default:
                    return ValueTaskSourceStatus.Pending;
            }
        }

        public void GetResult(short token)
        {
            m_task.GetAwaiter().GetResult();
        }

        public void OnCompleted(
            Action<object> continuation,
            object state,
            short token,
            ValueTaskSourceOnCompletedFlags flags)
        {
            m_continuation = continuation;
            m_continuationState = state;
            m_task.GetAwaiter().UnsafeOnCompleted(m_invokeContinuation);
        }

        private void InvokeContinuation()
        {
            Action<object> continuation = m_continuation;
            object state = m_continuationState;
            m_continuation = null;
            m_continuationState = null;
            continuation(state);
        }
    }

    /// <summary>
    /// Value-task source over a typed Onity task.
    /// </summary>
    /// <typeparam name="T">Result type.</typeparam>
    internal sealed class OnityValueTaskSource<T> : IValueTaskSource<T>
    {
        private readonly OnityTask<T> m_task;
        private readonly Action m_invokeContinuation;

        private Action<object> m_continuation;
        private object m_continuationState;

        public OnityValueTaskSource(OnityTask<T> task)
        {
            m_task = task;
            m_invokeContinuation = InvokeContinuation;
        }

        public ValueTaskSourceStatus GetStatus(short token)
        {
            switch (m_task.Status)
            {
                case OnityTaskStatus.Succeeded:
                    return ValueTaskSourceStatus.Succeeded;
                case OnityTaskStatus.Faulted:
                    return ValueTaskSourceStatus.Faulted;
                case OnityTaskStatus.Canceled:
                    return ValueTaskSourceStatus.Canceled;
                default:
                    return ValueTaskSourceStatus.Pending;
            }
        }

        public T GetResult(short token)
        {
            return m_task.GetAwaiter().GetResult();
        }

        public void OnCompleted(
            Action<object> continuation,
            object state,
            short token,
            ValueTaskSourceOnCompletedFlags flags)
        {
            m_continuation = continuation;
            m_continuationState = state;
            m_task.GetAwaiter().UnsafeOnCompleted(m_invokeContinuation);
        }

        private void InvokeContinuation()
        {
            Action<object> continuation = m_continuation;
            object state = m_continuationState;
            m_continuation = null;
            m_continuationState = null;
            continuation(state);
        }
    }
}
