using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Onity.DI;
using Onity.Unity.Async;
using Onity.Unity.Contexts;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Onity.Unity.SceneFlow
{
    /// <summary>
    /// Keeps a scene ready: it loads the scene additively behind the scene that shows, lets it build hidden, and
    /// starts it in one frame, so the scene opens with no load and no build frames while the scene it replaces hides
    /// and unloads behind it. One scene is kept prepared at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The scene being prepared reads its prepare data with <see cref="TryGetPrepareData{T}" /> for its own scene
    /// while it installs, builds hidden, and binds an <see cref="IOnityPreparedScene" /> in its
    /// <c>SceneContext</c>. A scene that finds no prepare data builds normally. Prepare data is kept per loading scene,
    /// never in the shared <see cref="OnitySceneTransitionStore" /> slot, so a scene-flow transition that runs at the
    /// same time cannot hand its enter data to the hidden scene, nor the other way round.
    /// </para>
    /// <para>
    /// Owner: a scope that outlives the scene that asks, usually the <c>ProjectContext</c> (bind it
    /// <c>AsSingle</c>). Stop points: a preparation ends when its scene waits prepared or has failed, a start when
    /// the replaced scene has unloaded, and <see cref="Dispose" /> cancels the lifetime token every wait observes. A
    /// <c>LoadSceneMode.Single</c> load unloads a prepared scene with every other scene; the next request notices.
    /// The preloader knows nothing about screen covers: to start a prepared scene behind one, call
    /// <see cref="TryStartAsync" /> inside <see cref="OnityCoveredSceneTransition" />'s scene change.
    /// </para>
    /// </remarks>
    public sealed class OnityScenePreloader : IDisposable
    {
        /// <summary>
        /// Starts loading a scene additively. It must call <paramref name="onSceneCreated" /> with the new scene
        /// before the scene wakes, and its task completes once the scene has loaded.
        /// </summary>
        /// <param name="sceneName">The scene to load.</param>
        /// <param name="onSceneCreated">Receives the loading scene, before any of its objects wake.</param>
        /// <param name="cancellationToken">Ends the wait for the load.</param>
        /// <returns>A task that completes when the scene has loaded.</returns>
        public delegate Task LoadStep(string sceneName, Action<Scene> onSceneCreated, CancellationToken cancellationToken);

        // Prepare data per scene handle for scenes a preloader is preparing or keeps prepared. Main thread only.
        private static readonly Dictionary<int, IOnitySceneEnterData> s_prepareData =
            new Dictionary<int, IOnitySceneEnterData>(4);

        private readonly LoadStep m_loadStep;
        private readonly CancellationTokenSource m_lifetime = new CancellationTokenSource();

        // The last preparation requested, chained after the ones before it, and its scene name.
        private Task m_preparation = Task.CompletedTask;
        private string m_requestedSceneName;
        private Scene m_loadingScene;
        private Scene m_preparedScene;
        private string m_preparedSceneName;
        private IOnitySceneEnterData m_preparedData;
        private IOnityPreparedScene m_prepared;
        private bool m_isDisposed;

        /// <summary>
        /// Initializes a preloader that loads scenes from the Build Settings with <see cref="LoadAdditiveAsync" />.
        /// The container uses this constructor.
        /// </summary>
        [Inject]
        public OnityScenePreloader()
            : this(LoadAdditiveAsync)
        {
        }

        /// <summary>
        /// Initializes a preloader with a custom load step, for example to load scenes from another source.
        /// </summary>
        /// <param name="loadStep">Loads a scene additively and reports it before it wakes.</param>
        /// <exception cref="ArgumentNullException"><paramref name="loadStep" /> is null.</exception>
        public OnityScenePreloader(LoadStep loadStep)
        {
            m_loadStep = loadStep ?? throw new ArgumentNullException(nameof(loadStep));
        }

        /// <summary>
        /// Gets whether a preparation runs.
        /// </summary>
        public bool IsPreparing => m_preparation.IsCompleted == false;

        /// <summary>
        /// Returns the prepare data a preloader set for <paramref name="scene" />. Call it from the scene's installer
        /// with its own scene (<c>gameObject.scene</c>) to learn whether the scene is being prepared.
        /// </summary>
        /// <typeparam name="TPrepareData">The expected prepare data type.</typeparam>
        /// <param name="scene">The scene being installed.</param>
        /// <param name="prepareData">The prepare data, or null when it returns false.</param>
        /// <returns>True while a preloader prepares or keeps <paramref name="scene" /> with data of that type.</returns>
        public static bool TryGetPrepareData<TPrepareData>(Scene scene, out TPrepareData prepareData)
            where TPrepareData : class, IOnitySceneEnterData
        {
            if (scene.IsValid()
                && s_prepareData.TryGetValue(scene.handle, out IOnitySceneEnterData data)
                && data is TPrepareData typedData)
            {
                prepareData = typedData;
                return true;
            }

            prepareData = null;
            return false;
        }

        /// <summary>
        /// The default <see cref="LoadStep" />: loads a scene from the Build Settings additively and reports it as soon
        /// as Unity lists it, before it wakes.
        /// </summary>
        /// <param name="sceneName">A scene in the Build Settings.</param>
        /// <param name="onSceneCreated">Receives the loading scene.</param>
        /// <param name="cancellationToken">Ends the wait; the load itself completes under Unity.</param>
        /// <returns>A task that completes when the scene has loaded.</returns>
        /// <exception cref="InvalidOperationException">Unity cannot load the scene.</exception>
        public static async Task LoadAdditiveAsync(
            string sceneName,
            Action<Scene> onSceneCreated,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                throw new ArgumentException("Scene name cannot be empty.", nameof(sceneName));
            }

            if (onSceneCreated == null)
            {
                throw new ArgumentNullException(nameof(onSceneCreated));
            }

            cancellationToken.ThrowIfCancellationRequested();
            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);

            if (operation == null)
            {
                throw new InvalidOperationException(
                    $"Scene '{sceneName}' could not be loaded; check that it is in the Build Settings.");
            }

            // Unity lists a scene as soon as its load is requested, before any of its objects wake.
            onSceneCreated(SceneManager.GetSceneAt(SceneManager.sceneCount - 1));

            while (operation.isDone == false)
            {
                await OnityTask.NextFrame(cancellationToken);
            }
        }

        /// <summary>
        /// Returns true when a loaded, hidden scene named <paramref name="sceneName" /> waits prepared.
        /// </summary>
        /// <param name="sceneName">The scene name.</param>
        /// <returns>True when that scene waits prepared.</returns>
        public bool IsPrepared(string sceneName)
        {
            return m_prepared != null
                && m_preparedScene.IsValid()
                && m_preparedScene.isLoaded
                && m_prepared.IsPrepared
                && string.Equals(m_preparedSceneName, sceneName, StringComparison.Ordinal);
        }

        /// <summary>
        /// Prepares <paramref name="sceneName" /> with <paramref name="prepareData" /> behind the current scene, after
        /// any running preparation, and releases a scene prepared for another request. A scene already prepared with
        /// data equal to <paramref name="prepareData" /> is kept.
        /// </summary>
        /// <param name="sceneName">The scene to prepare.</param>
        /// <param name="prepareData">The data the scene reads with <see cref="TryGetPrepareData{T}" />.</param>
        /// <param name="cancellationToken">Ends the caller's wait only; the preparation keeps running.</param>
        /// <returns>
        /// A task that completes when the scene waits prepared or the preparation failed. A failure is logged and
        /// leaves nothing prepared, so a start falls back to a normal load.
        /// </returns>
        /// <exception cref="ArgumentException"><paramref name="sceneName" /> is empty.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="prepareData" /> is null.</exception>
        public Task PrepareAsync(
            string sceneName,
            IOnitySceneEnterData prepareData,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                throw new ArgumentException("Scene name cannot be empty.", nameof(sceneName));
            }

            if (prepareData == null)
            {
                throw new ArgumentNullException(nameof(prepareData));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            if (m_isDisposed || IsPreparedFor(sceneName, prepareData))
            {
                return Task.CompletedTask;
            }

            // Always chained, even behind a running preparation of the same request: a Single scene load can unload
            // that one's scene mid-build. The chained run prepares only what is still missing when it starts.
            m_requestedSceneName = sceneName;
            m_preparation = RunPrepareAsync(sceneName, prepareData, m_preparation, m_lifetime.Token);
            return WaitForCaller(m_preparation, cancellationToken);
        }

        /// <summary>
        /// Starts the scene prepared as <paramref name="sceneName" /> when it can start for
        /// <paramref name="startData" />: <see cref="IOnityPreparedScene.PrepareStartAsync" /> runs while it is
        /// hidden, then in one frame it becomes the active scene, the active scene hides,
        /// <see cref="IOnityPreparedScene.Activate" /> runs, and the replaced scene unloads. A start for the scene
        /// still preparing waits for it.
        /// </summary>
        /// <param name="sceneName">The prepared scene to start.</param>
        /// <param name="startData">The start request, passed to the prepared scene; may be null.</param>
        /// <param name="cancellationToken">
        /// Ends the caller's wait only; a started switch completes, because the replaced scene is usually the caller's.
        /// </param>
        /// <returns>
        /// True once the prepared scene shows and the replaced scene has unloaded. False when no scene is prepared for
        /// the request, the prepared scene rejects it, or its start failed before it showed; the caller then loads the
        /// scene normally.
        /// </returns>
        /// <exception cref="ArgumentException"><paramref name="sceneName" /> is empty.</exception>
        public Task<bool> TryStartAsync(
            string sceneName,
            IOnitySceneEnterData startData,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                throw new ArgumentException("Scene name cannot be empty.", nameof(sceneName));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<bool>(cancellationToken);
            }

            return TryStartOwnedAsync(sceneName, startData, cancellationToken);
        }

        /// <summary>
        /// Cancels the lifetime token every preparation and start observes and ignores later requests. A prepared
        /// scene is not unloaded; its prepare data is dropped.
        /// </summary>
        public void Dispose()
        {
            if (m_isDisposed)
            {
                return;
            }

            m_isDisposed = true;
            RemovePrepareData(m_loadingScene);
            RemovePrepareData(m_preparedScene);
            ClearPrepared();
            m_lifetime.Cancel();
            m_lifetime.Dispose();
        }

        private async Task<bool> TryStartOwnedAsync(
            string sceneName,
            IOnitySceneEnterData startData,
            CancellationToken cancellationToken)
        {
            if (m_isDisposed)
            {
                return false;
            }

            if (IsPreparing && string.Equals(m_requestedSceneName, sceneName, StringComparison.Ordinal))
            {
                await WaitForCaller(m_preparation, cancellationToken);
            }

            if (m_isDisposed || IsPrepared(sceneName) == false || m_prepared.CanStart(startData) == false)
            {
                return false;
            }

            IOnityPreparedScene prepared = m_prepared;
            Scene scene = m_preparedScene;
            ClearPrepared();
            return await WaitForCaller(StartAsync(prepared, scene, startData, m_lifetime.Token), cancellationToken);
        }

        // Owner: this preloader, which stores the task. Stops: the scene waits prepared, the preparation failed, or
        // the lifetime token. It never faults, so the next preparation can await it.
        private async Task RunPrepareAsync(
            string sceneName,
            IOnitySceneEnterData prepareData,
            Task previous,
            CancellationToken token)
        {
            await previous;

            if (token.IsCancellationRequested || IsPreparedFor(sceneName, prepareData))
            {
                return;
            }

            Scene scene = default;

            try
            {
                await ReleaseAsync(token);
                await m_loadStep(
                    sceneName,
                    created =>
                    {
                        scene = created;
                        m_loadingScene = created;
                        SetPrepareData(created, prepareData);
                    },
                    token);

                IOnityPreparedScene prepared = await WaitForPreparedSceneAsync(scene, token);
                m_loadingScene = default;

                if (prepared != null)
                {
                    m_preparedScene = scene;
                    m_preparedSceneName = sceneName;
                    m_preparedData = prepareData;
                    m_prepared = prepared;
                    return;
                }

                // A Single scene load unloaded the scene mid-build: whoever asks next prepares again, so no warning.
                if (scene.IsValid() && scene.isLoaded)
                {
                    Debug.LogWarning(
                        $"Scene preload: '{sceneName}' bound no prepared {nameof(IOnityPreparedScene)}; it unloads and "
                        + "a start loads the scene normally.");
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // The owner scope ended; Dispose dropped the prepare data.
                return;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            m_loadingScene = default;
            RemovePrepareData(scene);

            try
            {
                // A scene that prepared nothing only costs memory, so it goes.
                await UnloadAsync(scene, token);
            }
            catch (OperationCanceledException)
            {
                // The owner scope ended during the unload, which Unity still completes.
            }
        }

        // Owner: this preloader. Stops: the replaced scene has unloaded, a failed start, or the lifetime token.
        private static async Task<bool> StartAsync(
            IOnityPreparedScene prepared,
            Scene scene,
            IOnitySceneEnterData startData,
            CancellationToken token)
        {
            Scene replaced = SceneManager.GetActiveScene();

            try
            {
                await prepared.PrepareStartAsync(startData, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // The scene never showed: the caller loads it normally instead.
                Debug.LogException(exception);
                RemovePrepareData(scene);
                await UnloadAsync(scene, token);
                return false;
            }

            RemovePrepareData(scene);

            if (scene.IsValid() == false || scene.isLoaded == false)
            {
                return false;
            }

            // One frame: the prepared scene becomes active and shows while the replaced one hides; it unloads after.
            SceneManager.SetActiveScene(scene);

            if (replaced != scene)
            {
                OnitySceneVisuals.Hide(replaced);
            }

            prepared.Activate();

            if (replaced != scene)
            {
                await UnloadAsync(replaced, token);
            }

            return true;
        }

        // The scene's prepared scene once its context has built, or null when the scene did not prepare.
        private static async Task<IOnityPreparedScene> WaitForPreparedSceneAsync(Scene scene, CancellationToken token)
        {
            if (OnitySceneScopes.TryFind(scene, out SceneContext context) == false)
            {
                return null;
            }

            // A failed or destroyed context completes its ready task too, so this wait always ends.
            while (context.ReadyTask.IsCompleted == false)
            {
                await OnityTask.NextFrame(token);
            }

            if (context == null || context.Container == null)
            {
                return null;
            }

            return context.Container.TryResolve(out IOnityPreparedScene prepared) && prepared.IsPrepared
                ? prepared
                : null;
        }

        // A scene prepared for another request is released before a new one loads.
        private async Task ReleaseAsync(CancellationToken token)
        {
            Scene scene = m_preparedScene;
            ClearPrepared();
            RemovePrepareData(scene);
            await UnloadAsync(scene, token);
        }

        // The unload completes under Unity; the token ends only this wait.
        private static async Task UnloadAsync(Scene scene, CancellationToken token)
        {
            if (scene.IsValid() == false || scene.isLoaded == false)
            {
                return;
            }

            AsyncOperation operation = SceneManager.UnloadSceneAsync(scene);

            while (operation != null && operation.isDone == false)
            {
                await OnityTask.NextFrame(token);
            }
        }

        private bool IsPreparedFor(string sceneName, IOnitySceneEnterData prepareData)
        {
            return IsPrepared(sceneName) && Equals(m_preparedData, prepareData);
        }

        private void ClearPrepared()
        {
            m_prepared = null;
            m_preparedScene = default;
            m_preparedSceneName = null;
            m_preparedData = null;
        }

        private static Task WaitForCaller(Task work, CancellationToken cancellationToken)
        {
            return cancellationToken.CanBeCanceled
                ? work.AsOnityTask().AttachExternalCancellation(cancellationToken).AsTask()
                : work;
        }

        private static Task<bool> WaitForCaller(Task<bool> work, CancellationToken cancellationToken)
        {
            return cancellationToken.CanBeCanceled
                ? work.AsOnityTask().AttachExternalCancellation(cancellationToken).AsTask()
                : work;
        }

        private static void SetPrepareData(Scene scene, IOnitySceneEnterData prepareData)
        {
            if (scene.IsValid())
            {
                s_prepareData[scene.handle] = prepareData;
            }
        }

        // Keyed by handle, so the entry of a scene that already unloaded can still be removed.
        private static void RemovePrepareData(Scene scene)
        {
            if (scene.handle != 0)
            {
                s_prepareData.Remove(scene.handle);
            }
        }

        // Clears entries left from an earlier Play session when domain reload is disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPrepareData()
        {
            s_prepareData.Clear();
        }
    }
}
