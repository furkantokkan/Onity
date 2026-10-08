using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Contexts;
using Onity.Unity.Installers;
using Onity.Unity.SceneFlow;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PrepareData = Onity.Tests.PlayMode.OnityPreparedSceneFixtureInstaller.PrepareData;
using PreparedScene = Onity.Tests.PlayMode.OnityPreparedSceneFixtureInstaller.PreparedScene;
using StartData = Onity.Tests.PlayMode.OnityPreparedSceneFixtureInstaller.StartData;

namespace Onity.Tests.PlayMode
{
    /// <summary>
    /// Verifies <see cref="OnityScenePreloader" />, <see cref="OnitySceneScopes" /> and
    /// <see cref="OnityActiveSceneReadiness" /> with scenes built at runtime: a scene prepares hidden behind the
    /// active one and starts in one frame, a rejected or failed start falls back, preparations survive an unloaded
    /// scene and a changed request, prepare data never crosses into the shared enter-data slot, and a prepared
    /// start composes with a covered transition with or without its cover.
    /// </summary>
    [TestFixture]
    public sealed class OnityScenePreloaderPlayModeTests
    {
        private const string k_sceneName = "PreloadGame";
        private const float k_timeoutSeconds = 10f;

        private static int s_sceneSerial;

        private readonly List<Scene> m_createdScenes = new List<Scene>();
        private Scene m_originalActiveScene;
        private Scene m_lastLoaded;
        private OnityScenePreloader m_preloader;

        [SetUp]
        public void SetUp()
        {
            OnityPreparedSceneFixtureInstaller.ResetShared();
            OnitySceneTransitionStore.Clear();
            m_createdScenes.Clear();
            m_lastLoaded = default;
            m_originalActiveScene = SceneManager.GetActiveScene();
            m_preloader = new OnityScenePreloader(LoadFixtureSceneAsync);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            m_preloader.Dispose();
            OnityPreparedSceneFixtureInstaller.BuildGate?.TrySetResult(true);
            OnityPreparedSceneFixtureInstaller.ResetShared();
            OnitySceneTransitionStore.Clear();

            if (m_originalActiveScene.IsValid() && m_originalActiveScene.isLoaded)
            {
                SceneManager.SetActiveScene(m_originalActiveScene);
            }

            for (int index = 0; index < m_createdScenes.Count; index++)
            {
                Scene scene = m_createdScenes[index];

                if (scene.IsValid() && scene.isLoaded)
                {
                    AsyncOperation unload = SceneManager.UnloadSceneAsync(scene);
                    yield return WaitUntil(() => unload == null || unload.isDone);
                }
            }
        }

        [UnityTest]
        public IEnumerator PrepareThenStart_SwapsInOneFrameAndUnloadsTheReplacedScene()
        {
            Scene home = CreateHomeScene(out Camera homeCamera, out Light homeLight, out AudioListener homeListener);
            SceneManager.SetActiveScene(home);

            Task prepare = m_preloader.PrepareAsync(k_sceneName, new PrepareData(3), CancellationToken.None);
            yield return WaitForTask(prepare);

            Scene gameScene = m_lastLoaded;
            Assert.That(m_preloader.IsPrepared(k_sceneName), Is.True);
            Assert.That(OnityScenePreloader.TryGetPrepareData(gameScene, out PrepareData data), Is.True);
            Assert.That(data.Id, Is.EqualTo(3));
            Assert.That(OnityPreparedSceneFixtureInstaller.NormalBuildCount, Is.EqualTo(0));
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(home), "preparing never changes the active scene");

            PreparedScene prepared = ResolvePrepared(gameScene);
            bool homeHiddenOnActivate = false;
            prepared.OnActivate = () => homeHiddenOnActivate =
                homeCamera.enabled == false && homeLight.enabled == false && homeListener.enabled == false;

