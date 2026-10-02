using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Cancellation-token helpers for OnityTask code: token registration that does not capture the
    /// execution context, conversions between tasks and tokens, and disposal on cancellation.
    /// Every member is thread-safe unless its remarks say otherwise.
    /// </summary>
    public static class OnityCancellationTokenExtensions
    {
        private static readonly Action<object> s_completeSource = CompleteSource;
        private static readonly Action<object> s_dispose = DisposeObject;

        /// <summary>
        /// Registers a callback that runs when the token is canceled, without capturing the current
        /// <see cref="ExecutionContext"/>, and without marshaling to a SynchronizationContext.
        /// The callback runs synchronously on the thread that cancels the token, or immediately when
        /// the token is already canceled.
        /// </summary>
        /// <param name="cancellationToken">Token to observe.</param>
        /// <param name="callback">Callback to run on cancellation.</param>
        /// <returns>Registration that unregisters the callback when disposed.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="callback"/> is null.</exception>
        public static CancellationTokenRegistration RegisterWithoutCaptureExecutionContext(
            this CancellationToken cancellationToken,
            Action callback)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            if (ExecutionContext.IsFlowSuppressed())
            {
                return cancellationToken.Register(callback, false);
            }

            using (ExecutionContext.SuppressFlow())
            {
                return cancellationToken.Register(callback, false);
            }
        }

        /// <summary>
        /// Registers a callback with a state argument that runs when the token is canceled, without
        /// capturing the current <see cref="ExecutionContext"/>, and without marshaling to a
        /// SynchronizationContext. The callback runs synchronously on the thread that cancels the
        /// token, or immediately when the token is already canceled.
        /// </summary>
        /// <param name="cancellationToken">Token to observe.</param>
        /// <param name="callback">Callback to run on cancellation.</param>
        /// <param name="state">State passed to the callback.</param>
        /// <returns>Registration that unregisters the callback when disposed.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="callback"/> is null.</exception>
        public static CancellationTokenRegistration RegisterWithoutCaptureExecutionContext(
            this CancellationToken cancellationToken,
            Action<object> callback,
            object state)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            if (ExecutionContext.IsFlowSuppressed())
            {
                return cancellationToken.Register(callback, state, false);
            }

            using (ExecutionContext.SuppressFlow())
            {
                return cancellationToken.Register(callback, state, false);
            }
        }

        /// <summary>
        /// Returns a token that is canceled when the task completes, whatever its outcome.
        /// </summary>
        /// <remarks>
        /// The task is consumed by this call. A fault is published through
        /// <see cref="OnityTaskScheduler"/> and still cancels the token; cancellation of the task is
        /// dropped by the scheduler unless <see cref="OnityTaskScheduler.PropagateOperationCanceledException"/>
        /// is set. The source behind the token is disposed right after it is canceled, and a task that
        /// has already completed returns a token that is already canceled. A task that never
        /// completes keeps its observer and the token alive.
        /// </remarks>
        /// <param name="task">Task whose completion cancels the token.</param>
        /// <returns>A token canceled when the task completes.</returns>
        public static CancellationToken ToCancellationToken(this OnityTask task)
        {
            CancellationTokenSource source = new CancellationTokenSource();

            // Read the token first: a task that already completed cancels and disposes the source
            // synchronously, and a disposed source refuses to hand out its token.
            CancellationToken token = source.Token;
            CancelWhenCompletedAsync(task, source).Forget();
            return token;
        }

        /// <summary>
        /// Returns a token that is canceled when the task completes or when a linked token is
        /// canceled, whichever happens first.
        /// </summary>
        /// <remarks>
        /// The task is consumed by this call, unless the linked token is already canceled: then that
        /// token is returned and the task is left untouched. A linked token that cannot be canceled
        /// behaves as <see cref="ToCancellationToken(OnityTask)"/>. See that overload for the fault
        /// rules. The link to the token is released when the task completes.
        /// </remarks>
        /// <param name="task">Task whose completion cancels the token.</param>
        /// <param name="linkToken">Token whose cancellation also cancels the result.</param>
        /// <returns>A token canceled by the task or by the linked token.</returns>
        public static CancellationToken ToCancellationToken(this OnityTask task, CancellationToken linkToken)
        {
            if (linkToken.IsCancellationRequested)
            {
                return linkToken;
            }

            if (!linkToken.CanBeCanceled)
            {
                return task.ToCancellationToken();
            }

            CancellationTokenSource source = CancellationTokenSource.CreateLinkedTokenSource(linkToken);
            CancellationToken token = source.Token;
            CancelWhenCompletedAsync(task, source).Forget();
            return token;
        }

        /// <summary>
        /// Returns a token that is canceled when the typed task completes, whatever its outcome.
        /// The task is consumed. See <see cref="ToCancellationToken(OnityTask)"/>.
        /// </summary>
        /// <typeparam name="T">Result type, which is discarded.</typeparam>
        /// <param name="task">Task whose completion cancels the token.</param>
        /// <returns>A token canceled when the task completes.</returns>
        public static CancellationToken ToCancellationToken<T>(this OnityTask<T> task)
        {
            return task.AsOnityTask().ToCancellationToken();
        }

        /// <summary>
        /// Returns a token that is canceled when the typed task completes or when a linked token is
        /// canceled. See <see cref="ToCancellationToken(OnityTask, CancellationToken)"/>.
        /// </summary>
        /// <typeparam name="T">Result type, which is discarded.</typeparam>
        /// <param name="task">Task whose completion cancels the token.</param>
        /// <param name="linkToken">Token whose cancellation also cancels the result.</param>
        /// <returns>A token canceled by the task or by the linked token.</returns>
        public static CancellationToken ToCancellationToken<T>(this OnityTask<T> task, CancellationToken linkToken)
        {
            if (linkToken.IsCancellationRequested)
            {
                return linkToken;
            }

            return task.AsOnityTask().ToCancellationToken(linkToken);
        }

        /// <summary>
        /// Returns a task that completes when the token is canceled, with the registration that
        /// observes the token.
        /// </summary>
        /// <remarks>
        /// As in UniTask, the task completes successfully when the token is canceled later, but it is
        /// returned canceled when the token is already canceled. Use <see cref="WaitUntilCanceled"/>
        /// for an await that never throws. Dispose the registration to stop observing the token; the
        /// task then never completes. The task is shareable.
        /// </remarks>
        /// <param name="cancellationToken">Token to observe.</param>
        /// <returns>The task and the registration to dispose when it is no longer needed.</returns>
        public static (OnityTask Task, CancellationTokenRegistration Registration) ToOnityTask(
            this CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return (OnityTask.FromCanceled(cancellationToken), default);
            }

            OnityTaskCompletionSource source = new OnityTaskCompletionSource();
            CancellationTokenRegistration registration =
                cancellationToken.RegisterWithoutCaptureExecutionContext(s_completeSource, source);
            return (source.Task, registration);
        }

        /// <summary>
        /// Returns an awaitable that completes when the token is canceled. Awaiting it never throws.
        /// </summary>
        /// <remarks>
        /// A token that cannot be canceled completes immediately. The continuation runs
        /// synchronously on the thread that cancels the token, without the execution context of the
        /// await. Each await registers one callback that stays registered until the token is
        /// canceled or its source is collected, so wait on a token with a defined end.
        /// </remarks>
        /// <param name="cancellationToken">Token to wait for.</param>
        /// <returns>An awaitable that completes on cancellation.</returns>
        public static OnityCancellationTokenAwaitable WaitUntilCanceled(this CancellationToken cancellationToken)
        {
            return new OnityCancellationTokenAwaitable(cancellationToken);
        }

        /// <summary>
        /// Disposes an object when the token is canceled, immediately when it already is.
        /// </summary>
        /// <param name="disposable">Object to dispose.</param>
        /// <param name="cancellationToken">Token whose cancellation disposes the object.</param>
        /// <returns>
        /// The registration. Dispose it to keep the object alive when the token is canceled later.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="disposable"/> is null.</exception>
        public static CancellationTokenRegistration AddTo(this IDisposable disposable, CancellationToken cancellationToken)
        {
            if (disposable == null)
            {
                throw new ArgumentNullException(nameof(disposable));
            }

            return cancellationToken.RegisterWithoutCaptureExecutionContext(s_dispose, disposable);
        }

        private static void CompleteSource(object state)
        {
            ((OnityTaskCompletionSource)state).TrySetResult();
        }

        private static void DisposeObject(object state)
        {
            ((IDisposable)state).Dispose();
        }

        private static async OnityTaskVoid CancelWhenCompletedAsync(OnityTask task, CancellationTokenSource source)
        {
            try
            {
                await task;
            }
            catch (Exception exception)
            {
                OnityTaskScheduler.PublishUnobservedException(exception);
            }

            try
            {
                source.Cancel();
            }
            finally
            {
                source.Dispose();
            }
        }
    }

    /// <summary>
    /// Exception helpers for cancellation checks.
    /// </summary>
    public static class OnityExceptionExtensions
    {
        /// <summary>
        /// Returns true for an <see cref="OperationCanceledException"/> or a type derived from it,
        /// such as <see cref="System.Threading.Tasks.TaskCanceledException"/>.
        /// </summary>
        /// <param name="exception">Exception to test.</param>
        /// <returns>True when the exception reports cancellation.</returns>
        public static bool IsOperationCanceledException(this Exception exception)
        {
            return exception is OperationCanceledException;
        }
    }

    /// <summary>
    /// Compares <see cref="CancellationToken"/> values by their cancellation source, for use as a
    /// dictionary or set comparer.
    /// </summary>
    public sealed class OnityCancellationTokenEqualityComparer : IEqualityComparer<CancellationToken>
    {
        /// <summary>
        /// Shared comparer instance.
        /// </summary>
        public static readonly IEqualityComparer<CancellationToken> Default =
            new OnityCancellationTokenEqualityComparer();

        /// <inheritdoc />
        public bool Equals(CancellationToken x, CancellationToken y)
        {
            return x.Equals(y);
        }

        /// <inheritdoc />
        public int GetHashCode(CancellationToken obj)
        {
            return obj.GetHashCode();
        }
    }

    /// <summary>
    /// Awaitable returned by <see cref="OnityCancellationTokenExtensions.WaitUntilCanceled"/>.
    /// Awaiting it completes when the token is canceled, and never throws.
    /// </summary>
    public readonly struct OnityCancellationTokenAwaitable
    {
        private readonly CancellationToken m_cancellationToken;

        /// <summary>
        /// Creates an awaitable for a token.
        /// </summary>
        /// <param name="cancellationToken">Token to wait for.</param>
        public OnityCancellationTokenAwaitable(CancellationToken cancellationToken)
        {
            m_cancellationToken = cancellationToken;
        }

        /// <summary>
        /// Returns the awaiter of this awaitable.
        /// </summary>
        /// <returns>The awaiter.</returns>
        public OnityCancellationTokenAwaiter GetAwaiter()
        {
            return new OnityCancellationTokenAwaiter(m_cancellationToken);
        }
    }

    /// <summary>
    /// Awaiter of <see cref="OnityCancellationTokenAwaitable"/>.
    /// </summary>
    public readonly struct OnityCancellationTokenAwaiter : ICriticalNotifyCompletion
    {
        private readonly CancellationToken m_cancellationToken;

        /// <summary>
        /// Creates an awaiter for a token.
        /// </summary>
        /// <param name="cancellationToken">Token to wait for.</param>
        public OnityCancellationTokenAwaiter(CancellationToken cancellationToken)
        {
            m_cancellationToken = cancellationToken;
        }

        /// <summary>
        /// True when the token is canceled, or can never be canceled.
        /// </summary>
        public bool IsCompleted => !m_cancellationToken.CanBeCanceled || m_cancellationToken.IsCancellationRequested;

        /// <summary>
        /// Completes the await. It never throws.
        /// </summary>
        public void GetResult()
        {
        }

        /// <summary>
        /// Runs the continuation when the token is canceled, without capturing the execution context.
        /// </summary>
        /// <param name="continuation">Continuation to run.</param>
        public void OnCompleted(Action continuation)
        {
            UnsafeOnCompleted(continuation);
        }

        /// <summary>
        /// Runs the continuation when the token is canceled, without capturing the execution context.
        /// </summary>
        /// <param name="continuation">Continuation to run.</param>
        public void UnsafeOnCompleted(Action continuation)
        {
            m_cancellationToken.RegisterWithoutCaptureExecutionContext(continuation);
        }
    }
}
