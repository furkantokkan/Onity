using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.DI;
using Onity.Unity.Async;
using Onity.Unity.Contexts;
using Onity.Unity.Installers;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Onity.Tests.PlayMode
{
    /// <summary>
    /// PlayMode coverage for the context scope lifetime: destroying or unloading a context cancels
    /// its token, the token reaches plain services and async build callbacks, and components find
    /// their owning scope.
    /// </summary>
    [TestFixture]
    public sealed class OnityScopeLifetimePlayModeTests
    {
        private const int k_maximumFrames = 10;

        [UnityTest]
        public IEnumerator DestroyingGameObjectContext_CancelsPlainServicePendingDelay()
        {
            GameObject contextObject = new GameObject(nameof(DestroyingGameObjectContext_CancelsPlainServicePendingDelay));
            contextObject.SetActive(false);
            DelayingServiceInstaller installer = contextObject.AddComponent<DelayingServiceInstaller>();
            GameObjectContext context = contextObject.AddComponent<GameObjectContext>();
            SetContextInstallers(context, installer);

            contextObject.SetActive(true);
            DelayingService service = context.Container.Resolve<DelayingService>();

            yield return null;

            Assert.That(service.IsStarted, Is.True);
            Assert.That(service.IsCanceled, Is.False);

            Object.Destroy(contextObject);

            for (int frame = 0; frame < k_maximumFrames && service.IsCanceled == false; frame++)
            {
                yield return null;
            }

            Assert.That(service.IsCanceled, Is.True);
            Assert.That(service.IsCompleted, Is.False);
        }

        [UnityTest]
        public IEnumerator UnloadingScene_CancelsSceneContextToken()
        {
            Scene scene = SceneManager.CreateScene(
                nameof(UnloadingScene_CancelsSceneContextToken) + Guid.NewGuid().ToString("N"));
            GameObject contextObject = new GameObject(nameof(UnloadingScene_CancelsSceneContextToken));
            SceneManager.MoveGameObjectToScene(contextObject, scene);
            SceneContext context = contextObject.AddComponent<SceneContext>();
            CancellationToken token = context.LifetimeToken;

            yield return null;

            Assert.That(token.IsCancellationRequested, Is.False);

            AsyncOperation unload = SceneManager.UnloadSceneAsync(scene);

            for (int frame = 0; frame < k_maximumFrames && unload.isDone == false; frame++)
            {
                yield return null;
            }

            Assert.That(unload.isDone, Is.True);
            Assert.That(token.IsCancellationRequested, Is.True);
        }

        [UnityTest]
        public IEnumerator DestroyingContextEarly_CancelsRunningBuildCallback()
        {
            GameObject contextObject = new GameObject(nameof(DestroyingContextEarly_CancelsRunningBuildCallback));
            contextObject.SetActive(false);
            PendingBuildInstaller installer = contextObject.AddComponent<PendingBuildInstaller>();
            TestContext context = contextObject.AddComponent<TestContext>();
            SetContextInstallers(context, installer);

            contextObject.SetActive(true);

            yield return null;

            Assert.That(installer.IsStarted, Is.True);
            Assert.That(installer.CallbackToken, Is.EqualTo(context.LifetimeToken));
            Assert.That(context.IsReady, Is.False);

            Object.Destroy(contextObject);

            for (int frame = 0; frame < k_maximumFrames && installer.IsCanceled == false; frame++)
            {
                yield return null;
            }

            Assert.That(installer.IsCanceled, Is.True);
            Assert.That(context.IsReady, Is.False);
            Assert.That(context.ReadyTask.IsCanceled, Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ScopeLifetime_InjectsTheOwningContextContainer()
        {
            GameObject parentObject = new GameObject(nameof(ScopeLifetime_InjectsTheOwningContextContainer));

            try
            {
                parentObject.SetActive(false);
                SceneContext parentContext = parentObject.AddComponent<SceneContext>();
                ParentScopeInstaller parentInstaller = parentObject.AddComponent<ParentScopeInstaller>();
                SetContextInstallers(parentContext, parentInstaller);

                GameObject childObject = new GameObject("Child Context");
                childObject.transform.SetParent(parentObject.transform);
                GameObjectContext childContext = childObject.AddComponent<GameObjectContext>();
                ChildScopeInstaller childInstaller = childObject.AddComponent<ChildScopeInstaller>();
                SetContextInstallers(childContext, childInstaller);

                parentObject.SetActive(true);

                ParentScopeService parentService = childContext.Container.Resolve<ParentScopeService>();
                ChildScopeService childService = childContext.Container.Resolve<ChildScopeService>();

                Assert.That(parentService.Lifetime, Is.SameAs(parentContext.Container));
                Assert.That(childService.Lifetime, Is.SameAs(childContext.Container));
                Assert.That(childService.Lifetime.Token, Is.EqualTo(childContext.LifetimeToken));
                Assert.That(parentContext.LifetimeToken, Is.Not.EqualTo(childContext.LifetimeToken));
            }
            finally
            {
                Object.Destroy(parentObject);
            }
        }

        [Test]
        public void GetScopeCancellationToken_UsesNearestContextOrDestroyToken()
        {
            GameObject parentObject = new GameObject(nameof(GetScopeCancellationToken_UsesNearestContextOrDestroyToken));
            GameObject looseObject = new GameObject("Loose Object");

            try
            {
                parentObject.SetActive(false);
                SceneContext parentContext = parentObject.AddComponent<SceneContext>();

                GameObject childObject = new GameObject("Child Context");
                childObject.transform.SetParent(parentObject.transform);
                GameObjectContext childContext = childObject.AddComponent<GameObjectContext>();

                GameObject grandChild = new GameObject("Grand Child");
                grandChild.transform.SetParent(childObject.transform);
                GameObject sibling = new GameObject("Sibling");
                sibling.transform.SetParent(parentObject.transform);

                parentObject.SetActive(true);

                Assert.That(grandChild.transform.GetScopeCancellationToken(), Is.EqualTo(childContext.LifetimeToken));
                Assert.That(childContext.GetScopeCancellationToken(), Is.EqualTo(childContext.LifetimeToken));
                Assert.That(sibling.transform.GetScopeCancellationToken(), Is.EqualTo(parentContext.LifetimeToken));
                Assert.That(
                    looseObject.transform.GetScopeCancellationToken(),
                    Is.EqualTo(looseObject.transform.GetCancellationTokenOnDestroy()));
            }
            finally
            {
                Object.Destroy(parentObject);
                Object.Destroy(looseObject);
            }
        }

        private static void SetContextInstallers(OnityContext context, params MonoInstaller[] installers)
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

        private sealed class DelayingService : IOnityInitializable
        {
            private readonly IOnityScopeLifetime m_lifetime;

            public DelayingService(IOnityScopeLifetime lifetime)
            {
                m_lifetime = lifetime;
            }

            public bool IsStarted { get; private set; }

            public bool IsCanceled { get; private set; }

            public bool IsCompleted { get; private set; }

            public void Initialize()
            {
                DelayAsync().Forget();
            }

            private async OnityTaskVoid DelayAsync()
            {
                IsStarted = true;

                try
                {
                    await OnityTask.Delay(60f, m_lifetime.Token);
                    IsCompleted = true;
                }
                catch (OperationCanceledException)
                {
                    IsCanceled = true;
                }
            }
        }

        private sealed class DelayingServiceInstaller : MonoInstaller
        {
            public override void InstallBindings(OnityContainer container)
            {
                container.Bind<DelayingService>().AsSingle();
            }
        }

        private sealed class PendingBuildInstaller : MonoInstaller
        {
            public bool IsStarted { get; private set; }

            public bool IsCanceled { get; private set; }

            public CancellationToken CallbackToken { get; private set; }

            public override void InstallBindings(OnityContainer container)
            {
                container.RegisterBuildCallbackAsync(WaitForeverAsync);
            }

            private async Task WaitForeverAsync(IResolver resolver, CancellationToken cancellationToken)
            {
                CallbackToken = cancellationToken;
                IsStarted = true;

                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    IsCanceled = true;
                    throw;
                }
            }
        }

        private sealed class ParentScopeService
        {
            public ParentScopeService(IOnityScopeLifetime lifetime)
            {
                Lifetime = lifetime;
            }

            public IOnityScopeLifetime Lifetime { get; }
        }

        private sealed class ChildScopeService
        {
            public ChildScopeService(IOnityScopeLifetime lifetime)
            {
                Lifetime = lifetime;
            }

            public IOnityScopeLifetime Lifetime { get; }
        }

        private sealed class ParentScopeInstaller : MonoInstaller
        {
            public override void InstallBindings(OnityContainer container)
            {
                container.Bind<ParentScopeService>().AsSingle();
            }
        }

        private sealed class ChildScopeInstaller : MonoInstaller
        {
            public override void InstallBindings(OnityContainer container)
            {
                container.Bind<ChildScopeService>().AsSingle();
            }
        }
    }
}
