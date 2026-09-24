using System;
using System.Reflection;
using NUnit.Framework;
using Onity.DI;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Asserts that the Phase 1 baked-resolve fast path
    /// (<c>OnityContainer.UseBakedResolve = true</c>) returns results identical to
    /// the proven reflection path (<c>false</c>) across representative graphs:
    /// singletons, transients, deep complex graphs, parent/child scopes, value-type
    /// and closed-generic dependencies, member-injection ordering, and re-entrant
    /// cycle detection. The flag is reflected because it is internal; each test
    /// builds the same graph twice, once per flag value, and compares observable
    /// behavior so the baked path cannot silently diverge from the reflection path
    /// that backs the existing green suite.
    /// </summary>
    [TestFixture]
    public sealed class OnityBakedGraphParityTests
    {
        private static readonly PropertyInfo s_useBakedResolveProperty =
            typeof(OnityContainer).GetProperty(
                "UseBakedResolve",
                BindingFlags.Static | BindingFlags.NonPublic);

        private bool m_originalUseBakedResolve;

        [SetUp]
        public void SetUp()
        {
            Assert.That(
                s_useBakedResolveProperty,
                Is.Not.Null,
                "Internal OnityContainer.UseBakedResolve flag was not found. The baked-resolve lane must expose it.");

            OnityContainer.DiagnosticsCollectionEnabled = false;
            m_originalUseBakedResolve = GetUseBakedResolve();
            ConstructionCounter.Reset();
            MemberOrderCounter.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            // Restore the flag so a baked-path test can never leak true into the
            // rest of the suite, which proves itself on the reflection default.
            SetUseBakedResolve(m_originalUseBakedResolve);
            OnityContainer.DiagnosticsCollectionEnabled = false;
        }

        [Test]
        public void FlagDefault_IsFalse_SoReflectionPathStaysDefault()
        {
            Assert.That(m_originalUseBakedResolve, Is.False);
        }

        [Test]
        public void SingletonResolve_BakedAndReflection_ShareIdentityAndConstructOnce()
        {
            ConstructionCounter.Reset();
            ICountedService reflectionInstance = ResolveSingletonOnce(useBaked: false);
            int reflectionConstructions = ConstructionCounter.Count;

            ConstructionCounter.Reset();
            ICountedService bakedInstance = ResolveSingletonOnce(useBaked: true);
            int bakedConstructions = ConstructionCounter.Count;

            Assert.That(reflectionInstance, Is.Not.Null);
            Assert.That(bakedInstance, Is.Not.Null);
            Assert.That(bakedConstructions, Is.EqualTo(reflectionConstructions));
            Assert.That(bakedConstructions, Is.EqualTo(1));
        }

        [Test]
        public void SingletonResolve_BakedPath_ReturnsSameInstanceAcrossRepeatedResolves()
        {
            SetUseBakedResolve(true);
            using OnityContainer container = new OnityContainer();
            container.Bind<IDependency>().To<Dependency>().AsSingle();
            container.Bind<ISingletonService>().To<SingletonService>().AsSingle();
            container.Build();

            ISingletonService first = container.Resolve<ISingletonService>();
            ISingletonService second = container.Resolve<ISingletonService>();
            ISingletonService third = container.Resolve<ISingletonService>();
            object viaType = container.Resolve(typeof(ISingletonService));

            Assert.That(second, Is.SameAs(first));
            Assert.That(third, Is.SameAs(first));
            Assert.That(viaType, Is.SameAs(first));
            Assert.That(first.Dependency, Is.SameAs(container.Resolve<IDependency>()));
        }

        [Test]
        public void TransientResolve_BakedAndReflection_ProduceDistinctFullyInjectedInstances()
        {
            AssertTransientDistinct(useBaked: false);
            AssertTransientDistinct(useBaked: true);
        }

        [Test]
        public void TransientResolve_BakedPath_SharesInjectedSingletonDependency()
        {
            SetUseBakedResolve(true);
            using OnityContainer container = new OnityContainer();
            container.Bind<IDependency>().To<Dependency>().AsSingle();
            container.Bind<TransientService>().AsTransient();
            container.Build();

            IDependency sharedDependency = container.Resolve<IDependency>();
            TransientService first = container.Resolve<TransientService>();
            TransientService second = container.Resolve<TransientService>();

            Assert.That(first, Is.Not.SameAs(second));
            Assert.That(first.Dependency, Is.SameAs(sharedDependency));
            Assert.That(second.Dependency, Is.SameAs(sharedDependency));
        }

        [Test]
        public void ComplexGraph_BakedAndReflection_ConstructDeepChainConsistently()
        {
            AssertComplexGraph(useBaked: false);
            AssertComplexGraph(useBaked: true);
        }

        [Test]
        public void ValueTypeDependency_BakedAndReflection_MarshalThroughActivatorIdentically()
        {
            int reflectionValue = ResolveValueConsumerMagnitude(useBaked: false);
            int bakedValue = ResolveValueConsumerMagnitude(useBaked: true);

            Assert.That(reflectionValue, Is.EqualTo(4242));
            Assert.That(bakedValue, Is.EqualTo(reflectionValue));
        }

        [Test]
        public void ClosedGenericDependency_BakedAndReflection_ResolveSameBoundInstance()
        {
            AssertClosedGenericResolves(useBaked: false);
            AssertClosedGenericResolves(useBaked: true);
        }

        [Test]
        public void ChildScope_BakedPath_ResolvesParentBindingThroughReflectionFallback()
        {
            SetUseBakedResolve(true);

            using OnityContainer parent = new OnityContainer();
            parent.Bind<IDependency>().To<Dependency>().AsSingle();
            parent.Build();

            using OnityContainer child = new OnityContainer(parent);
            child.Bind<ISingletonService>().To<SingletonService>().AsSingle();
            child.Build();

            IDependency parentDependency = parent.Resolve<IDependency>();
            ISingletonService childService = child.Resolve<ISingletonService>();
            IDependency childResolvedDependency = child.Resolve<IDependency>();

            Assert.That(childService, Is.Not.Null);
            Assert.That(childResolvedDependency, Is.SameAs(parentDependency));
            Assert.That(childService.Dependency, Is.SameAs(parentDependency));
        }

        [Test]
        public void ChildScope_BakedAndReflection_ResolveLocalOverrideOverParentBinding()
        {
            ISingletonService reflectionChild = ResolveChildOverride(useBaked: false, out ISingletonService reflectionParent);
            ISingletonService bakedChild = ResolveChildOverride(useBaked: true, out ISingletonService bakedParent);

            Assert.That(reflectionChild, Is.Not.SameAs(reflectionParent));
            Assert.That(bakedChild, Is.Not.SameAs(bakedParent));
        }

        [Test]
        public void CircularDependency_BakedAndReflection_BothThrowOnityResolveException()
        {
            AssertCircularThrows(useBaked: false);
            AssertCircularThrows(useBaked: true);
        }

        [Test]
        public void InjectOrder_BakedAndReflection_RunFieldsThenPropertiesThenMethods()
        {
            OrderSnapshot reflectionOrder = CaptureInjectOrder(useBaked: false);
            OrderSnapshot bakedOrder = CaptureInjectOrder(useBaked: true);

            AssertFieldPropertyMethodOrder(reflectionOrder);
            AssertFieldPropertyMethodOrder(bakedOrder);
        }

        [Test]
        public void ConstructorSelection_BakedAndReflection_PreferAttributedConstructor()
        {
            FewerParamInjectTarget reflectionTarget = ResolveConstructorSelection(useBaked: false);
            FewerParamInjectTarget bakedTarget = ResolveConstructorSelection(useBaked: true);

            Assert.That(reflectionTarget.ParameterCount, Is.EqualTo(1));
            Assert.That(bakedTarget.ParameterCount, Is.EqualTo(reflectionTarget.ParameterCount));
            Assert.That(bakedTarget.UsedInjectConstructor, Is.EqualTo(reflectionTarget.UsedInjectConstructor));
            Assert.That(bakedTarget.UsedInjectConstructor, Is.True);
            Assert.That(bakedTarget.Second, Is.Null);
        }

        [Test]
        public void TryResolve_BakedPath_MatchesReflectionForBoundAndUnboundContracts()
        {
            bool reflectionBound = TryResolveScenario(useBaked: false, out bool reflectionUnbound);
            bool bakedBound = TryResolveScenario(useBaked: true, out bool bakedUnbound);

            Assert.That(reflectionBound, Is.True);
            Assert.That(bakedBound, Is.EqualTo(reflectionBound));
            Assert.That(reflectionUnbound, Is.False);
            Assert.That(bakedUnbound, Is.EqualTo(reflectionUnbound));
        }

        [Test]
        public void SelfResolve_BakedPath_StillReturnsContainerAndResolver()
        {
            SetUseBakedResolve(true);
            using OnityContainer container = new OnityContainer();
            container.Bind<IDependency>().To<Dependency>().AsSingle();
            container.Build();

            Assert.That(container.Resolve<OnityContainer>(), Is.SameAs(container));
            Assert.That(container.Resolve<IResolver>(), Is.SameAs(container));
        }

        private static ICountedService ResolveSingletonOnce(bool useBaked)
        {
            SetUseBakedResolve(useBaked);
            using OnityContainer container = new OnityContainer();
            container.Bind<ICountedService>().To<CountedService>().AsSingle();
            container.Build();

            ICountedService first = container.Resolve<ICountedService>();
            ICountedService second = container.Resolve<ICountedService>();
            Assert.That(second, Is.SameAs(first));
            return first;
        }

        private static void AssertTransientDistinct(bool useBaked)
        {
            SetUseBakedResolve(useBaked);
            using OnityContainer container = new OnityContainer();
            container.Bind<IDependency>().To<Dependency>().AsSingle();
            container.Bind<ServiceWithDependency>().AsTransient();
            container.Build();

            ServiceWithDependency first = container.Resolve<ServiceWithDependency>();
            ServiceWithDependency second = container.Resolve<ServiceWithDependency>();

            Assert.That(first, Is.Not.SameAs(second), "Transient resolves must be distinct (useBaked={0}).", useBaked);
            Assert.That(first.Dependency, Is.Not.Null);
            Assert.That(second.Dependency, Is.Not.Null);
            Assert.That(first.Dependency, Is.SameAs(second.Dependency));
        }

        private static void AssertComplexGraph(bool useBaked)
        {
            SetUseBakedResolve(useBaked);
            using OnityContainer container = new OnityContainer();
            container.Bind<LevelA>().AsTransient();
            container.Bind<LevelB>().AsTransient();
            container.Bind<LevelC>().AsTransient();
            container.Bind<LevelD>().AsTransient();
            container.Bind<LevelE>().AsTransient();
            container.Bind<LevelF>().AsSingle();
            container.Build();

            LevelA root = container.Resolve<LevelA>();

            Assert.That(root, Is.Not.Null);
            Assert.That(root.B, Is.Not.Null);
            Assert.That(root.B.C, Is.Not.Null);
            Assert.That(root.B.C.D, Is.Not.Null);
            Assert.That(root.B.C.D.E, Is.Not.Null);
            Assert.That(root.B.C.D.E.F, Is.Not.Null);
            Assert.That(root.B.C.D.E.F.IsLeaf, Is.True);

            LevelF singletonLeaf = container.Resolve<LevelF>();
            Assert.That(root.B.C.D.E.F, Is.SameAs(singletonLeaf), "Singleton leaf identity must hold (useBaked={0}).", useBaked);
        }

        private static int ResolveValueConsumerMagnitude(bool useBaked)
        {
            SetUseBakedResolve(useBaked);
            using OnityContainer container = new OnityContainer();
            container.BindInstance(new ValueDependency(4242));
            container.Bind<ValueConsumer>().AsTransient();
            container.Build();

            return container.Resolve<ValueConsumer>().Value.Magnitude;
        }

        private static void AssertClosedGenericResolves(bool useBaked)
        {
            SetUseBakedResolve(useBaked);
            using OnityContainer container = new OnityContainer();
            IRepository<int> repository = new IntRepository();
            container.BindInstance(repository);
            container.Bind<RepositoryConsumer>().AsTransient();
            container.Build();

            RepositoryConsumer consumer = container.Resolve<RepositoryConsumer>();
            Assert.That(consumer.Repository, Is.SameAs(repository));
            Assert.That(consumer.Repository.Single, Is.EqualTo(99));
        }

        private static ISingletonService ResolveChildOverride(bool useBaked, out ISingletonService parentInstance)
        {
            SetUseBakedResolve(useBaked);

            OnityContainer parent = new OnityContainer();
            parent.Bind<IDependency>().To<Dependency>().AsSingle();
            parent.Bind<ISingletonService>().To<SingletonService>().AsSingle();
            parent.Build();

            OnityContainer child = new OnityContainer(parent);
            child.Bind<ISingletonService>().To<AlternateSingletonService>().AsSingle();
            child.Build();

            try
            {
                parentInstance = parent.Resolve<ISingletonService>();
                ISingletonService childInstance = child.Resolve<ISingletonService>();
                Assert.That(childInstance, Is.InstanceOf<AlternateSingletonService>());
                Assert.That(parentInstance, Is.InstanceOf<SingletonService>());
                return childInstance;
            }
            finally
            {
                child.Dispose();
                parent.Dispose();
            }
        }

        private static void AssertCircularThrows(bool useBaked)
        {
            SetUseBakedResolve(useBaked);
            using OnityContainer container = new OnityContainer();
            container.Bind<SelfReferentialService>().AsTransient();
            container.Build();

            Assert.That(
                () => container.Resolve<SelfReferentialService>(),
                Throws.TypeOf<OnityResolveException>(),
                "Circular dependency must throw under useBaked={0}.",
                useBaked);
        }

        private static OrderSnapshot CaptureInjectOrder(bool useBaked)
        {
            SetUseBakedResolve(useBaked);
            MemberOrderCounter.Reset();

            using OnityContainer container = new OnityContainer();
            container.Bind<FieldOrderProbe>().AsTransient();
            container.Bind<PropertyOrderProbe>().AsTransient();
            container.Bind<MethodOrderProbe>().AsTransient();
            container.Build();

            OrderedInjectionTarget target = new OrderedInjectionTarget();
            container.Inject(target);

            return new OrderSnapshot(target.FieldOrder, target.PropertyOrder, target.MethodOrder);
        }

        private static void AssertFieldPropertyMethodOrder(OrderSnapshot snapshot)
        {
            Assert.That(snapshot.FieldOrder, Is.GreaterThanOrEqualTo(0));
            Assert.That(snapshot.PropertyOrder, Is.GreaterThanOrEqualTo(0));
            Assert.That(snapshot.MethodOrder, Is.GreaterThanOrEqualTo(0));
            Assert.That(snapshot.FieldOrder, Is.LessThan(snapshot.PropertyOrder));
            Assert.That(snapshot.PropertyOrder, Is.LessThan(snapshot.MethodOrder));
        }

        private static FewerParamInjectTarget ResolveConstructorSelection(bool useBaked)
        {
            SetUseBakedResolve(useBaked);
            using OnityContainer container = new OnityContainer();
            container.Bind<IDependency>().To<Dependency>().AsSingle();
            container.Bind<ISecondDependency>().To<SecondDependency>().AsSingle();
            container.Bind<FewerParamInjectTarget>().AsTransient();
            container.Build();

            return container.Resolve<FewerParamInjectTarget>();
        }

        private static bool TryResolveScenario(bool useBaked, out bool unboundResolved)
        {
            SetUseBakedResolve(useBaked);
            using OnityContainer container = new OnityContainer();
            container.Bind<IDependency>().To<Dependency>().AsSingle();
            container.Build();

            bool boundResolved = container.TryResolve(out IDependency boundInstance);

            if (boundResolved)
            {
                Assert.That(boundInstance, Is.Not.Null);
            }

            unboundResolved = container.TryResolve(out ISecondDependency unboundInstance);
            Assert.That(unboundInstance, Is.Null);

            return boundResolved;
        }

        private static bool GetUseBakedResolve()
        {
            return (bool)s_useBakedResolveProperty.GetValue(null);
        }

        private static void SetUseBakedResolve(bool value)
        {
            s_useBakedResolveProperty.SetValue(null, value);
        }

        private readonly struct OrderSnapshot
        {
            public OrderSnapshot(int fieldOrder, int propertyOrder, int methodOrder)
            {
                FieldOrder = fieldOrder;
                PropertyOrder = propertyOrder;
                MethodOrder = methodOrder;
            }

            public int FieldOrder { get; }

            public int PropertyOrder { get; }

            public int MethodOrder { get; }
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

        private sealed class AlternateSingletonService : ISingletonService
        {
            public AlternateSingletonService(IDependency dependency)
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

        private sealed class ValueConsumer
        {
            public ValueConsumer(ValueDependency value)
            {
                Value = value;
            }

            public ValueDependency Value { get; }
        }

        private readonly struct ValueDependency
        {
            public ValueDependency(int magnitude)
            {
                Magnitude = magnitude;
            }

            public int Magnitude { get; }
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
