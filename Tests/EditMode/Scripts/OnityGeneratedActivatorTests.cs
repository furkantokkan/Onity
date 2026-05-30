using NUnit.Framework;
using Onity.DI;
using Onity.DI.Internal;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Verifies the AOT-generated activator registry path used by IL2CPP builds.
    /// The fixture registers a hand-written activator for unique test types, so
    /// the per-process constructor cache is first populated from the generated
    /// path instead of the JIT expression compiler or reflection fallback.
    /// </summary>
    [TestFixture]
    public sealed class OnityGeneratedActivatorTests
    {
        [SetUp]
        public void SetUp()
        {
            GeneratedRoot.GeneratedActivatorCalls = 0;
        }

        [Test]
        public void Resolve_UsesGeneratedActivator_WhenRegisteredForConstructorSignature()
        {
            GeneratedActivators.Register(
                typeof(GeneratedRoot),
                new[] { typeof(GeneratedDependency) },
                args =>
                {
                    GeneratedRoot.GeneratedActivatorCalls++;
                    return new GeneratedRoot((GeneratedDependency)args[0]);
                });

            using OnityContainer container = new OnityContainer();
            container.Bind<GeneratedDependency>().AsSingle();
            container.Bind<GeneratedRoot>().AsTransient();

            GeneratedRoot instance = container.Resolve<GeneratedRoot>();

            Assert.That(instance.Dependency, Is.Not.Null);
            Assert.That(GeneratedRoot.GeneratedActivatorCalls, Is.EqualTo(1));
        }

        private sealed class GeneratedDependency
        {
        }

        private sealed class GeneratedRoot
        {
            public static int GeneratedActivatorCalls;

            public GeneratedRoot(GeneratedDependency dependency)
            {
                Dependency = dependency;
            }

            public GeneratedDependency Dependency { get; }
        }
    }
}
