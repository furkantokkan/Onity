using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Onity.Unity.SceneFlow
{
    /// <summary>
    /// Where <see cref="OnitySceneService" /> loads and unloads scenes. The default is
    /// <see cref="OnitySceneManagerBackend" />; the package's tests load runtime-built scenes instead.
    /// </summary>
    internal interface IOnitySceneBackend
    {
        /// <summary>
        /// Resolves a requested scene name or path to the key <see cref="LoadAsync" /> takes.
        /// </summary>
        /// <param name="scene">The requested scene.</param>
        /// <param name="key">The exact scene to load.</param>
        /// <param name="error">Why the scene cannot load, when it returns false.</param>
        /// <returns>True when exactly one loadable scene matches.</returns>
        bool TryResolve(string scene, out string key, out string error);

        /// <summary>
        /// Starts loading <paramref name="key" /> and reports the new scene before any of its objects wake. With
        /// <paramref name="deferActivation" /> the returned load completes once the scene can activate, and its
        /// caller must activate it; a started deferred load is never abandoned on cancellation.
        /// </summary>
        /// <param name="key">A key from <see cref="TryResolve" />.</param>
        /// <param name="mode">The load mode.</param>
        /// <param name="deferActivation">True to hold the scene until <see cref="IOnitySceneLoad.ActivateAsync" />.</param>
        /// <param name="onSceneCreated">Receives the loading scene before it wakes.</param>
        /// <param name="onProgress">Receives normalized progress; may be null.</param>
        /// <param name="cancellationToken">Ends the wait before Unity starts the load, or an immediate load's wait.</param>
        /// <returns>The load.</returns>
        Task<IOnitySceneLoad> LoadAsync(
            string key,
            LoadSceneMode mode,
            bool deferActivation,
            Action<Scene> onSceneCreated,
            Action<float> onProgress,
            CancellationToken cancellationToken);

        /// <summary>
        /// Unloads exactly <paramref name="scene" />.
        /// </summary>
        /// <param name="scene">A loaded scene.</param>
        /// <param name="cancellationToken">Ends the wait; Unity still completes the unload.</param>
        /// <returns>A task that completes when the scene has unloaded.</returns>
        Task UnloadAsync(Scene scene, CancellationToken cancellationToken);
    }

    /// <summary>
    /// One scene load started by an <see cref="IOnitySceneBackend" />.
    /// </summary>
    internal interface IOnitySceneLoad
    {
        /// <summary>
        /// Gets the loaded scene.
        /// </summary>
        Scene Scene { get; }

        /// <summary>
        /// Lets a deferred scene activate and completes once it has; completes at once for an activated scene. It
        /// observes no token: Unity holds later scene operations behind an activation that never happens.
        /// </summary>
        /// <param name="onProgress">Receives normalized activation progress; may be null.</param>
        /// <returns>A task that completes when the scene is active.</returns>
        Task ActivateAsync(Action<float> onProgress);
    }

    /// <summary>
    /// Loads scenes from the Build Settings through <see cref="SceneManager" />, by their exact path, so two
    /// scenes that share a name are never confused.
    /// </summary>
    internal sealed class OnitySceneManagerBackend : IOnitySceneBackend
    {
        private const string k_sceneExtension = ".unity";

        // Unity holds a deferred load at this progress until its activation is allowed.
        private const float k_activationReadyProgress = 0.9f;

        /// <inheritdoc />
        public bool TryResolve(string scene, out string key, out string error)
        {
            int count = SceneManager.sceneCountInBuildSettings;
            List<string> buildScenePaths = new List<string>(count);

            for (int index = 0; index < count; index++)
            {
                buildScenePaths.Add(SceneUtility.GetScenePathByBuildIndex(index));
            }

            return TryResolveBuildScene(scene, buildScenePaths, out key, out error);
        }

        /// <summary>
        /// Matches a requested scene against the Build Settings scene paths: a name (case-insensitive, as Unity
        /// matches it) must match exactly one scene, and a path, with or without its extension and Assets prefix,
        /// must match the end of exactly one path.
        /// </summary>
        /// <param name="scene">The requested name or path.</param>
        /// <param name="buildScenePaths">The Build Settings scene paths.</param>
        /// <param name="key">The matched scene path.</param>
        /// <param name="error">Why nothing or several scenes match.</param>
        /// <returns>True when exactly one scene matches.</returns>
        internal static bool TryResolveBuildScene(
            string scene,
            IReadOnlyList<string> buildScenePaths,
            out string key,
            out string error)
        {
            key = null;
            string requested = scene?.Trim();

            if (string.IsNullOrEmpty(requested))
            {
                error = "The request names no scene.";
                return false;
            }

            if (requested.EndsWith(k_sceneExtension, StringComparison.OrdinalIgnoreCase))
            {
                requested = requested.Substring(0, requested.Length - k_sceneExtension.Length);
            }

            bool isPath = requested.IndexOf('/') >= 0;
            int matches = 0;

            for (int index = 0; index < buildScenePaths.Count; index++)
            {
                string path = buildScenePaths[index];

                if (string.IsNullOrEmpty(path) || IsMatch(path, requested, isPath) == false)
                {
                    continue;
                }

                matches++;
                key = path;
            }

            if (matches == 1)
            {
                error = null;
                return true;
            }

            key = null;
            error = matches == 0
                ? $"Scene '{scene}' is not in the Build Settings."
                : $"Scene '{scene}' matches {matches} scenes in the Build Settings; request it by path.";
            return false;
        }

        /// <inheritdoc />
        public async Task<IOnitySceneLoad> LoadAsync(
            string key,
            LoadSceneMode mode,
            bool deferActivation,
            Action<Scene> onSceneCreated,
            Action<float> onProgress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AsyncOperation operation = SceneManager.LoadSceneAsync(key, mode);

            if (operation == null)
            {
                throw new InvalidOperationException($"Scene '{key}' could not be loaded.");
            }

            operation.allowSceneActivation = deferActivation == false;

            // Unity lists a scene as soon as its load is requested, before any of its objects wake.
            Scene scene = SceneManager.GetSceneAt(SceneManager.sceneCount - 1);
            onSceneCreated?.Invoke(scene);
            SceneManagerLoad load = new SceneManagerLoad(operation, scene, deferActivation == false);

            if (deferActivation)
            {
                // A started deferred load returns even when canceled: its owner must still activate it.
                while (operation.isDone == false && operation.progress < k_activationReadyProgress)
                {
                    onProgress?.Invoke(Mathf.Clamp01(operation.progress / k_activationReadyProgress));
                    await OnityTask.NextFrame();
                }

                onProgress?.Invoke(1f);
                return load;
            }

            while (operation.isDone == false)
            {
                onProgress?.Invoke(Mathf.Clamp01(operation.progress));
                await OnityTask.NextFrame(cancellationToken);
            }

            onProgress?.Invoke(1f);
            return load;
        }

        /// <inheritdoc />
        public async Task UnloadAsync(Scene scene, CancellationToken cancellationToken)
        {
            if (scene.IsValid() == false || scene.isLoaded == false)
            {
                return;
            }

            AsyncOperation operation = SceneManager.UnloadSceneAsync(scene);

            if (operation == null)
            {
                throw new InvalidOperationException($"Scene '{scene.name}' could not be unloaded.");
            }

            while (operation.isDone == false)
            {
                await OnityTask.NextFrame(cancellationToken);
            }
        }

        private static bool IsMatch(string path, string requested, bool isPath)
        {
            string pathWithoutExtension = path.EndsWith(k_sceneExtension, StringComparison.OrdinalIgnoreCase)
                ? path.Substring(0, path.Length - k_sceneExtension.Length)
                : path;

            if (isPath)
            {
                // A whole path, or its end after a folder separator ("Levels/Menu" matches "Assets/Levels/Menu").
                return pathWithoutExtension.EndsWith(requested, StringComparison.OrdinalIgnoreCase)
                    && (pathWithoutExtension.Length == requested.Length
                        || pathWithoutExtension[pathWithoutExtension.Length - requested.Length - 1] == '/');
            }

            int nameStart = pathWithoutExtension.LastIndexOf('/') + 1;
            return pathWithoutExtension.Length - nameStart == requested.Length
                && string.Compare(
                    pathWithoutExtension,
                    nameStart,
                    requested,
                    0,
                    requested.Length,
                    StringComparison.OrdinalIgnoreCase) == 0;
        }

        private sealed class SceneManagerLoad : IOnitySceneLoad
        {
            private readonly AsyncOperation m_operation;
            private bool m_isActivated;

            public SceneManagerLoad(AsyncOperation operation, Scene scene, bool isActivated)
            {
                m_operation = operation;
                Scene = scene;
                m_isActivated = isActivated;
            }

            public Scene Scene { get; }

            public async Task ActivateAsync(Action<float> onProgress)
            {
                if (m_isActivated)
                {
                    return;
                }

                m_isActivated = true;
                m_operation.allowSceneActivation = true;

                while (m_operation.isDone == false)
                {
                    onProgress?.Invoke(Mathf.Clamp01(m_operation.progress));
                    await OnityTask.NextFrame();
                }

                onProgress?.Invoke(1f);
            }
        }
    }
}
