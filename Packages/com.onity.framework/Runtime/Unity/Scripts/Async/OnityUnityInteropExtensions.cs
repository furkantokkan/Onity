// AsyncInstantiateOperation exists from Unity 2022.3.20 and in the Unity 6 line.
#if (UNITY_2022_3 && !(UNITY_2022_3_0 || UNITY_2022_3_1 || UNITY_2022_3_2 || UNITY_2022_3_3 || UNITY_2022_3_4 || UNITY_2022_3_5 || UNITY_2022_3_6 || UNITY_2022_3_7 || UNITY_2022_3_8 || UNITY_2022_3_9 || UNITY_2022_3_10 || UNITY_2022_3_11 || UNITY_2022_3_12 || UNITY_2022_3_13 || UNITY_2022_3_14 || UNITY_2022_3_15 || UNITY_2022_3_16 || UNITY_2022_3_17 || UNITY_2022_3_18 || UNITY_2022_3_19)) || UNITY_2023_3_OR_NEWER
#define ONITY_ASYNC_INSTANTIATE
#endif

using System;
using System.Threading;
using Unity.Jobs;
using UnityEngine;

namespace Onity.Unity.Async
{
    /// <summary>
    /// OnityTask adapters for the remaining Unity asynchronous operations: an
    /// <see cref="AsyncOperation"/> with an <see cref="IProgress{T}"/> reporter, a job handle,
    /// <c>AsyncInstantiateOperation</c> and, in Unity 2023.1 and newer, <c>Awaitable</c>.
    /// </summary>
    /// <remarks>
    /// Every method that completes from the PlayerLoop must be called on Unity's main thread with an
    /// active Play/player session, like the operation adapters it extends. Pending tasks complete on
    /// Unity's main thread and are single-consumer.
    /// </remarks>
    public static class OnityUnityInteropExtensions
    {
        /// <summary>
        /// Converts a Unity async operation into an OnityTask that completes with the same operation,
        /// reporting progress to an <see cref="IProgress{T}"/>.
        /// </summary>
        /// <remarks>
        /// The progress argument is required, so a call without it keeps binding to the overload that
        /// takes an <c>Action&lt;float&gt;</c>. A <c>null</c> literal for the second argument is
        /// ambiguous between the two overloads; cast it or name the parameter.
        /// </remarks>
        /// <typeparam name="TAsyncOperation">Async operation type.</typeparam>
        /// <param name="operation">Target operation.</param>
        /// <param name="progress">Progress reporter, or null to report nothing.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>OnityTask completed by the Unity operation.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="operation"/> is null.</exception>
        public static OnityTask<TAsyncOperation> AsOnityTask<TAsyncOperation>(
            this TAsyncOperation operation,
            IProgress<float> progress,
            CancellationToken cancellationToken = default)
            where TAsyncOperation : AsyncOperation
        {
            Action<float> callback = progress == null ? null : progress.Report;
            return operation.AsOnityTask(callback, cancellationToken);
        }

        /// <summary>
        /// Returns the awaiter of a task that completes when the job handle is completed, so a job
        /// handle can be awaited directly.
        /// </summary>
        /// <remarks>
        /// Equivalent to <c>handle.AsOnityTask().GetAwaiter()</c>: the call accepts the obligation to
        /// complete the handle. See <see cref="OnityJobHandleExtensions.AsOnityTask(JobHandle)"/> for the
        /// main-thread requirement and the ownership rules.
        /// </remarks>
        /// <param name="jobHandle">Scheduled job or combined dependency to complete.</param>
        /// <returns>Awaiter of the job task.</returns>
        public static OnityTaskAwaiter GetAwaiter(this JobHandle jobHandle)
        {
            return jobHandle.AsOnityTask().GetAwaiter();
        }

#if ONITY_ASYNC_INSTANTIATE
        /// <summary>
        /// Awaits an asynchronous instantiation and returns the instantiated objects.
        /// </summary>
        /// <remarks>
        /// Cancellation stops the wait; it does not cancel the instantiation. Call
        /// <c>operation.Cancel()</c> to discard the objects. The wait polls the operation once per
        /// Update.
        /// </remarks>
        /// <param name="operation">Instantiate operation.</param>
        /// <param name="progress">Optional progress reporter, called each frame and with 1 on completion.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task whose result is the instantiated objects.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="operation"/> is null.</exception>
        public static OnityTask<UnityEngine.Object[]> AsInstancesOnityTask(
            this AsyncInstantiateOperation operation,
            IProgress<float> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return OnityTask<UnityEngine.Object[]>.FromCanceled(cancellationToken);
            }

