using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.DI;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class OnityContainerEdgeCaseTests
    {
        [Test]
        public void BindInstance_Null_ThrowsOnityBindingException()
        {
            using OnityContainer container = new OnityContainer();

            Assert.That(
                () => container.BindInstance<ITestContract>(null),
                Throws.TypeOf<OnityBindingException>());
        }

        [Test]
        public void Resolve_NullType_ThrowsOnityResolveException()
        {
            using OnityContainer container = new OnityContainer();

            Assert.That(
                () => container.Resolve((Type)null),
                Throws.TypeOf<OnityResolveException>());
        }

        [Test]
        public void TryResolve_NullType_ReturnsFalseAndNull()
        {
            using OnityContainer container = new OnityContainer();

            bool canResolve = container.TryResolve(null, out object instance);

            Assert.That(canResolve, Is.False);
            Assert.That(instance, Is.Null);
        }

        [Test]
        public void Inject_NullTarget_ThrowsOnityResolveException()
        {
            using OnityContainer container = new OnityContainer();

            Assert.That(
                () => container.Inject(null),
                Throws.TypeOf<OnityResolveException>());
        }

        [Test]
        public void Resolve_GenericTypeDefinition_ThrowsOnityResolveException()
        {
            using OnityContainer container = new OnityContainer();

            Assert.That(
                () => container.Resolve(typeof(List<>)),
                Throws.TypeOf<OnityResolveException>());
        }

        [Test]
        public void Resolve_UnboundAbstractType_ThrowsOnityResolveException()
        {
            using OnityContainer container = new OnityContainer();

            Assert.That(
                () => container.Resolve(typeof(AbstractContract)),
                Throws.TypeOf<OnityResolveException>());
        }

        [Test]
        public void Resolve_CircularDependency_ThrowsOnityResolveException()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<CircularA>().AsTransient();
            container.Bind<CircularB>().AsTransient();

            Assert.That(
                () => container.Resolve<CircularA>(),
                Throws.TypeOf<OnityResolveException>());
        }

        [Test]
        public void Resolve_MultipleInjectConstructors_ThrowsOnityBindingException()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<TestDependency>().AsTransient();
            container.Bind<MultipleInjectConstructorsTarget>().AsTransient();

            Assert.That(
                () => container.Resolve<MultipleInjectConstructorsTarget>(),
                Throws.TypeOf<OnityBindingException>());
        }

        [Test]
        public void Resolve_InjectPropertyWithoutSetter_ThrowsOnityBindingException()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<TestDependency>().AsTransient();
            container.Bind<InjectPropertyWithoutSetterTarget>().AsTransient();

            Assert.That(
                () => container.Resolve<InjectPropertyWithoutSetterTarget>(),
                Throws.TypeOf<OnityBindingException>());
        }

        [Test]
        public void Resolve_InjectIndexerProperty_ThrowsOnityBindingException()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<TestDependency>().AsTransient();
            container.Bind<InjectIndexerPropertyTarget>().AsTransient();

            Assert.That(
                () => container.Resolve<InjectIndexerPropertyTarget>(),
                Throws.TypeOf<OnityBindingException>());
        }

        [Test]
        public void Resolve_InjectGenericMethod_ThrowsOnityBindingException()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<TestDependency>().AsTransient();
            container.Bind<InjectGenericMethodTarget>().AsTransient();

            Assert.That(
                () => container.Resolve<InjectGenericMethodTarget>(),
                Throws.TypeOf<OnityBindingException>());
        }

        [Test]
        public void Dispose_DisposesSingletonInstance()
        {
            OnityContainer container = new OnityContainer();
            container.Bind<IDisposableContract>().To<DisposableContract>().AsSingle();

            IDisposableContract contract = container.Resolve<IDisposableContract>();
            container.Dispose();

            Assert.That(contract.IsDisposed, Is.True);
        }

        [Test]
        public async Task BuildAsync_CallbackReturningNullTask_DoesNotThrow()
        {
            using OnityContainer container = new OnityContainer();
            container.RegisterBuildCallbackAsync((_, _) => null);

            await container.BuildAsync();
        }

        [Test]
        public async Task BuildAsync_ExecutesSyncCallbacksBeforeAsyncCallbacks()
        {
            using OnityContainer container = new OnityContainer();
            List<string> order = new List<string>(2);

            container.RegisterBuildCallback(_ => order.Add("sync"));
            container.RegisterBuildCallbackAsync(
                async (_, _) =>
                {
                    await Task.Yield();
                    order.Add("async");
                });

            await container.BuildAsync();

            Assert.That(order.Count, Is.EqualTo(2));
            Assert.That(order[0], Is.EqualTo("sync"));
            Assert.That(order[1], Is.EqualTo("async"));
        }

        [Test]
        public void ChildBinding_OverridesParentBinding()
        {
            using OnityContainer parent = new OnityContainer();
            using OnityContainer child = new OnityContainer(parent);

            parent.Bind<ITestContract>().To<ParentContract>().AsSingle();
            child.Bind<ITestContract>().To<ChildContract>().AsSingle();

            ITestContract parentResolved = parent.Resolve<ITestContract>();
            ITestContract childResolved = child.Resolve<ITestContract>();

            Assert.That(parentResolved, Is.TypeOf<ParentContract>());
            Assert.That(childResolved, Is.TypeOf<ChildContract>());
        }

        [Test]
        public void BindInterfacesAndSelfTo_NoInterfaces_StillBindsConcreteType()
        {
            using OnityContainer container = new OnityContainer();
            container.BindInterfacesAndSelfTo<NoInterfaceConcrete>().AsSingle();

            NoInterfaceConcrete first = container.Resolve<NoInterfaceConcrete>();
            NoInterfaceConcrete second = container.Resolve<NoInterfaceConcrete>();

            Assert.That(first, Is.Not.Null);
            Assert.That(first, Is.SameAs(second));
        }

        [Test]
        public void Resolve_UnboundConcreteType_UsesImplicitTransientBinding()
        {
            using OnityContainer container = new OnityContainer();

            AutoConcreteTarget first = container.Resolve<AutoConcreteTarget>();
            AutoConcreteTarget second = container.Resolve<AutoConcreteTarget>();

            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);
            Assert.That(first, Is.Not.SameAs(second));
        }

        private interface ITestContract
        {
        }

        private abstract class AbstractContract
        {
        }

        private interface IDisposableContract
        {
            bool IsDisposed { get; }
        }

        private sealed class ParentContract : ITestContract
        {
        }

        private sealed class ChildContract : ITestContract
        {
        }

        private sealed class TestDependency
        {
        }

        private sealed class CircularA
        {
            public CircularA(CircularB dependency)
            {
            }
        }

        private sealed class CircularB
        {
            public CircularB(CircularA dependency)
            {
            }
        }

        private sealed class MultipleInjectConstructorsTarget
        {
            [Inject]
            public MultipleInjectConstructorsTarget()
            {
            }

            [Inject]
            public MultipleInjectConstructorsTarget(TestDependency dependency)
            {
            }
        }

        private sealed class InjectPropertyWithoutSetterTarget
        {
            [Inject]
            public TestDependency Dependency { get; }
        }

        private sealed class InjectIndexerPropertyTarget
        {
            [Inject]
            public TestDependency this[int index]
            {
                get => null;
                set
                {
                }
            }
        }

        private sealed class InjectGenericMethodTarget
        {
            [Inject]
            private void Initialize<TValue>(TValue value)
            {
            }
        }

        private sealed class DisposableContract : IDisposableContract, IDisposable
        {
            public bool IsDisposed { get; private set; }

            public void Dispose()
            {
                IsDisposed = true;
            }
        }

        private sealed class NoInterfaceConcrete
        {
        }

        private sealed class AutoConcreteTarget
        {
        }
    }
}
