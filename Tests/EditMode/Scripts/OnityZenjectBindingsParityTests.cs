using NUnit.Framework;
using Onity.DI;

namespace Onity.Tests.EditMode
{
    // Parity port of the Zenject "Bindings" unit-test category onto OnityContainer.
    // Each [Test] mirrors the original Zenject assertion intent, adapted to Onity's
    // frozen public API. Where Onity's documented behavior diverges from Zenject,
    // the test asserts Onity's real behavior with an inline divergence comment.
    [TestFixture]
    public sealed class OnityZenjectBindingsParityTests
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

        // --- TestFrom ---

        // Zenject TestFrom.TestSelfSingle: Bind<Foo>().AsSingle() resolves a non-null singleton.
        [Test]
        public void SelfSingle_ResolvesSameInstance()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<Foo>().AsSingle();

            Assert.That(container.Resolve<Foo>(), Is.Not.Null);
            Assert.That(container.Resolve<Foo>(), Is.SameAs(container.Resolve<Foo>()));
        }

        // Zenject TestFrom.TestSelfSingleExplicit: ToSelf().FromNew().AsSingle() maps to Bind().To<self>().AsSingle().
        [Test]
        public void SelfSingleExplicit_ResolvesSameInstance()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<Foo>().To<Foo>().AsSingle();

            Assert.That(container.Resolve<Foo>(), Is.Not.Null);
            Assert.That(container.Resolve<Foo>(), Is.SameAs(container.Resolve<Foo>()));
        }

        // Zenject TestFrom.TestSelfTransient: Bind<Foo>().AsTransient() yields a fresh instance each resolve.
        [Test]
        public void SelfTransient_ResolvesDifferentInstances()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<Foo>().AsTransient();

            Assert.That(container.Resolve<Foo>(), Is.Not.Null);
            Assert.That(container.Resolve<Foo>(), Is.Not.SameAs(container.Resolve<Foo>()));
        }

        // Zenject TestFrom.TestSelfCached: AsCached maps to Onity AsSingle (cached single instance).
        [Test]
        public void SelfCached_ResolvesSameInstance()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<Foo>().AsSingle();

            Assert.That(container.Resolve<Foo>(), Is.Not.Null);
            Assert.That(container.Resolve<Foo>(), Is.SameAs(container.Resolve<Foo>()));
        }

        // Zenject TestFrom.TestConcreteSingle: bind concrete + interface to one impl, shared singleton.
        [Test]
        public void ConcreteSingle_InterfaceAndConcreteShareInstance()
        {
            using OnityContainer container = new OnityContainer();
            container.BindInterfacesAndSelfTo<Foo>().AsSingle();

            Assert.That(container.Resolve<Foo>(), Is.Not.Null);
            Assert.That(container.Resolve<IFoo>(), Is.Not.Null);
            Assert.That(container.Resolve<Foo>(), Is.SameAs(container.Resolve<Foo>()));
            Assert.That(container.Resolve<IFoo>(), Is.SameAs(container.Resolve<Foo>()));
            Assert.That(container.Resolve<IFoo>(), Is.SameAs(container.Resolve<IFoo>()));
        }

        // Zenject TestFrom.TestConcreteTransient: Bind<IFoo>().To<Foo>().AsTransient() yields fresh instances.
        [Test]
        public void ConcreteTransient_ResolvesDifferentInstances()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IFoo>().To<Foo>().AsTransient();

            Assert.That(container.Resolve<IFoo>(), Is.Not.Null);
            Assert.That(container.Resolve<IFoo>(), Is.Not.SameAs(container.Resolve<IFoo>()));
        }

        // Zenject TestFrom.TestConcreteCached: separate cached bindings for concrete and interface
        // produce distinct singletons (each binding is its own cached scope).
        [Test]
        public void ConcreteCached_SeparateBindingsAreDistinctSingletons()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<Foo>().AsSingle();
            container.Bind<IFoo>().To<Foo>().AsSingle();

            Assert.That(container.Resolve<Foo>(), Is.Not.Null);
            Assert.That(container.Resolve<IFoo>(), Is.Not.Null);
            Assert.That(container.Resolve<IFoo>(), Is.SameAs(container.Resolve<IFoo>()));
            Assert.That(container.Resolve<Foo>(), Is.SameAs(container.Resolve<Foo>()));
            Assert.That(container.Resolve<IFoo>(), Is.Not.SameAs(container.Resolve<Foo>()));
        }

        // Zenject TestFrom.TestDuplicateBindingsFail: Zenject throws on duplicate Bind<Foo>().AsSingle().
        // Divergence: Onity uses last-binding-wins, so re-binding the same contract does not throw.
        [Test]
        public void DuplicateBindings_LastBindingWins_DoesNotThrow()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<Foo>().AsSingle();
            container.Bind<Foo>().AsSingle();

            Assert.That(container.Resolve<Foo>(), Is.Not.Null);
            Assert.That(container.Resolve<Foo>(), Is.SameAs(container.Resolve<Foo>()));
        }

        // Zenject TestFrom.TestMultipleBindingsSingle: bind two interfaces to one impl, shared singleton.
        [Test]
        public void MultipleBindingsSingle_SharedInstanceAcrossContracts()
        {
            using OnityContainer container = new OnityContainer();
            container.BindInterfacesTo<Foo>().AsSingle();

            Assert.That(container.Resolve<IFoo>(), Is.SameAs(container.Resolve<IBar>()));
            Assert.That(container.Resolve<IFoo>(), Is.InstanceOf<Foo>());
        }

        // Zenject TestFrom.TestMultipleBindingsTransient: two interfaces to one impl, transient yields fresh each resolve.
        [Test]
        public void MultipleBindingsTransient_FreshInstancePerResolve()
        {
            using OnityContainer container = new OnityContainer();
            container.BindInterfacesTo<Foo>().AsTransient();

            Assert.That(container.Resolve<IFoo>(), Is.InstanceOf<Foo>());
            Assert.That(container.Resolve<IBar>(), Is.InstanceOf<Foo>());
            Assert.That(container.Resolve<IFoo>(), Is.Not.SameAs(container.Resolve<IFoo>()));
            Assert.That(container.Resolve<IFoo>(), Is.Not.SameAs(container.Resolve<IBar>()));
        }

        // Zenject TestFrom.TestMultipleBindingsCached: two interfaces to one impl, cached shares singleton.
        [Test]
        public void MultipleBindingsCached_SharedSingletonAcrossContracts()
        {
            using OnityContainer container = new OnityContainer();
            container.BindInterfacesTo<Foo>().AsSingle();

            Assert.That(container.Resolve<IFoo>(), Is.SameAs(container.Resolve<IFoo>()));
            Assert.That(container.Resolve<IFoo>(), Is.SameAs(container.Resolve<IBar>()));
        }

        // --- TestFromInstance ---

        // Zenject TestFromInstance.TestTransient: FromInstance binds the same object to interface and concrete.
        [Test]
        public void FromInstance_SharedAcrossContracts()
        {
            using OnityContainer container = new OnityContainer();
            Foo foo = new Foo();

            container.BindInstance<IFoo>(foo);
            container.BindInstance<Foo>(foo);

            Assert.That(container.Resolve<Foo>(), Is.SameAs(container.Resolve<IFoo>()));
            Assert.That(container.Resolve<Foo>(), Is.SameAs(foo));
        }

        // Zenject TestFromInstance.TestCached: FromInstance().AsSingle() resolves the bound instance for both contracts.
        [Test]
        public void FromInstanceCached_ReturnsBoundInstance()
        {
            using OnityContainer container = new OnityContainer();
            Foo foo = new Foo();

            container.BindInstance<IFoo>(foo);
            container.BindInstance<Foo>(foo);

            Assert.That(container.Resolve<Foo>(), Is.SameAs(container.Resolve<IFoo>()));
            Assert.That(container.Resolve<Foo>(), Is.SameAs(foo));
        }

        // Zenject TestFromInstance.TestSingle: Zenject throws when an instance binding and a class
        // binding collide on the same contract. Divergence: Onity applies last-binding-wins, so the
        // last registration (the AsSingle class binding) takes effect without throwing.
        [Test]
        public void FromInstance_ThenClassBinding_LastBindingWins()
        {
            using OnityContainer container = new OnityContainer();
            Foo instance = new Foo();

            container.BindInstance<Foo>(instance);
            container.Bind<Foo>().AsSingle();

            Foo resolved = container.Resolve<Foo>();

            Assert.That(resolved, Is.Not.Null);
            Assert.That(resolved, Is.Not.SameAs(instance));
        }

        // --- TestFromResolve ---

        // Zenject TestFromResolve.TestTransient: aliasing an interface onto an existing concrete binding.
        // Onity maps this to binding both contracts to the same shared instance.
        [Test]
        public void FromResolve_InterfaceAliasesConcreteInstance()
        {
            using OnityContainer container = new OnityContainer();
            Foo foo = new Foo();

            container.BindInstance<Foo>(foo);
            container.BindInstance<IFoo>(foo);

            Assert.That(container.Resolve<IFoo>(), Is.SameAs(container.Resolve<Foo>()));
            Assert.That(container.Resolve<IFoo>(), Is.SameAs(foo));
        }

        // Zenject TestFromResolve.TestSingle: interface alias resolves to the single concrete instance repeatedly.
        [Test]
        public void FromResolve_SingleAliasIsStable()
        {
            using OnityContainer container = new OnityContainer();
            Foo foo = new Foo();
            container.BindInstance<Foo>(foo);
            container.BindInstance<IFoo>(foo);

            Assert.That(container.Resolve<IFoo>(), Is.SameAs(foo));
            Assert.That(container.Resolve<IFoo>(), Is.SameAs(container.Resolve<IFoo>()));
            Assert.That(container.Resolve<IFoo>(), Is.SameAs(container.Resolve<Foo>()));
        }

        // Zenject TestFromResolve.TestCached: a transient concrete and a separate cached interface alias.
        // In Onity: the singleton interface binding is stable while the transient concrete varies.
        [Test]
        public void FromResolve_CachedAliasStable_ConcreteTransientVaries()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<Foo>().AsTransient();
            container.Bind<IFoo>().To<Foo>().AsSingle();

            Assert.That(container.Resolve<Foo>(), Is.Not.SameAs(container.Resolve<Foo>()));
            Assert.That(container.Resolve<IFoo>(), Is.SameAs(container.Resolve<IFoo>()));
        }

        // Zenject TestFromResolve.TestNoMatch: resolving an interface with no resolvable target throws.
        // In Onity a bare interface with no binding is unresolvable and throws OnityResolveException.
        [Test]
        public void Resolve_UnboundInterface_Throws()
        {
            using OnityContainer container = new OnityContainer();

            Assert.That(() => container.Resolve<IFoo>(), Throws.TypeOf<OnityResolveException>());
        }

        // Zenject TestFromResolve.TestInfiniteLoop: a self-referential resolve alias throws.
        // Onity's equivalent self-referential dependency throws OnityResolveException.
        [Test]
        public void Resolve_SelfReferentialDependency_Throws()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<ICircular>().To<CircularSelf>().AsSingle();

            Assert.That(() => container.Resolve<ICircular>(), Throws.TypeOf<OnityResolveException>());
        }

        private interface IBar
        {
        }

        private interface IFoo
        {
        }

        private interface ICircular
        {
        }

        private sealed class Foo : IFoo, IBar
        {
        }

        private sealed class CircularSelf : ICircular
        {
            public CircularSelf(ICircular self)
            {
                _ = self;
            }
        }
    }
}
