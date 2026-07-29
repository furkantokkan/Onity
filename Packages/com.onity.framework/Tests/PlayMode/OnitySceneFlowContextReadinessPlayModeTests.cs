using System.Collections;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.DI;
using Onity.Unity.Contexts;
using Onity.Unity.Installers;
using Onity.Unity.SceneFlow;
using UnityEngine;
using UnityEngine.TestTools;

namespace Onity.Tests.PlayMode
{
    [TestFixture]
    public sealed class OnitySceneFlowContextReadinessPlayModeTests
    {
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
