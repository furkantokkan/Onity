using System.Collections.Generic;
using NUnit.Framework;
using Onity.DI;

namespace Onity.Tests.EditMode
{
    // Parity port of Zenject's "Other" unit-test category onto OnityContainer.
    // Each [Test] preserves the original assertion intent, adapted to Onity's
    // frozen public API. Where Onity behavior diverges from Zenject, the test
    // asserts Onity's real behavior with an inline divergence comment.
    [TestFixture]
    public sealed class OnityZenjectOtherParityTests
    {
        // --- TestCircularDependencies.TestThrows ---
        [Test]
        public void ConstructorCircularDependency_Throws()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<CircularFoo1>().AsSingle();
            container.Bind<CircularBar1>().AsSingle();

            Assert.That(() => container.Resolve<CircularFoo1>(), Throws.TypeOf<OnityResolveException>());
            Assert.That(() => container.Resolve<CircularBar1>(), Throws.TypeOf<OnityResolveException>());
        }

        // --- TestCircularDependencies.TestPostInject ---
        // Divergence: Zenject resolves method-injection circular dependencies because
        // post-inject runs after construction. Onity injects members inside the same
        // resolution-stack guard, so a method-injection cycle is still a circular
        // dependency and throws OnityResolveException.
        [Test]
        public void MethodInjectionCircularDependency_Throws()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<CircularFoo2>().AsSingle();
            container.Bind<CircularBar2>().AsSingle();

