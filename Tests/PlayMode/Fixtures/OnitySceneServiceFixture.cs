using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Onity.DI;
using Onity.Unity.Async;
using Onity.Unity.Contexts;
using Onity.Unity.Installers;
using Onity.Unity.SceneFlow;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Onity.Tests.PlayMode
{
    /// <summary>
    /// What a fixture scene contains once it activates.
    /// </summary>
    internal enum OnitySceneFixtureKind
    {
        Context = 0,
        NoContext = 1,
        Loading = 2,
        LoadingWithoutInitiator = 3
    }

    /// <summary>
    /// The scene service's backend for the tests: every "load" creates a runtime scene, reports it before its root
    /// exists, and builds the root when the scene activates. A Single load activates the scene and unloads every
    /// other scene the fixture tracks, as Unity's Single mode would; the test runner's scene is never touched.
    /// </summary>
    internal sealed class OnitySceneServiceFixture : IOnitySceneBackend
    {
        private static readonly FieldInfo s_installersField = typeof(OnityContext).GetField(
            "m_installers",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo s_minimumVisibleField = typeof(OnityLoadingSceneInitiator).GetField(
            "m_minimumVisibleDurationSeconds",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private static int s_sceneSerial;

        private readonly Dictionary<string, OnitySceneFixtureKind> m_catalog =
            new Dictionary<string, OnitySceneFixtureKind>(StringComparer.Ordinal);

        public OnitySceneServiceFixture()
        {
            Scenes = new List<Scene>();
        }

        /// <summary>Every scene the fixture created or tracks, for replacement and cleanup.</summary>
        public List<Scene> Scenes { get; }

        /// <summary>When set, every load waits for it after its scene exists.</summary>
        public TaskCompletionSource<bool> LoadGate { get; set; }

        /// <summary>A key whose load throws before it creates its scene.</summary>
        public string FailingKey { get; set; }

        /// <summary>The progress callback of the last load, so a test can drive progress.</summary>
        public Action<float> LastProgress { get; private set; }

        public int LoadCount { get; private set; }

        public int ActivationCount { get; private set; }

        public void Add(string key, OnitySceneFixtureKind kind)
        {
            m_catalog[key] = kind;
        }

        /// <summary>Creates a tracked scene with a camera, outside any load.</summary>
        public Scene CreateTrackedScene(string baseName)
        {
            Scene scene = SceneManager.CreateScene($"{baseName}-{++s_sceneSerial}");
            Scenes.Add(scene);
            return scene;
        }

        public bool TryResolve(string scene, out string key, out string error)
        {
            if (scene != null && m_catalog.ContainsKey(scene))
            {
                key = scene;
                error = null;
                return true;
            }

            key = null;
            error = $"Scene '{scene}' is not in the fixture catalog.";
            return false;
        }

        public async Task<IOnitySceneLoad> LoadAsync(
            string key,
            LoadSceneMode mode,
            bool deferActivation,
            Action<Scene> onSceneCreated,
            Action<float> onProgress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadCount++;
            LastProgress = onProgress;

            if (string.Equals(key, FailingKey, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"fixture load of '{key}' failed");
            }

            Scene scene = CreateTrackedScene(key);
            onSceneCreated?.Invoke(scene);
            onProgress?.Invoke(0.5f);
            FixtureLoad load = new FixtureLoad(this, scene, m_catalog[key], mode);

            if (LoadGate != null)
            {
                Task gate = LoadGate.Task;

                if (deferActivation)
                {
                    await gate;
                }
                else
                {
                    await gate.AsOnityTask().AttachExternalCancellation(cancellationToken);
                }
            }

            if (deferActivation == false)
            {
                await load.ActivateAsync(onProgress);
            }

            return load;
        }

        public async Task UnloadAsync(Scene scene, CancellationToken cancellationToken)
        {
            if (scene.IsValid() == false || scene.isLoaded == false)
            {
                return;
            }

            AsyncOperation operation = SceneManager.UnloadSceneAsync(scene);

            while (operation != null && operation.isDone == false)
            {
                await OnityTask.NextFrame(cancellationToken);
            }
        }

        /// <summary>Builds a root with a SceneContext and the fixture installer in <paramref name="scene" />.</summary>
        public static SceneContext BuildContextRoot(Scene scene)
        {
            GameObject root = new GameObject("ContextRoot");
            root.SetActive(false);
            SceneManager.MoveGameObjectToScene(root, scene);
            OnitySceneServiceFixtureInstaller installer = root.AddComponent<OnitySceneServiceFixtureInstaller>();
            SceneContext context = root.AddComponent<SceneContext>();
            s_installersField.SetValue(context, new MonoInstaller[] { installer });
            root.SetActive(true);
            return context;
        }

        private void BuildRoot(Scene scene, OnitySceneFixtureKind kind)
        {
            switch (kind)
            {
                case OnitySceneFixtureKind.Context:
                    BuildContextRoot(scene);
                    return;

                case OnitySceneFixtureKind.Loading:
                    GameObject root = new GameObject("LoadingRoot");
                    root.SetActive(false);
                    SceneManager.MoveGameObjectToScene(root, scene);
                    OnityLoadingSceneInitiator initiator = root.AddComponent<OnityLoadingSceneInitiator>();
                    s_minimumVisibleField.SetValue(initiator, 0f);
                    root.SetActive(true);
                    return;

                default:
                    // An empty scene: no context, or a Loading scene that forgot its initiator.
                    return;
            }
        }

        private async Task ActivateSceneAsync(Scene scene, OnitySceneFixtureKind kind, LoadSceneMode mode)
        {
            ActivationCount++;
            BuildRoot(scene, kind);

            if (mode != LoadSceneMode.Single)
            {
                return;
            }

            SceneManager.SetActiveScene(scene);

            // Unity's Single mode replaces the scenes loaded when it activates; a scene whose load starts while
            // they unload (the next target behind a Loading scene) stays.
            List<Scene> replacedScenes = new List<Scene>(Scenes.Count);

            for (int index = 0; index < Scenes.Count; index++)
            {
                Scene replaced = Scenes[index];

                if (replaced != scene && replaced.IsValid() && replaced.isLoaded)
                {
                    replacedScenes.Add(replaced);
                }
            }

            for (int index = 0; index < replacedScenes.Count; index++)
            {
                AsyncOperation unload = SceneManager.UnloadSceneAsync(replacedScenes[index]);

                while (unload != null && unload.isDone == false)
                {
                    await OnityTask.NextFrame();
                }
            }
        }

        private sealed class FixtureLoad : IOnitySceneLoad
        {
            private readonly OnitySceneServiceFixture m_fixture;
            private readonly OnitySceneFixtureKind m_kind;
            private readonly LoadSceneMode m_mode;
            private bool m_isActivated;

            public FixtureLoad(
                OnitySceneServiceFixture fixture,
                Scene scene,
                OnitySceneFixtureKind kind,
                LoadSceneMode mode)
            {
                m_fixture = fixture;
                Scene = scene;
                m_kind = kind;
                m_mode = mode;
            }

            public Scene Scene { get; }

            public async Task ActivateAsync(Action<float> onProgress)
            {
                if (m_isActivated)
                {
                    return;
                }

                m_isActivated = true;
                await m_fixture.ActivateSceneAsync(Scene, m_kind, m_mode);
                onProgress?.Invoke(1f);
            }
        }
    }

    /// <summary>
    /// Installer of the fixture scenes: records the enter data each scene finds for itself while it installs, and
    /// can hold or fail the scene's asynchronous build.
    /// </summary>
    public sealed class OnitySceneServiceFixtureInstaller : MonoInstaller
    {
        /// <summary>When set, every fixture scene's asynchronous build waits for it.</summary>
        public static TaskCompletionSource<bool> BuildGate;

        /// <summary>When set, every fixture scene's asynchronous build fails with it.</summary>
        public static Exception BuildFailure;

        /// <summary>The enter data each installed scene read for its own handle; null when it found none.</summary>
        public static readonly List<IOnitySceneEnterData> ReceivedEnterData = new List<IOnitySceneEnterData>();

        /// <summary>The shared-slot enter data each installed scene consumed; null when it found none.</summary>
        public static readonly List<IOnitySceneEnterData> ConsumedSharedEnterData = new List<IOnitySceneEnterData>();

        /// <summary>Resets the shared fixture state.</summary>
        public static void ResetShared()
        {
            BuildGate = null;
            BuildFailure = null;
            ReceivedEnterData.Clear();
            ConsumedSharedEnterData.Clear();
        }

        /// <inheritdoc />
        public override void InstallBindings(OnityContainer container)
        {
            OnitySceneTransitionStore.TryGetEnterData(gameObject.scene, out IOnitySceneEnterData enterData);
            ReceivedEnterData.Add(enterData);
            OnitySceneTransitionStore.TryConsumeActiveEnterData(out IOnitySceneEnterData sharedData);
            ConsumedSharedEnterData.Add(sharedData);

            TaskCompletionSource<bool> gate = BuildGate;
            Exception failure = BuildFailure;

            if (gate != null)
            {
                container.RegisterBuildCallbackAsync(_ => gate.Task);
            }

            if (failure != null)
            {
                container.RegisterBuildCallbackAsync(_ => Task.FromException(failure));
            }
        }
    }

    /// <summary>
    /// A cover that records its calls in a shared log; its show can be held open.
    /// </summary>
    internal sealed class OnityRecordingSceneCover : IOnitySceneCover
    {
        private readonly string m_name;
        private readonly List<string> m_log;

        public OnityRecordingSceneCover(string name, List<string> log)
        {
            m_name = name;
            m_log = log;
        }

        public TaskCompletionSource<bool> ShowGate { get; set; }

        public bool IsVisible { get; private set; }

        public int ShowCount { get; private set; }

        public int HideCount { get; private set; }

        public Task ShowAsync(CancellationToken cancellationToken)
        {
            ShowCount++;
            m_log.Add($"{m_name} show");
            IsVisible = true;

            if (ShowGate == null)
            {
                return Task.CompletedTask;
            }

            return ShowGate.Task.AsOnityTask().AttachExternalCancellation(cancellationToken).AsTask();
        }

        public Task HideAsync(CancellationToken cancellationToken)
        {
            HideCount++;
            m_log.Add($"{m_name} hide");
            IsVisible = false;
            return Task.CompletedTask;
        }
    }
}
