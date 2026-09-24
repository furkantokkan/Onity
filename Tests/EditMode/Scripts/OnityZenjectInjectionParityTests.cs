using NUnit.Framework;
using Onity.DI;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Parity port of Zenject's "Injection" unit-test category onto OnityContainer.
    /// Each test mirrors the original Zenject assertion intent using only the frozen
    /// Onity public API. Where Onity behavior diverges from Zenject, the test asserts
    /// Onity's real behavior with an inline divergence note.
    /// </summary>
    [TestFixture]
    public sealed class OnityZenjectInjectionParityTests
    {
        [SetUp]
        public void SetUp()
        {
            OnityContainer.DiagnosticsCollectionEnabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            OnityContainer.DiagnosticsCollectionEnabled = false;
        }

        // --- TestAllInjectionTypes.TestCase1 (adapted) -------------------------------
        // Original asserts every injection variation including static members staying
        // null and post-inject ordering counters. Onity supports instance field/
        // property/method injection across base + derived (public/private/protected)
        // and never injects static members. Ordering counters are a Zenject-specific
        // sequencing detail not part of Onity's contract, so this asserts the supported
        // core: all instance members injected, statics untouched, methods invoked.
        [Test]
        public void AllInjectionTypes_InstanceMembersInjected_StaticsUntouched()
        {
            using OnityContainer container = new OnityContainer();
            container.BindInstance(new Test0());
            container.Bind<IFoo>().To<FooDerived>().AsSingle();

            IFoo foo = container.Resolve<IFoo>();

            Assert.That(foo.DidPostInjectBase, Is.True);
            Assert.That(foo.DidPostInjectDerived, Is.True);
            Assert.That(foo.AllBaseInstanceMembersInjected, Is.True);
            Assert.That(foo.AllDerivedInstanceMembersInjected, Is.True);
            // Divergence: Onity injects only instance members; static fields/properties stay null.
            Assert.That(FooDerived.DerivedStaticFieldPublic, Is.Null);
            Assert.That(FooBase.BaseStaticFieldPublic, Is.Null);
        }

        // --- TestBaseClassPropertyInjection.TestCaseBaseClassPropertyInjection --------
        [Test]
        public void BaseClassFieldInjection_InjectedThroughGrandchild()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<BaseInjectTarget0>().To<BaseInjectTarget0>().AsSingle();
            container.Bind<BaseInjectLeaf>().To<BaseInjectLeaf>().AsSingle();

            BaseInjectLeaf leaf = container.Resolve<BaseInjectLeaf>();

            Assert.That(leaf.GetVal(), Is.Not.Null);
        }

        // --- TestCircularDependencies.TestFields -------------------------------------
        // Divergence: Zenject resolves field-based circular dependencies between two
        // singletons. Onity instantiates then injects (singleton instance is published
        // only after full construction), so a field-circular graph re-enters and throws.
        [Test]
        public void FieldCircularDependency_Throws()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<CircularField1>().To<CircularField1>().AsSingle();
            container.Bind<CircularField2>().To<CircularField2>().AsSingle();

            Assert.That(() => container.Resolve<CircularField1>(), Throws.TypeOf<OnityResolveException>());
        }

        // --- TestCircularDependencies.TestPostInject ---------------------------------
        // Divergence: same as field circular — Onity throws on method-injected circular
        // singletons rather than wiring them up post-construction like Zenject.
        [Test]
        public void MethodCircularDependency_Throws()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<CircularMethod3>().To<CircularMethod3>().AsSingle();
            container.Bind<CircularMethod4>().To<CircularMethod4>().AsSingle();

            Assert.That(() => container.Resolve<CircularMethod3>(), Throws.TypeOf<OnityResolveException>());
        }

        // --- TestCircularDependencies.TestConstructorInject --------------------------
        [Test]
        public void ConstructorCircularDependency_Throws()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<CircularCtor5>().To<CircularCtor5>().AsSingle();
            container.Bind<CircularCtor6>().To<CircularCtor6>().AsSingle();

            Assert.That(() => container.Resolve<CircularCtor5>(), Throws.TypeOf<OnityResolveException>());
            Assert.That(() => container.Resolve<CircularCtor6>(), Throws.TypeOf<OnityResolveException>());
        }

        // --- TestCircularDependencies.TestSelfDependency -----------------------------
        [Test]
        public void SelfConstructorDependency_Throws()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<SelfDependent>().To<SelfDependent>().AsSingle();

            Assert.That(() => container.Resolve<SelfDependent>(), Throws.TypeOf<OnityResolveException>());
        }

        // --- TestConstructorInjection.TestResolve ------------------------------------
        [Test]
        public void ConstructorInjection_ResolvesDependency()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<CtorFoo>().To<CtorFoo>().AsSingle();
            container.Bind<CtorBar>().To<CtorBar>().AsSingle();

            CtorFoo foo = container.Resolve<CtorFoo>();

            Assert.That(foo, Is.Not.Null);
            Assert.That(foo.Bar, Is.Not.Null);
        }

        // --- TestConstructorInjection.TestMultipleWithOneTagged ----------------------
        // Qux has a parameterless ctor and a [Inject]-tagged single-arg ctor; the tagged
        // one is chosen, so Bar must be resolvable.
        [Test]
        public void MultipleConstructors_InjectTaggedConstructorChosen()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<CtorBar>().To<CtorBar>().AsSingle();
            container.Bind<CtorQux>().To<CtorQux>().AsSingle();

            CtorQux qux = container.Resolve<CtorQux>();

            Assert.That(qux, Is.Not.Null);
            Assert.That(qux.UsedTaggedConstructor, Is.True);
        }

        // --- TestParameters.TestMissingParameterThrows -------------------------------
        // Test1(int, int) with no int binding. Onity has no value-type construction
        // (an int has no accessible constructor), so resolving throws.
        [Test]
        public void MissingPrimitiveConstructorParameter_Throws()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<PrimitiveParams>().To<PrimitiveParams>().AsTransient();

            Assert.That(() => container.Resolve<PrimitiveParams>(), Throws.TypeOf<OnityResolveException>());
        }

        // --- TestInjectSources.TestAny (adapted) -------------------------------------
        // Onity always resolves "any" source: a child resolves a dependency bound in the
        // parent. (Original [Inject(Source = Any)] attribute has no Onity equivalent.)
        [Test]
        public void ChildResolvesConstructorDependencyFromParent()
        {
            using OnityContainer root = new OnityContainer();
            root.Bind<SourceDep>().To<SourceDep>().AsSingle();
            using OnityContainer child = new OnityContainer(root);
            child.Bind<SourceConsumer>().To<SourceConsumer>().AsSingle();

            SourceConsumer consumer = child.Resolve<SourceConsumer>();

            Assert.That(consumer, Is.Not.Null);
            Assert.That(consumer.Value, Is.Not.Null);
        }

        // --- TestInjectSources.TestLocal2 (adapted) ----------------------------------
        // Dependency and consumer both bound in the child scope resolve locally.
        [Test]
        public void ChildResolvesConstructorDependencyFromLocalScope()
        {
            using OnityContainer root = new OnityContainer();
            using OnityContainer child = new OnityContainer(root);
            child.Bind<SourceDep>().To<SourceDep>().AsSingle();
            child.Bind<SourceConsumer>().To<SourceConsumer>().AsSingle();

            SourceConsumer consumer = child.Resolve<SourceConsumer>();

            Assert.That(consumer, Is.Not.Null);
            Assert.That(consumer.Value, Is.Not.Null);
        }

        // --- TestInjectSources.TestParentAny1 (adapted) ------------------------------
        // Dependency bound in the grandparent resolves through two levels of parent.
        [Test]
        public void ChildResolvesConstructorDependencyFromGrandparent()
        {
            using OnityContainer root = new OnityContainer();
            root.Bind<SourceDep>().To<SourceDep>().AsSingle();
            using OnityContainer middle = new OnityContainer(root);
            using OnityContainer leaf = new OnityContainer(middle);
            leaf.Bind<SourceConsumer>().To<SourceConsumer>().AsSingle();

            SourceConsumer consumer = leaf.Resolve<SourceConsumer>();

            Assert.That(consumer, Is.Not.Null);
            Assert.That(consumer.Value, Is.Not.Null);
        }

        // --- TestPostInjectCall.Test -------------------------------------------------
        [Test]
        public void PostInjectCall_FieldsAndConstructorAvailable()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<PostInject0>().To<PostInject0>().AsSingle();
            container.Bind<PostInject1>().To<PostInject1>().AsSingle();
            container.Bind<PostInject2>().To<PostInject2>().AsSingle();
            container.Bind<PostInject3>().To<PostInject3>().AsSingle();

            PostInject3 target = container.Resolve<PostInject3>();

            Assert.That(target.HasInitialized, Is.True);
            Assert.That(target.HasInitialized2, Is.True);
        }

        // --- TestPostInjectCall.TestPrivateBaseClassPostInject -----------------------
        [Test]
        public void PostInjectCall_PrivateBaseClassMethodInvoked()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<SimpleBase>().To<SimpleDerived>().AsSingle();

            SimpleBase resolved = container.Resolve<SimpleBase>();

            Assert.That(resolved.WasCalled, Is.True);
        }

        // --- TestPostInjectCall.TestInheritance --------------------------------------
        [Test]
        public void PostInjectCall_BaseAndDerivedMethodsInvoked()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IInheritFoo>().To<InheritDerived>().AsSingle();

            IInheritFoo foo = container.Resolve<IInheritFoo>();

            Assert.That(((InheritDerived)foo).WasDerivedCalled, Is.True);
            Assert.That(((InheritBase)foo).WasBaseCalled, Is.True);
            Assert.That(((InheritDerived)foo).WasDerivedCalled2, Is.True);
            Assert.That(((InheritBase)foo).WasBaseCalled2, Is.True);
        }

        // --- TestPostInjectCall.TestInheritanceOrder ---------------------------------
        // Base inject methods run before derived; Onity walks the type hierarchy from
        // base to derived when invoking [Inject] methods.
        [Test]
        public void PostInjectCall_BaseMethodsRunBeforeDerived()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IInheritFoo>().To<OrderDerived2>().AsSingle();

            OrderDerived2.ResetCallOrder();

            container.Resolve<IInheritFoo>();

            Assert.That(OrderBase.BaseCallOrder, Is.EqualTo(0));
            Assert.That(OrderDerived.DerivedCallOrder, Is.EqualTo(1));
            Assert.That(OrderDerived2.Derived2CallOrder, Is.EqualTo(2));
        }

        // --- TestPropertyInjection.TestPublicPrivate ---------------------------------
        [Test]
        public void PropertyInjection_PublicAndPrivateSetters()
        {
            using OnityContainer container = new OnityContainer();
            PropDep dep = new PropDep();
            container.Bind<PropTarget>().To<PropTarget>().AsSingle();
            container.BindInstance(dep);

            PropTarget target = container.Resolve<PropTarget>();

            Assert.That(target.PublicProp, Is.SameAs(dep));
            Assert.That(target.GetPrivateProp(), Is.SameAs(dep));
        }

        // --- TestStructInjection.TestInjectStructIntoClass ---------------------------
        // A struct bound as an instance is injected into a class constructor parameter.
        [Test]
        public void StructInstanceInjectedIntoClassConstructor()
        {
            using OnityContainer container = new OnityContainer();
            container.BindInstance(new StructDep());
            container.Bind<StructConsumer>().To<StructConsumer>().AsSingle();

            StructConsumer consumer = container.Resolve<StructConsumer>();

            Assert.That(consumer, Is.Not.Null);
            Assert.That(consumer.ReceivedStruct, Is.True);
        }

        // --- TestStructInjection.TestInjectConstructorOfStruct -----------------------
        // A struct with an explicit constructor is built from a resolvable dependency.
        [Test]
        public void StructConstructedFromResolvedDependency()
        {
            using OnityContainer container = new OnityContainer();
            container.BindInstance("asdf");
            container.Bind<StructWithCtor>().To<StructWithCtor>().AsSingle();

            StructWithCtor value = container.Resolve<StructWithCtor>();

            Assert.That(value.Value, Is.EqualTo("asdf"));
        }

        // ===================== Private nested helper types ==========================

        private sealed class Test0
        {
        }

        private interface IFoo
        {
            bool DidPostInjectBase { get; }
            bool DidPostInjectDerived { get; }
            bool AllBaseInstanceMembersInjected { get; }
            bool AllDerivedInstanceMembersInjected { get; }
        }

        private abstract class FooBase : IFoo
        {
            private bool m_didPostInjectBase;

            [Inject]
            public static Test0 BaseStaticFieldPublic = null;

            [Inject]
            public Test0 BaseFieldPublic = null;

            [Inject]
            private Test0 BaseFieldPrivate = null;

            [Inject]
            protected Test0 BaseFieldProtected = null;

            [Inject]
            public Test0 BasePropertyPublic { get; set; }

            [Inject]
            private Test0 BasePropertyPrivate { get; set; }

            [Inject]
            protected Test0 BasePropertyProtected { get; set; }

            [Inject]
            public void PostInjectBase()
            {
                m_didPostInjectBase = true;
            }

            public bool DidPostInjectBase => m_didPostInjectBase;

            public abstract bool DidPostInjectDerived { get; }

            public bool AllBaseInstanceMembersInjected =>
                BaseFieldPublic != null
                && BaseFieldPrivate != null
                && BaseFieldProtected != null
                && BasePropertyPublic != null
                && BasePropertyPrivate != null
                && BasePropertyProtected != null;

            public abstract bool AllDerivedInstanceMembersInjected { get; }
        }

        private sealed class FooDerived : FooBase
        {
            private bool m_didPostInjectDerived;
            private readonly Test0 m_constructorParam;

            [Inject]
            public static Test0 DerivedStaticFieldPublic = null;

            [Inject]
            public Test0 DerivedFieldPublic = null;

            [Inject]
            private Test0 DerivedFieldPrivate = null;

            [Inject]
            protected Test0 DerivedFieldProtected = null;

            [Inject]
            public Test0 DerivedPropertyPublic { get; set; }

            [Inject]
            private Test0 DerivedPropertyPrivate { get; set; }

            [Inject]
            protected Test0 DerivedPropertyProtected { get; set; }

            public FooDerived(Test0 param)
            {
                m_constructorParam = param;
            }

            [Inject]
            public void PostInjectDerived()
            {
                m_didPostInjectDerived = true;
            }

            public override bool DidPostInjectDerived => m_didPostInjectDerived;

            public override bool AllDerivedInstanceMembersInjected =>
                m_constructorParam != null
                && DerivedFieldPublic != null
                && DerivedFieldPrivate != null
                && DerivedFieldProtected != null
                && DerivedPropertyPublic != null
                && DerivedPropertyPrivate != null
                && DerivedPropertyProtected != null;
        }

        private class BaseInjectTarget0
        {
        }

        private class BaseInjectRoot
        {
        }

        private class BaseInjectMiddle : BaseInjectRoot
        {
            [Inject]
            protected BaseInjectTarget0 m_val = null;

            public BaseInjectTarget0 GetVal()
            {
                return m_val;
            }
        }

        private sealed class BaseInjectLeaf : BaseInjectMiddle
        {
        }

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

        private sealed class CircularMethod3
        {
            public CircularMethod4 Other;

            [Inject]
            public void Initialize(CircularMethod4 other)
            {
                Other = other;
            }
        }

        private sealed class CircularMethod4
        {
            public CircularMethod3 Other;

            [Inject]
            public void Initialize(CircularMethod3 other)
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

        private sealed class SelfDependent
        {
            public SelfDependent(SelfDependent other)
            {
            }
        }

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
            }

            [Inject]
            public CtorQux(CtorBar val)
            {
                UsedTaggedConstructor = true;
            }

            public bool UsedTaggedConstructor { get; }
        }

        private sealed class PrimitiveParams
        {
            public PrimitiveParams(int f1, int f2)
            {
            }
        }

        private sealed class SourceDep
        {
        }

        private sealed class SourceConsumer
        {
            public SourceConsumer(SourceDep value)
            {
                Value = value;
            }

            public SourceDep Value { get; }
        }

        private sealed class PostInject0
        {
        }

        private sealed class PostInject1
        {
        }

        private sealed class PostInject2
        {
        }

        private sealed class PostInject3
        {
            public bool HasInitialized;
            public bool HasInitialized2;

            [Inject]
            public PostInject1 Field1 = null;

            [Inject]
            public PostInject0 Field0 = null;

            private readonly PostInject2 m_field2;

            public PostInject3(PostInject2 test2)
            {
                m_field2 = test2;
            }

            [Inject]
            public void Init()
            {
                Assert.That(HasInitialized, Is.False);
                Assert.That(Field1, Is.Not.Null);
                Assert.That(Field0, Is.Not.Null);
                Assert.That(m_field2, Is.Not.Null);
                HasInitialized = true;
            }

            [Inject]
            private void PrivatePostInject()
            {
                HasInitialized2 = true;
            }
        }

        private class SimpleBase
        {
            public bool WasCalled;

            [Inject]
            private void Init()
            {
                WasCalled = true;
            }
        }

        private sealed class SimpleDerived : SimpleBase
        {
        }

        private interface IInheritFoo
        {
        }

        private class InheritBase : IInheritFoo
        {
            public bool WasBaseCalled;
            public bool WasBaseCalled2;

            [Inject]
            private void TestBase()
            {
                WasBaseCalled = true;
            }

            [Inject]
            public virtual void TestVirtual1()
            {
                WasBaseCalled2 = true;
            }
        }

        private class InheritDerived : InheritBase
        {
            public bool WasDerivedCalled;
            public bool WasDerivedCalled2;

            [Inject]
            private void TestDerived()
            {
                WasDerivedCalled = true;
            }

            public override void TestVirtual1()
            {
                base.TestVirtual1();
                WasDerivedCalled2 = true;
            }
        }

        private class OrderBase : IInheritFoo
        {
            public static int BaseCallOrder;
            protected static int s_initOrder;

            [Inject]
            private void TestBase()
            {
                BaseCallOrder = s_initOrder++;
            }
        }

        private class OrderDerived : OrderBase
        {
            public static int DerivedCallOrder;

            [Inject]
            private void TestDerived()
            {
                DerivedCallOrder = s_initOrder++;
            }
        }

        private sealed class OrderDerived2 : OrderDerived
        {
            public static int Derived2CallOrder;

            public static void ResetCallOrder()
            {
                s_initOrder = 0;
                BaseCallOrder = 0;
                DerivedCallOrder = 0;
                Derived2CallOrder = 0;
            }

            [Inject]
            public void TestVirtual2()
            {
                Derived2CallOrder = s_initOrder++;
            }
        }

        private sealed class PropDep
        {
        }

        private sealed class PropTarget
        {
            [Inject]
            public PropDep PublicProp { get; private set; }

            [Inject]
            private PropDep PrivateProp { get; set; }

            public PropDep GetPrivateProp()
            {
                return PrivateProp;
            }
        }

        private struct StructDep
        {
        }

        private sealed class StructConsumer
        {
            public StructConsumer(StructDep dep)
            {
                ReceivedStruct = true;
            }

            public bool ReceivedStruct { get; }
        }

        private struct StructWithCtor
        {
            public StructWithCtor(string value)
            {
                Value = value;
            }

            public string Value { get; private set; }
        }
    }
}
