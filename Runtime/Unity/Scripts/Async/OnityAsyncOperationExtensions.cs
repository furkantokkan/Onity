using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Await helpers for Unity async operations.
    /// </summary>
    public static class OnityAsyncOperationExtensions
    {
        /// <summary>
        /// Converts a Unity async operation into a task that completes with the same operation instance.
        /// </summary>
        /// <typeparam name="TAsyncOperation">Async operation type.</typeparam>
        /// <param name="operation">Target operation.</param>
        /// <param name="onProgress">Optional progress callback.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task completed by the Unity operation.</returns>
        public static Task<TAsyncOperation> AsTask<TAsyncOperation>(
            this TAsyncOperation operation,
            Action<float> onProgress = null,
            CancellationToken cancellationToken = default)
            where TAsyncOperation : AsyncOperation
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return OnityTaskTracker.Track(
                    Task.FromCanceled<TAsyncOperation>(cancellationToken),
                    "OnityAsyncOperationExtensions.AsTask");
            }

            if (operation.isDone)
            {
                onProgress?.Invoke(1f);
                return OnityTaskTracker.Track(
                    Task.FromResult(operation),
                    "OnityAsyncOperationExtensions.AsTask");
            }

            return OnityTaskTracker.Track(
                WaitForCompletionAsync(operation, onProgress, cancellationToken),
                "OnityAsyncOperationExtensions.AsTask");
        }

        /// <summary>
        /// Converts a Unity async operation into a cancelable task with UniTask-like naming.
        /// </summary>
        /// <typeparam name="TAsyncOperation">Async operation type.</typeparam>
        /// <param name="operation">Target operation.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="onProgress">Optional progress callback.</param>
        /// <returns>Task completed by the Unity operation.</returns>
        public static Task<TAsyncOperation> WithCancellation<TAsyncOperation>(
            this TAsyncOperation operation,
            CancellationToken cancellationToken,
            Action<float> onProgress = null)
            where TAsyncOperation : AsyncOperation
        {
            return operation.AsTask(onProgress, cancellationToken);
        }

        /// <summary>
        /// Enables direct await usage on Unity async operations.
        /// </summary>
        /// <typeparam name="TAsyncOperation">Async operation type.</typeparam>
        /// <param name="operation">Target operation.</param>
        /// <returns>Task awaiter for the operation.</returns>
        public static TaskAwaiter<TAsyncOperation> GetAwaiter<TAsyncOperation>(this TAsyncOperation operation)
            where TAsyncOperation : AsyncOperation
        {
            return operation.AsTask().GetAwaiter();
        }

        private static async Task<TAsyncOperation> WaitForCompletionAsync<TAsyncOperation>(
            TAsyncOperation operation,
            Action<float> onProgress,
            CancellationToken cancellationToken)
            where TAsyncOperation : AsyncOperation
        {
            onProgress?.Invoke(Mathf.Clamp01(operation.progress));

            while (operation.isDone == false)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                onProgress?.Invoke(Mathf.Clamp01(operation.progress));
            }

            onProgress?.Invoke(1f);
            return operation;
        }
    }
}
