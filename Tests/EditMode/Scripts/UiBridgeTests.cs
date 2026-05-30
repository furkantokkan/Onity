using System;
using NUnit.Framework;
using Onity.DI;
using Onity.Unity.UI;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class UiBridgeTests
    {
        [SetUp]
        public void SetUp()
        {
            OnityUiServiceLocator.Clear();
            OnityUiPresenterFactory.CustomFactory = null;
            OnityUiPresenterFactory.ClearCache();
        }

        [TearDown]
        public void TearDown()
        {
            OnityUiServiceLocator.Clear();
            OnityUiPresenterFactory.CustomFactory = null;
            OnityUiPresenterFactory.ClearCache();
        }

        [Test]
        public void UiServiceLocator_PushResolverScope_ResolvesServiceAndPops()
        {
            using IDisposable scope = OnityUiServiceLocator.PushResolverScope(
                type => type == typeof(ITestService) ? new TestService() : null);

            ITestService resolved = OnityUiServiceLocator.Get<ITestService>();
            Assert.That(resolved, Is.Not.Null);

            scope.Dispose();
            ITestService afterDispose = OnityUiServiceLocator.Get<ITestService>();
            Assert.That(afterDispose, Is.Null);
        }

        [Test]
        public void UiServiceLocator_ResolverStack_UsesTopMostScope()
        {
            using IDisposable firstScope = OnityUiServiceLocator.PushResolverScope(
                type => type == typeof(ITestService) ? new TestService("A") : null);
            using IDisposable secondScope = OnityUiServiceLocator.PushResolverScope(
                type => type == typeof(ITestService) ? new TestService("B") : null);

            ITestService resolved = OnityUiServiceLocator.Get<ITestService>();

            Assert.That(resolved, Is.Not.Null);
            Assert.That(resolved.Name, Is.EqualTo("B"));
        }

        [Test]
        public void UiServiceLocator_ResolverScope_CachesResolvedInstance()
        {
            int resolveCount = 0;

            using IDisposable scope = OnityUiServiceLocator.PushResolverScope(
                type =>
                {
                    if (type != typeof(ITestService))
                    {
                        return null;
                    }

                    resolveCount++;
                    return new TestService("Cached");
                });

            ITestService first = OnityUiServiceLocator.Get<ITestService>();
            ITestService second = OnityUiServiceLocator.Get<ITestService>();

            Assert.That(resolveCount, Is.EqualTo(1));
            Assert.That(first, Is.SameAs(second));
            Assert.That(first.Name, Is.EqualTo("Cached"));
        }

        [Test]
        public void UiPresenterFactory_AutoInjectsOnityUiInjectProperties()
        {
            OnityUiServiceLocator.Register<ITestService>(new TestService());
            OnityUiPresenterFactory.CustomFactory = null;

            PropertyInjectedPresenter presenter = OnityUiPresenterFactory.Create<PropertyInjectedPresenter>();

            Assert.That(presenter, Is.Not.Null);
            Assert.That(presenter.Service, Is.Not.Null);
        }

        [Test]
        public void UiResolverBridge_ResolvesPresentersFromContainer()
        {
            using OnityContainer container = new OnityContainer();
            container.BindInstance<ITestService>(new TestService());
            container.BindInterfacesAndSelfTo<BridgeResolvedPresenter>().AsTransient();
            container.Build();

            using OnityUiResolverBridge bridge = new OnityUiResolverBridge(container);
            BridgeResolvedPresenter presenter = OnityUiPresenterFactory.Create<BridgeResolvedPresenter>();

            Assert.That(presenter, Is.Not.Null);
            Assert.That(presenter.ConstructorService, Is.Not.Null);
            Assert.That(presenter.PropertyService, Is.Not.Null);
        }

        private interface ITestService
        {
            string Name { get; }
        }

        private sealed class TestService : ITestService
        {
            public TestService()
                : this("Default")
            {
            }

            public TestService(string name)
            {
                Name = name;
            }

            public string Name { get; }
        }

        private sealed class PropertyInjectedPresenter : IOnityUiPresenter
        {
            [OnityUiInject]
            public ITestService Service { get; private set; }

            public void SetView(object view)
            {
            }

            public void OnViewOpening()
            {
            }

            public void OnViewOpened()
            {
            }

            public void OnViewClosing()
            {
            }

            public void OnViewClosed()
            {
            }

            public void Dispose()
            {
            }
        }

        private sealed class BridgeResolvedPresenter : IOnityUiPresenter
        {
            public BridgeResolvedPresenter(ITestService constructorService)
            {
                ConstructorService = constructorService;
            }

            public ITestService ConstructorService { get; }

            [OnityUiInject]
            public ITestService PropertyService { get; private set; }

            public void SetView(object view)
            {
            }

            public void OnViewOpening()
            {
            }

            public void OnViewOpened()
            {
            }

            public void OnViewClosing()
            {
            }

            public void OnViewClosed()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
