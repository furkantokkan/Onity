using System.Collections;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.DI;
using Onity.Unity.Async;
using Onity.Unity.Contexts;
using Onity.Unity.Installers;
using Onity.Unity.SceneFlow;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Onity.Tests.PlayMode
{
    [TestFixture]
    public sealed class OnitySceneFlowContextReadinessPlayModeTests
    {
        private const string k_deferredLoadSceneName = "OnityDeferredLoadFixture";
        private const string k_deferredLoadScenePath =
            "Packages/com.onity.framework/Tests/Fixtures/OnityDeferredLoadFixture.unity";

        [UnityTest]
        public IEnumerator ReadyTask_WaitsForAsyncBuildCallback()
        {
            GameObject contextObject =
                new GameObject(nameof(OnitySceneFlowContextReadinessPlayModeTests));
            contextObject.SetActive(false);

            DelayedBuildInstaller installer =
                contextObject.AddComponent<DelayedBuildInstaller>();
            TestContext context = contextObject.AddComponent<TestContext>();
            SetContextInstallers(context, installer);

            try
            {
                contextObject.SetActive(true);
                Assert.That(context.IsReady, Is.False);
                Assert.That(context.ReadyTask.IsCompleted, Is.False);

                yield return null;

                Assert.That(context.IsReady, Is.False);
                Assert.That(context.ReadyTask.IsCompleted, Is.False);

                installer.CompleteBuild();

                const int maximumFrames = 5;

                for (int frame = 0; frame < maximumFrames && context.IsReady == false; frame++)
                {
                    yield return null;
                }

                Assert.That(context.IsReady, Is.True);
                Assert.That(context.ReadyTask.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            }
            finally
            {
                Object.Destroy(contextObject);
            }
        }

        [UnityTest]
        public IEnumerator LoadingGate_WaitsForFrameAndUnscaledMinimumDuration()
        {
            const float minimumVisibleDurationSeconds = 0.2f;
            GameObject initiatorObject =
                new GameObject(nameof(LoadingGate_WaitsForFrameAndUnscaledMinimumDuration));
            initiatorObject.SetActive(false);

            OnityLoadingSceneInitiator initiator =
                initiatorObject.AddComponent<OnityLoadingSceneInitiator>();
            FieldInfo durationField = typeof(OnityLoadingSceneInitiator).GetField(
                "m_minimumVisibleDurationSeconds",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo waitMethod = typeof(OnityLoadingSceneInitiator).GetMethod(
                "WaitForMinimumVisibleDurationAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);
            float originalTimeScale = Time.timeScale;

            try
            {
                Assert.That(durationField, Is.Not.Null);
                Assert.That(waitMethod, Is.Not.Null);

                durationField.SetValue(initiator, minimumVisibleDurationSeconds);
                Time.timeScale = 0f;

                Task waitTask = (Task)waitMethod.Invoke(
                    initiator,
                    new object[] { CancellationToken.None });

                Assert.That(waitTask.IsCompleted, Is.False);

                yield return null;

                Assert.That(
                    waitTask.IsCompleted,
                    Is.False,
                    "The loading gate must remain open after its first rendered frame.");

                float timeoutAt = Time.realtimeSinceStartup + 2f;

                while (waitTask.IsCompleted == false &&
                       Time.realtimeSinceStartup < timeoutAt)
                {
                    yield return null;
                }

                Assert.That(waitTask.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            }
            finally
            {
                Time.timeScale = originalTimeScale;
                Object.DestroyImmediate(initiatorObject);
            }
        }

        [UnityTest]
        public IEnumerator CanceledLoadingGate_StillCompletesPreparedOperation()
        {
            MethodInfo activateMethod = typeof(OnityLoadingSceneInitiator).GetMethod(
                "ActivatePreparedSceneAsync",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(activateMethod, Is.Not.Null);

            AsyncOperation operation = Resources.UnloadUnusedAssets();
            operation.allowSceneActivation = false;
            try
            {
                using CancellationTokenSource cancellationTokenSource =
                    new CancellationTokenSource();
                cancellationTokenSource.Cancel();

                int progressCount = 0;
                Task canceledGate = Task.FromCanceled(cancellationTokenSource.Token);
                Task completionTask = (Task)activateMethod.Invoke(
                    null,
                    new object[]
                    {
                        operation,
                        canceledGate,
                        (System.Action<float>)(_ => progressCount++)
                    });

                float timeoutAt = Time.realtimeSinceStartup + 5f;
                while (completionTask.IsCompleted == false &&
                       Time.realtimeSinceStartup < timeoutAt)
                {
                    yield return null;
                }

                Assert.That(operation.allowSceneActivation, Is.True);
                Assert.That(operation.isDone, Is.True);
                Assert.That(progressCount, Is.GreaterThan(0));
                Assert.That(completionTask.IsCanceled, Is.True);
            }
            finally
            {
                operation.allowSceneActivation = true;
            }
        }

        [UnityTest]
        public IEnumerator DeferredSceneLoad_CanceledAfterStart_ReturnsActivatableOperation()
        {
            Scene[] scenesBeforeLoad = CaptureScenes();
            using CancellationTokenSource cancellationTokenSource =
                new CancellationTokenSource();
            Task<AsyncOperation> loadTask = OnitySceneLoader.LoadAsync(
                k_deferredLoadSceneName,
                LoadSceneMode.Additive,
                false,
                cancellationToken: cancellationTokenSource.Token);
            cancellationTokenSource.Cancel();

            AsyncOperation operation = null;
            Scene loadedScene = default;
            try
            {
                yield return WaitForTask(loadTask);
                Assert.That(loadTask.IsCompletedSuccessfully, Is.True, loadTask.Exception?.ToString());

                operation = loadTask.Result;
                Assert.That(operation, Is.Not.Null);
                Assert.That(operation.progress, Is.GreaterThanOrEqualTo(0.9f));
                Assert.That(operation.isDone, Is.False);
                Assert.That(operation.allowSceneActivation, Is.False);

                Task activationTask = OnitySceneLoader.ActivateAsync(operation);
                yield return WaitForTask(activationTask);
                Assert.That(activationTask.IsCompletedSuccessfully, Is.True,
                    activationTask.Exception?.ToString());

                loadedScene = FindNewFixtureScene(scenesBeforeLoad);
                Assert.That(loadedScene.IsValid() && loadedScene.isLoaded, Is.True);

                AsyncOperation unloadOperation = SceneManager.UnloadSceneAsync(loadedScene);
                yield return WaitForOperation(unloadOperation);
                Assert.That(unloadOperation.isDone, Is.True);
            }
            finally
            {
                if (operation != null)
                {
                    operation.allowSceneActivation = true;
                }

                if (loadedScene.IsValid() && loadedScene.isLoaded)
                {
                    SceneManager.UnloadSceneAsync(loadedScene);
                }
            }
        }

        [UnityTest]
        public IEnumerator DeferredSceneLoad_ThrowingProgressCallback_StillReleasesQueue()
        {
            Scene[] scenesBeforeLoad = CaptureScenes();
            int callbackCount = 0;
            System.Action<float> onProgress = _ =>
            {
                callbackCount++;
                throw new System.InvalidOperationException("deferred scene progress failed");
            };
            LogAssert.Expect(
                LogType.Exception,
                new System.Text.RegularExpressions.Regex(
                    "InvalidOperationException: deferred scene progress failed"));

            Task<AsyncOperation> loadTask = OnitySceneLoader.LoadAsync(
                k_deferredLoadSceneName,
                LoadSceneMode.Additive,
                false,
                onProgress);

            AsyncOperation operation = null;
            Scene loadedScene = default;
            try
            {
                yield return WaitForTask(loadTask);
                Assert.That(loadTask.IsCompletedSuccessfully, Is.True, loadTask.Exception?.ToString());
                Assert.That(callbackCount, Is.EqualTo(1));

                operation = loadTask.Result;
                Task activationTask = OnitySceneLoader.ActivateAsync(operation);
                yield return WaitForTask(activationTask);
                Assert.That(activationTask.IsCompletedSuccessfully, Is.True,
                    activationTask.Exception?.ToString());

                loadedScene = FindNewFixtureScene(scenesBeforeLoad);
                Assert.That(loadedScene.IsValid() && loadedScene.isLoaded, Is.True);

                AsyncOperation unloadOperation = SceneManager.UnloadSceneAsync(loadedScene);
                yield return WaitForOperation(unloadOperation);
                Assert.That(unloadOperation.isDone, Is.True);
            }
            finally
            {
                if (operation != null)
                {
                    operation.allowSceneActivation = true;
                }

                if (loadedScene.IsValid() && loadedScene.isLoaded)
                {
                    SceneManager.UnloadSceneAsync(loadedScene);
                }
            }
        }

        private static Scene[] CaptureScenes()
        {
            Scene[] scenes = new Scene[SceneManager.sceneCount];
            for (int index = 0; index < scenes.Length; index++)
            {
                scenes[index] = SceneManager.GetSceneAt(index);
            }

            return scenes;
        }

        private static Scene FindNewFixtureScene(Scene[] scenesBeforeLoad)
        {
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (scene.path != k_deferredLoadScenePath)
                {
                    continue;
                }

                bool wasLoadedBefore = false;
                for (int priorIndex = 0; priorIndex < scenesBeforeLoad.Length; priorIndex++)
                {
                    if (scene == scenesBeforeLoad[priorIndex])
                    {
                        wasLoadedBefore = true;
                        break;
                    }
                }

                if (wasLoadedBefore == false)
                {
                    return scene;
                }
            }

            return default;
        }

        private static IEnumerator WaitForTask(Task task)
        {
            float timeoutAt = Time.realtimeSinceStartup + 15f;
            while (task.IsCompleted == false && Time.realtimeSinceStartup < timeoutAt)
            {
                yield return null;
            }

            Assert.That(task.IsCompleted, Is.True, "Unity task did not complete in time.");
        }

        private static IEnumerator WaitForOperation(AsyncOperation operation)
        {
            Assert.That(operation, Is.Not.Null);
            float timeoutAt = Time.realtimeSinceStartup + 15f;
            while (operation.isDone == false && Time.realtimeSinceStartup < timeoutAt)
            {
                yield return null;
            }

            Assert.That(operation.isDone, Is.True, "Unity async operation did not complete in time.");
        }

        private static void SetContextInstallers(
            OnityContext context,
            params MonoInstaller[] installers)
        {
            FieldInfo installersField = typeof(OnityContext).GetField(
                "m_installers",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(installersField, Is.Not.Null);
            installersField.SetValue(context, installers);
        }

        private sealed class TestContext : OnityContext
        {
        }

        private sealed class DelayedBuildInstaller : MonoInstaller
        {
            private readonly TaskCompletionSource<bool> m_completionSource =
                new TaskCompletionSource<bool>();

            public override void InstallBindings(OnityContainer container)
            {
                container.RegisterBuildCallbackAsync(_ => m_completionSource.Task);
            }

            public void CompleteBuild()
            {
                m_completionSource.TrySetResult(true);
            }
        }
    }
}
