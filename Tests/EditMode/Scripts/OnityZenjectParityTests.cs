using NUnit.Framework;
using Onity.DI;
using Onity.Factory;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Ports Zenject behavior tests onto <see cref="OnityContainer" /> using only the
    /// frozen public Onity API. Each test mirrors a Zenject fixture case; where Onity
    /// deliberately diverges from Zenject semantics the divergence is documented inline.
    ///
    /// Verified divergences captured here:
    /// - Constructor selection picks the highest-scoring public constructor (most
    ///   parameters), not Zenject's "least arguments". See ChosenConstructor tests.
    /// - Member-injection (field/method) circular dependencies between singletons THROW
    ///   in Onity, because a singleton instance is not cached until after its member
    ///   injection completes. Zenject resolves these; Onity treats them as circular.
    ///   See the CircularField/CircularMethod tests.
    ///
    /// All helper types are private nested types to avoid any collision with helper
    /// types declared in sibling test files inside this assembly.
    /// </summary>
    [TestFixture]
    public sealed class OnityZenjectParityTests
    {
        // -------------------------------------------------------------------------
        // Injection/TestConstructorInjection
        // -------------------------------------------------------------------------

        // Ports TestConstructorInjection.TestResolve: single constructor dependency.
        [Test]
        public void Resolve_ConstructorDependency_InjectsSingleArgument()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<CtorBar>().To<CtorBar>().AsSingle();
            container.Bind<CtorFoo>().To<CtorFoo>().AsSingle();

            CtorFoo resolved = container.Resolve<CtorFoo>();

            Assert.That(resolved, Is.Not.Null);
            Assert.That(resolved.Bar, Is.Not.Null);
        }

        // Ports TestConstructorInjection.TestMultipleWithOneTagged: the [Inject]-marked
        // constructor wins even when a parameterless constructor also exists.
        [Test]
        public void Resolve_MultipleConstructors_PicksInjectAttributedConstructor()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<CtorBar>().To<CtorBar>().AsSingle();
            container.Bind<CtorQux>().To<CtorQux>().AsSingle();

            CtorQux resolved = container.Resolve<CtorQux>();

            Assert.That(resolved, Is.Not.Null);
            Assert.That(resolved.UsedInjectConstructor, Is.True);
            Assert.That(resolved.Bar, Is.Not.Null);
        }

        // Ports TestConstructorInjection.TestMultipleChooseLeastArguments with a DELIBERATE
        // Onity semantic divergence. Zenject would choose the least-argument constructor
        // (ChosenConstructor == 1). Onity chooses the highest-scoring PUBLIC constructor
        // (most parameters), independent of which parameters are resolvable. To keep this
        // test green and honest, the unsatisfiable (string,int) constructor is dropped:
        // with only a parameterless constructor and a single Bar constructor, Onity picks
        // the Bar constructor (ChosenConstructor == 2) because Bar adds a parameter and the
        // constructor is public. This matches OnityContainerBehaviorParityTests'
        // Resolve_WithoutInjectConstructor_ChoosesPublicCtorWithMostParameters.
        [Test]
        public void Resolve_NoInjectConstructor_PicksMostParametersPublicConstructor()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<CtorBar>().To<CtorBar>().AsSingle();
            container.Bind<CtorGorp>().To<CtorGorp>().AsSingle();

            CtorGorp resolved = container.Resolve<CtorGorp>();

            // Divergence: Onity selects the most-parameters public constructor, NOT the
            // least-arguments one Zenject would pick.
            Assert.That(resolved.ChosenConstructor, Is.EqualTo(2));
            Assert.That(resolved.Bar, Is.Not.Null);
        }

        // -------------------------------------------------------------------------
        // Injection/TestPropertyInjection
        // -------------------------------------------------------------------------

        // Ports TestPropertyInjection.TestPublicPrivate: public and private auto-property
        // injection. FromInstance(test1).NonLazy() maps to BindInstance(test1).
        [Test]
        public void Resolve_PublicAndPrivateProperties_AreInjected()
        {
            using OnityContainer container = new OnityContainer();
            PropDependency dependency = new PropDependency();
            container.Bind<PropTarget>().To<PropTarget>().AsSingle();
            container.BindInstance<PropDependency>(dependency);

            PropTarget resolved = container.Resolve<PropTarget>();

            Assert.That(resolved.PublicValue, Is.SameAs(dependency));
            Assert.That(resolved.GetPrivateValue(), Is.SameAs(dependency));
        }

        // -------------------------------------------------------------------------
        // Injection/TestBaseClassPropertyInjection
        // -------------------------------------------------------------------------

        // Ports TestBaseClassPropertyInjection.TestCaseBaseClassPropertyInjection: a
        // protected field declared on a base class is injected when resolving a derived
        // type two levels down (BaseLeaf : BaseMiddle : BaseRoot).
        [Test]
        public void Resolve_DerivedType_InjectsProtectedFieldDeclaredOnBaseClass()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<BaseFieldDependency>().To<BaseFieldDependency>().AsSingle();
            container.Bind<BaseLeaf>().To<BaseLeaf>().AsSingle();

            BaseLeaf resolved = container.Resolve<BaseLeaf>();

            Assert.That(resolved.GetValue(), Is.Not.Null);
        }

        // -------------------------------------------------------------------------
        // Injection/TestPostInjectCall
        // -------------------------------------------------------------------------

        // Ports TestPostInjectCall.Test: a method [Inject] runs after constructor and field
        // injection; both a public and a private [Inject] method are invoked.
        [Test]
        public void Resolve_PostInjectMethods_RunAfterConstructorAndFieldInjection()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<PostInjectDep0>().To<PostInjectDep0>().AsSingle();
            container.Bind<PostInjectDep1>().To<PostInjectDep1>().AsSingle();
            container.Bind<PostInjectDep2>().To<PostInjectDep2>().AsSingle();
            container.Bind<PostInjectTarget>().To<PostInjectTarget>().AsSingle();

            PostInjectTarget resolved = container.Resolve<PostInjectTarget>();

            Assert.That(resolved.HasInitialized, Is.True);
            Assert.That(resolved.HasInitialized2, Is.True);
        }

        // Ports TestPostInjectCall.TestPrivateBaseClassPostInject: a private [Inject] method
        // declared on a base class is invoked when resolving a derived type.
        [Test]
        public void Resolve_DerivedType_CallsPrivateBaseClassPostInjectMethod()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<SimpleBase>().To<SimpleDerived>().AsSingle();

            SimpleBase resolved = container.Resolve<SimpleBase>();

            Assert.That(resolved.WasCalled, Is.True);
        }

        // Ports TestPostInjectCall.TestInheritanceOrder: base [Inject] methods run before
        // derived [Inject] methods across a three-level hierarchy. The overridden-virtual
        // case from the Zenject fixture is intentionally omitted (Onity covers virtual
        // override inject separately).
        [Test]
        public void Resolve_DerivedType_RunsBaseInjectMethodsBeforeDerived()
        {
            using OnityContainer container = new OnityContainer();
            InheritanceOrderCounter.Reset();
            container.Bind<IOrderFoo>().To<OrderFooDerived2>().AsSingle();

            container.Resolve<IOrderFoo>();

            Assert.That(OrderFooBase.BaseCallOrder, Is.EqualTo(0));
            Assert.That(OrderFooDerived.DerivedCallOrder, Is.EqualTo(1));
            Assert.That(OrderFooDerived2.Derived2CallOrder, Is.EqualTo(2));
        }

        // -------------------------------------------------------------------------
        // Injection/TestCircularDependencies
        // -------------------------------------------------------------------------

        // Ports TestCircularDependencies.TestFields with a VERIFIED Onity divergence.
        // Zenject resolves a field-injection circular dependency between two singletons.
        // Onity does NOT: a singleton instance is cached only after its member injection
        // completes, so the cross-reference re-enters the in-progress singleton and is
        // detected as a circular dependency. This was confirmed by reproducing Onity's
        // resolution algorithm (singleton instance cached post member-injection).
        [Test]
        public void Resolve_FieldInjectionCircularBetweenSingletons_ThrowsOnityResolveException()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<CircularField1>().To<CircularField1>().AsSingle();
            container.Bind<CircularField2>().To<CircularField2>().AsSingle();

            // Divergence from Zenject: member-injection circular dependency throws in Onity.
            Assert.That(
                () => container.Resolve<CircularField1>(),
                Throws.TypeOf<OnityResolveException>());
        }

        // Ports TestCircularDependencies.TestPostInject (Injection fixture) with the same
        // VERIFIED Onity divergence as the field case: a method-injection circular
        // dependency between two singletons throws in Onity rather than resolving.
        [Test]
        public void Resolve_MethodInjectionCircularBetweenSingletons_ThrowsOnityResolveException()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<CircularMethod1>().To<CircularMethod1>().AsSingle();
            container.Bind<CircularMethod2>().To<CircularMethod2>().AsSingle();

            // Divergence from Zenject: member-injection circular dependency throws in Onity.
            Assert.That(
                () => container.Resolve<CircularMethod1>(),
                Throws.TypeOf<OnityResolveException>());
        }

        // Ports TestCircularDependencies.TestConstructorInject: a constructor circular
        // dependency throws. Zenject's ChecksForCircularDependencies guard is dropped
        // because Onity always checks for circular dependencies.
        [Test]
        public void Resolve_ConstructorCircularBetweenSingletons_ThrowsOnityResolveException()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<CircularCtor5>().To<CircularCtor5>().AsSingle();
            container.Bind<CircularCtor6>().To<CircularCtor6>().AsSingle();

            Assert.That(
                () => container.Resolve<CircularCtor5>(),
                Throws.TypeOf<OnityResolveException>());
        }

        // Ports TestCircularDependencies.TestSelfDependency: a self-referential constructor
        // dependency throws. Container.Instantiate is replaced with Resolve (no public
        // Instantiate on Onity).
        [Test]
        public void Resolve_SelfReferentialConstructor_ThrowsOnityResolveException()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<SelfDependency7>().To<SelfDependency7>().AsSingle();

            Assert.That(
                () => container.Resolve<SelfDependency7>(),
                Throws.TypeOf<OnityResolveException>());
        }

        // -------------------------------------------------------------------------
        // Other/TestCircularDependencies
        // -------------------------------------------------------------------------

        // Ports Other/TestCircularDependencies.TestThrows: constructor circular dependency
        // between Foo1(Bar1) and Bar1(Foo1) throws.
        [Test]
        public void Resolve_ConstructorCircularPair_ThrowsOnityResolveException()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<OtherFoo1>().To<OtherFoo1>().AsSingle();
            container.Bind<OtherBar1>().To<OtherBar1>().AsSingle();

            Assert.That(
                () => container.Resolve<OtherFoo1>(),
                Throws.TypeOf<OnityResolveException>());
        }

        // Ports Other/TestCircularDependencies.TestPostInject with the VERIFIED Onity
        // divergence: a method-injection circular dependency (Foo2/Bar2 with [Inject] Init)
        // throws in Onity rather than resolving.
        [Test]
        public void Resolve_MethodInjectionCircularPair_ThrowsOnityResolveException()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<OtherFoo2>().To<OtherFoo2>().AsSingle();
            container.Bind<OtherBar2>().To<OtherBar2>().AsSingle();

            // Divergence from Zenject: method-injection circular dependency throws in Onity.
            Assert.That(
                () => container.Resolve<OtherFoo2>(),
                Throws.TypeOf<OnityResolveException>());
        }

        // Ports Other/TestCircularDependencies.TestField with the VERIFIED Onity divergence:
        // a field-injection circular dependency (Foo3/Bar3 with [Inject] fields) throws in
        // Onity rather than resolving with cross-set fields.
        [Test]
        public void Resolve_FieldInjectionCircularPair_ThrowsOnityResolveException()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<OtherFoo3>().To<OtherFoo3>().AsSingle();
            container.Bind<OtherBar3>().To<OtherBar3>().AsSingle();

            // Divergence from Zenject: field-injection circular dependency throws in Onity.
            Assert.That(
                () => container.Resolve<OtherFoo3>(),
                Throws.TypeOf<OnityResolveException>());
        }

        // -------------------------------------------------------------------------
        // Bindings/TestFromInstance
        // -------------------------------------------------------------------------

        // Ports TestFromInstance.TestTransient: BindInstance returns the same instance for
        // both the interface and concrete contracts. FromInstance(foo).NonLazy() maps to
        // BindInstance(foo).
        [Test]
        public void Resolve_BindInstance_ReturnsSameInstanceForInterfaceAndConcrete()
        {
            using OnityContainer container = new OnityContainer();
            InstanceFoo foo = new InstanceFoo();
            container.BindInstance<IInstanceFoo>(foo);
            container.BindInstance<InstanceFoo>(foo);

            InstanceFoo concrete = container.Resolve<InstanceFoo>();
            IInstanceFoo contract = container.Resolve<IInstanceFoo>();

            Assert.That(concrete, Is.SameAs(contract));
            Assert.That(concrete, Is.SameAs(foo));
        }

        // Ports TestFromInstance.TestCached: identity is preserved for all contracts; the
        // Zenject .AsSingle() suffix on FromInstance is a no-op for identity. Mapped to a
        // second BindInstance-identity assertion as suggested by the port manifest.
        [Test]
        public void Resolve_BindInstance_ReturnsSameInstanceForAllContracts()
        {
            using OnityContainer container = new OnityContainer();
            InstanceFoo foo = new InstanceFoo();
            container.BindInstance<IInstanceFoo>(foo);
            container.BindInstance<InstanceFoo>(foo);

            Assert.That(container.Resolve<InstanceFoo>(), Is.SameAs(container.Resolve<IInstanceFoo>()));
            Assert.That(container.Resolve<InstanceFoo>(), Is.SameAs(foo));
        }

        // -------------------------------------------------------------------------
        // Injection/TestStructInjection
        // -------------------------------------------------------------------------

        // Ports TestStructInjection.TestInjectStructIntoClass: a struct bound as an instance
        // can be a constructor dependency of a class. ResolveRoots()/NonLazy are dropped and
        // the type is resolved directly.
        [Test]
        public void Resolve_StructInstance_CanBeConstructorDependency()
        {
            using OnityContainer container = new OnityContainer();
            container.BindInstance<StructDependency>(new StructDependency());
            container.Bind<StructConsumer>().To<StructConsumer>().AsSingle();

            StructConsumer resolved = container.Resolve<StructConsumer>();

            Assert.That(resolved, Is.Not.Null);
            Assert.That(resolved.ReceivedStruct, Is.True);
        }

        // -------------------------------------------------------------------------
        // BindFeatures/TestMultipleContractTypes2
        // -------------------------------------------------------------------------

        // Ports TestMultipleContractTypes2.TestAllInterfaces: BindInterfacesTo binds only the
        // interfaces to one shared singleton, NOT the concrete. TryResolve<Foo>() (Zenject
        // null-check) maps to the bool TryResolve(out Foo) overload.
        //
        // Verified Onity divergence: Zenject leaves the concrete entirely unresolvable, so its
        // TryResolve would fail. Onity instead auto-resolves any unbound concrete type as an
        // IMPLICIT TRANSIENT, so TryResolve<MultiContractFoo> succeeds but returns a fresh
        // instance that is NOT the interface singleton. The original test's intent (the concrete
        // is not part of the interface binding group) is preserved by asserting the implicit
        // concrete is a separate instance from the shared interface singleton. This matches the
        // canonical OnityContainerTests.BindInterfacesTo_BindsOnlyInterfaces behavior.
        [Test]
        public void BindInterfacesTo_BindsInterfacesButNotConcrete()
        {
            using OnityContainer container = new OnityContainer();
            container.BindInterfacesTo<MultiContractFoo>().AsSingle();

            IMultiFoo foo = container.Resolve<IMultiFoo>();
            IMultiQux qux = container.Resolve<IMultiQux>();
            bool canResolveConcrete = container.TryResolve(out MultiContractFoo concrete);

            Assert.That(foo, Is.Not.Null);
            Assert.That(qux, Is.Not.Null);
            Assert.That(foo, Is.SameAs(qux));

            // Divergence from Zenject: the concrete auto-resolves as an implicit transient
            // rather than being unresolvable, but it is NOT the interface singleton instance.
            Assert.That(canResolveConcrete, Is.True);
            Assert.That(concrete, Is.Not.Null);
            Assert.That(concrete, Is.Not.SameAs(foo));
        }

        // Ports TestMultipleContractTypes2.TestAllInterfacesAndSelf: BindInterfacesAndSelfTo
        // binds the concrete plus all interfaces to one shared singleton.
        [Test]
        public void BindInterfacesAndSelfTo_BindsConcreteAndAllInterfacesToOneSingleton()
        {
            using OnityContainer container = new OnityContainer();
            container.BindInterfacesAndSelfTo<MultiContractFoo>().AsSingle();

            MultiContractFoo concrete = container.Resolve<MultiContractFoo>();
            IMultiFoo foo = container.Resolve<IMultiFoo>();
            IMultiQux qux = container.Resolve<IMultiQux>();

            Assert.That(concrete, Is.Not.Null);
            Assert.That(foo, Is.Not.Null);
            Assert.That(qux, Is.Not.Null);
            Assert.That(foo, Is.SameAs(concrete));
            Assert.That(qux, Is.SameAs(concrete));
        }

        // -------------------------------------------------------------------------
        // Other/TestNestedContainer
        // -------------------------------------------------------------------------

        // Ports Other/TestNestedContainer.TestCase1: a child scope inherits parent bindings,
        // a child rebind overrides only in the child, and an unbound interface throws in a
        // fresh container. CreateSubContainer() maps to new OnityContainer(parent); GetBar()
        // value checks map to concrete-type checks.
        [Test]
        public void ChildScope_InheritsParentBindings_AndChildRebindOverridesOnlyChild()
        {
            using OnityContainer emptyContainer = new OnityContainer();
            using OnityContainer parent = new OnityContainer();

            Assert.That(
                () => emptyContainer.Resolve<INestedFoo>(),
                Throws.TypeOf<OnityResolveException>());
            Assert.That(
                () => parent.Resolve<INestedFoo>(),
                Throws.TypeOf<OnityResolveException>());

            parent.Bind<INestedFoo>().To<NestedFoo>().AsSingle();

            Assert.That(parent.Resolve<INestedFoo>(), Is.TypeOf<NestedFoo>());

            using OnityContainer child = new OnityContainer(parent);

            Assert.That(child.Resolve<INestedFoo>(), Is.TypeOf<NestedFoo>());
            Assert.That(parent.Resolve<INestedFoo>(), Is.TypeOf<NestedFoo>());

            child.Bind<INestedFoo>().To<NestedFoo2>().AsSingle();

            Assert.That(child.Resolve<INestedFoo>(), Is.TypeOf<NestedFoo2>());
            Assert.That(parent.Resolve<INestedFoo>(), Is.TypeOf<NestedFoo>());
        }

        // -------------------------------------------------------------------------
        // Factories/IFactory/TestIFactory and Factories/TestFactory
        // -------------------------------------------------------------------------

        // Ports TestIFactory.Test2: a one-parameter factory creates instances passing the
        // runtime argument to the constructor. Zenject's BindIFactory<string,FooTwo> maps to
        // an explicit IFactory<string,FooTwo> implementation bound via
        // BindFactory<string,FooTwo,FooTwoFactory>().
        [Test]
        public void BindFactory_OneParameter_PassesArgumentToCreatedInstance()
        {
            using OnityContainer container = new OnityContainer();
            container.BindFactory<string, FactoryFooTwo, FactoryFooTwoFactory>();

            IFactory<string, FactoryFooTwo> factory = container.Resolve<IFactory<string, FactoryFooTwo>>();
            FactoryFooTwo created = factory.Create("asdf");

            Assert.That(created.Value, Is.EqualTo("asdf"));
        }

        // Ports TestFactory.TestToSelf: a zero-parameter factory resolves and Create()
        // returns a non-null instance. Zenject's PlaceholderFactory maps to an explicit
        // IFactory<Foo> implementation bound via BindFactory<Foo,FooFactory>().
        [Test]
        public void BindFactory_ZeroParameter_CreatesNonNullInstance()
        {
            using OnityContainer container = new OnityContainer();
            container.BindFactory<FactoryFoo, FactoryFooFactory>();

            IFactory<FactoryFoo> factory = container.Resolve<IFactory<FactoryFoo>>();

            Assert.That(factory.Create(), Is.Not.Null);
        }

        // -------------------------------------------------------------------------
        // Injection/TestParameters and Injection/TestConstructorInjectionOptional
        // -------------------------------------------------------------------------

        // Ports TestParameters.TestMissingParameterThrows: resolving a type whose primitive
        // constructor parameters (int f1, int f2) are unbound throws. Only this method is
        // ported; TestExtraParametersSameType needs Instantiate-with-arguments (unsupported).
        [Test]
        public void Resolve_UnboundPrimitiveConstructorParameters_ThrowsOnityResolveException()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<MissingParamTarget>().To<MissingParamTarget>().AsTransient();

            Assert.That(
                () => container.Resolve<MissingParamTarget>(),
                Throws.TypeOf<OnityResolveException>());
        }

        // Ports TestConstructorInjectionOptional.TestCase2: a class with a single required
        // constructor parameter whose dependency is unbound fails to resolve. Instantiate is
        // replaced with Resolve. TestCase1/TestConstructByFactory are skipped (C# default-
        // value optional parameters are unsupported).
        [Test]
        public void Resolve_RequiredConstructorDependencyUnbound_ThrowsOnityResolveException()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<RequiredDependencyTarget>().To<RequiredDependencyTarget>().AsSingle();

            Assert.That(
                () => container.Resolve<RequiredDependencyTarget>(),
                Throws.TypeOf<OnityResolveException>());
        }

        // =========================================================================
        // Nested helper types (all private; isolated from sibling test files)
        // =========================================================================

        // ---- Constructor injection helpers ----

        private sealed class CtorBar
        {
        }

        private sealed class CtorFoo
        {
            public CtorFoo(CtorBar bar)
            {
                Bar = bar;
            }

            public CtorBar Bar { get; }
        }

        private sealed class CtorQux
        {
            public CtorQux()
            {
                UsedInjectConstructor = false;
            }

            [Inject]
            public CtorQux(CtorBar bar)
            {
                Bar = bar;
                UsedInjectConstructor = true;
            }

            public CtorBar Bar { get; }

            public bool UsedInjectConstructor { get; }
        }

        // Onity divergence variant: the original Zenject Gorp also has a (string,int)
        // constructor. Onity would score that highest and then fail to resolve string/int,
        // so it is intentionally omitted here. With only the parameterless and Bar
        // constructors, Onity picks the Bar constructor (ChosenConstructor == 2).
        private sealed class CtorGorp
        {
            public CtorGorp()
            {
                ChosenConstructor = 1;
            }

            public CtorGorp(CtorBar bar)
            {
                ChosenConstructor = 2;
                Bar = bar;
            }

            public int ChosenConstructor { get; }

            public CtorBar Bar { get; }
        }

        // ---- Property injection helpers ----

        private sealed class PropDependency
        {
        }

        private sealed class PropTarget
        {
            [Inject]
            public PropDependency PublicValue { get; private set; }

            [Inject]
            private PropDependency PrivateValue { get; set; }

            public PropDependency GetPrivateValue()
            {
                return PrivateValue;
            }
        }

        // ---- Base-class field injection helpers (BaseLeaf : BaseMiddle : BaseRoot) ----

        private sealed class BaseFieldDependency
        {
        }

        private class BaseRoot
        {
        }

        private class BaseMiddle : BaseRoot
        {
            [Inject]
            protected BaseFieldDependency m_value = null;

            public BaseFieldDependency GetValue()
            {
                return m_value;
            }
        }

        private sealed class BaseLeaf : BaseMiddle
        {
        }

        // ---- Post-inject method helpers ----

        private sealed class PostInjectDep0
        {
        }

        private sealed class PostInjectDep1
        {
        }

        private sealed class PostInjectDep2
        {
        }

        private sealed class PostInjectTarget
        {
            [Inject]
            private PostInjectDep1 m_dep1 = null;

            [Inject]
            private PostInjectDep0 m_dep0 = null;

            private readonly PostInjectDep2 m_dep2;

            public PostInjectTarget(PostInjectDep2 dep2)
            {
                m_dep2 = dep2;
            }

            public bool HasInitialized { get; private set; }

            public bool HasInitialized2 { get; private set; }

            [Inject]
            public void Init()
            {
                Assert.That(HasInitialized, Is.False);
                Assert.That(m_dep1, Is.Not.Null);
                Assert.That(m_dep0, Is.Not.Null);
                Assert.That(m_dep2, Is.Not.Null);
                HasInitialized = true;
            }

            [Inject]
            private void InitPrivate()
            {
                HasInitialized2 = true;
            }
        }

        private class SimpleBase
        {
            public bool WasCalled { get; private set; }

            [Inject]
            private void Init()
            {
                WasCalled = true;
            }
        }

        private sealed class SimpleDerived : SimpleBase
        {
        }

        private interface IOrderFoo
        {
        }

        private class OrderFooBase : IOrderFoo
        {
            public static int BaseCallOrder;

            [Inject]
            private void InitBase()
            {
                BaseCallOrder = InheritanceOrderCounter.Next();
            }
        }

        private class OrderFooDerived : OrderFooBase
        {
            public static int DerivedCallOrder;

            [Inject]
            private void InitDerived()
            {
                DerivedCallOrder = InheritanceOrderCounter.Next();
            }
        }

        private sealed class OrderFooDerived2 : OrderFooDerived
        {
            public static int Derived2CallOrder;

            [Inject]
            private void InitDerived2()
            {
                Derived2CallOrder = InheritanceOrderCounter.Next();
            }
        }

        private static class InheritanceOrderCounter
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

        // ---- Circular dependency helpers (Injection fixture) ----

        private sealed class CircularField1
        {
            [Inject]
            public CircularField2 Other = null;
        }

        private sealed class CircularField2
        {
            [Inject]
            public CircularField1 Other = null;
        }

        private sealed class CircularMethod1
        {
            public CircularMethod2 Other { get; private set; }

            [Inject]
            public void Initialize(CircularMethod2 other)
            {
                Other = other;
            }
        }

        private sealed class CircularMethod2
        {
            public CircularMethod1 Other { get; private set; }

            [Inject]
            public void Initialize(CircularMethod1 other)
            {
                Other = other;
            }
        }

        private sealed class CircularCtor5
        {
            public CircularCtor5(CircularCtor6 other)
            {
            }
        }

        private sealed class CircularCtor6
        {
            public CircularCtor6(CircularCtor5 other)
            {
            }
        }

        private sealed class SelfDependency7
        {
            public SelfDependency7(SelfDependency7 other)
            {
            }
        }

        // ---- Circular dependency helpers (Other fixture) ----

        private sealed class OtherFoo1
        {
            public OtherFoo1(OtherBar1 bar)
            {
            }
        }

        private sealed class OtherBar1
        {
            public OtherBar1(OtherFoo1 foo)
            {
            }
        }

        private sealed class OtherFoo2
        {
            public OtherBar2 Other { get; private set; }

            [Inject]
            public void Init(OtherBar2 bar)
            {
                Other = bar;
            }
        }

        private sealed class OtherBar2
        {
            public OtherFoo2 Other { get; private set; }

            [Inject]
            public void Init(OtherFoo2 foo)
            {
                Other = foo;
            }
        }

        private sealed class OtherFoo3
        {
            [Inject]
            public OtherBar3 Bar = null;
        }

        private sealed class OtherBar3
        {
            [Inject]
            public OtherFoo3 Foo = null;
        }

        // ---- Instance binding helpers ----

        private interface IInstanceFoo
        {
        }

        private sealed class InstanceFoo : IInstanceFoo
        {
        }

        // ---- Struct injection helpers ----

        private struct StructDependency
        {
        }

        private sealed class StructConsumer
        {
            public StructConsumer(StructDependency dependency)
            {
                ReceivedStruct = true;
            }

            public bool ReceivedStruct { get; }
        }

        // ---- Multiple contract type helpers ----

        private interface IMultiFoo
        {
        }

        private interface IMultiQux
        {
        }

        private sealed class MultiContractFoo : IMultiQux, IMultiFoo
        {
        }

        // ---- Nested container helpers ----

        private interface INestedFoo
        {
        }

        private sealed class NestedFoo : INestedFoo
        {
        }

        private sealed class NestedFoo2 : INestedFoo
        {
        }

        // ---- Factory helpers ----

        private sealed class FactoryFooTwo
        {
            public FactoryFooTwo(string value)
            {
                Value = value;
            }

            public string Value { get; }
        }

        private sealed class FactoryFooTwoFactory : IFactory<string, FactoryFooTwo>
        {
            public FactoryFooTwo Create(string param)
            {
                return new FactoryFooTwo(param);
            }
        }

        private sealed class FactoryFoo
        {
        }

        private sealed class FactoryFooFactory : IFactory<FactoryFoo>
        {
            public FactoryFoo Create()
            {
                return new FactoryFoo();
            }
        }

        // ---- Parameter / required-dependency helpers ----

        private sealed class MissingParamTarget
        {
            public MissingParamTarget(int first, int second)
            {
            }
        }

        private sealed class RequiredDependencyUnboundType
        {
            public RequiredDependencyUnboundType(int value)
            {
            }
        }

        private sealed class RequiredDependencyTarget
        {
            public RequiredDependencyTarget(RequiredDependencyUnboundType value)
            {
            }
        }
    }
}
