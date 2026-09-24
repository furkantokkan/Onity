using System.Reflection;
using NUnit.Framework;
using Onity.DI;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Verifies the reflection activation fallback that backs AOT/IL2CPP support.
    /// On those runtimes <c>Expression.Compile</c> cannot emit dynamic code, so the
    /// container must construct types and inject members through reflection instead
    /// of compiled delegates. The internal <c>OnityContainer.ForceReflectionActivation</c>
    /// flag (reflected here because it is internal, mirroring the baked-resolve
    /// parity suite) forces that exact strategy on the JIT test runner, so this
    /// fixture exercises the reflection constructor activator and the reflection
    /// field/property/method setters that would otherwise only run on device.
    /// The types are unique to this fixture so their per-process activator cache is
    /// first populated under the forced-reflection flag.
    /// </summary>
    [TestFixture]
    public sealed class OnityAotFallbackTests
    {
        private static readonly PropertyInfo s_forceReflectionProperty =
            typeof(OnityContainer).GetProperty(
                "ForceReflectionActivation",
                BindingFlags.Static | BindingFlags.NonPublic);

        private bool m_originalForceReflection;

        [SetUp]
        public void SetUp()
        {
            Assert.That(
                s_forceReflectionProperty,
                Is.Not.Null,
                "Internal OnityContainer.ForceReflectionActivation flag was not found. The AOT fallback must expose it.");

            OnityContainer.DiagnosticsCollectionEnabled = false;
            m_originalForceReflection = GetForceReflection();
            FallbackConstructionCounter.Reset();
            SetForceReflection(true);
        }

        [TearDown]
        public void TearDown()
        {
            // Restore so the forced-reflection flag never leaks into the rest of the
            // suite, which proves itself on the auto-detected (compiled) default.
            SetForceReflection(m_originalForceReflection);
            OnityContainer.DiagnosticsCollectionEnabled = false;
        }

        [Test]
        public void ForceReflectionActivation_DefaultsToFalse_SoCompiledPathStaysDefault()
        {
            Assert.That(m_originalForceReflection, Is.False);
        }

        [Test]
        public void TransientResolve_ReflectionActivator_ProducesDistinctFullyConstructedInstances()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IFallbackDependency>().To<FallbackDependency>().AsSingle();
            container.Bind<FallbackTransientService>().AsTransient();

            FallbackTransientService first = container.Resolve<FallbackTransientService>();
            FallbackTransientService second = container.Resolve<FallbackTransientService>();

            Assert.That(first, Is.Not.SameAs(second));
            Assert.That(first.Dependency, Is.Not.Null);
            Assert.That(first.Dependency, Is.SameAs(container.Resolve<IFallbackDependency>()));
            Assert.That(second.Dependency, Is.SameAs(first.Dependency));
        }

        [Test]
        public void SingletonResolve_ReflectionActivator_CachesSingleInstanceAndConstructsOnce()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<FallbackSingletonService>().AsSingle();

            FallbackSingletonService first = container.Resolve<FallbackSingletonService>();
            FallbackSingletonService second = container.Resolve<FallbackSingletonService>();

            Assert.That(first, Is.SameAs(second));
            Assert.That(FallbackConstructionCounter.Count, Is.EqualTo(1));
        }

        [Test]
        public void Inject_ReflectionSetters_InjectFieldPropertyAndMethod()
        {
            using OnityContainer container = new OnityContainer();
            container.Bind<IFallbackDependency>().To<FallbackDependency>().AsSingle();

            FallbackInjectionTarget target = new FallbackInjectionTarget();
            container.Inject(target);

            IFallbackDependency expected = container.Resolve<IFallbackDependency>();
            Assert.That(target.FieldValue, Is.SameAs(expected));
            Assert.That(target.PropertyValue, Is.SameAs(expected));
            Assert.That(target.MethodValue, Is.SameAs(expected));
        }

        [Test]
        public void Resolve_ValueTypeConstructorDependency_ReflectionActivatorBoxesCorrectly()
        {
            using OnityContainer container = new OnityContainer();
            container.BindInstance(new FallbackValueDependency(4242));
            container.Bind<FallbackValueConsumer>().AsTransient();

            FallbackValueConsumer instance = container.Resolve<FallbackValueConsumer>();

            Assert.That(instance.Value.Magnitude, Is.EqualTo(4242));
        }

        private static bool GetForceReflection()
        {
            return (bool)s_forceReflectionProperty.GetValue(null);
        }

        private static void SetForceReflection(bool value)
        {
            s_forceReflectionProperty.SetValue(null, value);
        }

        private interface IFallbackDependency
        {
        }

        private sealed class FallbackDependency : IFallbackDependency
        {
        }

        private sealed class FallbackTransientService
        {
            public FallbackTransientService(IFallbackDependency dependency)
            {
                Dependency = dependency;
            }

            public IFallbackDependency Dependency { get; }
        }

        private sealed class FallbackSingletonService
        {
            public FallbackSingletonService()
            {
                FallbackConstructionCounter.Increment();
            }
        }

        private sealed class FallbackInjectionTarget
        {
            [Inject]
            private IFallbackDependency m_field = null;

            [Inject]
            private IFallbackDependency Property { get; set; }

            private IFallbackDependency m_method;

            public IFallbackDependency FieldValue => m_field;

            public IFallbackDependency PropertyValue => Property;

            public IFallbackDependency MethodValue => m_method;

            [Inject]
            private void Initialize(IFallbackDependency dependency)
            {
                m_method = dependency;
            }
        }

        private readonly struct FallbackValueDependency
        {
            public FallbackValueDependency(int magnitude)
            {
                Magnitude = magnitude;
            }

            public int Magnitude { get; }
        }

        private sealed class FallbackValueConsumer
        {
            public FallbackValueConsumer(FallbackValueDependency value)
            {
                Value = value;
            }

            public FallbackValueDependency Value { get; }
        }

        private static class FallbackConstructionCounter
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
    }
}
