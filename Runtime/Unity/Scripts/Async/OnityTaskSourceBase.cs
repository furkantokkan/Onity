using System;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Unity.IL2CPP.CompilerServices;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Base of untyped pooled sources. The completion protocol lives in <see cref="OnityTaskSourceCore"/>;
    /// this class adds the untyped result surface and the <see cref="bool"/> task bridge.
    /// </summary>
    [Il2CppSetOption(Option.NullChecks, false)]
    internal abstract class OnityTaskSourceBase : OnityTaskSourceCore, IOnityTaskSource, IOnityPreservedTaskSource
    {
        public Task AsTask(int token)
        {
            return ((TaskCompletionSource<bool>)GetOrCreateBridge(token)).Task;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void GetResult(int token)
        {
            GetResultCore(token, null);
        }

        internal void GetPreservedResult(int token, IOnityPreservedTaskContinuation continuation)
        {
            GetResultCore(token, continuation);
        }

        internal sealed override void ConsumeCore(int token)
        {
            GetResultCore(token, null);
        }

        internal sealed override Task AsCoreTask(int token)
        {
            return AsTask(token);
        }

        protected bool TrySetResult()
        {
            if (!TryClaimCompletion())
            {
                return false;
            }

            PublishClaimed((int)OnityTaskSourceStatus.Succeeded);
            return true;
        }

        /// <summary>
        /// Publishes success for a cycle that only this caller completes, with one compare-and-swap.
        /// False when a retirement or another completer already won.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TrySetOwnedResult()
        {
            return TryPublishOwned((int)OnityTaskSourceStatus.Succeeded);
        }

        /// <summary>
        /// Version-checked owned success for an owner that may race an immediate cancellation on another
        /// thread. False when that cancellation won, even if its cycle has been consumed since.
        /// </summary>
        internal bool TrySetOwnedResult(int version)
        {
            return TryPublishOwned((int)OnityTaskSourceStatus.Succeeded, version);
        }

        private protected sealed override object CreateBridge()
        {
            return OnityAsyncExecutionContext.CreateTaskBridge<bool>();
        }

        private protected sealed override void ApplyStatusToBridge(object bridge, int status)
        {
            TaskCompletionSource<bool> taskCompletionSource = (TaskCompletionSource<bool>)bridge;
            if (taskCompletionSource == null)
            {
                return;
            }

            if (status == (int)OnityTaskSourceStatus.Succeeded)
            {
                taskCompletionSource.TrySetResult(true);
            }
            else if (status == (int)OnityTaskSourceStatus.Canceled)
            {
                taskCompletionSource.TrySetCanceled(CompletionException is OperationCanceledException exception
                    ? exception.CancellationToken
                    : CompletionCancellationToken);
            }
            else
            {
                taskCompletionSource.TrySetException(CompletionException);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void GetResultCore(int token, IOnityPreservedTaskContinuation continuation)
        {
            int status = ClaimResult(token, continuation);
            if (status == (int)OnityTaskSourceStatus.Succeeded)
            {
                ClearCompletionReferences();
                ReleaseSource();
                return;
            }

            ThrowCompletion(status);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ThrowCompletion(int status)
        {
            Exception exception = GetCompletionException(status);
            ClearCompletionReferences();
            ReleaseSource();
            ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }

    /// <summary>
    /// Base of typed pooled sources. The completion protocol lives in <see cref="OnityTaskSourceCore"/>;
    /// this class adds the result field, the typed result surface and the typed task bridge. A typed
    /// source is also an untyped <see cref="OnityTaskCore"/>, so an untyped view of a typed task shares
    /// the same object and token without consuming it.
    /// </summary>
    /// <typeparam name="T">Result type.</typeparam>
    [Il2CppSetOption(Option.NullChecks, false)]
    internal abstract class OnityTaskSourceBase<T> : OnityTaskSourceCore, IOnityTaskSource<T>, IOnityPreservedTaskSource
    {
        private T m_result;

        public Task<T> AsTask(int token)
        {
            return ((TaskCompletionSource<T>)GetOrCreateBridge(token)).Task;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T GetResult(int token)
        {
            return GetResultCore(token, null);
        }

        internal T GetPreservedResult(int token, IOnityPreservedTaskContinuation continuation)
        {
            return GetResultCore(token, continuation);
        }

        internal sealed override void ConsumeCore(int token)
        {
            GetResultCore(token, null);
        }

        internal sealed override Task AsCoreTask(int token)
        {
            return AsTask(token);
        }

        protected bool TrySetResult(T result)
        {
            if (!TryClaimCompletion())
            {
                return false;
            }

            m_result = result;
            PublishClaimed((int)OnityTaskSourceStatus.Succeeded);
            return true;
        }

        /// <summary>
        /// Publishes a result for a cycle that only this caller completes, with one compare-and-swap.
        /// False when a retirement or another completer already won; the stored value is then ignored
        /// and cleared when the cycle is released.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TrySetOwnedResult(T result)
        {
            m_result = result;
            return TryPublishOwned((int)OnityTaskSourceStatus.Succeeded);
        }

        /// <summary>
        /// Version-checked success for an owner that may race an immediate cancellation on another thread:
        /// the result is stored only after the claim of the cycle that issued <paramref name="version"/>.
        /// </summary>
        internal bool TrySetResult(T result, int version)
        {
            if (!TryClaimCompletion(version))
            {
                return false;
            }

            m_result = result;
            PublishClaimed((int)OnityTaskSourceStatus.Succeeded);
            return true;
        }

        private protected sealed override void ClearResult()
        {
            m_result = default;
        }

        private protected sealed override object CreateBridge()
        {
            return OnityAsyncExecutionContext.CreateTaskBridge<T>();
        }

        private protected sealed override void ApplyStatusToBridge(object bridge, int status)
        {
            TaskCompletionSource<T> taskCompletionSource = (TaskCompletionSource<T>)bridge;
            if (taskCompletionSource == null)
            {
                return;
            }

            if (status == (int)OnityTaskSourceStatus.Succeeded)
            {
                taskCompletionSource.TrySetResult(m_result);
            }
            else if (status == (int)OnityTaskSourceStatus.Canceled)
            {
                taskCompletionSource.TrySetCanceled(CompletionException is OperationCanceledException exception
                    ? exception.CancellationToken
                    : CompletionCancellationToken);
            }
            else
            {
                taskCompletionSource.TrySetException(CompletionException);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private T GetResultCore(int token, IOnityPreservedTaskContinuation continuation)
        {
            int status = ClaimResult(token, continuation);
            if (status == (int)OnityTaskSourceStatus.Succeeded)
            {
                T result = m_result;
                m_result = default;
                ClearCompletionReferences();
                ReleaseSource();
                return result;
            }

            ThrowCompletion(status);
            return default;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ThrowCompletion(int status)
        {
            Exception exception = GetCompletionException(status);
            m_result = default;
            ClearCompletionReferences();
            ReleaseSource();
            ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }
}
