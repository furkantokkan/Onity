using System;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;

namespace Onity.Unity.Async
{
    /// <summary>
    /// OnityTask adapters that expose the typed payload of Unity asynchronous requests.
    /// </summary>
    /// <remarks>
    /// Every method must be called on Unity's main thread with an active Play/player session.
    /// Pending tasks complete on Unity's main thread from the Update phase and are single-consumer;
    /// call <c>Preserve</c> before sharing one. A cancellation token that is already canceled
    /// returns a canceled task without touching the operation. An operation that is already done
    /// returns a completed task and reports progress 1 once.
    /// </remarks>
    public static class OnityUnityOperationExtensions
    {
        /// <summary>
        /// Awaits a Resources load and returns the loaded asset.
        /// </summary>
        /// <param name="request">Resources request.</param>
        /// <param name="progress">Optional progress reporter.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task whose result is the loaded asset, or null when it is missing.</returns>
        public static OnityTask<UnityEngine.Object> AsAssetOnityTask(
            this ResourceRequest request,
            IProgress<float> progress = null,
            CancellationToken cancellationToken = default)
        {
            return request.AsAssetOnityTask<UnityEngine.Object>(progress, cancellationToken);
        }

        /// <summary>
        /// Awaits a Resources load and returns the loaded asset as <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Expected asset type.</typeparam>
        /// <param name="request">Resources request.</param>
        /// <param name="progress">Optional progress reporter.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task whose result is the asset, or null when missing or of another type.</returns>
        public static OnityTask<T> AsAssetOnityTask<T>(
            this ResourceRequest request,
            IProgress<float> progress = null,
            CancellationToken cancellationToken = default)
            where T : UnityEngine.Object
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return OnityTask<T>.FromCanceled(cancellationToken);
            }

            if (request.isDone)
            {
                progress?.Report(1f);
                return OnityTask<T>.FromResult(request.asset as T);
            }

