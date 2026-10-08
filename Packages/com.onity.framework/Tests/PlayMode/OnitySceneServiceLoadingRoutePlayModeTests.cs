using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.SceneFlow;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Onity.Tests.PlayMode
{
    /// <summary>
    /// Verifies the Loading-scene route of <see cref="OnitySceneService" />: the Loading scene's
    /// <see cref="OnityLoadingSceneInitiator" /> takes the operation's handoff, the service loads the target behind
    /// it, covers before the target activates and completes only once the target is ready and revealed; failures
    /// reach the requester and an activation the service started is never abandoned.
    /// </summary>
    [TestFixture]
    public sealed class OnitySceneServiceLoadingRoutePlayModeTests
    {
        private const float k_timeoutSeconds = 10f;

        private readonly List<string> m_log = new List<string>();
        private OnitySceneServiceFixture m_fixture;
        private OnityRecordingSceneCover m_cover;
        private OnitySceneFlowProfile m_profile;
        private OnitySceneService m_service;
        private Scene m_originalActiveScene;

        [SetUp]
        public void SetUp()
        {
            OnitySceneServiceFixtureInstaller.ResetShared();
            OnitySceneTransitionStore.Clear();
            m_log.Clear();
            m_originalActiveScene = SceneManager.GetActiveScene();
            m_fixture = new OnitySceneServiceFixture();
            m_fixture.Add("Loading", OnitySceneFixtureKind.Loading);
            m_fixture.Add("LoadingWithoutInitiator", OnitySceneFixtureKind.LoadingWithoutInitiator);
            m_fixture.Add("Game", OnitySceneFixtureKind.Context);
            SceneManager.SetActiveScene(m_fixture.CreateTrackedScene("Home"));
            m_cover = new OnityRecordingSceneCover("default", m_log);
            m_profile = ScriptableObject.CreateInstance<OnitySceneFlowProfile>();
            m_profile.SetSceneName(OnitySceneFlowStateId.Loading, "Loading");
            m_profile.SetRouteTransitionsThroughLoadingScene(true);
            m_service = CreateService();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            m_service.Dispose();
            m_fixture.LoadGate?.TrySetResult(true);
            m_cover.ShowGate?.TrySetResult(true);
            OnitySceneServiceFixtureInstaller.BuildGate?.TrySetResult(true);
            OnitySceneServiceFixtureInstaller.ResetShared();
            OnitySceneTransitionStore.Clear();
            UnityEngine.Object.Destroy(m_profile);
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
        public IEnumerator LoadingRoute_StaysPendingUntilTheTargetIsReadyAndRevealed()
        {
            TaskCompletionSource<bool> build = new TaskCompletionSource<bool>();
            OnitySceneServiceFixtureInstaller.BuildGate = build;
            LevelData data = new LevelData();

            Task<OnitySceneResult> load = m_service.LoadAsync(OnitySceneRequest.Single("Game", data)).AsTask();
            Scene loading = FindTracked("Loading-");
            yield return WaitUntil(() => OnitySceneServiceFixtureInstaller.ReceivedEnterData.Count == 1);
            Scene game = SceneManager.GetActiveScene();

            Assert.That(game.name, Does.StartWith("Game-"), "the target activated behind the Loading scene");
            Assert.That(OnitySceneServiceFixtureInstaller.ReceivedEnterData[0], Is.SameAs(data));
            Assert.That(m_log, Is.EqualTo(new[] { "default show", "default hide", "default show" }),
                "the cover lifted over the Loading scene and covered it again before the target activated");

            for (int frame = 0; frame < 3; frame++)
            {
                yield return null;
            }

            Assert.That(load.IsCompleted, Is.False, "the Loading scene's load is not the end of the operation");
            Assert.That(loading.IsValid() && loading.isLoaded, Is.False, "the target replaced the Loading scene");

            build.TrySetResult(true);
            yield return WaitForTask(load);

            Assert.That(load.Result.Status, Is.EqualTo(OnitySceneResultStatus.Succeeded), load.Result.ToString());
            Assert.That(load.Result.Scene, Is.EqualTo(game));
            Assert.That(m_log, Is.EqualTo(new[] { "default show", "default hide", "default show", "default hide" }));
            Assert.That(m_fixture.ActivationCount, Is.EqualTo(2), "the Loading scene and then the target");
            Assert.That(m_service.IsBusy, Is.False);
            Assert.That(OnitySceneTransitionStore.TryGetEnterData(game, out LevelData _), Is.False);
        }

        [UnityTest]
        public IEnumerator LoadingRoute_TargetLoadFailure_ReachesTheRequester()
        {
            m_fixture.FailingKey = "Game";
            LogAssert.Expect(LogType.Exception, new Regex("fixture load of 'Game' failed"));

            Task<OnitySceneResult> load = m_service.LoadAsync(OnitySceneRequest.Single("Game")).AsTask();
            yield return WaitForTask(load);

            Assert.That(load.Result.Status, Is.EqualTo(OnitySceneResultStatus.LoadFailed));
            Assert.That(load.Result.Exception, Is.InstanceOf<InvalidOperationException>());
            Assert.That(m_log, Is.EqualTo(new[] { "default show", "default hide" }),
                "the Loading scene stays revealed; nothing covers it again");
            Assert.That(SceneManager.GetActiveScene().name, Does.StartWith("Loading-"));
            Assert.That(m_service.IsBusy, Is.False);
        }

        [UnityTest]
        public IEnumerator LoadingRoute_WithoutAnInitiator_FailsAndLiftsTheCover()
        {
            m_profile.SetSceneName(OnitySceneFlowStateId.Loading, "LoadingWithoutInitiator");
            m_service.Dispose();
            m_service = CreateService();

            Task<OnitySceneResult> load = m_service.LoadAsync(OnitySceneRequest.Single("Game")).AsTask();
            yield return WaitForTask(load);

            Assert.That(load.Result.Status, Is.EqualTo(OnitySceneResultStatus.LoadFailed));
            Assert.That(load.Result.Message, Does.Contain(nameof(OnityLoadingSceneInitiator)));
            Assert.That(m_log, Is.EqualTo(new[] { "default show", "default hide" }));
            Assert.That(m_fixture.LoadCount, Is.EqualTo(1), "the target never loads");
        }

        [UnityTest]
        public IEnumerator LoadingRoute_DisposedBeforeActivation_StillActivatesTheTarget()
        {
            Task<OnitySceneResult> load = m_service.LoadAsync(OnitySceneRequest.Single("Game")).AsTask();

            // The first show completed at once; the show before the target's activation now waits.
            m_cover.ShowGate = new TaskCompletionSource<bool>();
            yield return WaitUntil(() => m_cover.ShowCount == 2);
            Assert.That(m_fixture.ActivationCount, Is.EqualTo(1), "the target loaded but has not activated");

            m_service.Dispose();
            yield return WaitForTask(load);
            yield return WaitUntil(() => m_fixture.ActivationCount == 2);

            Assert.That(load.Result.Status, Is.EqualTo(OnitySceneResultStatus.Disposed));
            Assert.That(SceneManager.GetActiveScene().name, Does.StartWith("Game-"),
                "a deferred activation the service started is never abandoned");
        }

        [Test]
        public void LoadingRoute_UnloadableLoadingScene_IsAnInvalidTarget()
        {
            m_profile.SetSceneName(OnitySceneFlowStateId.Loading, "Missing");
            m_service.Dispose();
            m_service = CreateService();

            OnitySceneResult result = m_service.LoadAsync(OnitySceneRequest.Single("Game")).AsTask().Result;

            Assert.That(result.Status, Is.EqualTo(OnitySceneResultStatus.InvalidTarget));
            Assert.That(result.Message, Does.Contain("Loading scene"));
            Assert.That(m_fixture.LoadCount, Is.EqualTo(0));
            Assert.That(m_log, Is.Empty);
        }

        private OnitySceneService CreateService()
        {
            return new OnitySceneService(m_fixture, m_profile, m_cover, null, new OnitySceneRevealPolicy(0, k_timeoutSeconds));
        }

        private Scene FindTracked(string prefix)
        {
            for (int index = 0; index < m_fixture.Scenes.Count; index++)
            {
                Scene scene = m_fixture.Scenes[index];

                if (scene.IsValid() && scene.name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return scene;
                }
            }

            Assert.Fail($"No loaded tracked scene starts with '{prefix}'.");
            return default;
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

        private sealed class LevelData : IOnitySceneEnterData
        {
        }
    }
}
