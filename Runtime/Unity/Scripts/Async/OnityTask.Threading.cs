using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Thread-switching awaitables and the state and async-delegate forms of
    /// <see cref="RunOnThreadPool(Action, bool, CancellationToken)"/>.
    /// </summary>
    public readonly partial struct OnityTask
    {
        /// <summary>
        /// Returns an awaitable that always queues its continuation as a work item of the default
        /// .NET task scheduler, including when requested from a worker thread.
        /// </summary>
        /// <remarks>
        /// <see cref="SwitchToThreadPool"/> is the recommended switch; this one exists for parity with
        /// code that expects the task scheduler. The awaiter cannot be canceled and does not capture
        /// execution context. WebGL players do not support this operation.
        /// </remarks>
        /// <returns>Task-pool switch awaitable.</returns>
        public static OnityTaskPoolSwitch SwitchToTaskPool()
        {
            return default;
        }

        /// <summary>
        /// Returns an awaitable that posts its continuation to a <see cref="SynchronizationContext"/>.
        /// </summary>
        /// <remarks>
        /// Every await posts, even when the caller already runs on that context. Cancellation is
        /// observed when the await completes, on the context.
        /// </remarks>
        /// <param name="synchronizationContext">Context that runs the continuation.</param>
        /// <param name="cancellationToken">Cancellation token observed when the switch completes.</param>
        /// <returns>Context switch awaitable.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="synchronizationContext"/> is null.</exception>
        public static OnitySynchronizationContextSwitch SwitchToSynchronizationContext(
            SynchronizationContext synchronizationContext,
            CancellationToken cancellationToken = default)
        {
            if (synchronizationContext == null)
            {
                throw new ArgumentNullException(nameof(synchronizationContext));
            }

            return new OnitySynchronizationContextSwitch(synchronizationContext, cancellationToken);
        }

        /// <summary>
        /// Returns a scope that resumes on a <see cref="SynchronizationContext"/> when it closes.
        /// Use it with <c>await using</c> around code that switches to another thread.
        /// </summary>
        /// <remarks>
        /// Closing the scope always posts to the context. Cancellation is observed when the scope
        /// completes, on the context.
        /// </remarks>
        /// <param name="synchronizationContext">Context to return to.</param>
        /// <param name="cancellationToken">Cancellation token observed when the scope completes.</param>
        /// <returns>Return scope.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="synchronizationContext"/> is null.</exception>
        public static OnityReturnToSynchronizationContext ReturnToSynchronizationContext(
            SynchronizationContext synchronizationContext,
            CancellationToken cancellationToken = default)
        {
            if (synchronizationContext == null)
            {
                throw new ArgumentNullException(nameof(synchronizationContext));
            }

            return new OnityReturnToSynchronizationContext(synchronizationContext, false, cancellationToken);
        }

        /// <summary>
        /// Returns a scope that resumes on the <see cref="SynchronizationContext"/> that is current now
        /// when it closes. Use it with <c>await using</c> around code that switches to another thread.
        /// </summary>
        /// <remarks>
        /// When no context is current, there is nothing to return to and the scope closes inline.
        /// Cancellation is observed when the scope completes.
        /// </remarks>
        /// <param name="dontPostWhenSameContext">
        /// True to continue inline when the scope closes on the same context, instead of posting.
        /// </param>
        /// <param name="cancellationToken">Cancellation token observed when the scope completes.</param>
        /// <returns>Return scope.</returns>
        public static OnityReturnToSynchronizationContext ReturnToCurrentSynchronizationContext(
            bool dontPostWhenSameContext = true,
            CancellationToken cancellationToken = default)
        {
            return new OnityReturnToSynchronizationContext(
                SynchronizationContext.Current, dontPostWhenSameContext, cancellationToken);
        }

        /// <summary>
        /// Runs synchronous work with a state argument on the thread pool and optionally returns to
        /// Unity's main thread before publishing completion, fault or cancellation. Behaves as
        /// <see cref="RunOnThreadPool(Action, bool, CancellationToken)"/>.
        /// </summary>
        /// <param name="action">Synchronous work. Do not access Unity objects from the worker.</param>
        /// <param name="state">State passed to the work.</param>
        /// <param name="returnToMainThread">Return through the main-thread dispatcher after work.</param>
        /// <param name="cancellationToken">Cancellation token observed before and after work.</param>
        /// <returns>Task representing work and the optional return to the main thread.</returns>
        /// <exception cref="ArgumentNullException">The action is null.</exception>
        /// <exception cref="PlatformNotSupportedException">Called in a WebGL player.</exception>
        public static OnityTask RunOnThreadPool(
            Action<object> action,
            object state,
            bool returnToMainThread = true,
            CancellationToken cancellationToken = default)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            OnityTaskThreadPoolDispatcher.ThrowIfUnsupported();
            if (cancellationToken.IsCancellationRequested)
            {
                return FromCanceled(cancellationToken);
            }

            return RunOnThreadPoolAsync(action, state, returnToMainThread, cancellationToken);
        }

        /// <summary>
        /// Runs an async delegate on the thread pool and optionally returns to Unity's main thread
        /// before publishing completion, fault or cancellation. The task completes after the task the
        /// delegate returns does.
        /// </summary>
        /// <remarks>
        /// Pre-canceled work is not dispatched. Cancellation is checked before the delegate starts and
        /// after it completes, but cannot interrupt it; pass the token to the delegate's own awaits.
        /// A delegate exception takes precedence over cancellation. Main-thread returns use the
        /// originating session and discard stale-session continuations.
        /// </remarks>
        /// <param name="action">Async work. Do not access Unity objects before it returns to the main thread.</param>
        /// <param name="returnToMainThread">Return through the main-thread dispatcher after work.</param>
        /// <param name="cancellationToken">Cancellation token observed before and after work.</param>
        /// <returns>Task representing work and the optional return to the main thread.</returns>
        /// <exception cref="ArgumentNullException">The delegate is null.</exception>
        /// <exception cref="PlatformNotSupportedException">Called in a WebGL player.</exception>
        public static OnityTask RunOnThreadPool(
            Func<OnityTask> action,
            bool returnToMainThread = true,
            CancellationToken cancellationToken = default)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            OnityTaskThreadPoolDispatcher.ThrowIfUnsupported();
            if (cancellationToken.IsCancellationRequested)
            {
                return FromCanceled(cancellationToken);
            }

            return RunOnThreadPoolAsync(action, returnToMainThread, cancellationToken);
        }

        /// <summary>
        /// Runs an async delegate with a state argument on the thread pool and optionally returns to
        /// Unity's main thread before publishing completion, fault or cancellation. Behaves as
        /// <see cref="RunOnThreadPool(Func{OnityTask}, bool, CancellationToken)"/>.
        /// </summary>
        /// <param name="action">Async work. Do not access Unity objects before it returns to the main thread.</param>
        /// <param name="state">State passed to the work.</param>
        /// <param name="returnToMainThread">Return through the main-thread dispatcher after work.</param>
        /// <param name="cancellationToken">Cancellation token observed before and after work.</param>
        /// <returns>Task representing work and the optional return to the main thread.</returns>
        /// <exception cref="ArgumentNullException">The delegate is null.</exception>
        /// <exception cref="PlatformNotSupportedException">Called in a WebGL player.</exception>
        public static OnityTask RunOnThreadPool(
            Func<object, OnityTask> action,
            object state,
            bool returnToMainThread = true,
            CancellationToken cancellationToken = default)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            OnityTaskThreadPoolDispatcher.ThrowIfUnsupported();
            if (cancellationToken.IsCancellationRequested)
            {
                return FromCanceled(cancellationToken);
            }

            return RunOnThreadPoolAsync(action, state, returnToMainThread, cancellationToken);
        }

        /// <summary>
        /// Runs an async delegate on the thread pool and optionally returns to Unity's main thread
        /// before publishing its result, fault or cancellation. See
        /// <see cref="RunOnThreadPool(Func{OnityTask}, bool, CancellationToken)"/> for the rules.
        /// </summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="function">Async work. Do not access Unity objects before it returns to the main thread.</param>
        /// <param name="returnToMainThread">Return through the main-thread dispatcher after work.</param>
        /// <param name="cancellationToken">Cancellation token observed before and after work.</param>
        /// <returns>Task containing the result after the optional main-thread return.</returns>
        /// <exception cref="ArgumentNullException">The delegate is null.</exception>
        /// <exception cref="PlatformNotSupportedException">Called in a WebGL player.</exception>
        public static OnityTask<T> RunOnThreadPool<T>(
            Func<OnityTask<T>> function,
            bool returnToMainThread = true,
            CancellationToken cancellationToken = default)
        {
            if (function == null)
            {
                throw new ArgumentNullException(nameof(function));
            }

            OnityTaskThreadPoolDispatcher.ThrowIfUnsupported();
            if (cancellationToken.IsCancellationRequested)
            {
                return OnityTask<T>.FromCanceled(cancellationToken);
            }

            return RunOnThreadPoolAsync(function, returnToMainThread, cancellationToken);
        }

        /// <summary>
        /// Runs synchronous work with a state argument on the thread pool and optionally returns to
        /// Unity's main thread before publishing its result, fault or cancellation. Behaves as
        /// <see cref="RunOnThreadPool{T}(Func{T}, bool, CancellationToken)"/>.
        /// </summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="function">Synchronous work. Do not access Unity objects from the worker.</param>
        /// <param name="state">State passed to the work.</param>
        /// <param name="returnToMainThread">Return through the main-thread dispatcher after work.</param>
        /// <param name="cancellationToken">Cancellation token observed before and after work.</param>
        /// <returns>Task containing the worker result after the optional main-thread return.</returns>
        /// <exception cref="ArgumentNullException">The function is null.</exception>
        /// <exception cref="PlatformNotSupportedException">Called in a WebGL player.</exception>
        public static OnityTask<T> RunOnThreadPool<T>(
            Func<object, T> function,
            object state,
            bool returnToMainThread = true,
            CancellationToken cancellationToken = default)
        {
            if (function == null)
            {
                throw new ArgumentNullException(nameof(function));
            }

            OnityTaskThreadPoolDispatcher.ThrowIfUnsupported();
            if (cancellationToken.IsCancellationRequested)
            {
                return OnityTask<T>.FromCanceled(cancellationToken);
            }

            return RunOnThreadPoolAsync(function, state, returnToMainThread, cancellationToken);
        }

        /// <summary>
        /// Runs an async delegate with a state argument on the thread pool and optionally returns to
        /// Unity's main thread before publishing its result, fault or cancellation. See
        /// <see cref="RunOnThreadPool(Func{OnityTask}, bool, CancellationToken)"/> for the rules.
        /// </summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="function">Async work. Do not access Unity objects before it returns to the main thread.</param>
        /// <param name="state">State passed to the work.</param>
        /// <param name="returnToMainThread">Return through the main-thread dispatcher after work.</param>
        /// <param name="cancellationToken">Cancellation token observed before and after work.</param>
        /// <returns>Task containing the result after the optional main-thread return.</returns>
        /// <exception cref="ArgumentNullException">The delegate is null.</exception>
        /// <exception cref="PlatformNotSupportedException">Called in a WebGL player.</exception>
        public static OnityTask<T> RunOnThreadPool<T>(
            Func<object, OnityTask<T>> function,
            object state,
            bool returnToMainThread = true,
            CancellationToken cancellationToken = default)
        {
            if (function == null)
            {
                throw new ArgumentNullException(nameof(function));
            }

            OnityTaskThreadPoolDispatcher.ThrowIfUnsupported();
            if (cancellationToken.IsCancellationRequested)
            {
                return OnityTask<T>.FromCanceled(cancellationToken);
            }

            return RunOnThreadPoolAsync(function, state, returnToMainThread, cancellationToken);
        }

        // The main-thread switch value is taken before the hop to the pool, so the return carries the
        // session that started the work and a continuation from an ended session is discarded.

        private static async OnityTask RunOnThreadPoolAsync(
            Action<object> action,
            object state,
            bool returnToMainThread,
            CancellationToken cancellationToken)
        {
            var mainThread = SwitchToMainThread();
            try
            {
                await SwitchToThreadPool(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                action(state);
            }
            finally
            {
                if (returnToMainThread)
                {
                    await mainThread;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        private static async OnityTask RunOnThreadPoolAsync(
            Func<OnityTask> action,
            bool returnToMainThread,
            CancellationToken cancellationToken)
        {
            var mainThread = SwitchToMainThread();
            try
            {
                await SwitchToThreadPool(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                await action();
            }
            finally
            {
                if (returnToMainThread)
                {
                    await mainThread;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        private static async OnityTask RunOnThreadPoolAsync(
            Func<object, OnityTask> action,
            object state,
            bool returnToMainThread,
            CancellationToken cancellationToken)
        {
            var mainThread = SwitchToMainThread();
            try
            {
                await SwitchToThreadPool(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                await action(state);
            }
            finally
            {
                if (returnToMainThread)
                {
                    await mainThread;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        private static async OnityTask<T> RunOnThreadPoolAsync<T>(
            Func<OnityTask<T>> function,
            bool returnToMainThread,
            CancellationToken cancellationToken)
        {
            var mainThread = SwitchToMainThread();
            T result;
            try
            {
                await SwitchToThreadPool(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                result = await function();
            }
            finally
            {
                if (returnToMainThread)
                {
                    await mainThread;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }

        private static async OnityTask<T> RunOnThreadPoolAsync<T>(
            Func<object, T> function,
            object state,
            bool returnToMainThread,
            CancellationToken cancellationToken)
        {
            var mainThread = SwitchToMainThread();
            T result;
            try
            {
                await SwitchToThreadPool(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                result = function(state);
            }
            finally
            {
                if (returnToMainThread)
                {
                    await mainThread;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }

        private static async OnityTask<T> RunOnThreadPoolAsync<T>(
            Func<object, OnityTask<T>> function,
            object state,
            bool returnToMainThread,
            CancellationToken cancellationToken)
        {
            var mainThread = SwitchToMainThread();
            T result;
            try
            {
                await SwitchToThreadPool(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                result = await function(state);
            }
            finally
            {
                if (returnToMainThread)
                {
                    await mainThread;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
    }
}
