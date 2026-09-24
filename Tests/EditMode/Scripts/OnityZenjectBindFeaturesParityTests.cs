using NUnit.Framework;
using Onity.DI;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Parity port of the Zenject "BindFeatures" unit-test category onto OnityContainer.
    /// Only tests that map to Onity's frozen public API are ported; unsupported Zenject
    /// features (identifiers, conditions, WithArguments, Unbind, sub-container Move/Copy,
    /// List multi-injection, ResolveRoots/FlushBindings validation) are intentionally omitted.
    /// Where Onity behavior differs from Zenject, the assertion reflects Onity's real behavior.
    /// </summary>
    [TestFixture]
    public sealed class OnityZenjectBindFeaturesParityTests
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

        // Ported from TestBindingInheritanceMethod.TestNoCopy: a parent singleton is shared
        // with a child container by default (child falls back to parent, same instance).
        [Test]
        public void ChildContainer_ResolvesParentSingleton_SameInstance()
        {
            using OnityContainer parent = new OnityContainer();
            parent.Bind<Foo>().AsSingle();

            using OnityContainer child = new OnityContainer(parent);

            Foo fromChild = child.Resolve<Foo>();
            Foo fromParent = parent.Resolve<Foo>();

            Assert.That(fromChild, Is.SameAs(fromParent));
        }

        // Ported from TestMultipleContractTypes2.Test1: binding multiple contracts to one
        // implementation. Divergence: Zenject's Bind(IFoo, IQux).To<Foo>().AsSingle() groups the
        // contracts onto ONE shared instance. Onity has no contract-grouping on Bind<T>(); each
        // separate single-contract bind creates its own provider, so the two interfaces resolve to
        // DISTINCT singleton instances of the same concrete type. (BindInterfacesAndSelfTo is the
        // Onity way to share one instance across contracts; see the dedicated test below.)
        [Test]
        public void MultipleContractTypes_SeparateBindsProduceDistinctSingletons()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IFoo>().To<Foo>().AsSingle();
            container.Bind<IQux>().To<Foo>().AsSingle();

            IFoo asFoo = container.Resolve<IFoo>();
            IQux asQux = container.Resolve<IQux>();

            Assert.That(asFoo, Is.Not.Null);
            Assert.That(asQux, Is.Not.Null);
            Assert.That(asFoo, Is.TypeOf<Foo>());
            Assert.That(asQux, Is.TypeOf<Foo>());
            // Each single-contract bind owns a separate provider, so the instances differ.
            Assert.That((object)asFoo, Is.Not.SameAs((object)asQux));
        }

        // Ported from TestMultipleContractTypes2.TestAllInterfaces: BindInterfacesTo binds only
        // the interfaces. Divergence: Onity has no explicit-only resolution gate, so the concrete
        // Foo still resolves implicitly as a transient; assert interfaces share one instance.
        [Test]
        public void BindInterfacesTo_BindsAllInterfaces_SharedInstance()
        {
            using OnityContainer container = new OnityContainer();
            container.BindInterfacesTo<Foo>().AsSingle();

            IFoo asFoo = container.Resolve<IFoo>();
            IQux asQux = container.Resolve<IQux>();

            Assert.That(asFoo, Is.Not.Null);
            Assert.That(asQux, Is.Not.Null);
            Assert.That(asFoo, Is.SameAs(asQux));
            // Divergence: Zenject TryResolve<Foo> is null; Onity implicitly resolves the concrete
            // as a distinct transient instead of returning the interface-bound singleton.
            bool resolvedConcrete = container.TryResolve(out Foo concrete);
            Assert.That(resolvedConcrete, Is.True);
            Assert.That(concrete, Is.Not.SameAs(asFoo));
        }

        // Ported from TestMultipleContractTypes2.TestAllInterfacesAndSelf: BindInterfacesAndSelfTo
        // binds the concrete plus every interface to one shared singleton instance.
        [Test]
        public void BindInterfacesAndSelfTo_BindsConcreteAndInterfaces_SharedInstance()
        {
            using OnityContainer container = new OnityContainer();
            container.BindInterfacesAndSelfTo<Foo>().AsSingle();

            Foo asConcrete = container.Resolve<Foo>();
            IFoo asFoo = container.Resolve<IFoo>();
            IQux asQux = container.Resolve<IQux>();

            Assert.That(asConcrete, Is.Not.Null);
            Assert.That(asFoo, Is.Not.Null);
            Assert.That(asQux, Is.Not.Null);
            Assert.That(asConcrete, Is.SameAs(asFoo));
            Assert.That(asFoo, Is.SameAs(asQux));
        }

        // Ported from TestRebind.Run: re-binding the same contract makes the last binding win.
        // Zenject's Rebind<T>() maps to simply binding the contract again in Onity.
        [Test]
        public void Rebind_LastBindingWins()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<ITest>().To<Test2>().AsSingle();

            Assert.That(container.Resolve<ITest>(), Is.TypeOf<Test2>());

            container.Bind<ITest>().To<Test3>().AsSingle();

            Assert.That(container.Resolve<ITest>(), Is.TypeOf<Test3>());
        }

        private sealed class Foo : IFoo, IQux
        {
        }

        private interface IFoo
        {
        }

        private interface IQux
        {
        }

        private interface ITest
        {
        }

        private sealed class Test2 : ITest
        {
        }

        private sealed class Test3 : ITest
        {
        }
    }
}