            Task<bool> start = m_preloader.TryStartAsync(k_sceneName, new StartData(3), CancellationToken.None);
            yield return WaitForTask(start);

            Assert.That(start.Result, Is.True);
            Assert.That(prepared.ActivateCount, Is.EqualTo(1));
            Assert.That(prepared.WasActiveSceneOnActivate, Is.True, "active before Activate runs");
            Assert.That(homeHiddenOnActivate, Is.True, "the replaced scene hid in the same frame");
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(gameScene));
            Assert.That(home.IsValid() && home.isLoaded, Is.False, "the replaced scene unloaded");
            Assert.That(OnityScenePreloader.TryGetPrepareData(gameScene, out PrepareData _), Is.False,
                "a started scene keeps no prepare data");
            Assert.That(m_preloader.IsPrepared(k_sceneName), Is.False);
        }

        [UnityTest]
        public IEnumerator TryStart_WithoutAPreparedScene_ReturnsFalse()
        {
            Task<bool> start = m_preloader.TryStartAsync(k_sceneName, new StartData(1), CancellationToken.None);
            yield return WaitForTask(start);

            Assert.That(start.Result, Is.False);
        }

        [UnityTest]
        public IEnumerator TryStart_RejectedStartData_KeepsTheSceneAndReturnsFalse()
        {
            Scene home = CreateHomeScene(out _, out _, out _);
            SceneManager.SetActiveScene(home);
            yield return WaitForTask(m_preloader.PrepareAsync(k_sceneName, new PrepareData(1), CancellationToken.None));
            PreparedScene prepared = ResolvePrepared(m_lastLoaded);

            Task<bool> start = m_preloader.TryStartAsync(k_sceneName, new StartData(2), CancellationToken.None);
            yield return WaitForTask(start);

            Assert.That(start.Result, Is.False);
            Assert.That(prepared.ActivateCount, Is.EqualTo(0));
            Assert.That(m_preloader.IsPrepared(k_sceneName), Is.True, "another start may still match");
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(home));
        }

        [UnityTest]
        public IEnumerator TryStart_FailedStart_UnloadsTheSceneAndReturnsFalse()
        {
            Scene home = CreateHomeScene(out _, out _, out _);
            SceneManager.SetActiveScene(home);
            yield return WaitForTask(m_preloader.PrepareAsync(k_sceneName, new PrepareData(1), CancellationToken.None));
            Scene gameScene = m_lastLoaded;
            PreparedScene prepared = ResolvePrepared(gameScene);
            LogAssert.Expect(LogType.Exception, new Regex("fixture start failed"));

            Task<bool> start = m_preloader.TryStartAsync(k_sceneName, new StartData(1, true), CancellationToken.None);
            yield return WaitForTask(start);

            Assert.That(start.Result, Is.False);
            Assert.That(prepared.ActivateCount, Is.EqualTo(0));
            Assert.That(gameScene.IsValid() && gameScene.isLoaded, Is.False, "the failed scene unloaded");
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(home));
            Assert.That(m_preloader.IsPrepared(k_sceneName), Is.False);
        }

        [UnityTest]
        public IEnumerator Prepare_SceneUnloadedMidBuild_NeitherFaultsNorKeepsAStaleScene()
        {
            TaskCompletionSource<bool> firstGate = new TaskCompletionSource<bool>();
            OnityPreparedSceneFixtureInstaller.BuildGate = firstGate;
            Task prepare = m_preloader.PrepareAsync(k_sceneName, new PrepareData(1), CancellationToken.None);
            yield return WaitUntil(() => m_lastLoaded.IsValid() && m_lastLoaded.isLoaded);
            Scene first = m_lastLoaded;

            // What a LoadSceneMode.Single load does to a scene that is still building.
            AsyncOperation unload = SceneManager.UnloadSceneAsync(first);
            yield return WaitUntil(() => unload.isDone);
            yield return WaitForTask(prepare);

            Assert.That(prepare.Status, Is.EqualTo(TaskStatus.RanToCompletion), "the preparation never faults");
            Assert.That(m_preloader.IsPreparing, Is.False);
            Assert.That(m_preloader.IsPrepared(k_sceneName), Is.False);

            OnityPreparedSceneFixtureInstaller.BuildGate = null;
            firstGate.TrySetResult(true);
            Task again = m_preloader.PrepareAsync(k_sceneName, new PrepareData(1), CancellationToken.None);
            yield return WaitForTask(again);

            Assert.That(m_preloader.IsPrepared(k_sceneName), Is.True, "the next request prepares again");
            Assert.That(m_lastLoaded, Is.Not.EqualTo(first));
        }

        [UnityTest]
        public IEnumerator Prepare_OtherData_ReleasesTheScenePreparedBefore()
        {
            yield return WaitForTask(m_preloader.PrepareAsync(k_sceneName, new PrepareData(1), CancellationToken.None));
            Scene first = m_lastLoaded;

            yield return WaitForTask(m_preloader.PrepareAsync(k_sceneName, new PrepareData(2), CancellationToken.None));
            Scene second = m_lastLoaded;

            Assert.That(second, Is.Not.EqualTo(first));
            Assert.That(first.IsValid() && first.isLoaded, Is.False, "the earlier scene unloaded");
            Assert.That(m_preloader.IsPrepared(k_sceneName), Is.True);
            Assert.That(ResolvePrepared(second).Data.Id, Is.EqualTo(2));

            Task same = m_preloader.PrepareAsync(k_sceneName, new PrepareData(2), CancellationToken.None);
            Assert.That(same.Status, Is.EqualTo(TaskStatus.RanToCompletion), "equal data keeps the prepared scene");
            Assert.That(m_lastLoaded, Is.EqualTo(second));
        }

        [UnityTest]
        public IEnumerator Prepare_KeepsItsDataOutOfTheSharedEnterDataSlot()
        {
            StartData launch = new StartData(9);
            OnitySceneTransitionStore.SetActiveEnterData(launch);
            TaskCompletionSource<bool> gate = new TaskCompletionSource<bool>();
            OnityPreparedSceneFixtureInstaller.BuildGate = gate;

            Task prepare = m_preloader.PrepareAsync(k_sceneName, new PrepareData(5), CancellationToken.None);
            yield return WaitUntil(() => m_lastLoaded.IsValid() && m_lastLoaded.isLoaded);
            Scene preparing = m_lastLoaded;

            // A scene that loads while the preparation runs, as a scene-flow target with the same base name would.
            Scene concurrent = CreateTrackedScene($"{k_sceneName}-concurrent");
            Assert.That(OnityScenePreloader.TryGetPrepareData(concurrent, out PrepareData _), Is.False,
                "only the scene being prepared gets prepare data");
            Assert.That(OnityScenePreloader.TryGetPrepareData(preparing, out PrepareData data), Is.True);
            Assert.That(data.Id, Is.EqualTo(5));

            gate.TrySetResult(true);
            yield return WaitForTask(prepare);

            Assert.That(m_preloader.IsPrepared(k_sceneName), Is.True);
            Assert.That(OnitySceneTransitionStore.TryConsumeActiveEnterData(out StartData consumed), Is.True,
                "the preloader never writes the shared slot");
            Assert.That(consumed, Is.SameAs(launch));
        }

        [UnityTest]
        public IEnumerator TryStart_InsideACoveredTransition_StartsWithAndWithoutTheCover()
        {
            Scene home = CreateHomeScene(out _, out _, out _);
            SceneManager.SetActiveScene(home);
            RecordingCover cover = new RecordingCover();
            using OnityCoveredSceneTransition transition = new OnityCoveredSceneTransition(
                cover,
                new OnityActiveSceneReadiness(new OnitySceneRevealPolicy(0, 5f)));
            int fallbacks = 0;

            Func<CancellationToken, Task> StartPrepared(int id)
            {
                return async token =>
                {
                    if (await m_preloader.TryStartAsync(k_sceneName, new StartData(id), token) == false)
                    {
                        fallbacks++;
                    }
                };
            }

            yield return WaitForTask(m_preloader.PrepareAsync(k_sceneName, new PrepareData(1), CancellationToken.None));
            Scene firstGame = m_lastLoaded;
            Task<bool> covered = transition.RunAsync(StartPrepared(1), true, CancellationToken.None);
            yield return WaitForTask(covered);

            Assert.That(covered.Result, Is.True);
            Assert.That(fallbacks, Is.EqualTo(0), "the prepared scene started behind the cover");
            Assert.That(cover.ShowCount, Is.EqualTo(1));
            Assert.That(cover.HideCount, Is.EqualTo(1));
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(firstGame));
            Assert.That(ResolvePrepared(firstGame).ActivateCount, Is.EqualTo(1));

            yield return WaitForTask(m_preloader.PrepareAsync(k_sceneName, new PrepareData(2), CancellationToken.None));
            Scene secondGame = m_lastLoaded;
            Task<bool> uncovered = transition.RunAsync(StartPrepared(2), false, CancellationToken.None);
            yield return WaitForTask(uncovered);

            Assert.That(uncovered.Result, Is.True);
            Assert.That(fallbacks, Is.EqualTo(0), "the prepared scene started with no cover");
            Assert.That(cover.ShowCount, Is.EqualTo(1), "no cover this time");
            Assert.That(cover.HideCount, Is.EqualTo(1));
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(secondGame));
        }

        [UnityTest]
        public IEnumerator SceneScopes_TryFind_ReturnsTheScenesContextOnly()
        {
            Scene withContext = CreateTrackedScene("ScopesWithContext");
            SceneContext context = BuildFixtureRoot(withContext);
            Scene withoutContext = CreateTrackedScene("ScopesWithoutContext");
            yield return null;

            Assert.That(OnitySceneScopes.TryFind(withContext, out SceneContext found), Is.True);
            Assert.That(found, Is.SameAs(context));
            Assert.That(OnitySceneScopes.TryFind(withoutContext, out SceneContext none), Is.False);
            Assert.That(none, Is.Null);
            Assert.That(OnitySceneScopes.TryFind(default, out _), Is.False);

            AsyncOperation unload = SceneManager.UnloadSceneAsync(withContext);
            yield return WaitUntil(() => unload.isDone);
            Assert.That(OnitySceneScopes.TryFind(withContext, out _), Is.False, "an unloaded scene has no context");
        }

        [UnityTest]
        public IEnumerator ActiveSceneReadiness_WaitsForTheBuildThenTheSettleFrames()
        {
            TaskCompletionSource<bool> gate = new TaskCompletionSource<bool>();
            OnityPreparedSceneFixtureInstaller.BuildGate = gate;
            Scene scene = CreateTrackedScene("ReadinessScene");
            BuildFixtureRoot(scene);
            SceneManager.SetActiveScene(scene);
            OnityActiveSceneReadiness readiness = new OnityActiveSceneReadiness(new OnitySceneRevealPolicy(2, 10f));

            Task wait = readiness.WaitUntilReadyAsync(CancellationToken.None);

            for (int frame = 0; frame < 3; frame++)
            {
                yield return null;
            }

            Assert.That(wait.IsCompleted, Is.False, "the build has not finished");

            gate.TrySetResult(true);
            int framesAfterOpen = 0;

            while (wait.IsCompleted == false && framesAfterOpen < 30)
            {
                yield return null;
                framesAfterOpen++;
            }

            Assert.That(wait.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(framesAfterOpen, Is.GreaterThanOrEqualTo(2), "two settle frames after the build");
        }

        [UnityTest]
        public IEnumerator ActiveSceneReadiness_BuildNeverEnds_RevealsAfterTheMaximumUnscaledWait()
        {
            OnityPreparedSceneFixtureInstaller.BuildGate = new TaskCompletionSource<bool>();
            Scene scene = CreateTrackedScene("NeverReadyScene");
            BuildFixtureRoot(scene);
            SceneManager.SetActiveScene(scene);
            OnityActiveSceneReadiness readiness = new OnityActiveSceneReadiness(new OnitySceneRevealPolicy(1, 0.3f));
            float originalTimeScale = Time.timeScale;
            Time.timeScale = 0f;

            try
            {
                float start = Time.realtimeSinceStartup;
                Task wait = readiness.WaitUntilReadyAsync(CancellationToken.None);
                yield return WaitForTask(wait);

                Assert.That(Time.realtimeSinceStartup - start, Is.GreaterThanOrEqualTo(0.3f));
            }
            finally
            {
                Time.timeScale = originalTimeScale;
            }
        }

        // The preloader's load step for the tests: a runtime scene, reported before its fixture objects wake.
        private Task LoadFixtureSceneAsync(string sceneName, Action<Scene> onSceneCreated, CancellationToken token)
        {
            Scene scene = CreateTrackedScene(sceneName);
            onSceneCreated(scene);
            BuildFixtureRoot(scene);
            m_lastLoaded = scene;
            return Task.CompletedTask;
        }

        private Scene CreateTrackedScene(string baseName)
        {
            Scene scene = SceneManager.CreateScene($"{baseName}-{++s_sceneSerial}");
            m_createdScenes.Add(scene);
            return scene;
        }

        private Scene CreateHomeScene(out Camera camera, out Light light, out AudioListener listener)
        {
            Scene home = CreateTrackedScene("PreloadHome");
            GameObject root = new GameObject("HomeRoot");
            SceneManager.MoveGameObjectToScene(root, home);
            camera = root.AddComponent<Camera>();
            light = root.AddComponent<Light>();
            listener = root.AddComponent<AudioListener>();
            return home;
        }

        private static SceneContext BuildFixtureRoot(Scene scene)
        {
            GameObject root = new GameObject("FixtureRoot");
            root.SetActive(false);
            SceneManager.MoveGameObjectToScene(root, scene);
            OnityPreparedSceneFixtureInstaller installer = root.AddComponent<OnityPreparedSceneFixtureInstaller>();
            SceneContext context = root.AddComponent<SceneContext>();
            FieldInfo installersField = typeof(OnityContext).GetField(
                "m_installers",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(installersField, Is.Not.Null);
            installersField.SetValue(context, new MonoInstaller[] { installer });
            root.SetActive(true);
            return context;
        }

        private static PreparedScene ResolvePrepared(Scene scene)
        {
            Assert.That(OnitySceneScopes.TryFind(scene, out SceneContext context), Is.True);
            Assert.That(context.Container.TryResolve(out PreparedScene prepared), Is.True);
            return prepared;
        }

        private static IEnumerator WaitForTask(Task task)
        {
            yield return WaitUntil(() => task.IsCompleted);
            Assert.That(task.IsFaulted, Is.False, task.Exception?.ToString());
        }

        private static IEnumerator WaitUntil(Func<bool> condition)
        {
            float deadline = Time.realtimeSinceStartup + k_timeoutSeconds;

            while (condition() == false && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(condition(), Is.True, "timed out");
        }

        private sealed class RecordingCover : IOnitySceneCover
        {
            public bool IsVisible { get; private set; }

            public int ShowCount { get; private set; }

            public int HideCount { get; private set; }

            public Task ShowAsync(CancellationToken cancellationToken)
            {
                ShowCount++;
                IsVisible = true;
                return Task.CompletedTask;
            }

            public Task HideAsync(CancellationToken cancellationToken)
            {
                HideCount++;
                IsVisible = false;
                return Task.CompletedTask;
            }
        }
    }
}
