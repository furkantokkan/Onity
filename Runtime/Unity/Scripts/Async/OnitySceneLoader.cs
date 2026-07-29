using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Lightweight scene loading helpers for gameplay and bootstrap flows.
    /// </summary>
    public static class OnitySceneLoader
    {
        /// <summary>
        /// Loads a scene in single mode and awaits completion.
        /// </summary>
        /// <param name="sceneName">Scene name from Build Settings.</param>
        /// <param name="onProgress">Optional normalized progress callback (0..1).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static Task LoadSingleAsync(
            string sceneName,
            Action<float> onProgress = null,
            CancellationToken cancellationToken = default)
        {
            return OnityTaskTracker.Track(
                LoadAsync(sceneName, LoadSceneMode.Single, true, onProgress, cancellationToken),
                "OnitySceneLoader.LoadSingleAsync");
        }

        /// <summary>
        /// Loads a scene in additive mode and awaits completion.
        /// </summary>
        /// <param name="sceneName">Scene name from Build Settings.</param>
        /// <param name="onProgress">Optional normalized progress callback (0..1).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static Task LoadAdditiveAsync(
            string sceneName,
            Action<float> onProgress = null,
            CancellationToken cancellationToken = default)
        {
            return OnityTaskTracker.Track(
                LoadAsync(sceneName, LoadSceneMode.Additive, true, onProgress, cancellationToken),
                "OnitySceneLoader.LoadAdditiveAsync");
        }

        /// <summary>
        /// Loads a scene and optionally delays scene activation.
        /// </summary>
        /// <param name="sceneName">Scene name from Build Settings.</param>
        /// <param name="loadSceneMode">Load mode.</param>
        /// <param name="activateOnLoad">Scene activation flag.</param>
        /// <param name="onProgress">Optional normalized progress callback (0..1).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// Returned async operation.
        /// When <paramref name="activateOnLoad"/> is false, returns once scene reaches activation-ready state.
        /// </returns>
        public static Task<AsyncOperation> LoadAsync(
            string sceneName,
            LoadSceneMode loadSceneMode = LoadSceneMode.Single,
            bool activateOnLoad = true,
            Action<float> onProgress = null,
            CancellationToken cancellationToken = default)
        {
            return OnityTaskTracker.Track(
                LoadAsyncInternal(sceneName, loadSceneMode, activateOnLoad, onProgress, cancellationToken),
                "OnitySceneLoader.LoadAsync");
        }

        private static async Task<AsyncOperation> LoadAsyncInternal(
            string sceneName,
            LoadSceneMode loadSceneMode,
            bool activateOnLoad,
            Action<float> onProgress,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                throw new ArgumentException("Scene name cannot be empty.", nameof(sceneName));
            }

            cancellationToken.ThrowIfCancellationRequested();

            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, loadSceneMode);

            if (operation == null)
            {
                return null;
            }

            operation.allowSceneActivation = activateOnLoad;
            onProgress?.Invoke(0f);

            while (operation.isDone == false)
            {
                cancellationToken.ThrowIfCancellationRequested();
                onProgress?.Invoke(NormalizeLoadProgress(operation.progress, activateOnLoad));

                if (activateOnLoad == false && operation.progress >= 0.9f)
                {
                    onProgress?.Invoke(1f);
                    return operation;
                }

                await Task.Yield();
            }

            onProgress?.Invoke(1f);
            return operation;
        }

        /// <summary>
        /// Activates a previously prepared load operation and awaits completion.
        /// </summary>
        /// <param name="operation">Prepared scene load operation.</param>
        /// <param name="onProgress">Optional normalized progress callback (0..1).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static Task ActivateAsync(
            AsyncOperation operation,
            Action<float> onProgress = null,
            CancellationToken cancellationToken = default)
        {
            return OnityTaskTracker.Track(
                ActivateInternalAsync(operation, onProgress, cancellationToken),
                "OnitySceneLoader.ActivateAsync");
        }

        private static async Task ActivateInternalAsync(
            AsyncOperation operation,
            Action<float> onProgress,
            CancellationToken cancellationToken)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            operation.allowSceneActivation = true;

            while (operation.isDone == false)
            {
                cancellationToken.ThrowIfCancellationRequested();
                onProgress?.Invoke(Mathf.Clamp01(operation.progress));
                await Task.Yield();
            }

            onProgress?.Invoke(1f);
        }

        /// <summary>
        /// Unloads a scene and awaits completion.
        /// </summary>
        /// <param name="sceneName">Scene name from Build Settings.</param>
        /// <param name="onProgress">Optional normalized progress callback (0..1).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static Task UnloadAsync(
            string sceneName,
            Action<float> onProgress = null,
            CancellationToken cancellationToken = default)
        {
            return OnityTaskTracker.Track(
                UnloadInternalAsync(sceneName, onProgress, cancellationToken),
                "OnitySceneLoader.UnloadAsync");
        }

        private static async Task UnloadInternalAsync(
            string sceneName,
            Action<float> onProgress,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                throw new ArgumentException("Scene name cannot be empty.", nameof(sceneName));
            }

            cancellationToken.ThrowIfCancellationRequested();

            AsyncOperation operation = SceneManager.UnloadSceneAsync(sceneName);

            if (operation == null)
            {
                return;
            }

            onProgress?.Invoke(0f);

            while (operation.isDone == false)
            {
                cancellationToken.ThrowIfCancellationRequested();
                onProgress?.Invoke(Mathf.Clamp01(operation.progress));
                await Task.Yield();
            }

            onProgress?.Invoke(1f);
        }

        private static float NormalizeLoadProgress(float progress, bool activateOnLoad)
        {
            if (activateOnLoad == false)
            {
                return Mathf.Clamp01(progress / 0.9f);
            }

            return Mathf.Clamp01(progress);
        }
    }
}
