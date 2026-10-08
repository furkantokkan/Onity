using System;
using System.Threading;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Pooled completion source for an untyped <see cref="OnityTask"/> that is consumed exactly once.
    /// <see cref="Create"/> rents an instance, the producer completes it once, and the single
    /// consumer's <c>await</c> (or <see cref="OnityTask.AsTask"/> bridge) returns it to its pool.
    /// This mirrors UniTask's <c>AutoResetUniTaskCompletionSource</c>: after the result is consumed the
    /// instance is recycled with a new version, so a retained <see cref="OnityTask"/> value fails with
    /// <see cref="InvalidOperationException"/> when awaited a second time, and the producer must not
    /// touch the source after the consumer has taken the result.
    /// </summary>
    /// <remarks>
    /// Thread affinity: renting, completing and awaiting are thread-safe and do not require the Unity
    /// main thread. The awaiting continuation runs inline on the thread that completes the source, so
    /// code that needs the main thread must switch to it explicitly. A source whose task is never
    /// awaited is not returned to the pool; it is simply collected.
    /// </remarks>
    public sealed class OnityAutoResetTaskCompletionSource :
        IOnityPooledRunner<OnityAutoResetTaskCompletionSource>
    {
        private static OnityRunnerPool<OnityAutoResetTaskCompletionSource> s_pool;

        private readonly PooledSource m_source;
        private OnityAutoResetTaskCompletionSource m_nextPooled;
        private int m_createdVersion;

        private OnityAutoResetTaskCompletionSource()
        {
            m_source = new PooledSource(this);
        }

        ref OnityAutoResetTaskCompletionSource
            IOnityPooledRunner<OnityAutoResetTaskCompletionSource>.NextPooled => ref m_nextPooled;

        /// <summary>
        /// Gets the task completed by this source. It is bound to the cycle started by the last
        /// <see cref="Create"/> and can be awaited only once.
        /// </summary>
        public OnityTask Task => new OnityTask(m_source, m_createdVersion);

        /// <summary>
        /// Rents an incomplete source from the pool, or allocates one when the pool is empty.
        /// </summary>
        /// <returns>A source in a new, pending cycle.</returns>
        public static OnityAutoResetTaskCompletionSource Create()
        {
            if (!s_pool.TryPop(out OnityAutoResetTaskCompletionSource source))
            {
                source = new OnityAutoResetTaskCompletionSource();
            }

            source.m_source.Begin();
            source.m_createdVersion = source.m_source.Version;
            return source;
        }

        /// <summary>
        /// Rents a source that is already canceled.
        /// </summary>
        /// <param name="cancellationToken">Token reported to the awaiting consumer.</param>
        /// <param name="token">Version of the cycle, the token of <see cref="Task"/>.</param>
        /// <returns>The canceled source.</returns>
        public static OnityAutoResetTaskCompletionSource CreateFromCanceled(
            CancellationToken cancellationToken,
            out int token)
        {
            OnityAutoResetTaskCompletionSource source = Create();
            source.TrySetCanceled(cancellationToken);
            token = source.m_createdVersion;
            return source;
        }

        /// <summary>
        /// Rents a source that is already completed with an exception.
        /// </summary>
        /// <param name="exception">Failure to publish.</param>
        /// <param name="token">Version of the cycle, the token of <see cref="Task"/>.</param>
        /// <returns>The faulted source.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="exception"/> is null.</exception>
        public static OnityAutoResetTaskCompletionSource CreateFromException(
            Exception exception,
            out int token)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            OnityAutoResetTaskCompletionSource source = Create();
            source.TrySetException(exception);
            token = source.m_createdVersion;
            return source;
        }

        /// <summary>
        /// Rents a source that is already completed successfully.
        /// </summary>
        /// <param name="token">Version of the cycle, the token of <see cref="Task"/>.</param>
        /// <returns>The completed source.</returns>
        public static OnityAutoResetTaskCompletionSource CreateCompleted(out int token)
        {
            OnityAutoResetTaskCompletionSource source = Create();
            source.TrySetResult();
            token = source.m_createdVersion;
            return source;
        }

        /// <summary>
        /// Completes the task successfully if the current cycle is still pending.
        /// </summary>
        /// <returns>True if this call completed the task; false if it was already completed or the
        /// source has been recycled.</returns>
        public bool TrySetResult()
        {
            return m_createdVersion == m_source.Version && m_source.Complete();
        }

        /// <summary>
        /// Cancels the task if the current cycle is still pending.
        /// </summary>
        /// <param name="cancellationToken">Token reported to the awaiting consumer.</param>
        /// <returns>True if this call completed the task; false if it was already completed or the
        /// source has been recycled.</returns>
        public bool TrySetCanceled(CancellationToken cancellationToken = default)
        {
            return m_createdVersion == m_source.Version
                && m_source.Cancel(new OperationCanceledException(cancellationToken));
        }

        /// <summary>
        /// Completes the task with an exception if the current cycle is still pending. An
        /// <see cref="OperationCanceledException"/> completes it as canceled, as in UniTask.
        /// </summary>
        /// <param name="exception">Failure to publish.</param>
        /// <returns>True if this call completed the task; false if it was already completed or the
        /// source has been recycled.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="exception"/> is null.</exception>
        public bool TrySetException(Exception exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            if (m_createdVersion != m_source.Version)
            {
                return false;
            }

            return exception is OperationCanceledException canceled
                ? m_source.Cancel(canceled)
                : m_source.Fail(exception);
        }

        private void Return()
        {
            // The consumed or bridged release already retired the version and cleared the outcome.
            s_pool.TryPush(this, OnityTaskSettings.s_sourcePoolCapacity);
        }

        private sealed class PooledSource : OnityTaskSourceBase
        {
            private readonly OnityAutoResetTaskCompletionSource m_owner;

            public PooledSource(OnityAutoResetTaskCompletionSource owner)
            {
                m_owner = owner;
            }

            public void Begin()
            {
                Reset(default);
            }

            public bool Complete()
            {
                return TrySetResult();
            }

            public bool Fail(Exception exception)
            {
                return TrySetException(exception);
            }

            public bool Cancel(OperationCanceledException exception)
            {
                return TrySetCanceled(exception);
            }

            protected override void ReleaseSource()
            {
                m_owner.Return();
            }
        }
    }

    /// <summary>
    /// Pooled completion source for a typed <see cref="OnityTask{T}"/> that is consumed exactly once.
    /// <see cref="Create"/> rents an instance, the producer completes it once, and the single
    /// consumer's <c>await</c> (or <see cref="OnityTask{T}.AsTask"/> bridge) returns it to its pool.
    /// This mirrors UniTask's <c>AutoResetUniTaskCompletionSource&lt;T&gt;</c>: after the result is
    /// consumed the instance is recycled with a new version, so a retained <see cref="OnityTask{T}"/>
    /// value fails with <see cref="InvalidOperationException"/> when awaited a second time, and the
    /// producer must not touch the source after the consumer has taken the result.
    /// </summary>
    /// <typeparam name="T">Result type.</typeparam>
    /// <remarks>
    /// Thread affinity: renting, completing and awaiting are thread-safe and do not require the Unity
    /// main thread. The awaiting continuation runs inline on the thread that completes the source, so
    /// code that needs the main thread must switch to it explicitly. A source whose task is never
    /// awaited is not returned to the pool; it is simply collected. Each result type has its own pool.
    /// </remarks>
    public sealed class OnityAutoResetTaskCompletionSource<T> :
        IOnityPooledRunner<OnityAutoResetTaskCompletionSource<T>>
    {
        private static OnityRunnerPool<OnityAutoResetTaskCompletionSource<T>> s_pool;

        private readonly PooledSource m_source;
        private OnityAutoResetTaskCompletionSource<T> m_nextPooled;
        private int m_createdVersion;

        private OnityAutoResetTaskCompletionSource()
        {
            m_source = new PooledSource(this);
        }

        ref OnityAutoResetTaskCompletionSource<T>
            IOnityPooledRunner<OnityAutoResetTaskCompletionSource<T>>.NextPooled => ref m_nextPooled;

        /// <summary>
        /// Gets the task completed by this source. It is bound to the cycle started by the last
        /// <see cref="Create"/> and can be awaited only once.
        /// </summary>
        public OnityTask<T> Task => new OnityTask<T>(m_source, m_createdVersion);

        /// <summary>
        /// Rents an incomplete source from the pool, or allocates one when the pool is empty.
        /// </summary>
        /// <returns>A source in a new, pending cycle.</returns>
        public static OnityAutoResetTaskCompletionSource<T> Create()
        {
            if (!s_pool.TryPop(out OnityAutoResetTaskCompletionSource<T> source))
            {
                source = new OnityAutoResetTaskCompletionSource<T>();
            }

            source.m_source.Begin();
            source.m_createdVersion = source.m_source.Version;
            return source;
        }

        /// <summary>
        /// Rents a source that is already canceled.
        /// </summary>
        /// <param name="cancellationToken">Token reported to the awaiting consumer.</param>
        /// <param name="token">Version of the cycle, the token of <see cref="Task"/>.</param>
        /// <returns>The canceled source.</returns>
        public static OnityAutoResetTaskCompletionSource<T> CreateFromCanceled(
            CancellationToken cancellationToken,
            out int token)
        {
            OnityAutoResetTaskCompletionSource<T> source = Create();
            source.TrySetCanceled(cancellationToken);
            token = source.m_createdVersion;
            return source;
        }

        /// <summary>
        /// Rents a source that is already completed with an exception.
        /// </summary>
        /// <param name="exception">Failure to publish.</param>
        /// <param name="token">Version of the cycle, the token of <see cref="Task"/>.</param>
        /// <returns>The faulted source.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="exception"/> is null.</exception>
        public static OnityAutoResetTaskCompletionSource<T> CreateFromException(
            Exception exception,
            out int token)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            OnityAutoResetTaskCompletionSource<T> source = Create();
            source.TrySetException(exception);
            token = source.m_createdVersion;
            return source;
        }

        /// <summary>
        /// Rents a source that is already completed with a result.
        /// </summary>
        /// <param name="result">Result to publish.</param>
        /// <param name="token">Version of the cycle, the token of <see cref="Task"/>.</param>
        /// <returns>The completed source.</returns>
        public static OnityAutoResetTaskCompletionSource<T> CreateFromResult(T result, out int token)
        {
            OnityAutoResetTaskCompletionSource<T> source = Create();
            source.TrySetResult(result);
            token = source.m_createdVersion;
            return source;
        }

        /// <summary>
        /// Completes the task with a result if the current cycle is still pending.
        /// </summary>
        /// <param name="result">Result to publish.</param>
        /// <returns>True if this call completed the task; false if it was already completed or the
        /// source has been recycled.</returns>
        public bool TrySetResult(T result)
        {
            return m_createdVersion == m_source.Version && m_source.Complete(result);
        }

        /// <summary>
        /// Cancels the task if the current cycle is still pending.
        /// </summary>
        /// <param name="cancellationToken">Token reported to the awaiting consumer.</param>
        /// <returns>True if this call completed the task; false if it was already completed or the
        /// source has been recycled.</returns>
        public bool TrySetCanceled(CancellationToken cancellationToken = default)
        {
            return m_createdVersion == m_source.Version
                && m_source.Cancel(new OperationCanceledException(cancellationToken));
        }

        /// <summary>
        /// Completes the task with an exception if the current cycle is still pending. An
        /// <see cref="OperationCanceledException"/> completes it as canceled, as in UniTask.
        /// </summary>
        /// <param name="exception">Failure to publish.</param>
        /// <returns>True if this call completed the task; false if it was already completed or the
        /// source has been recycled.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="exception"/> is null.</exception>
        public bool TrySetException(Exception exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            if (m_createdVersion != m_source.Version)
            {
                return false;
            }

            return exception is OperationCanceledException canceled
                ? m_source.Cancel(canceled)
                : m_source.Fail(exception);
        }

        private void Return()
        {
            // The consumed or bridged release already retired the version and cleared the outcome.
            s_pool.TryPush(this, OnityTaskSettings.s_sourcePoolCapacity);
        }

        private sealed class PooledSource : OnityTaskSourceBase<T>
        {
            private readonly OnityAutoResetTaskCompletionSource<T> m_owner;

            public PooledSource(OnityAutoResetTaskCompletionSource<T> owner)
            {
                m_owner = owner;
            }

            public void Begin()
            {
                Reset(default);
            }

            public bool Complete(T result)
            {
                return TrySetResult(result);
            }

            public bool Fail(Exception exception)
            {
                return TrySetException(exception);
            }

            public bool Cancel(OperationCanceledException exception)
            {
                return TrySetCanceled(exception);
            }

            protected override void ReleaseSource()
            {
                m_owner.Return();
            }
        }
    }
}
