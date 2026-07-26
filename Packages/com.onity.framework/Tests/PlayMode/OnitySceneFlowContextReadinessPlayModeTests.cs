using System.Collections;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.DI;
using Onity.Unity.Contexts;
using Onity.Unity.Installers;
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
