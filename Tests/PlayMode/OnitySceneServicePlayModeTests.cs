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
    /// Verifies <see cref="OnitySceneService" /> with scenes built at runtime through its internal backend: a load
    /// completes only at its exact, ready destination; additive loads and unloads keep or remove exact instances;
    /// covers, busy requests, caller cancellation, disposal, readiness failures, enter-data isolation, prepared
    /// scenes, external scene changes and state publication follow the service contract.
    /// </summary>
    [TestFixture]
    public sealed class OnitySceneServicePlayModeTests
    {
        private const float k_timeoutSeconds = 10f;

        private readonly List<string> m_log = new List<string>();
        private OnitySceneServiceFixture m_fixture;
        private OnityRecordingSceneCover m_cover;
        private OnitySceneService m_service;
        private Scene m_originalActiveScene;
        private Scene m_home;

        [SetUp]
        public void SetUp()
        {
            OnitySceneServiceFixtureInstaller.ResetShared();
            OnityPreparedSceneFixtureInstaller.ResetShared();
            OnitySceneTransitionStore.Clear();
            m_log.Clear();
            m_originalActiveScene = SceneManager.GetActiveScene();
            m_fixture = new OnitySceneServiceFixture();
            m_fixture.Add("Game", OnitySceneFixtureKind.Context);
            m_fixture.Add("Plain", OnitySceneFixtureKind.NoContext);
            m_home = m_fixture.CreateTrackedScene("Home");
            SceneManager.SetActiveScene(m_home);
            m_cover = new OnityRecordingSceneCover("default", m_log);
            m_service = CreateService(new OnitySceneRevealPolicy(0, k_timeoutSeconds));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            m_service.Dispose();
            m_fixture.LoadGate?.TrySetResult(true);
            OnitySceneServiceFixtureInstaller.BuildGate?.TrySetResult(true);
            OnityPreparedSceneFixtureInstaller.BuildGate?.TrySetResult(true);
            OnitySceneServiceFixtureInstaller.ResetShared();
            OnityPreparedSceneFixtureInstaller.ResetShared();
            OnitySceneTransitionStore.Clear();
            yield return null;

            if (m_originalActiveScene.IsValid() && m_originalActiveScene.isLoaded)
            {
                SceneManager.SetActiveScene(m_originalActiveScene);
            }

            for (int index = 0; index < m_fixture.Scenes.Count; index++)
            {
                Scene scene = m_fixture.Scenes[index];

                if (scene.IsValid() && scene.isLoaded)
                {
                    AsyncOperation unload = SceneManager.UnloadSceneAsync(scene);
                    yield return WaitUntil(() => unload == null || unload.isDone);
                }
            }
        }

        [UnityTest]
        public IEnumerator Single_CompletesAtTheReadyTargetAndUnloadsTheReplacedScenes()
        {
            TaskCompletionSource<bool> build = new TaskCompletionSource<bool>();
            OnitySceneServiceFixtureInstaller.BuildGate = build;
            SceneEnterData data = new SceneEnterData(1);

            Task<OnitySceneResult> load = m_service.LoadAsync(OnitySceneRequest.Single("Game", data)).AsTask();
            yield return WaitUntil(() => OnitySceneServiceFixtureInstaller.ReceivedEnterData.Count == 1);
            Scene game = SceneManager.GetActiveScene();

            Assert.That(game.name, Does.StartWith("Game-"));
            Assert.That(OnitySceneServiceFixtureInstaller.ReceivedEnterData[0], Is.SameAs(data),
                "the scene reads its own enter data while it installs");
            Assert.That(OnitySceneServiceFixtureInstaller.ConsumedSharedEnterData[0], Is.SameAs(data),
                "a scene written for the shared slot receives it too");

            for (int frame = 0; frame < 3; frame++)
            {
                yield return null;
            }

            Assert.That(load.IsCompleted, Is.False, "the target's build has not finished");
            Assert.That(m_service.IsBusy, Is.True);
            Assert.That(m_cover.IsVisible, Is.True, "the cover holds over the unready scene");

            build.TrySetResult(true);
            yield return WaitForTask(load);

            Assert.That(load.Result.Status, Is.EqualTo(OnitySceneResultStatus.Succeeded), load.Result.ToString());
            Assert.That(load.Result.Scene, Is.EqualTo(game));
            Assert.That(load.Result.OperationId, Is.GreaterThan(0));
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(game));
            Assert.That(m_home.IsValid() && m_home.isLoaded, Is.False, "the replaced scene unloaded");
            Assert.That(m_log, Is.EqualTo(new[] { "default show", "default hide" }));
            Assert.That(OnitySceneTransitionStore.TryGetEnterData(game, out SceneEnterData _), Is.False,
                "the enter data ends with its operation");
            Assert.That(m_service.IsBusy, Is.False);
            Assert.That(m_service.State.Value, Is.EqualTo(OnitySceneOperationState.Idle));
        }

        [UnityTest]
        public IEnumerator Additive_KeepsTheLoadedScenesAndReturnsEachExactInstance()
        {
            Task<OnitySceneResult> first = m_service.LoadAsync(OnitySceneRequest.Additive("Game")).AsTask();
            yield return WaitForTask(first);
            Task<OnitySceneResult> second = m_service.LoadAsync(OnitySceneRequest.Additive("Game")).AsTask();
            yield return WaitForTask(second);

            Assert.That(first.Result.IsSuccess && second.Result.IsSuccess, Is.True);
            Assert.That(second.Result.Scene, Is.Not.EqualTo(first.Result.Scene), "a second instance is its own scene");
            Assert.That(first.Result.Scene.isLoaded && second.Result.Scene.isLoaded, Is.True);
            Assert.That(m_home.isLoaded, Is.True, "an additive load keeps the other scenes");
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(m_home), "it stays inactive unless asked");
            Assert.That(m_log, Is.Empty, "an additive request has no cover by default");

            Task<OnitySceneResult> third = m_service
                .LoadAsync(OnitySceneRequest.Additive("Game", setActive: true))
                .AsTask();
            yield return WaitForTask(third);

            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(third.Result.Scene));
        }

        [UnityTest]
        public IEnumerator Additive_WaitsForItsOwnSceneNotTheActiveOne()
        {
            OnitySceneServiceFixtureInstaller.BuildGate = new TaskCompletionSource<bool>();
            OnitySceneServiceFixture.BuildContextRoot(m_home);
            yield return null;

            Task<OnitySceneResult> load = m_service.LoadAsync(OnitySceneRequest.Additive("Plain")).AsTask();
            yield return WaitForTask(load);

            Assert.That(load.Result.Status, Is.EqualTo(OnitySceneResultStatus.Succeeded),
                "the never-ready active scene does not hold an additive load");
        }

        [UnityTest]
        public IEnumerator Unload_RemovesExactlyThatInstance()
        {
            Task<OnitySceneResult> first = m_service.LoadAsync(OnitySceneRequest.Additive("Plain")).AsTask();
            yield return WaitForTask(first);
            Task<OnitySceneResult> second = m_service.LoadAsync(OnitySceneRequest.Additive("Plain")).AsTask();
            yield return WaitForTask(second);
            Scene firstScene = first.Result.Scene;

            Task<OnitySceneResult> unload = m_service.UnloadAsync(firstScene).AsTask();
            yield return WaitForTask(unload);

            Assert.That(unload.Result.Status, Is.EqualTo(OnitySceneResultStatus.Succeeded));
            Assert.That(unload.Result.Scene, Is.EqualTo(firstScene));
            Assert.That(firstScene.isLoaded, Is.False);
            Assert.That(second.Result.Scene.isLoaded, Is.True, "the other instance with the same name stays");

            Task<OnitySceneResult> again = m_service.UnloadAsync(firstScene).AsTask();
            Assert.That(again.IsCompleted, Is.True);
            Assert.That(again.Result.Status, Is.EqualTo(OnitySceneResultStatus.NotLoaded));
            Assert.That(m_service.UnloadAsync(default).AsTask().Result.Status,
                Is.EqualTo(OnitySceneResultStatus.NotLoaded));
        }

        [UnityTest]
        public IEnumerator Cover_DefaultNoneAndCustomShowAndHideOnlyTheirCover()
        {
            OnityRecordingSceneCover iris = new OnityRecordingSceneCover("iris", m_log);

            yield return WaitForTask(m_service.LoadAsync(OnitySceneRequest.Single("Plain")).AsTask());
            Assert.That(m_log, Is.EqualTo(new[] { "default show", "default hide" }));

            yield return WaitForTask(m_service.LoadAsync(OnitySceneRequest.Single("Plain").WithoutCover()).AsTask());
            Assert.That(m_log.Count, Is.EqualTo(2), "no cover");

            yield return WaitForTask(m_service.LoadAsync(OnitySceneRequest.Single("Plain").WithCover(iris)).AsTask());
            Assert.That(m_log, Is.EqualTo(new[] { "default show", "default hide", "iris show", "iris hide" }));

            yield return WaitForTask(m_service.LoadAsync(OnitySceneRequest.Additive("Plain").WithDefaultCover()).AsTask());
            Assert.That(m_cover.ShowCount, Is.EqualTo(2));
            Assert.That(m_cover.HideCount, Is.EqualTo(2), "every show has exactly one hide");
            Assert.That(m_cover.IsVisible || iris.IsVisible, Is.False);
        }

        [UnityTest]
        public IEnumerator Busy_ASecondRequestStartsNothing()
        {
            m_fixture.LoadGate = new TaskCompletionSource<bool>();
            Task<OnitySceneResult> running = m_service.LoadAsync(OnitySceneRequest.Single("Game")).AsTask();
            yield return WaitUntil(() => m_fixture.LoadCount == 1);
            int shows = m_cover.ShowCount;

            Task<OnitySceneResult> busy = m_service.LoadAsync(OnitySceneRequest.Single("Plain")).AsTask();
            Task<OnitySceneResult> busyUnload = m_service.UnloadAsync(m_home).AsTask();

            Assert.That(busy.IsCompleted && busyUnload.IsCompleted, Is.True, "busy returns at once");
            Assert.That(busy.Result.Status, Is.EqualTo(OnitySceneResultStatus.Busy));
            Assert.That(busy.Result.OperationId, Is.EqualTo(0));
            Assert.That(busyUnload.Result.Status, Is.EqualTo(OnitySceneResultStatus.Busy));
            Assert.That(m_fixture.LoadCount, Is.EqualTo(1), "no second load");
            Assert.That(m_cover.ShowCount, Is.EqualTo(shows), "the cover is not shown again");
            Assert.That(m_home.isLoaded, Is.True);

            m_fixture.LoadGate.TrySetResult(true);
            yield return WaitForTask(running);
            Assert.That(running.Result.Status, Is.EqualTo(OnitySceneResultStatus.Succeeded));
        }

        [UnityTest]
        public IEnumerator Completed_ASubscriberStartsTheNextOperationAtOnce()
        {
            Task<OnitySceneResult> next = null;
            bool wasBusyDuringNotification = true;
            using IDisposable subscription = m_service.Completed.Subscribe(result =>
            {
                if (next != null)
                {
                    return;
                }

                wasBusyDuringNotification = m_service.IsBusy;
                next = m_service.LoadAsync(OnitySceneRequest.Single("Plain").WithoutCover()).AsTask();
            });

            Task<OnitySceneResult> first = m_service.LoadAsync(OnitySceneRequest.Single("Game").WithoutCover()).AsTask();
            yield return WaitForTask(first);
            Assert.That(next, Is.Not.Null);
            yield return WaitForTask(next);

            Assert.That(wasBusyDuringNotification, Is.False, "the service is idle before its result is published");
            Assert.That(first.Result.IsSuccess, Is.True);
            Assert.That(next.Result.Status, Is.EqualTo(OnitySceneResultStatus.Succeeded), "it was admitted, not busy");
            Assert.That(next.Result.OperationId, Is.EqualTo(first.Result.OperationId + 1));
            Assert.That(m_service.IsBusy, Is.False);
        }

        [UnityTest]
        public IEnumerator CallerCancellation_EndsOnlyThatWait()
        {
            Task<OnitySceneResult> precancelled = m_service
                .LoadAsync(OnitySceneRequest.Single("Game"), new CancellationToken(true))
                .AsTask();

            Assert.That(precancelled.IsCanceled, Is.True);
            Assert.That(m_fixture.LoadCount, Is.EqualTo(0), "a cancelled caller starts nothing");
            Assert.That(m_log, Is.Empty);
            Assert.That(m_service.IsBusy, Is.False);

            m_fixture.LoadGate = new TaskCompletionSource<bool>();
            OnitySceneResult? published = null;
            using IDisposable subscription = m_service.Completed.Subscribe(result => published = result);
            using CancellationTokenSource caller = new CancellationTokenSource();
            Task<OnitySceneResult> waiting = m_service.LoadAsync(OnitySceneRequest.Single("Game"), caller.Token).AsTask();
            yield return WaitUntil(() => m_fixture.LoadCount == 1);

            // The scene that asked unloads, which cancels its token.
            caller.Cancel();
            yield return WaitUntil(() => waiting.IsCompleted);
            Assert.That(waiting.IsCanceled, Is.True, "the caller's wait ends");
            Assert.That(m_service.IsBusy, Is.True, "the operation goes on");

            m_fixture.LoadGate.TrySetResult(true);
            yield return WaitUntil(() => published.HasValue);
            Assert.That(published.Value.Status, Is.EqualTo(OnitySceneResultStatus.Succeeded));
            Assert.That(m_log, Is.EqualTo(new[] { "default show", "default hide" }));
        }

        [UnityTest]
        public IEnumerator Dispose_CompletesTheRunningOperationAndRejectsLaterRequests()
        {
            m_fixture.LoadGate = new TaskCompletionSource<bool>();
            Task<OnitySceneResult> running = m_service.LoadAsync(OnitySceneRequest.Single("Game")).AsTask();
            yield return WaitUntil(() => m_fixture.LoadCount == 1);

            m_service.Dispose();
            yield return WaitForTask(running);

            Assert.That(running.Result.Status, Is.EqualTo(OnitySceneResultStatus.Disposed));
            Assert.That(m_service.IsBusy, Is.False);
            Assert.That(m_service.LoadAsync(OnitySceneRequest.Single("Plain")).AsTask().Result.Status,
                Is.EqualTo(OnitySceneResultStatus.Disposed));
            Assert.That(m_service.UnloadAsync(m_home).AsTask().Result.Status,
                Is.EqualTo(OnitySceneResultStatus.Disposed));
            Assert.That(m_fixture.LoadCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Readiness_FailedBuild_IsAFailureAndStillLiftsTheCover()
        {
            OnitySceneServiceFixtureInstaller.BuildFailure = new InvalidOperationException("fixture build failed");
            LogAssert.Expect(LogType.Exception, new Regex("fixture build failed"));

            Task<OnitySceneResult> load = m_service.LoadAsync(OnitySceneRequest.Single("Game")).AsTask();
            yield return WaitForTask(load);

            Assert.That(load.Result.Status, Is.EqualTo(OnitySceneResultStatus.ReadinessFailed));
            Assert.That(load.Result.Exception, Is.InstanceOf<InvalidOperationException>());
            Assert.That(load.Result.Scene, Is.EqualTo(SceneManager.GetActiveScene()), "the result names the scene");
            Assert.That(m_log, Is.EqualTo(new[] { "default show", "default hide" }));
        }

        [UnityTest]
        public IEnumerator Readiness_NeverReady_TimesOutAndStillLiftsTheCover()
        {
            OnitySceneServiceFixtureInstaller.BuildGate = new TaskCompletionSource<bool>();
            m_service.Dispose();
            m_service = CreateService(new OnitySceneRevealPolicy(0, 0.3f));

            Task<OnitySceneResult> load = m_service.LoadAsync(OnitySceneRequest.Single("Game")).AsTask();
            yield return WaitForTask(load);

            Assert.That(load.Result.Status, Is.EqualTo(OnitySceneResultStatus.ReadinessTimedOut),
                "a timeout is never a successful entry");
            Assert.That(m_log, Is.EqualTo(new[] { "default show", "default hide" }));
        }

        [UnityTest]
        public IEnumerator Readiness_SceneWithoutContext_Succeeds()
        {
            Task<OnitySceneResult> load = m_service.LoadAsync(OnitySceneRequest.Single("Plain")).AsTask();
            yield return WaitForTask(load);

            Assert.That(load.Result.Status, Is.EqualTo(OnitySceneResultStatus.Succeeded));
        }

        [UnityTest]
        public IEnumerator Readiness_TargetUnloadedBeforeReady_IsAFailure()
        {
            OnitySceneServiceFixtureInstaller.BuildGate = new TaskCompletionSource<bool>();
            Task<OnitySceneResult> load = m_service.LoadAsync(OnitySceneRequest.Single("Game")).AsTask();
            yield return WaitUntil(() => OnitySceneServiceFixtureInstaller.ReceivedEnterData.Count == 1);
            Scene game = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(m_originalActiveScene);

            AsyncOperation unload = SceneManager.UnloadSceneAsync(game);
            yield return WaitForTask(load);

            Assert.That(load.Result.Status, Is.EqualTo(OnitySceneResultStatus.ReadinessFailed));
            Assert.That(m_log, Is.EqualTo(new[] { "default show", "default hide" }));
            yield return WaitUntil(() => unload == null || unload.isDone);
        }

        [UnityTest]
        public IEnumerator EnterData_ReachesOnlyItsOwnDestination()
        {
            SceneEnterData firstData = new SceneEnterData(1);
            SceneEnterData secondData = new SceneEnterData(2);

            Task<OnitySceneResult> first = m_service.LoadAsync(OnitySceneRequest.Additive("Game", firstData)).AsTask();
            yield return WaitForTask(first);
            Task<OnitySceneResult> second = m_service.LoadAsync(OnitySceneRequest.Additive("Game", secondData)).AsTask();
            yield return WaitForTask(second);
            Task<OnitySceneResult> third = m_service.LoadAsync(OnitySceneRequest.Additive("Game")).AsTask();
            yield return WaitForTask(third);

            Assert.That(OnitySceneServiceFixtureInstaller.ReceivedEnterData,
                Is.EqualTo(new IOnitySceneEnterData[] { firstData, secondData, null }),
                "each scene reads only its own operation's data, and a later operation without data reads none");
            Assert.That(OnitySceneTransitionStore.TryGetEnterData(first.Result.Scene, out SceneEnterData _), Is.False);
            Assert.That(OnitySceneTransitionStore.TryGetEnterData(second.Result.Scene, out SceneEnterData _), Is.False);
            Assert.That(OnitySceneTransitionStore.TryConsumeActiveEnterData(out SceneEnterData _), Is.False,
                "nothing is left in the shared slot");
        }

        [Test]
        public void InvalidTarget_StartsNothing()
        {
            OnitySceneResult unknown = m_service.LoadAsync(OnitySceneRequest.Single("Missing")).AsTask().Result;
            OnitySceneResult additivePrepared = m_service
                .LoadAsync(OnitySceneRequest.Additive("Game").WithPreparedScene())
                .AsTask()
                .Result;

            Assert.That(unknown.Status, Is.EqualTo(OnitySceneResultStatus.InvalidTarget));
            Assert.That(unknown.Message, Does.Contain("Missing"));
            Assert.That(additivePrepared.Status, Is.EqualTo(OnitySceneResultStatus.InvalidTarget));
            Assert.That(m_fixture.LoadCount, Is.EqualTo(0));
            Assert.That(m_log, Is.Empty);
            Assert.That(m_service.IsBusy, Is.False);
        }

        [UnityTest]
        public IEnumerator PreparedScene_StartsWithoutALoadAndFallsBackOnAMismatch()
        {
            const string sceneName = "PreloadGame";
            m_fixture.Add(sceneName, OnitySceneFixtureKind.Context);
            Scene lastPrepared = default;
            using OnityScenePreloader preloader = new OnityScenePreloader((name, onCreated, token) =>
            {
                lastPrepared = m_fixture.CreateTrackedScene(name);
                onCreated(lastPrepared);
                BuildPreparedRoot(lastPrepared);
                return Task.CompletedTask;
            });
            m_service.Dispose();
            m_service = CreateService(new OnitySceneRevealPolicy(0, k_timeoutSeconds), preloader);

            yield return WaitForTask(preloader.PrepareAsync(sceneName, new PrepareData(3), CancellationToken.None));
            Scene prepared = lastPrepared;
            Task<OnitySceneResult> start = m_service
                .LoadAsync(OnitySceneRequest.Single(sceneName, new StartData(3)).WithPreparedScene())
                .AsTask();
            yield return WaitForTask(start);

            Assert.That(start.Result.Status, Is.EqualTo(OnitySceneResultStatus.Succeeded), start.Result.ToString());
            Assert.That(start.Result.Scene, Is.EqualTo(prepared));
            Assert.That(m_fixture.LoadCount, Is.EqualTo(0), "the prepared scene started without a load");
            Assert.That(ResolvePrepared(prepared).ActivateCount, Is.EqualTo(1));
            Assert.That(m_log, Is.EqualTo(new[] { "default show", "default hide" }));

            yield return WaitForTask(preloader.PrepareAsync(sceneName, new PrepareData(4), CancellationToken.None));
            Scene mismatched = lastPrepared;
            Task<OnitySceneResult> fallback = m_service
                .LoadAsync(OnitySceneRequest.Single(sceneName, new StartData(5)).WithPreparedScene())
                .AsTask();
            yield return WaitForTask(fallback);

            Assert.That(fallback.Result.Status, Is.EqualTo(OnitySceneResultStatus.Succeeded));
            Assert.That(m_fixture.LoadCount, Is.EqualTo(1), "a mismatch loads the scene normally");
            Assert.That(fallback.Result.Scene, Is.Not.EqualTo(mismatched));
            Assert.That(preloader.IsPrepared(sceneName), Is.False, "the Single load unloaded the stale prepared scene");
        }

        [UnityTest]
        public IEnumerator ActiveScene_ReportsChangesMadeAroundTheService()
        {
            Scene other = m_fixture.CreateTrackedScene("Other");
            yield return null;

            SceneManager.SetActiveScene(other);
            Assert.That(m_service.ActiveScene, Is.EqualTo(other));

            SceneManager.SetActiveScene(m_home);
            Assert.That(m_service.ActiveScene, Is.EqualTo(m_home));
        }

        [UnityTest]
        public IEnumerator State_PublishesOnlyChangesWithoutAllocating()
        {
            m_fixture.LoadGate = new TaskCompletionSource<bool>();
            int publications = 0;
            using IDisposable subscription = m_service.State.Subscribe(_ => publications++, false);
            Task<OnitySceneResult> load = m_service.LoadAsync(OnitySceneRequest.Single("Plain").WithoutCover()).AsTask();
            yield return WaitUntil(() => m_fixture.LastProgress != null);
            Action<float> reportProgress = m_fixture.LastProgress;

            reportProgress(0.25f);
            int afterFirst = publications;
            reportProgress(0.25f);
            Assert.That(publications, Is.EqualTo(afterFirst), "an unchanged state is not published");
            Assert.That(m_service.State.Value.Phase, Is.EqualTo(OnitySceneOperationPhase.Loading));
            Assert.That(m_service.State.Value.Progress, Is.EqualTo(0.25f));

            // Warmed up: a changed progress allocates nothing.
            long before = GC.GetAllocatedBytesForCurrentThread();

            for (int step = 0; step < 100; step++)
            {
                reportProgress(step * 0.001f + 0.3f);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0), "progress publication allocates per update");
            Assert.That(publications, Is.EqualTo(afterFirst + 100));

            m_fixture.LoadGate.TrySetResult(true);
            yield return WaitForTask(load);
            Assert.That(m_service.State.Value, Is.EqualTo(OnitySceneOperationState.Idle));
        }

        [UnityTest]
        public IEnumerator ThrowingSubscribers_CannotLeaveTheServiceBusy()
        {
            using IDisposable state = m_service.State.Subscribe(_ => throw new InvalidOperationException("state"), false);
            using IDisposable completed = m_service.Completed.Subscribe(_ => throw new InvalidOperationException("done"));

            Task<OnitySceneResult> first = m_service.LoadAsync(OnitySceneRequest.Single("Plain")).AsTask();
            yield return WaitForTask(first);

            Assert.That(first.Result.Status, Is.EqualTo(OnitySceneResultStatus.Succeeded));
            Assert.That(m_service.IsBusy, Is.False);

            Task<OnitySceneResult> next = m_service.LoadAsync(OnitySceneRequest.Single("Plain")).AsTask();
            yield return WaitForTask(next);
            Assert.That(next.Result.Status, Is.EqualTo(OnitySceneResultStatus.Succeeded));
        }

        private static void BuildPreparedRoot(Scene scene)
        {
            GameObject root = new GameObject("PreparedRoot");
            root.SetActive(false);
            SceneManager.MoveGameObjectToScene(root, scene);
            OnityPreparedSceneFixtureInstaller installer = root.AddComponent<OnityPreparedSceneFixtureInstaller>();
            SceneContext context = root.AddComponent<SceneContext>();
            typeof(OnityContext)
                .GetField("m_installers", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(context, new MonoInstaller[] { installer });
            root.SetActive(true);
        }

        private static PreparedScene ResolvePrepared(Scene scene)
        {
            Assert.That(OnitySceneScopes.TryFind(scene, out SceneContext context), Is.True);
            Assert.That(context.Container.TryResolve(out PreparedScene prepared), Is.True);
            return prepared;
        }

        private OnitySceneService CreateService(OnitySceneRevealPolicy policy, OnityScenePreloader preloader = null)
        {
            return new OnitySceneService(m_fixture, null, m_cover, preloader, policy);
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

        private sealed class SceneEnterData : IOnitySceneEnterData
        {
            public SceneEnterData(int id)
            {
                Id = id;
            }

            public int Id { get; }
        }
    }
}
