using System;
using NUnit.Framework;
using Onity.DI;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Verifies that Onity.DI throws actionable error messages: each enriched message
    /// names the offending type or member and points at a concrete fix, while keeping
    /// the original exception type so existing type-only assertions remain valid.
    /// </summary>
    [TestFixture]
    public sealed class OnityErrorMessageTests
    {
        [Test]
        public void Resolve_CircularDependency_MessageContainsFullChain()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<CircularA>().AsTransient();
            container.Bind<CircularB>().AsTransient();

            OnityResolveException exception =
                Assert.Throws<OnityResolveException>(() => container.Resolve<CircularA>());

            Assert.That(exception.Message, Does.Contain("Circular dependency"));
            Assert.That(exception.Message, Does.Contain(typeof(CircularA).FullName));
            Assert.That(exception.Message, Does.Contain(typeof(CircularB).FullName));
            Assert.That(exception.Message, Does.Contain("Resolution chain"));
            Assert.That(exception.Message, Does.Contain("->"));
        }

        [Test]
        public void Resolve_CircularDependency_ChainClosesOnRepeatedType()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<CircularA>().AsTransient();
            container.Bind<CircularB>().AsTransient();

            OnityResolveException exception =
                Assert.Throws<OnityResolveException>(() => container.Resolve<CircularA>());

            string expectedChain =
                $"{typeof(CircularA).FullName} -> {typeof(CircularB).FullName} -> {typeof(CircularA).FullName}";

            Assert.That(exception.Message, Does.Contain(expectedChain));
        }

        [Test]
        public void Resolve_UnboundInterface_MessageNamesTypeAndFix()
        {
            using OnityContainer container = new OnityContainer();

            OnityResolveException exception =
                Assert.Throws<OnityResolveException>(() => container.Resolve(typeof(IUnboundService)));

            Assert.That(exception.Message, Does.Contain(typeof(IUnboundService).FullName));
            Assert.That(exception.Message, Does.Contain("unbound interface"));
            Assert.That(exception.Message, Does.Contain("container.Bind<"));
            Assert.That(exception.Message, Does.Contain(".To<Impl>()"));
            Assert.That(exception.Message, Does.Contain("register an instance"));
        }

        [Test]
        public void Resolve_UnboundAbstractType_MessageNamesTypeAndFix()
        {
            using OnityContainer container = new OnityContainer();

            OnityResolveException exception =
                Assert.Throws<OnityResolveException>(() => container.Resolve(typeof(AbstractService)));

            Assert.That(exception.Message, Does.Contain(typeof(AbstractService).FullName));
            Assert.That(exception.Message, Does.Contain("unbound abstract type"));
            Assert.That(exception.Message, Does.Contain("container.Bind<"));
        }

        [Test]
        public void Resolve_OpenGenericDefinition_MessageNamesTypeAndFix()
        {
            using OnityContainer container = new OnityContainer();

            OnityResolveException exception =
                Assert.Throws<OnityResolveException>(() => container.Resolve(typeof(System.Collections.Generic.List<>)));

            Assert.That(exception.Message, Does.Contain("open generic"));
            Assert.That(exception.Message, Does.Contain("closed"));
        }

        [Test]
        public void Resolve_MultipleInjectConstructors_MessageNamesTypeAndFix()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<MultipleInjectConstructorsTarget>().AsTransient();

            OnityBindingException exception =
                Assert.Throws<OnityBindingException>(() => container.Resolve<MultipleInjectConstructorsTarget>());

            Assert.That(exception.Message, Does.Contain(typeof(MultipleInjectConstructorsTarget).FullName));
            Assert.That(exception.Message, Does.Contain("multiple [Inject] constructors"));
            Assert.That(exception.Message, Does.Contain("exactly one"));
        }

        [Test]
        public void Resolve_InjectPropertyWithoutSetter_MessageNamesMemberAndFix()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<InjectPropertyWithoutSetterTarget>().AsTransient();

            OnityBindingException exception =
                Assert.Throws<OnityBindingException>(() => container.Resolve<InjectPropertyWithoutSetterTarget>());

            Assert.That(exception.Message, Does.Contain(typeof(InjectPropertyWithoutSetterTarget).FullName));
            Assert.That(exception.Message, Does.Contain(nameof(InjectPropertyWithoutSetterTarget.Dependency)));
            Assert.That(exception.Message, Does.Contain("must have a setter"));
            Assert.That(exception.Message, Does.Contain("set accessor"));
        }

        [Test]
        public void Resolve_InjectIndexerProperty_MessageNamesTypeAndFix()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<InjectIndexerPropertyTarget>().AsTransient();

            OnityBindingException exception =
                Assert.Throws<OnityBindingException>(() => container.Resolve<InjectIndexerPropertyTarget>());

            Assert.That(exception.Message, Does.Contain(typeof(InjectIndexerPropertyTarget).FullName));
            Assert.That(exception.Message, Does.Contain("cannot be an indexer"));
        }

        [Test]
        public void Resolve_InjectGenericMethod_MessageNamesMemberAndFix()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<InjectGenericMethodTarget>().AsTransient();

            OnityBindingException exception =
                Assert.Throws<OnityBindingException>(() => container.Resolve<InjectGenericMethodTarget>());

            Assert.That(exception.Message, Does.Contain(typeof(InjectGenericMethodTarget).FullName));
            Assert.That(exception.Message, Does.Contain("Initialize"));
            Assert.That(exception.Message, Does.Contain("cannot be generic"));
        }

        [Test]
        public void Resolve_FailedInstantiation_MessageNamesTypeConstructorAndError()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<ThrowingConstructorTarget>().AsTransient();

            OnityResolveException exception =
                Assert.Throws<OnityResolveException>(() => container.Resolve<ThrowingConstructorTarget>());

            Assert.That(exception.Message, Does.Contain("Failed to instantiate"));
            Assert.That(exception.Message, Does.Contain(typeof(ThrowingConstructorTarget).FullName));
            Assert.That(exception.Message, Does.Contain("constructor"));
            Assert.That(exception.Message, Does.Contain(ThrowingConstructorTarget.FailureMarker));
        }

        private interface IUnboundService
        {
        }

        private abstract class AbstractService
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
            public MultipleInjectConstructorsTarget(IUnboundService dependency)
            {
            }
        }

        private sealed class InjectPropertyWithoutSetterTarget
        {
            [Inject]
            public IUnboundService Dependency { get; }
        }

        private sealed class InjectIndexerPropertyTarget
        {
            [Inject]
            public IUnboundService this[int index]
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

        private sealed class ConstructorDependency
        {
        }

        // Takes a resolvable dependency so construction runs through the container's
        // argument-bound path, where a throwing constructor is wrapped as an
        // OnityResolveException. A parameterless throwing constructor would bypass that
        // wrapping, so the parameter is required to exercise the enriched message.
        private sealed class ThrowingConstructorTarget
        {
            public const string FailureMarker = "ThrowingConstructorTarget ctor failed";

            public ThrowingConstructorTarget(ConstructorDependency dependency)
            {
                throw new InvalidOperationException(FailureMarker);
            }
        }
    }
}