            Assert.That(() => container.Resolve<CircularFoo2>(), Throws.TypeOf<OnityResolveException>());
        }

        // --- TestCircularDependencies.TestField ---
        // Divergence: same as above. Field-injection cycles also throw under Onity.
        [Test]
        public void FieldInjectionCircularDependency_Throws()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<CircularFoo3>().AsSingle();
            container.Bind<CircularBar3>().AsSingle();

            Assert.That(() => container.Resolve<CircularFoo3>(), Throws.TypeOf<OnityResolveException>());
        }

        // --- TestNestedContainer.TestCase1 ---
        [Test]
        public void NestedContainer_ParentFallbackAndChildShadowing()
        {
            using OnityContainer empty = new OnityContainer();
            using OnityContainer parent = new OnityContainer();

            Assert.That(() => empty.Resolve<INestedFoo>(), Throws.TypeOf<OnityResolveException>());
            Assert.That(() => parent.Resolve<INestedFoo>(), Throws.TypeOf<OnityResolveException>());

            parent.Bind<INestedFoo>().To<NestedFoo>().AsSingle();

            Assert.That(() => empty.Resolve<INestedFoo>(), Throws.TypeOf<OnityResolveException>());
            Assert.That(parent.Resolve<INestedFoo>().GetBar(), Is.EqualTo(0));

            using OnityContainer child = new OnityContainer(parent);

            // Child falls back to parent binding.
            Assert.That(child.Resolve<INestedFoo>().GetBar(), Is.EqualTo(0));
            Assert.That(parent.Resolve<INestedFoo>().GetBar(), Is.EqualTo(0));

            // Child binding shadows the parent binding for the child scope only.
            child.Bind<INestedFoo>().To<NestedFoo2>().AsSingle();

            Assert.That(child.Resolve<INestedFoo>().GetBar(), Is.EqualTo(1));
            Assert.That(parent.Resolve<INestedFoo>().GetBar(), Is.EqualTo(0));
        }

        // --- TestSubContainers.TestIsRemoved ---
        // Adapts CreateSubContainer -> new OnityContainer(parent) and
        // FromInstance -> BindInstance. A binding made only in the child is not
        // visible from the parent scope.
        [Test]
        public void ChildInstanceBinding_NotVisibleFromParent()
        {
            using OnityContainer parent = new OnityContainer();
            using OnityContainer child = new OnityContainer(parent);

            SubTest0 instance = new SubTest0();
            child.BindInstance<ISubTest>(instance);

            // Interface contract bound only in the child. The parent has no binding for it
            // and interfaces are not implicitly auto-resolved, so the parent cannot resolve it.
            Assert.That(child.Resolve<ISubTest>(), Is.SameAs(instance));
            Assert.That(() => parent.Resolve<ISubTest>(), Throws.TypeOf<OnityResolveException>());
        }

        // --- TestSubContainers.TestCase2 ---
        [Test]
        public void ChildBindings_AreIsolatedFromParent()
        {
            using OnityContainer parent = new OnityContainer();
            using OnityContainer child = new OnityContainer(parent);

            SubTest0 childTest0 = new SubTest0();
            child.BindInstance<ISubTest>(childTest0);
            child.Bind<SubTest1>().AsSingle();

            Assert.That(child.Resolve<ISubTest>(), Is.SameAs(childTest0));
            SubTest1 childTest1 = child.Resolve<SubTest1>();
            Assert.That(childTest1.Test, Is.SameAs(childTest0));

            // The child binds ISubTest only in its own scope. SubTest1 constructor-injects
            // ISubTest, so a parent attempt to build SubTest1 cannot satisfy the dependency
            // (interface contract, no parent binding, no implicit auto-resolve for interfaces)
            // and throws. This proves the child binding does not leak up to the parent.
            Assert.That(() => parent.Resolve<SubTest1>(), Throws.TypeOf<OnityResolveException>());

            parent.BindInstance<ISubTest>(new SubTest0());
            parent.Bind<SubTest1>().AsSingle();

            // Parent now has its own bindings, distinct from the child instances.
            Assert.That(parent.Resolve<ISubTest>(), Is.Not.SameAs(childTest0));
            Assert.That(parent.Resolve<SubTest1>(), Is.Not.SameAs(childTest1));
        }

        // --- TestSubContainers.TestMultipleSingletonDifferentScope ---
        [Test]
        public void SingletonsInDifferentChildScopes_AreDistinct()
        {
            using OnityContainer parent = new OnityContainer();

            using OnityContainer child1 = new OnityContainer(parent);
            child1.Bind<ISubFoo>().To<SubFoo>().AsSingle();
            ISubFoo foo1 = child1.Resolve<ISubFoo>();

            using OnityContainer child2 = new OnityContainer(parent);
            child2.Bind<ISubFoo>().To<SubFoo>().AsSingle();
            ISubFoo foo2 = child2.Resolve<ISubFoo>();

            Assert.That(foo2, Is.Not.SameAs(foo1));
        }

        // --- TestGenericContract: closed-generic contract resolution ---
        // Zenject's TestToSingle/TestToTransient register the OPEN generic
        // (typeof(Test1<>)), which Onity does not support. Onity supports
        // resolving CLOSED generic contracts, so this ports the same singleton vs
        // transient intent against a closed generic type.
        [Test]
        public void ClosedGenericContract_AsSingle_ReturnsSameInstance()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<GenericHolder<int>>().AsSingle();

            GenericHolder<int> first = container.Resolve<GenericHolder<int>>();
            Assert.That(first.Data, Is.EqualTo(0));
            first.Data = 5;

            GenericHolder<int> second = container.Resolve<GenericHolder<int>>();

            Assert.That(second, Is.SameAs(first));
            Assert.That(second.Data, Is.EqualTo(5));
        }

        [Test]
        public void ClosedGenericContract_AsTransient_ReturnsDistinctInstances()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<GenericHolder<int>>().AsTransient();

            GenericHolder<int> first = container.Resolve<GenericHolder<int>>();
            Assert.That(first.Data, Is.EqualTo(0));

            GenericHolder<int> second = container.Resolve<GenericHolder<int>>();
            Assert.That(second.Data, Is.EqualTo(0));
            Assert.That(second, Is.Not.SameAs(first));
        }

        // --- TestGenericContract.TestToSingleMultipleContracts (closed adaptation) ---
        // Open generic multi-contract binding is unsupported; this ports the shared
        // single-instance-across-multiple-contracts intent using closed generics.
        [Test]
        public void ClosedGenericMultipleContracts_ShareSingleInstance()
        {
            using OnityContainer container = new OnityContainer();
            container.BindInterfacesAndSelfTo<GenericImpl<int>>().AsSingle();

            IGenericFoo<int> foo = container.Resolve<IGenericFoo<int>>();
            IGenericBar<int> bar = container.Resolve<IGenericBar<int>>();

            Assert.That(foo, Is.InstanceOf<GenericImpl<int>>());
            Assert.That(bar, Is.InstanceOf<GenericImpl<int>>());
            Assert.That(foo, Is.SameAs(bar));
            Assert.That(foo, Is.SameAs(container.Resolve<IGenericFoo<int>>()));
            Assert.That(bar, Is.SameAs(container.Resolve<IGenericBar<int>>()));
        }

        // --- TestAsSingle.TestAsSingleAndResolveNoThrow (last-binding-wins adaptation) ---
        // Zenject's FromResolve aliasing is unsupported. The portable intent is that
        // two contracts can share one singleton instance, which Onity expresses via
        // BindInterfacesAndSelfTo.
        [Test]
        public void InterfaceAndConcreteContracts_ResolveSameSingleton()
        {
            using OnityContainer container = new OnityContainer();
            container.BindInterfacesAndSelfTo<AsSingleFoo>().AsSingle();

            Assert.That(container.Resolve<IAsSingleFoo>(), Is.SameAs(container.Resolve<AsSingleFoo>()));
        }

        // --- TestClearCacheProvider.Test1 (rebind / last-binding-wins) ---
        // ClearCacheProvider/AllProviders introspection is unsupported. The portable
        // intent is rebind: re-binding a contract makes the last binding win.
        [Test]
        public void Rebind_LastBindingWins()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IClearFoo>().To<ClearFoo1>().AsSingle();

            Assert.That(container.Resolve<IClearFoo>(), Is.InstanceOf<ClearFoo1>());

            container.Bind<IClearFoo>().To<ClearFoo2>().AsSingle();

            Assert.That(container.Resolve<IClearFoo>(), Is.InstanceOf<ClearFoo2>());
        }

        private interface INestedFoo
        {
            int GetBar();
        }

        private sealed class NestedFoo : INestedFoo
        {
            public int GetBar()
            {
                return 0;
            }
        }

        private sealed class NestedFoo2 : INestedFoo
        {
            public int GetBar()
            {
                return 1;
            }
        }

        private sealed class CircularFoo1
        {
            public CircularFoo1(CircularBar1 bar)
            {
            }
        }

        private sealed class CircularBar1
        {
            public CircularBar1(CircularFoo1 foo)
            {
            }
        }

        private sealed class CircularFoo2
        {
            [Inject]
            public void Init(CircularBar2 bar)
            {
            }
        }

        private sealed class CircularBar2
        {
            [Inject]
            public void Init(CircularFoo2 foo)
            {
            }
        }

        private sealed class CircularFoo3
        {
            [Inject]
            public CircularBar3 Bar = null;
        }

        private sealed class CircularBar3
        {
            [Inject]
            public CircularFoo3 Foo = null;
        }

        private interface ISubTest
        {
        }

        private sealed class SubTest0 : ISubTest
        {
        }

        private sealed class SubTest1
        {
            public SubTest1(ISubTest test)
            {
                Test = test;
            }

            public ISubTest Test { get; }
        }

        private interface ISubFoo
        {
        }

        private sealed class SubFoo : ISubFoo
        {
        }

        private sealed class GenericHolder<T>
        {
            public T Data;
        }

        private interface IGenericFoo<T>
        {
        }

        private interface IGenericBar<T>
        {
        }

        private sealed class GenericImpl<T> : IGenericFoo<T>, IGenericBar<T>
        {
        }

        private interface IAsSingleFoo
        {
        }

        private sealed class AsSingleFoo : IAsSingleFoo
        {
        }

        private interface IClearFoo
        {
        }

        private sealed class ClearFoo1 : IClearFoo
        {
        }

        private sealed class ClearFoo2 : IClearFoo
        {
        }
    }
}