            if (operation.isDone)
            {
                progress?.Report(1f);
                return OnityTask<UnityEngine.Object[]>.FromResult(operation.Result);
            }

            return AwaitInstantiateAsync(operation, progress, cancellationToken);
        }

        /// <summary>
        /// Awaits a typed asynchronous instantiation and returns the instantiated objects.
        /// </summary>
        /// <remarks>
        /// Cancellation stops the wait; it does not cancel the instantiation. Call
        /// <c>operation.Cancel()</c> to discard the objects. The wait polls the operation once per
        /// Update.
        /// </remarks>
        /// <typeparam name="T">Instantiated object type.</typeparam>
        /// <param name="operation">Instantiate operation.</param>
        /// <param name="progress">Optional progress reporter, called each frame and with 1 on completion.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task whose result is the typed instantiated objects.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="operation"/> is null.</exception>
        public static OnityTask<T[]> AsInstancesOnityTask<T>(
            this AsyncInstantiateOperation<T> operation,
            IProgress<float> progress = null,
            CancellationToken cancellationToken = default)
            where T : UnityEngine.Object
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return OnityTask<T[]>.FromCanceled(cancellationToken);
            }

            if (operation.isDone)
            {
                progress?.Report(1f);
                return OnityTask<T[]>.FromResult(operation.Result);
            }

            return AwaitInstantiateAsync(operation, progress, cancellationToken);
        }

        private static async OnityTask<UnityEngine.Object[]> AwaitInstantiateAsync(
            AsyncInstantiateOperation operation,
            IProgress<float> progress,
            CancellationToken cancellationToken)
        {
            while (!operation.isDone)
            {
                progress?.Report(Mathf.Clamp01(operation.progress));
                await OnityTask.Yield(OnityPlayerLoopTiming.Update, cancellationToken);
            }

            progress?.Report(1f);
            return operation.Result;
        }

        private static async OnityTask<T[]> AwaitInstantiateAsync<T>(
            AsyncInstantiateOperation<T> operation,
            IProgress<float> progress,
            CancellationToken cancellationToken)
            where T : UnityEngine.Object
        {
            while (!operation.isDone)
            {
                progress?.Report(Mathf.Clamp01(operation.progress));
                await OnityTask.Yield(OnityPlayerLoopTiming.Update, cancellationToken);
            }

            progress?.Report(1f);
            return operation.Result;
        }
#endif

#if UNITY_2023_1_OR_NEWER
        /// <summary>
        /// Converts a Unity <see cref="Awaitable"/> to an OnityTask by awaiting it. The continuation
        /// runs where the awaitable resumes it, which is Unity's main thread for the PlayerLoop
        /// awaitables.
        /// </summary>
        /// <param name="awaitable">Awaitable to await; it must not be awaited elsewhere.</param>
        /// <returns>A task that completes, faults or cancels as the awaitable does.</returns>
        public static async OnityTask AsOnityTask(this Awaitable awaitable)
        {
            await awaitable;
        }

        /// <summary>
        /// Converts a Unity <see cref="Awaitable{T}"/> to a typed OnityTask by awaiting it. See
        /// <see cref="AsOnityTask(Awaitable)"/>.
        /// </summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="awaitable">Awaitable to await; it must not be awaited elsewhere.</param>
        /// <returns>A task that completes, faults or cancels as the awaitable does.</returns>
        public static async OnityTask<T> AsOnityTask<T>(this Awaitable<T> awaitable)
        {
            return await awaitable;
        }
#endif
    }
}
