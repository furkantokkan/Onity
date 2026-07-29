using NUnit.Framework;
using Onity.DI;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Ports the DI optimization behaviors onto <see cref="OnityContainer" />.
    /// Each test validates the compiled-activator / injection-plan hot path that
    /// shipped in Phase 1.1, focusing on construction correctness, lifetime
    /// caching, member ordering, constructor selection, value-type and
    /// closed-generic argument marshalling, deep graphs, re-entrancy guards, and
    /// steady-state correctness under repeated resolves.
    /// </summary>
    [TestFixture]
    public sealed class OnityBakedContainerTests
    {
        [SetUp]
        public void SetUp()
        {
            OnityContainer.DiagnosticsCollectionEnabled = false;
            ConstructionCounter.Reset();
            MemberOrderCounter.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            OnityContainer.DiagnosticsCollectionEnabled = false;
        }

        [Test]
        public void TransientResolve_CompiledActivator_ProducesDistinctFullyConstructedInstances()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IDependency>().To<Dependency>().AsSingle();
            container.Bind<ServiceWithDependency>().AsTransient();

            ServiceWithDependency first = container.Resolve<ServiceWithDependency>();
            ServiceWithDependency second = container.Resolve<ServiceWithDependency>();

            Assert.That(first, Is.Not.SameAs(second));
            Assert.That(first.Dependency, Is.Not.Null);
            Assert.That(second.Dependency, Is.Not.Null);
            Assert.That(first.Dependency, Is.SameAs(container.Resolve<IDependency>()));
            Assert.That(second.Dependency, Is.SameAs(container.Resolve<IDependency>()));
        }

        [Test]
        public void SingletonResolve_CompiledActivator_CachesSingleInstanceAndConstructsOnce()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<ICountedService>().To<CountedService>().AsSingle();

            ICountedService first = container.Resolve<ICountedService>();
            ICountedService second = container.Resolve<ICountedService>();
            ICountedService third = container.Resolve<ICountedService>();

            Assert.That(first, Is.SameAs(second));
            Assert.That(second, Is.SameAs(third));
            Assert.That(ConstructionCounter.Count, Is.EqualTo(1));
        }

        [Test]
        public void Inject_MemberOrder_RunsFieldsThenPropertiesThenMethods()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<FieldOrderProbe>().AsTransient();
            container.Bind<PropertyOrderProbe>().AsTransient();
            container.Bind<MethodOrderProbe>().AsTransient();

            OrderedInjectionTarget target = new OrderedInjectionTarget();
            container.Inject(target);

            Assert.That(target.FieldOrder, Is.GreaterThanOrEqualTo(0));
            Assert.That(target.PropertyOrder, Is.GreaterThanOrEqualTo(0));
            Assert.That(target.MethodOrder, Is.GreaterThanOrEqualTo(0));
            Assert.That(target.FieldOrder, Is.LessThan(target.PropertyOrder));
            Assert.That(target.PropertyOrder, Is.LessThan(target.MethodOrder));
        }

        [Test]
        public void Resolve_InjectConstructorWithFewerParams_BeatsMorePublicParams()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IDependency>().To<Dependency>().AsSingle();
            container.Bind<ISecondDependency>().To<SecondDependency>().AsSingle();
            container.Bind<FewerParamInjectTarget>().AsTransient();

            FewerParamInjectTarget instance = container.Resolve<FewerParamInjectTarget>();

            Assert.That(instance.UsedInjectConstructor, Is.True);
            Assert.That(instance.ParameterCount, Is.EqualTo(1));
            Assert.That(instance.First, Is.Not.Null);
            Assert.That(instance.Second, Is.Null);
        }

        [Test]
        public void Resolve_ValueTypeConstructorDependency_BoxesAndUnboxesThroughActivator()
        {
            using OnityContainer container = new OnityContainer();
            ValueDependency expected = new ValueDependency(4242);
            container.BindInstance(expected);
            container.Bind<ValueConsumer>().AsTransient();

            ValueConsumer instance = container.Resolve<ValueConsumer>();

            Assert.That(instance.Value.Magnitude, Is.EqualTo(4242));
        }

        [Test]
        public void Resolve_ClosedGenericConstructorDependency_ResolvesAndInjects()
        {
            using OnityContainer container = new OnityContainer();
            IRepository<int> repository = new IntRepository();
            container.BindInstance<IRepository<int>>(repository);
            container.Bind<RepositoryConsumer>().AsTransient();

            RepositoryConsumer instance = container.Resolve<RepositoryConsumer>();

            Assert.That(instance.Repository, Is.SameAs(repository));
            Assert.That(instance.Repository.Single, Is.EqualTo(99));
        }

        [Test]
        public void Resolve_SixLevelDependencyChain_ConstructsEntireGraphCorrectly()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<LevelA>().AsTransient();
            container.Bind<LevelB>().AsTransient();
            container.Bind<LevelC>().AsTransient();
            container.Bind<LevelD>().AsTransient();
            container.Bind<LevelE>().AsTransient();
            container.Bind<LevelF>().AsTransient();

            LevelA root = container.Resolve<LevelA>();

            Assert.That(root, Is.Not.Null);
            Assert.That(root.B, Is.Not.Null);
            Assert.That(root.B.C, Is.Not.Null);
            Assert.That(root.B.C.D, Is.Not.Null);
            Assert.That(root.B.C.D.E, Is.Not.Null);
            Assert.That(root.B.C.D.E.F, Is.Not.Null);
            Assert.That(root.B.C.D.E.F.IsLeaf, Is.True);
        }

        [Test]
        public void Resolve_SelfReferentialConstructorDependency_ThrowsOnityResolveException()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<SelfReferentialService>().AsTransient();

            Assert.That(
                () => container.Resolve<SelfReferentialService>(),
                Throws.TypeOf<OnityResolveException>());
        }

        [Test]
        public void Resolve_RepeatedResolvesOnSameContainer_StayCorrectAcrossMixedLifetimes()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IDependency>().To<Dependency>().AsSingle();
            container.Bind<ISingletonService>().To<SingletonService>().AsSingle();
            container.Bind<TransientService>().AsTransient();

            ISingletonService singletonBaseline = container.Resolve<ISingletonService>();
            TransientService previousTransient = null;
            ImplicitConcreteService previousImplicit = null;

            for (int i = 0; i < 50; i++)
            {
                ISingletonService singleton = container.Resolve<ISingletonService>();
                Assert.That(singleton, Is.Not.Null);
                Assert.That(singleton, Is.SameAs(singletonBaseline));
                Assert.That(singleton.Dependency, Is.Not.Null);

                TransientService transient = container.Resolve<TransientService>();
                Assert.That(transient, Is.Not.Null);
                Assert.That(transient.Dependency, Is.Not.Null);

                if (previousTransient != null)
                {
                    Assert.That(transient, Is.Not.SameAs(previousTransient));
                }

                previousTransient = transient;

                ImplicitConcreteService implicitService = container.Resolve<ImplicitConcreteService>();
                Assert.That(implicitService, Is.Not.Null);
                Assert.That(implicitService.Dependency, Is.Not.Null);

                if (previousImplicit != null)
                {
                    Assert.That(implicitService, Is.Not.SameAs(previousImplicit));
                }

                previousImplicit = implicitService;
            }
        }

        private interface IDependency
        {
        }

        private interface ISecondDependency
        {
        }

        private interface ICountedService
        {
        }

        private interface ISingletonService
        {
            IDependency Dependency { get; }
        }

        private interface IRepository<TItem>
        {
            TItem Single { get; }
        }

        private sealed class Dependency : IDependency
        {
        }

        private sealed class SecondDependency : ISecondDependency
        {
        }

        private sealed class ServiceWithDependency
        {
            public ServiceWithDependency(IDependency dependency)
            {
                Dependency = dependency;
            }

            public IDependency Dependency { get; }
        }

        private sealed class CountedService : ICountedService
        {
            public CountedService()
            {
                ConstructionCounter.Increment();
            }
        }

        private sealed class SingletonService : ISingletonService
        {
            public SingletonService(IDependency dependency)
            {
                Dependency = dependency;
            }

            public IDependency Dependency { get; }
        }

        private sealed class TransientService
        {
            public TransientService(IDependency dependency)
            {
                Dependency = dependency;
            }

            public IDependency Dependency { get; }
        }

        private sealed class ImplicitConcreteService
        {
            public ImplicitConcreteService(IDependency dependency)
            {
                Dependency = dependency;
            }

            public IDependency Dependency { get; }
        }

        private sealed class FieldOrderProbe
        {
            public FieldOrderProbe()
            {
                Order = MemberOrderCounter.Next();
            }

            public int Order { get; }
        }

        private sealed class PropertyOrderProbe
        {
            public PropertyOrderProbe()
            {
                Order = MemberOrderCounter.Next();
            }

            public int Order { get; }
        }

        private sealed class MethodOrderProbe
        {
            public MethodOrderProbe()
            {
                Order = MemberOrderCounter.Next();
            }

            public int Order { get; }
        }

        private sealed class OrderedInjectionTarget
        {
            [Inject]
            private FieldOrderProbe m_fieldProbe = null;

            public int FieldOrder => m_fieldProbe == null ? -1 : m_fieldProbe.Order;

            public int PropertyOrder { get; private set; } = -1;

            public int MethodOrder { get; private set; } = -1;

            [Inject]
            private PropertyOrderProbe PropertyProbe
            {
                set => PropertyOrder = value == null ? -1 : value.Order;
            }

            [Inject]
            private void Initialize(MethodOrderProbe probe)
            {
                MethodOrder = probe == null ? -1 : probe.Order;
            }
        }

        private sealed class FewerParamInjectTarget
        {
            public FewerParamInjectTarget(IDependency first, ISecondDependency second)
            {
                First = first;
                Second = second;
                UsedInjectConstructor = false;
                ParameterCount = 2;
            }

            [Inject]
            public FewerParamInjectTarget(IDependency first)
            {
                First = first;
                Second = null;
                UsedInjectConstructor = true;
                ParameterCount = 1;
            }

            public IDependency First { get; }

            public ISecondDependency Second { get; }

            public bool UsedInjectConstructor { get; }

            public int ParameterCount { get; }
        }

        private readonly struct ValueDependency
        {
            public ValueDependency(int magnitude)
            {
                Magnitude = magnitude;
            }

            public int Magnitude { get; }
        }

        private sealed class ValueConsumer
        {
            public ValueConsumer(ValueDependency value)
            {
                Value = value;
            }

            public ValueDependency Value { get; }
        }

        private sealed class IntRepository : IRepository<int>
        {
            public int Single => 99;
        }

        private sealed class RepositoryConsumer
        {
            public RepositoryConsumer(IRepository<int> repository)
            {
                Repository = repository;
            }

            public IRepository<int> Repository { get; }
        }

        private sealed class LevelA
        {
            public LevelA(LevelB b)
            {
                B = b;
            }

            public LevelB B { get; }
        }

        private sealed class LevelB
        {
            public LevelB(LevelC c)
            {
                C = c;
            }

            public LevelC C { get; }
        }

        private sealed class LevelC
        {
            public LevelC(LevelD d)
            {
                D = d;
            }

            public LevelD D { get; }
        }

        private sealed class LevelD
        {
            public LevelD(LevelE e)
            {
                E = e;
            }

            public LevelE E { get; }
        }

        private sealed class LevelE
        {
            public LevelE(LevelF f)
            {
                F = f;
            }

            public LevelF F { get; }
        }

        private sealed class LevelF
        {
            public bool IsLeaf => true;
        }

        private sealed class SelfReferentialService
        {
            public SelfReferentialService(SelfReferentialService self)
            {
                _ = self;
            }
        }

        private static class ConstructionCounter
        {
            public static int Count { get; private set; }

            public static void Reset()
            {
                Count = 0;
            }

            public static void Increment()
            {
                Count++;
            }
        }

        private static class MemberOrderCounter
        {
            private static int s_order;

            public static void Reset()
            {
                s_order = 0;
            }

            public static int Next()
            {
                return s_order++;
            }
        }
    }
}