            return AwaitResourceAsync<T>(request, progress, cancellationToken);
        }

        /// <summary>
        /// Awaits an asset load from an asset bundle and returns the loaded asset.
        /// </summary>
        /// <param name="request">Asset bundle load request.</param>
        /// <param name="progress">Optional progress reporter.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task whose result is the loaded asset, or null when it is missing.</returns>
        public static OnityTask<UnityEngine.Object> AsAssetOnityTask(
            this AssetBundleRequest request,
            IProgress<float> progress = null,
            CancellationToken cancellationToken = default)
        {
            return request.AsAssetOnityTask<UnityEngine.Object>(progress, cancellationToken);
        }

        /// <summary>
        /// Awaits an asset load from an asset bundle and returns the asset as <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Expected asset type.</typeparam>
        /// <param name="request">Asset bundle load request.</param>
        /// <param name="progress">Optional progress reporter.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task whose result is the asset, or null when missing or of another type.</returns>
        public static OnityTask<T> AsAssetOnityTask<T>(
            this AssetBundleRequest request,
            IProgress<float> progress = null,
            CancellationToken cancellationToken = default)
            where T : UnityEngine.Object
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return OnityTask<T>.FromCanceled(cancellationToken);
            }

            if (request.isDone)
            {
                progress?.Report(1f);
                return OnityTask<T>.FromResult(request.asset as T);
            }

            return AwaitBundleAssetAsync<T>(request, progress, cancellationToken);
        }

        /// <summary>
        /// Awaits a sub-asset load from an asset bundle and returns all loaded assets.
        /// </summary>
        /// <param name="request">Asset bundle load request.</param>
        /// <param name="progress">Optional progress reporter.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task whose result is the loaded asset array.</returns>
        public static OnityTask<UnityEngine.Object[]> AwaitForAllAssets(
            this AssetBundleRequest request,
            IProgress<float> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return OnityTask<UnityEngine.Object[]>.FromCanceled(cancellationToken);
            }

            if (request.isDone)
            {
                progress?.Report(1f);
                return OnityTask<UnityEngine.Object[]>.FromResult(request.allAssets);
            }

            return AwaitAllAssetsAsync(request, progress, cancellationToken);
        }

        /// <summary>
        /// Awaits an asset bundle load and returns the loaded bundle.
        /// </summary>
        /// <param name="request">Asset bundle create request.</param>
        /// <param name="progress">Optional progress reporter.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task whose result is the bundle, or null when loading failed.</returns>
        public static OnityTask<AssetBundle> AsAssetBundleOnityTask(
            this AssetBundleCreateRequest request,
            IProgress<float> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return OnityTask<AssetBundle>.FromCanceled(cancellationToken);
            }

            if (request.isDone)
            {
                progress?.Report(1f);
                return OnityTask<AssetBundle>.FromResult(request.assetBundle);
            }

            return AwaitBundleAsync(request, progress, cancellationToken);
        }

        /// <summary>
        /// Awaits a web request and returns it when it succeeds.
        /// </summary>
        /// <param name="operation">Operation returned by <c>SendWebRequest</c>.</param>
        /// <param name="progress">Optional progress reporter.</param>
        /// <param name="cancellationToken">
        /// Cancellation token. Cancelling a pending request aborts it.
        /// </param>
        /// <returns>
        /// Task whose result is the web request. A connection, protocol or data-processing failure
        /// faults the task with <see cref="OnityUnityWebRequestException"/>.
        /// </returns>
        public static OnityTask<UnityWebRequest> AsWebRequestOnityTask(
            this UnityWebRequestAsyncOperation operation,
            IProgress<float> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return OnityTask<UnityWebRequest>.FromCanceled(cancellationToken);
            }

            if (operation.isDone)
            {
                progress?.Report(1f);
                UnityWebRequest finished = operation.webRequest;
                return IsFailed(finished)
                    ? OnityTask<UnityWebRequest>.FromException(new OnityUnityWebRequestException(finished))
                    : OnityTask<UnityWebRequest>.FromResult(finished);
            }

            return AwaitWebRequestAsync(operation, progress, cancellationToken);
        }

        /// <summary>
        /// Awaits a GPU readback request and returns it when it is done.
        /// </summary>
        /// <param name="request">Readback request.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// Task whose result is the finished request. The task faults with an
        /// <see cref="InvalidOperationException"/> when <see cref="AsyncGPUReadbackRequest.hasError"/>
        /// is observed, including when the request is already done (a failed request reports
        /// done and error together). Pinned UniTask skips the error check on its already-done path.
        /// </returns>
        public static OnityTask<AsyncGPUReadbackRequest> AsOnityTask(
            this AsyncGPUReadbackRequest request,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return OnityTask<AsyncGPUReadbackRequest>.FromCanceled(cancellationToken);
            }

            if (request.done)
            {
                return request.hasError
                    ? OnityTask<AsyncGPUReadbackRequest>.FromException(CreateReadbackException())
                    : OnityTask<AsyncGPUReadbackRequest>.FromResult(request);
            }

            return AwaitReadbackAsync(request, cancellationToken);
        }

        private static async OnityTask<T> AwaitResourceAsync<T>(
            ResourceRequest request,
            IProgress<float> progress,
            CancellationToken cancellationToken)
            where T : UnityEngine.Object
        {
            await request.AsOnityTask(ToCallback(progress), cancellationToken);
            return request.asset as T;
        }

        private static async OnityTask<T> AwaitBundleAssetAsync<T>(
            AssetBundleRequest request,
            IProgress<float> progress,
            CancellationToken cancellationToken)
            where T : UnityEngine.Object
        {
            await request.AsOnityTask(ToCallback(progress), cancellationToken);
            return request.asset as T;
        }

        private static async OnityTask<UnityEngine.Object[]> AwaitAllAssetsAsync(
            AssetBundleRequest request,
            IProgress<float> progress,
            CancellationToken cancellationToken)
        {
            await request.AsOnityTask(ToCallback(progress), cancellationToken);
            return request.allAssets;
        }

        private static async OnityTask<AssetBundle> AwaitBundleAsync(
            AssetBundleCreateRequest request,
            IProgress<float> progress,
            CancellationToken cancellationToken)
        {
            await request.AsOnityTask(ToCallback(progress), cancellationToken);
            return request.assetBundle;
        }

        private static async OnityTask<UnityWebRequest> AwaitWebRequestAsync(
            UnityWebRequestAsyncOperation operation,
            IProgress<float> progress,
            CancellationToken cancellationToken)
        {
            try
            {
                await operation.AsOnityTask(ToCallback(progress), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                if (operation.isDone == false)
                {
                    operation.webRequest.Abort();
                }

                throw;
            }

            UnityWebRequest request = operation.webRequest;
            if (IsFailed(request))
            {
                throw new OnityUnityWebRequestException(request);
            }

            return request;
        }

        private static async OnityTask<AsyncGPUReadbackRequest> AwaitReadbackAsync(
            AsyncGPUReadbackRequest request,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                if (request.hasError)
                {
                    throw CreateReadbackException();
                }

                if (request.done)
                {
                    return request;
                }

                await OnityTask.Yield(OnityPlayerLoopTiming.Update, cancellationToken);
            }
        }

        private static InvalidOperationException CreateReadbackException()
        {
            return new InvalidOperationException("AsyncGPUReadbackRequest.hasError = true");
        }

        private static Action<float> ToCallback(IProgress<float> progress)
        {
            return progress == null ? null : progress.Report;
        }

        private static bool IsFailed(UnityWebRequest request)
        {
            return request.result == UnityWebRequest.Result.ConnectionError
                || request.result == UnityWebRequest.Result.ProtocolError
                || request.result == UnityWebRequest.Result.DataProcessingError;
        }
    }
}
