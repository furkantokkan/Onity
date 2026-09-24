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
