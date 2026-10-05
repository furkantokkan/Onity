using System;
using System.Threading;
using System.Threading.Tasks;
using Onity.DI;
using Onity.Unity.Installers;
using Onity.Unity.SceneFlow;
using UnityEngine.SceneManagement;

namespace Onity.Tests.PlayMode
{
    /// <summary>
    /// Installer of the runtime-built scenes that <see cref="OnityScenePreloaderPlayModeTests" /> prepares. A scene
    /// that finds <see cref="PrepareData" /> for itself binds a <see cref="PreparedScene" />; any other scene builds
    /// normally. <see cref="BuildGate" /> holds the scene's asynchronous build until a test opens it.
    /// </summary>
    public sealed class OnityPreparedSceneFixtureInstaller : MonoInstaller
    {
        /// <summary>
        /// When set, every fixture scene's asynchronous build waits for it.
        /// </summary>
        public static TaskCompletionSource<bool> BuildGate;

        /// <summary>
        /// Counts fixture scenes that found no prepare data.
        /// </summary>
        public static int NormalBuildCount;

        /// <summary>
        /// Resets the shared fixture state.
        /// </summary>
        public static void ResetShared()
        {
            BuildGate = null;
            NormalBuildCount = 0;
        }

        /// <inheritdoc />
        public override void InstallBindings(OnityContainer container)
        {
            TaskCompletionSource<bool> gate = BuildGate;

            if (gate != null)
            {
                container.RegisterBuildCallbackAsync(_ => gate.Task);
            }

            if (OnityScenePreloader.TryGetPrepareData(gameObject.scene, out PrepareData prepareData) == false)
            {
                NormalBuildCount++;
                return;
            }

            PreparedScene preparedScene = new PreparedScene(gameObject.scene, prepareData);
            container.BindInstance<IOnityPreparedScene>(preparedScene);
            container.BindInstance(preparedScene);
        }

        /// <summary>
        /// Prepare data that names the content a scene builds.
        /// </summary>
        public sealed class PrepareData : IOnitySceneEnterData
        {
            public PrepareData(int id)
            {
                Id = id;
            }

            public int Id { get; }

            public override bool Equals(object obj)
            {
                return obj is PrepareData other && other.Id == Id;
            }

            public override int GetHashCode()
            {
                return Id;
            }
        }

        /// <summary>
        /// A start request; <see cref="Fail" /> makes the prepared scene's start throw.
        /// </summary>
        public sealed class StartData : IOnitySceneEnterData
        {
            public StartData(int id, bool fail = false)
            {
                Id = id;
                Fail = fail;
            }

            public int Id { get; }

            public bool Fail { get; }
        }

        /// <summary>
        /// The fixture's prepared scene; it records how the preloader started it.
        /// </summary>
        public sealed class PreparedScene : IOnityPreparedScene
        {
            private readonly Scene m_scene;

            public PreparedScene(Scene scene, PrepareData prepareData)
            {
                m_scene = scene;
                Data = prepareData;
            }

            public PrepareData Data { get; }

            public int ActivateCount { get; private set; }

            public IOnitySceneEnterData StartedWith { get; private set; }

            public bool WasActiveSceneOnActivate { get; private set; }

            /// <summary>
            /// Runs inside <see cref="Activate" />, so a test can observe the frame of the switch.
            /// </summary>
            public Action OnActivate { get; set; }

            public bool IsPrepared => ActivateCount == 0;

            public bool CanStart(IOnitySceneEnterData startData)
            {
                return startData is StartData start && start.Id == Data.Id;
            }

            public Task PrepareStartAsync(IOnitySceneEnterData startData, CancellationToken cancellationToken)
            {
                StartedWith = startData;

                return startData is StartData start && start.Fail
                    ? Task.FromException(new InvalidOperationException("fixture start failed"))
                    : Task.CompletedTask;
            }

            public void Activate()
            {
                ActivateCount++;
                WasActiveSceneOnActivate = SceneManager.GetActiveScene() == m_scene;
                OnActivate?.Invoke();
            }
        }
    }
}
