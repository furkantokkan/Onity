using System;
using System.Reflection;
using NUnit.Framework;
using Onity.DI;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Verifies open generic registration: <c>Bind(typeof(IRepository&lt;&gt;)).To(typeof(Repository&lt;&gt;))</c>
    /// makes a later resolve of a closed <c>IRepository&lt;Foo&gt;</c> construct
    /// <c>Repository&lt;Foo&gt;</c> on demand (with its own dependencies injected), honoring
    /// the declared lifetime per closed type and overriding/aggregating across a scope
    /// hierarchy. This is the VContainer/Zenject open-generic feature Onity previously
    /// lacked. (Closed-type construction uses <c>MakeGenericType</c>; on IL2CPP the closed
    /// type must survive AOT stripping.)
    /// </summary>
    [TestFixture]
    public sealed class OnityOpenGenericTests
    {
        [SetUp]
        public void SetUp()
        {
            OnityContainer.DiagnosticsCollectionEnabled = false;
        }

        [Test]
        public void ResolveClosed_FromOpenGenericBinding_ConstructsClosedImplWithDependency()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IClock>().To<Clock>().AsSingle();
            container.Bind(typeof(IRepository<>)).To(typeof(Repository<>)).AsSingle();

            IRepository<int> repository = container.Resolve<IRepository<int>>();

            Assert.That(repository, Is.TypeOf<Repository<int>>());
            Assert.That(((Repository<int>)repository).Clock, Is.Not.Null);
        }

        [Test]
        public void OpenGenericSingleton_SameClosedType_ReturnsSameInstance()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IClock>().To<Clock>().AsSingle();
            container.Bind(typeof(IRepository<>)).To(typeof(Repository<>)).AsSingle();

            IRepository<int> first = container.Resolve<IRepository<int>>();
            IRepository<int> second = container.Resolve<IRepository<int>>();

            Assert.That(first, Is.SameAs(second));
        }

        [Test]
        public void OpenGeneric_DifferentClosedTypes_AreDistinctClosedImplementations()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IClock>().To<Clock>().AsSingle();
            container.Bind(typeof(IRepository<>)).To(typeof(Repository<>)).AsSingle();

            IRepository<int> intRepository = container.Resolve<IRepository<int>>();
            IRepository<string> stringRepository = container.Resolve<IRepository<string>>();

            Assert.That(intRepository, Is.TypeOf<Repository<int>>());
            Assert.That(stringRepository, Is.TypeOf<Repository<string>>());
            Assert.That((object)intRepository, Is.Not.SameAs(stringRepository));
        }

        [Test]
        public void OpenGenericTransient_SameClosedType_ReturnsDistinctInstances()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IClock>().To<Clock>().AsSingle();
            container.Bind(typeof(IRepository<>)).To(typeof(Repository<>)).AsTransient();

            IRepository<int> first = container.Resolve<IRepository<int>>();
            IRepository<int> second = container.Resolve<IRepository<int>>();

            Assert.That(first, Is.Not.SameAs(second));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OpenGenericRebind_AfterClosedResolve_UsesCurrentImplementation(bool useBaked)
        {
            PropertyInfo bakedFlag = typeof(OnityContainer).GetProperty(
                "UseBakedResolve",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(bakedFlag, Is.Not.Null);
            bool originalFlag = (bool)bakedFlag.GetValue(null);

            try
            {
                bakedFlag.SetValue(null, useBaked);
                using OnityContainer container = new OnityContainer();
                container.Bind<IClock>().To<Clock>().AsSingle();
                container.Bind(typeof(IRepository<>)).To(typeof(Repository<>)).AsSingle();
                container.Build();

                Assert.That(container.Resolve<IRepository<int>>(), Is.TypeOf<Repository<int>>());

                container.Bind(typeof(IRepository<>)).To(typeof(AlternateRepository<>)).AsSingle();

                Assert.That(container.Resolve<IRepository<int>>(), Is.TypeOf<AlternateRepository<int>>());
                Assert.That(container.Resolve<IRepository<string>>(), Is.TypeOf<AlternateRepository<string>>());
            }
            finally
            {
                bakedFlag.SetValue(null, originalFlag);
            }
        }

        [Test]
        public void OpenGenericRebind_UpdatesConstructorDependenciesAndLifetime()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IClock>().To<Clock>().AsSingle();
            container.Bind(typeof(IRepository<>)).To(typeof(Repository<>)).AsSingle();
            container.Bind<RepositoryConsumer>().AsTransient();
            container.Build();

            Assert.That(container.Resolve<RepositoryConsumer>().Repository, Is.TypeOf<Repository<int>>());

            container.Bind(typeof(IRepository<>)).To(typeof(AlternateRepository<>)).AsTransient();

            IRepository<int> first = container.Resolve<RepositoryConsumer>().Repository;
            IRepository<int> second = container.Resolve<IRepository<int>>();
            Assert.That(first, Is.TypeOf<AlternateRepository<int>>());
            Assert.That(second, Is.Not.SameAs(first));

            container.Bind(typeof(IRepository<>)).To(typeof(AlternateRepository<>)).AsSingle();

            Assert.That(container.Resolve<RepositoryConsumer>().Repository,
                Is.SameAs(container.Resolve<IRepository<int>>()));
        }

        [Test]
        public void OpenGenericRebind_PreservesExplicitClosedOverrideAndReplacesCollectionEntry()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IClock>().To<Clock>().AsSingle();
            container.Bind(typeof(IRepository<>)).To(typeof(Repository<>)).AsSingle();
            container.Build();
            container.Resolve<IRepository<int>>();
            container.Bind<IRepository<int>>().To<ExplicitRepository<int>>().AsSingle();

            container.Bind(typeof(IRepository<>)).To(typeof(AlternateRepository<>)).AsSingle();

            Assert.That(container.Resolve<IRepository<int>>(), Is.TypeOf<ExplicitRepository<int>>());
            IRepository<int>[] repositories = container.Resolve<IRepository<int>[]>();
            Assert.That(repositories, Has.Length.EqualTo(2));
            Assert.That(repositories[0], Is.TypeOf<AlternateRepository<int>>());
            Assert.That(repositories[1], Is.TypeOf<ExplicitRepository<int>>());
        }

        [Test]
        public void OpenGenericRebind_KeepsOldSingletonOwnedUntilContainerDisposal()
        {
            OnityContainer container = new OnityContainer();
            container.Bind(typeof(IRepository<>)).To(typeof(DisposableRepository<>)).AsSingle();
            DisposableRepository<int> oldInstance =
                (DisposableRepository<int>)container.Resolve<IRepository<int>>();

            container.Bind(typeof(IRepository<>)).To(typeof(AlternateDisposableRepository<>)).AsSingle();
            AlternateDisposableRepository<int> newInstance =
                (AlternateDisposableRepository<int>)container.Resolve<IRepository<int>>();

            Assert.That(oldInstance.DisposeCount, Is.Zero);
            container.Dispose();
            Assert.That(oldInstance.DisposeCount, Is.EqualTo(1));
            Assert.That(newInstance.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void OpenGenericRebind_InvalidClosedType_PreservesExistingRegistration()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IClock>().To<Clock>().AsSingle();
            container.Bind(typeof(IRepository<>)).To(typeof(Repository<>)).AsSingle();
            container.Resolve<IRepository<int>>();
            container.Resolve<IRepository<string>>();

            Assert.That(
                () => container.Bind(typeof(IRepository<>)).To(typeof(StructRepository<>)).AsSingle(),
                Throws.Exception);

            Assert.That(container.Resolve<IRepository<int>>(), Is.TypeOf<Repository<int>>());
            Assert.That(container.Resolve<IRepository<string>>(), Is.TypeOf<Repository<string>>());
        }

        [Test]
        public void OpenGeneric_ConstructorInjectionOfClosedContract_Works()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IClock>().To<Clock>().AsSingle();
            container.Bind(typeof(IRepository<>)).To(typeof(Repository<>)).AsSingle();
            container.Bind<RepositoryConsumer>().AsTransient();

            RepositoryConsumer consumer = container.Resolve<RepositoryConsumer>();

            Assert.That(consumer.Repository, Is.TypeOf<Repository<int>>());
        }

        [Test]
        public void CanResolve_ClosedFormOfOpenGeneric_IsTrue()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IClock>().To<Clock>().AsSingle();
            container.Bind(typeof(IRepository<>)).To(typeof(Repository<>)).AsSingle();

            Assert.That(container.CanResolve(typeof(IRepository<int>)), Is.True);
            Assert.That(container.CanResolve(typeof(IRepository<string>)), Is.True);
        }

        [Test]
        public void ChildContainer_ResolvesAncestorOpenGenericBinding()
        {
            using OnityContainer parent = new OnityContainer();
            parent.Bind<IClock>().To<Clock>().AsSingle();
            parent.Bind(typeof(IRepository<>)).To(typeof(Repository<>)).AsSingle();

            using OnityContainer child = new OnityContainer(parent);

            IRepository<int> repository = child.Resolve<IRepository<int>>();

            Assert.That(repository, Is.TypeOf<Repository<int>>());
        }

        [Test]
        public void BindRuntimeType_ClosedContract_BindsLikeGeneric()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind(typeof(IClock)).To(typeof(Clock)).AsSingle();

            IClock clock = container.Resolve<IClock>();

            Assert.That(clock, Is.TypeOf<Clock>());
        }

        [Test]
        public void RegisterOpenGeneric_ImplementationDoesNotImplementContract_Throws()
        {
            using OnityContainer container = new OnityContainer();

            Assert.That(
                () => container.Bind(typeof(IRepository<>)).To(typeof(Unrelated<>)).AsSingle(),
                Throws.TypeOf<OnityBindingException>());
        }

        [Test]
        public void RegisterOpenGeneric_NonConcreteImplementation_Throws()
        {
            using OnityContainer container = new OnityContainer();

            Assert.That(
                () => container.Bind(typeof(IRepository<>)).To(typeof(IRepository<>)).AsSingle(),
                Throws.TypeOf<OnityBindingException>());
        }

        private interface IClock
        {
        }

        private sealed class Clock : IClock
        {
        }

        private interface IRepository<T>
        {
        }

        private sealed class Repository<T> : IRepository<T>
        {
            public Repository(IClock clock)
            {
                Clock = clock;
            }

            public IClock Clock { get; }
        }

        private sealed class Unrelated<T>
        {
        }

        private sealed class AlternateRepository<T> : IRepository<T>
        {
        }

        private sealed class ExplicitRepository<T> : IRepository<T>
        {
        }

        private sealed class StructRepository<T> : IRepository<T> where T : struct
        {
        }

        private sealed class DisposableRepository<T> : IRepository<T>, IDisposable
        {
            public int DisposeCount { get; private set; }

            public void Dispose()
            {
                DisposeCount++;
            }
        }

        private sealed class AlternateDisposableRepository<T> : IRepository<T>, IDisposable
        {
            public int DisposeCount { get; private set; }

            public void Dispose()
            {
                DisposeCount++;
            }
        }

        private sealed class RepositoryConsumer
        {
            public RepositoryConsumer(IRepository<int> repository)
            {
                Repository = repository;
            }

            public IRepository<int> Repository { get; }
        }
    }
}
