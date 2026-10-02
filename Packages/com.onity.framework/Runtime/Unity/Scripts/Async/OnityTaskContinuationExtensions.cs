using System;
using System.Threading.Tasks;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Continuation, unwrapping and sharing helpers for <see cref="OnityTask"/> and
    /// <see cref="OnityTask{T}"/>.
    /// </summary>
    /// <remarks>
    /// Every helper consumes the task it extends, so a pooled single-consumer task must not be
    /// awaited again afterwards. A continuation runs on the thread that completes the antecedent
    /// task, which is Unity's main thread for PlayerLoop-driven tasks. The returned task is a
    /// single-consumer native task; call <c>Preserve</c> before sharing it. When the antecedent
    /// faults or is canceled the continuation does not run and the returned task completes the
    /// same way.
    /// </remarks>
    public static class OnityTaskContinuationExtensions
    {
        /// <summary>
        /// Awaits the task, then runs an action with its result.
        /// </summary>
        /// <typeparam name="T">Result type of the antecedent.</typeparam>
        /// <param name="task">Antecedent task.</param>
        /// <param name="continuationFunction">Action that receives the result.</param>
        /// <returns>A task that completes after the action ran.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="continuationFunction"/> is null.</exception>
        public static OnityTask ContinueWith<T>(this OnityTask<T> task, Action<T> continuationFunction)
        {
            if (continuationFunction == null)
            {
                throw new ArgumentNullException(nameof(continuationFunction));
            }

            return ContinueCoreAsync(task, continuationFunction);
        }

        /// <summary>
        /// Awaits the task, then awaits the task returned by a function that receives its result.
        /// </summary>
        /// <typeparam name="T">Result type of the antecedent.</typeparam>
        /// <param name="task">Antecedent task.</param>
        /// <param name="continuationFunction">Function that starts the next operation.</param>
        /// <returns>A task that completes after the next operation did.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="continuationFunction"/> is null.</exception>
        public static OnityTask ContinueWith<T>(this OnityTask<T> task, Func<T, OnityTask> continuationFunction)
        {
            if (continuationFunction == null)
            {
                throw new ArgumentNullException(nameof(continuationFunction));
            }

            return ContinueCoreAsync(task, continuationFunction);
        }

        /// <summary>
        /// Awaits the task, then returns the value computed from its result.
        /// </summary>
        /// <typeparam name="T">Result type of the antecedent.</typeparam>
        /// <typeparam name="TResult">Result type of the continuation.</typeparam>
        /// <param name="task">Antecedent task.</param>
        /// <param name="continuationFunction">Function that computes the result.</param>
        /// <returns>A task that completes with the computed value.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="continuationFunction"/> is null.</exception>
        public static OnityTask<TResult> ContinueWith<T, TResult>(
            this OnityTask<T> task,
            Func<T, TResult> continuationFunction)
        {
            if (continuationFunction == null)
            {
                throw new ArgumentNullException(nameof(continuationFunction));
            }

            return ContinueCoreAsync(task, continuationFunction);
        }

        /// <summary>
        /// Awaits the task, then awaits the typed task returned by a function that receives its
        /// result.
        /// </summary>
        /// <typeparam name="T">Result type of the antecedent.</typeparam>
        /// <typeparam name="TResult">Result type of the next operation.</typeparam>
        /// <param name="task">Antecedent task.</param>
        /// <param name="continuationFunction">Function that starts the next operation.</param>
        /// <returns>A task that completes with the result of the next operation.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="continuationFunction"/> is null.</exception>
        public static OnityTask<TResult> ContinueWith<T, TResult>(
            this OnityTask<T> task,
            Func<T, OnityTask<TResult>> continuationFunction)
        {
            if (continuationFunction == null)
            {
                throw new ArgumentNullException(nameof(continuationFunction));
            }

            return ContinueCoreAsync(task, continuationFunction);
        }

        /// <summary>
        /// Awaits the task, then runs an action.
        /// </summary>
        /// <param name="task">Antecedent task.</param>
        /// <param name="continuationFunction">Action to run.</param>
        /// <returns>A task that completes after the action ran.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="continuationFunction"/> is null.</exception>
        public static OnityTask ContinueWith(this OnityTask task, Action continuationFunction)
        {
            if (continuationFunction == null)
            {
                throw new ArgumentNullException(nameof(continuationFunction));
            }

            return ContinueCoreAsync(task, continuationFunction);
        }

        /// <summary>
        /// Awaits the task, then awaits the task returned by a function.
        /// </summary>
        /// <param name="task">Antecedent task.</param>
        /// <param name="continuationFunction">Function that starts the next operation.</param>
        /// <returns>A task that completes after the next operation did.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="continuationFunction"/> is null.</exception>
        public static OnityTask ContinueWith(this OnityTask task, Func<OnityTask> continuationFunction)
        {
            if (continuationFunction == null)
            {
                throw new ArgumentNullException(nameof(continuationFunction));
            }

            return ContinueCoreAsync(task, continuationFunction);
        }

        /// <summary>
        /// Awaits the task, then returns the value computed by a function.
        /// </summary>
        /// <typeparam name="T">Result type of the continuation.</typeparam>
        /// <param name="task">Antecedent task.</param>
        /// <param name="continuationFunction">Function that computes the result.</param>
        /// <returns>A task that completes with the computed value.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="continuationFunction"/> is null.</exception>
        public static OnityTask<T> ContinueWith<T>(this OnityTask task, Func<T> continuationFunction)
        {
            if (continuationFunction == null)
            {
                throw new ArgumentNullException(nameof(continuationFunction));
            }

            return ContinueCoreAsync(task, continuationFunction);
        }

        /// <summary>
        /// Awaits the task, then awaits the typed task returned by a function.
        /// </summary>
        /// <typeparam name="T">Result type of the next operation.</typeparam>
        /// <param name="task">Antecedent task.</param>
        /// <param name="continuationFunction">Function that starts the next operation.</param>
        /// <returns>A task that completes with the result of the next operation.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="continuationFunction"/> is null.</exception>
        public static OnityTask<T> ContinueWith<T>(this OnityTask task, Func<OnityTask<T>> continuationFunction)
        {
            if (continuationFunction == null)
            {
                throw new ArgumentNullException(nameof(continuationFunction));
            }

            return ContinueCoreAsync(task, continuationFunction);
        }

        /// <summary>
        /// Awaits a task that produces a typed task, and awaits that inner task.
        /// </summary>
        /// <typeparam name="T">Result type of the inner task.</typeparam>
        /// <param name="task">Outer task.</param>
        /// <returns>A task that completes with the inner result.</returns>
        public static async OnityTask<T> Unwrap<T>(this OnityTask<OnityTask<T>> task)
        {
            return await await task;
        }

        /// <summary>
        /// Awaits a task that produces a task, and awaits that inner task.
        /// </summary>
        /// <param name="task">Outer task.</param>
        /// <returns>A task that completes when the inner task does.</returns>
        public static async OnityTask Unwrap(this OnityTask<OnityTask> task)
        {
            await await task;
        }

        /// <summary>
        /// Awaits a .NET task that produces a typed Onity task, and awaits that inner task.
        /// </summary>
        /// <typeparam name="T">Result type of the inner task.</typeparam>
        /// <param name="task">Outer .NET task.</param>
        /// <returns>A task that completes with the inner result.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="task"/> is null.</exception>
        public static OnityTask<T> Unwrap<T>(this Task<OnityTask<T>> task)
        {
            if (task == null)
            {
                throw new ArgumentNullException(nameof(task));
            }

            return UnwrapCoreAsync(task, true);
        }

        /// <summary>
        /// Awaits a .NET task that produces a typed Onity task, and awaits that inner task.
        /// </summary>
        /// <typeparam name="T">Result type of the inner task.</typeparam>
        /// <param name="task">Outer .NET task.</param>
        /// <param name="continueOnCapturedContext">
        /// True to resume the outer await on the captured SynchronizationContext.
        /// </param>
        /// <returns>A task that completes with the inner result.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="task"/> is null.</exception>
        public static OnityTask<T> Unwrap<T>(this Task<OnityTask<T>> task, bool continueOnCapturedContext)
        {
            if (task == null)
            {
                throw new ArgumentNullException(nameof(task));
            }

            return UnwrapCoreAsync(task, continueOnCapturedContext);
        }

        /// <summary>
        /// Awaits a .NET task that produces an Onity task, and awaits that inner task.
        /// </summary>
        /// <param name="task">Outer .NET task.</param>
        /// <returns>A task that completes when the inner task does.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="task"/> is null.</exception>
        public static OnityTask Unwrap(this Task<OnityTask> task)
        {
            if (task == null)
            {
                throw new ArgumentNullException(nameof(task));
            }

            return UnwrapCoreAsync(task, true);
        }

        /// <summary>
        /// Awaits a .NET task that produces an Onity task, and awaits that inner task.
        /// </summary>
        /// <param name="task">Outer .NET task.</param>
        /// <param name="continueOnCapturedContext">
        /// True to resume the outer await on the captured SynchronizationContext.
        /// </param>
        /// <returns>A task that completes when the inner task does.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="task"/> is null.</exception>
        public static OnityTask Unwrap(this Task<OnityTask> task, bool continueOnCapturedContext)
        {
            if (task == null)
            {
                throw new ArgumentNullException(nameof(task));
            }

            return UnwrapCoreAsync(task, continueOnCapturedContext);
        }

        /// <summary>
        /// Awaits an Onity task that produces a .NET task, and awaits that inner task.
        /// </summary>
        /// <typeparam name="T">Result type of the inner task.</typeparam>
        /// <param name="task">Outer task.</param>
        /// <returns>A task that completes with the inner result.</returns>
        public static async OnityTask<T> Unwrap<T>(this OnityTask<Task<T>> task)
        {
            return await await task;
        }

        /// <summary>
        /// Awaits an Onity task that produces a .NET task, and awaits that inner task.
        /// </summary>
        /// <typeparam name="T">Result type of the inner task.</typeparam>
        /// <param name="task">Outer task.</param>
        /// <param name="continueOnCapturedContext">
        /// True to resume the inner await on the captured SynchronizationContext.
        /// </param>
        /// <returns>A task that completes with the inner result.</returns>
        public static async OnityTask<T> Unwrap<T>(this OnityTask<Task<T>> task, bool continueOnCapturedContext)
        {
            return await (await task).ConfigureAwait(continueOnCapturedContext);
        }

        /// <summary>
        /// Awaits an Onity task that produces a .NET task, and awaits that inner task.
        /// </summary>
        /// <param name="task">Outer task.</param>
        /// <returns>A task that completes when the inner task does.</returns>
        public static async OnityTask Unwrap(this OnityTask<Task> task)
        {
            await await task;
        }

        /// <summary>
        /// Awaits an Onity task that produces a .NET task, and awaits that inner task.
        /// </summary>
        /// <param name="task">Outer task.</param>
        /// <param name="continueOnCapturedContext">
        /// True to resume the inner await on the captured SynchronizationContext.
        /// </param>
        /// <returns>A task that completes when the inner task does.</returns>
        public static async OnityTask Unwrap(this OnityTask<Task> task, bool continueOnCapturedContext)
        {
            await (await task).ConfigureAwait(continueOnCapturedContext);
        }

        /// <summary>
        /// Wraps a running task in a shared <see cref="OnityAsyncLazy"/>. The task is consumed by
        /// this call; the lazy wrapper can be awaited any number of times.
        /// </summary>
        /// <param name="task">Running task to share.</param>
        /// <returns>Shared wrapper that completes as the task does.</returns>
        public static OnityAsyncLazy ToAsyncLazy(this OnityTask task)
        {
            return new OnityAsyncLazy(task);
        }

        /// <summary>
        /// Wraps a running typed task in a shared <see cref="OnityAsyncLazy{T}"/>. The task is
        /// consumed by this call; the lazy wrapper can be awaited any number of times.
        /// </summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="task">Running task to share.</param>
        /// <returns>Shared wrapper that completes as the task does.</returns>
        public static OnityAsyncLazy<T> ToAsyncLazy<T>(this OnityTask<T> task)
        {
            return new OnityAsyncLazy<T>(task);
        }

        /// <summary>
        /// Runs the task without awaiting it and passes a failure to a handler that can be
        /// marshaled to Unity's main thread. Unlike the instance <c>Forget(handler)</c>, which runs
        /// the handler on the thread that completes the task, this overload can resume on the main
        /// thread first. Cancellation is reported to the handler like any other exception. An
        /// exception thrown by the handler is published through <see cref="OnityTaskScheduler"/>.
        /// </summary>
        /// <remarks>
        /// Without a handler this is the instance <c>Forget()</c>, whose failures are published
        /// through <see cref="OnityTaskScheduler"/>. A pooled single-consumer task is consumed.
        /// </remarks>
        /// <param name="task">Task to observe.</param>
        /// <param name="exceptionHandler">Failure callback; null publishes failures to the scheduler.</param>
        /// <param name="handleExceptionOnMainThread">
        /// True to resume on Unity's main thread before calling the handler.
        /// </param>
        public static void Forget(this OnityTask task, Action<Exception> exceptionHandler, bool handleExceptionOnMainThread)
        {
            if (exceptionHandler == null)
            {
                task.Forget();
                return;
            }

            ForgetWithHandlerAsync(task, exceptionHandler, handleExceptionOnMainThread).Forget();
        }

        /// <summary>
        /// Runs the typed task without awaiting it and passes a failure to a handler that can be
        /// marshaled to Unity's main thread. See
        /// <see cref="Forget(OnityTask, Action{Exception}, bool)"/> for the handler rules.
        /// </summary>
        /// <typeparam name="T">Result type, which is discarded.</typeparam>
        /// <param name="task">Task to observe.</param>
        /// <param name="exceptionHandler">Failure callback; null publishes failures to the scheduler.</param>
        /// <param name="handleExceptionOnMainThread">
        /// True to resume on Unity's main thread before calling the handler.
        /// </param>
        public static void Forget<T>(
            this OnityTask<T> task,
            Action<Exception> exceptionHandler,
            bool handleExceptionOnMainThread)
        {
            if (exceptionHandler == null)
            {
                task.Forget();
                return;
            }

            ForgetWithHandlerAsync(task, exceptionHandler, handleExceptionOnMainThread).Forget();
        }

        private static async OnityTaskVoid ForgetWithHandlerAsync(
            OnityTask task,
            Action<Exception> exceptionHandler,
            bool handleExceptionOnMainThread)
        {
            try
            {
                await task;
            }
            catch (Exception exception)
            {
                await InvokeHandlerAsync(exception, exceptionHandler, handleExceptionOnMainThread);
            }
        }

        private static async OnityTaskVoid ForgetWithHandlerAsync<T>(
            OnityTask<T> task,
            Action<Exception> exceptionHandler,
            bool handleExceptionOnMainThread)
        {
            try
            {
                await task;
            }
            catch (Exception exception)
            {
                await InvokeHandlerAsync(exception, exceptionHandler, handleExceptionOnMainThread);
            }
        }

        private static async OnityTask InvokeHandlerAsync(
            Exception exception,
            Action<Exception> exceptionHandler,
            bool handleExceptionOnMainThread)
        {
            try
            {
                if (handleExceptionOnMainThread)
                {
                    await OnityTask.SwitchToMainThread();
                }

                exceptionHandler(exception);
            }
            catch (Exception handlerException)
            {
                OnityTaskScheduler.PublishUnobservedException(handlerException);
            }
        }

        private static async OnityTask ContinueCoreAsync<T>(OnityTask<T> task, Action<T> continuation)
        {
            continuation(await task);
        }

        private static async OnityTask ContinueCoreAsync<T>(OnityTask<T> task, Func<T, OnityTask> continuation)
        {
            await continuation(await task);
        }

        private static async OnityTask<TResult> ContinueCoreAsync<T, TResult>(
            OnityTask<T> task,
            Func<T, TResult> continuation)
        {
            return continuation(await task);
        }

        private static async OnityTask<TResult> ContinueCoreAsync<T, TResult>(
            OnityTask<T> task,
            Func<T, OnityTask<TResult>> continuation)
        {
            return await continuation(await task);
        }

        private static async OnityTask ContinueCoreAsync(OnityTask task, Action continuation)
        {
            await task;
            continuation();
        }

        private static async OnityTask ContinueCoreAsync(OnityTask task, Func<OnityTask> continuation)
        {
            await task;
            await continuation();
        }

        private static async OnityTask<T> ContinueCoreAsync<T>(OnityTask task, Func<T> continuation)
        {
            await task;
            return continuation();
        }

        private static async OnityTask<T> ContinueCoreAsync<T>(OnityTask task, Func<OnityTask<T>> continuation)
        {
            await task;
            return await continuation();
        }

        private static async OnityTask<T> UnwrapCoreAsync<T>(Task<OnityTask<T>> task, bool continueOnCapturedContext)
        {
            return await await task.ConfigureAwait(continueOnCapturedContext);
        }

        private static async OnityTask UnwrapCoreAsync(Task<OnityTask> task, bool continueOnCapturedContext)
        {
            await await task.ConfigureAwait(continueOnCapturedContext);
        }
    }
}
