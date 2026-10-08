using NUnit.Framework;
using Onity.DI;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Ports VContainer registration/injection/scope behaviors onto OnityContainer using only the frozen
    /// public Onity API. Each test mirrors a VContainer scenario from the port manifest while asserting the
    /// equivalent Onity behavior (singleton identity, transient non-identity, injection population, scope
    /// fallback/shadowing, self-resolve, missing-binding and circular-dependency exceptions). Divergences from
    /// VContainer (null instance rejection, resolve-time circular detection, per-child-container scoping) are
    /// asserted against Onity's stricter behavior, as documented in the manifest.
    /// </summary>
    [TestFixture]
    public sealed class OnityVContainerParityTests
    {
        // VContainer: Register<TInterface, TImpl>(Lifetime.Singleton) resolves to the implementation type.
        [Test]
        public void RegisterInterfaceToImplementation_AsSingle_ResolvesImplementationType()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IService>().To<Service>().AsSingle();

            IService resolved = container.Resolve<IService>();

            Assert.That(resolved, Is.TypeOf<Service>());
        }

        // VContainer: Singleton lifetime returns the same instance on repeated resolves (singleton identity).
        [Test]
        public void RegisterInterfaceToImplementation_AsSingle_ReturnsSameInstanceOnRepeatedResolves()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IService>().To<Service>().AsSingle();

            IService first = container.Resolve<IService>();
            IService second = container.Resolve<IService>();

            Assert.That(first, Is.SameAs(second));
        }

        // VContainer: Transient lifetime returns a new instance on each resolve (non-identity).
        [Test]
        public void RegisterInterfaceToImplementation_AsTransient_ReturnsNewInstanceOnEachResolve()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IService>().To<Service>().AsTransient();

            IService first = container.Resolve<IService>();
            IService second = container.Resolve<IService>();

            Assert.That(first, Is.Not.SameAs(second));
        }

        // VContainer: a singleton resolved by interface and by concrete implementation type is the same object.
        [Test]
        public void Singleton_ResolvedByInterfaceAndConcrete_IsSameInstance()
        {
            using OnityContainer container = new OnityContainer();
            container.BindInterfacesAndSelfTo<Service>().AsSingle();

            IService byInterface = container.Resolve<IService>();
            Service byConcrete = container.Resolve<Service>();

            Assert.That(byInterface, Is.SameAs(byConcrete));
        }

        // VContainer: RegisterInstance<T>(instance) resolves the exact same instance.
        [Test]
        public void BindInstance_ResolvesExactSameInstance()
        {
            using OnityContainer container = new OnityContainer();
            Service instance = new Service();
            container.BindInstance<IService>(instance);

            IService resolved = container.Resolve<IService>();

            Assert.That(resolved, Is.SameAs(instance));
        }

        // Divergence from VContainer (which allows null instances): Onity BindInstance(null) throws.
        [Test]
        public void BindInstance_Null_ThrowsBindingException()
        {
            using OnityContainer container = new OnityContainer();

            Assert.That(
                () => container.BindInstance<IService>(null),
                Throws.TypeOf<OnityBindingException>());
        }

        // VContainer: constructor injection supplies dependencies to the single public constructor.
        [Test]
        public void ConstructorInjection_SuppliesDependencyToSingleConstructor()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IDependency>().To<Dependency>().AsSingle();
            container.Bind<ConstructorConsumer>().AsTransient();

            ConstructorConsumer consumer = container.Resolve<ConstructorConsumer>();
            IDependency expectedDependency = container.Resolve<IDependency>();

            Assert.That(consumer.Dependency, Is.Not.Null);
            Assert.That(consumer.Dependency, Is.SameAs(expectedDependency));
        }

        // VContainer: the greediest resolvable constructor is selected when multiple exist (no [Inject] marker).
        [Test]
        public void ConstructorInjection_SelectsGreediestResolvableConstructor()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IDependency>().To<Dependency>().AsSingle();
            container.Bind<GreedyConstructorConsumer>().AsTransient();

            GreedyConstructorConsumer consumer = container.Resolve<GreedyConstructorConsumer>();

            Assert.That(consumer.UsedGreedyConstructor, Is.True);
            Assert.That(consumer.Dependency, Is.Not.Null);
        }

        // VContainer: the [Inject]-marked constructor is chosen over other constructors.
        [Test]
        public void ConstructorInjection_InjectAttributedConstructorIsChosen()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IDependency>().To<Dependency>().AsSingle();
            container.Bind<InjectMarkedConstructorConsumer>().AsTransient();

            InjectMarkedConstructorConsumer consumer = container.Resolve<InjectMarkedConstructorConsumer>();

            Assert.That(consumer.UsedInjectConstructor, Is.True);
            Assert.That(consumer.Dependency, Is.Not.Null);
        }

        // VContainer: an [Inject] method is invoked with resolved arguments after construction.
        [Test]
        public void MethodInjection_InvokesInjectMethodWithResolvedArguments()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IDependency>().To<Dependency>().AsSingle();
            container.Bind<MethodInjectionConsumer>().AsTransient();

            MethodInjectionConsumer consumer = container.Resolve<MethodInjectionConsumer>();
            IDependency expectedDependency = container.Resolve<IDependency>();

            Assert.That(consumer.InjectedDependency, Is.Not.Null);
            Assert.That(consumer.InjectedDependency, Is.SameAs(expectedDependency));
        }

        // VContainer: an [Inject] field is assigned a resolved dependency (non-public field supported).
        [Test]
        public void FieldInjection_AssignsResolvedDependency()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IDependency>().To<Dependency>().AsSingle();
            container.Bind<FieldInjectionConsumer>().AsTransient();

            FieldInjectionConsumer consumer = container.Resolve<FieldInjectionConsumer>();
            IDependency expectedDependency = container.Resolve<IDependency>();

            Assert.That(consumer.Dependency, Is.Not.Null);
            Assert.That(consumer.Dependency, Is.SameAs(expectedDependency));
        }

        // VContainer: an [Inject] property (with setter) is assigned a resolved dependency.
        [Test]
        public void PropertyInjection_AssignsResolvedDependency()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IDependency>().To<Dependency>().AsSingle();
            container.Bind<PropertyInjectionConsumer>().AsTransient();

            PropertyInjectionConsumer consumer = container.Resolve<PropertyInjectionConsumer>();
            IDependency expectedDependency = container.Resolve<IDependency>();

            Assert.That(consumer.Dependency, Is.Not.Null);
            Assert.That(consumer.Dependency, Is.SameAs(expectedDependency));
        }

        // Companion to property injection: an [Inject] property without a setter throws a binding exception.
        [Test]
        public void PropertyInjection_WithoutSetter_ThrowsBindingException()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IDependency>().To<Dependency>().AsSingle();
            container.Bind<SetterlessPropertyConsumer>().AsTransient();

            Assert.That(
                () => container.Resolve<SetterlessPropertyConsumer>(),
                Throws.TypeOf<OnityBindingException>());
        }

        // VContainer: one implementation registered to multiple interfaces via As<>(): each resolves to the same singleton.
        [Test]
        public void MultipleInterfaces_AsSingle_ResolveToSameSingleton()
        {
            using OnityContainer container = new OnityContainer();
            container.BindInterfacesAndSelfTo<MultiContractService>().AsSingle();

            IService asService = container.Resolve<IService>();
            ISecondaryService asSecondary = container.Resolve<ISecondaryService>();
            MultiContractService asConcrete = container.Resolve<MultiContractService>();

            Assert.That(asService, Is.SameAs(asSecondary));
            Assert.That(asService, Is.SameAs(asConcrete));
        }

        // VContainer: a child scope resolves a registration that exists only in the parent.
        [Test]
        public void ChildScope_ResolvesParentOnlyRegistration()
        {
            using OnityContainer parent = new OnityContainer();
            parent.Bind<IDependency>().To<Dependency>().AsSingle();

            using OnityContainer child = new OnityContainer(parent);

            IDependency childResolved = child.Resolve<IDependency>();
            IDependency parentResolved = parent.Resolve<IDependency>();

            Assert.That(childResolved, Is.Not.Null);
            Assert.That(childResolved, Is.SameAs(parentResolved));
        }

        // VContainer: a child-local registration overrides/shadows the parent registration for that type.
        [Test]
        public void ChildScope_LocalRegistrationShadowsParent()
        {
            using OnityContainer parent = new OnityContainer();
            parent.Bind<IDependency>().To<Dependency>().AsSingle();

            using OnityContainer child = new OnityContainer(parent);
            child.Bind<IDependency>().To<AlternateDependency>().AsSingle();

            IDependency childResolved = child.Resolve<IDependency>();
            IDependency parentResolved = parent.Resolve<IDependency>();

            Assert.That(childResolved, Is.TypeOf<AlternateDependency>());
            Assert.That(parentResolved, Is.TypeOf<Dependency>());
        }

        // VContainer: a parent-registered Singleton resolves to the same instance from parent and child.
        [Test]
        public void ChildScope_ParentSingleton_SharedBetweenParentAndChild()
        {
            using OnityContainer parent = new OnityContainer();
            parent.Bind<Dependency>().AsSingle();

            using OnityContainer child = new OnityContainer(parent);

            Dependency parentResolved = parent.Resolve<Dependency>();
            Dependency childResolved = child.Resolve<Dependency>();

            Assert.That(parentResolved, Is.SameAs(childResolved));
        }

        // VContainer: resolving the container/resolver itself returns the active container.
        [Test]
        public void ResolveContainerAndResolver_ReturnsActiveContainer()
        {
            using OnityContainer container = new OnityContainer();

            IResolver asResolver = container.Resolve<IResolver>();
            OnityContainer asContainer = container.Resolve<OnityContainer>();

            Assert.That(asResolver, Is.SameAs(container));
            Assert.That(asContainer, Is.SameAs(container));
        }

        // VContainer: resolving an unregistered interface/abstract type throws.
        [Test]
        public void MissingRegistration_Resolve_ThrowsResolveException()
        {
            using OnityContainer container = new OnityContainer();

            Assert.That(
                () => container.Resolve<IUnregistered>(),
                Throws.TypeOf<OnityResolveException>());
        }

        // Companion to missing registration: TryResolve returns false and out=null.
        [Test]
        public void MissingRegistration_TryResolve_ReturnsFalseAndNull()
        {
            using OnityContainer container = new OnityContainer();

            bool resolved = container.TryResolve(out IUnregistered instance);

            Assert.That(resolved, Is.False);
            Assert.That(instance, Is.Null);
        }

        // VContainer detects circular dependencies at build; Onity detects them at resolve. Either way an exception is thrown.
        [Test]
        public void CircularDependency_Resolve_ThrowsResolveException()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<CircularA>().AsTransient();
            container.Bind<CircularB>().AsTransient();

            Assert.That(
                () => container.Resolve<CircularA>(),
                Throws.TypeOf<OnityResolveException>());
        }

        // VContainer Lifetime.Scoped: shared within a scope, distinct across sibling scopes.
        // Onity has no Scoped lifetime; nearest equivalent is per-child-container AsSingle().
        [Test]
        public void ScopedApproximation_SharedWithinChildAndDistinctAcrossSiblings()
        {
            using OnityContainer parent = new OnityContainer();

            using OnityContainer childA = new OnityContainer(parent);
            childA.Bind<ScopedService>().AsSingle();

            using OnityContainer childB = new OnityContainer(parent);
            childB.Bind<ScopedService>().AsSingle();

            ScopedService childAFirst = childA.Resolve<ScopedService>();
            ScopedService childASecond = childA.Resolve<ScopedService>();
            ScopedService childBInstance = childB.Resolve<ScopedService>();

            Assert.That(childAFirst, Is.SameAs(childASecond));
            Assert.That(childAFirst, Is.Not.SameAs(childBInstance));
        }

        private interface IService
        {
        }

        private interface ISecondaryService
        {
        }

        private interface IDependency
        {
        }

        private interface IUnregistered
        {
        }

        private sealed class Service : IService
        {
        }

        private sealed class MultiContractService : IService, ISecondaryService
        {
        }

        private sealed class Dependency : IDependency
        {
        }

        private sealed class AlternateDependency : IDependency
        {
        }

        private sealed class ScopedService
        {
        }

        private sealed class ConstructorConsumer
        {
            public ConstructorConsumer(IDependency dependency)
            {
                Dependency = dependency;
            }

            public IDependency Dependency { get; }
        }

        private sealed class GreedyConstructorConsumer
        {
            public GreedyConstructorConsumer()
            {
                UsedGreedyConstructor = false;
            }

            public GreedyConstructorConsumer(IDependency dependency)
            {
                Dependency = dependency;
                UsedGreedyConstructor = true;
            }

            public IDependency Dependency { get; }

            public bool UsedGreedyConstructor { get; }
        }

        private sealed class InjectMarkedConstructorConsumer
        {
            public InjectMarkedConstructorConsumer()
            {
                UsedInjectConstructor = false;
            }

            [Inject]
            public InjectMarkedConstructorConsumer(IDependency dependency)
            {
                Dependency = dependency;
                UsedInjectConstructor = true;
            }

            public IDependency Dependency { get; }

            public bool UsedInjectConstructor { get; }
        }

        private sealed class MethodInjectionConsumer
        {
            public IDependency InjectedDependency { get; private set; }

            [Inject]
            private void Initialize(IDependency dependency)
            {
                InjectedDependency = dependency;
            }
        }

        private sealed class FieldInjectionConsumer
        {
            [Inject]
            private IDependency m_dependency = null;

            public IDependency Dependency => m_dependency;
        }

        private sealed class PropertyInjectionConsumer
        {
            [Inject]
            public IDependency Dependency { get; set; }
        }

        private sealed class SetterlessPropertyConsumer
        {
            [Inject]
            public IDependency Dependency => null;
        }

        private sealed class CircularA
        {
            public CircularA(CircularB dependency)
            {
                Dependency = dependency;
            }

            public CircularB Dependency { get; }
        }

        private sealed class CircularB
        {
            public CircularB(CircularA dependency)
            {
                Dependency = dependency;
            }

            public CircularA Dependency { get; }
        }
    }
}
